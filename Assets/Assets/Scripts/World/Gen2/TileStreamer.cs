using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// world gen v2: 128m tiles around both girls, nearest first, a couple of ms a frame, pooled when out of reach.
// a tile is placeholder flat snow ground, its roads (RoadMesh), its lots' buildings and rubble (LotMesh)
// and its props from the WorldKit: small landmarks, street lamps, item spawners.
// everything is in layout coords (world + TotalOffset)
public class TileStreamer : MonoBehaviour
{
	public const float BuildRadius = 700f;

	// tiles stay until this much further out, so they don't flicker at the edge
	private const float keepMargin = 128f;

	private const double budgetMs = 2.0;

	private const float refreshInterval = 0.25f;

	private const float thickness = 2f;

	public LayerPlan Plan { get; private set; }

	public int TileCount => tiles.Count;

	// layout y of the deck
	public float GroundY => groundY;

	// both girls in layout coords, as of the last refresh
	public List<Vector3> Points => points;

	private FloatingOrigin floatingOrigin;

	// layout y of the deck surface, taken from the floor under Chito at the start
	private float groundY;

	private int groundLayer;

	private Material material;

	// asphalt, buried road, concrete: the game's own textures (Resources/Roads), flat colours if those are missing
	private Material[] roadMaterials;

	private static readonly string[] roadMaterialNames = { "Asphalt", "Buried", "Concrete" };

	private static readonly Color[] roadColors =
	{
		new Color(0.25f, 0.25f, 0.27f),
		new Color(0.56f, 0.58f, 0.62f),
		new Color(0.58f, 0.58f, 0.56f)
	};

	private readonly RoadMesh roadMesh = new RoadMesh();

	// office walls, concrete, dark (windows, voids), metal (tanks, pipes)
	private Material[] lotMaterials;

	private readonly LotMesh lotMesh = new LotMesh();

	private readonly List<Lot> lots = new List<Lot>();

	private WorldKit kit;

	// each landmark's size in its own orientation, and from its pivot to the middle of its base
	private Vector3[] landmarkSizes;

	private Vector3[] landmarkBases;

	private readonly List<int> placed = new List<int>();

	private readonly List<int> pieceOf = new List<int>();

	// share of lots with an item spawner, about one per city tile like the legacy nodes (tuned with SURV-4)
	private const float itemOnLandmark = 0.5f;

	private const float itemOnOpen = 0.1f;

	private const float itemOnBuilding = 0.03f;

	private readonly List<RoadPiece> pieces = new List<RoadPiece>();

	private readonly List<RoadPatch> patches = new List<RoadPatch>();

	// road pieces by tile, so a wire can reach the next pole in the neighbouring tile (pure data, cleared when big)
	private readonly Dictionary<long, RoadPiece[]> pieceCache = new Dictionary<long, RoadPiece[]>();

	// street and buried-road pieces around the tile by their start, to walk along a road
	private readonly Dictionary<XZ, RoadPiece> chain = new Dictionary<XZ, RoadPiece>();

	private readonly List<RoadPiece> otherPieces = new List<RoadPiece>();

	private readonly List<Viaduct> viaducts = new List<Viaduct>();

	private readonly List<RoadPatch> otherPatches = new List<RoadPatch>();

	// made here at runtime, destroyed with the streamer (never the loaded assets)
	private readonly List<Material> made = new List<Material>();

	private readonly Dictionary<long, GameObject> tiles = new Dictionary<long, GameObject>();

	private readonly Stack<GameObject> pool = new Stack<GameObject>();

	private readonly List<long> missing = new List<long>();

	private readonly List<long> leaving = new List<long>();

	private readonly HashSet<long> wanted = new HashSet<long>();

	private readonly List<Vector3> points = new List<Vector3>();

	private readonly Stopwatch stopwatch = new Stopwatch();

	private Transform chito;

	private Transform yuuri;

	private float nextRefresh;

	public void Init(LayerPlan plan, FloatingOrigin origin)
	{
		Plan = plan;
		floatingOrigin = origin;
	}

	private void Start()
	{
		// no girl registry yet (WP-ARCH-2), same lookup as CoopPlayers
		chito = Girl("ChitoParent");
		yuuri = Girl("YuuriParent");
		Vector3 start = (chito != null) ? chito.position : transform.position;
		groundY = start.y + Offset().y;
		groundLayer = 0;
		Material floor = null;
		if (Physics.Raycast(start + Vector3.up, Vector3.down, out RaycastHit hit, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
		{
			groundY = hit.point.y + Offset().y;
			groundLayer = hit.collider.gameObject.layer;
			Renderer renderer = hit.collider.GetComponent<Renderer>();
			if (renderer != null)
			{
				floor = renderer.sharedMaterial;
			}
		}
		Shader lit = Shader.Find("Universal Render Pipeline/Lit");
		// one snow for every district, Resources/Ground/Snow
		material = Resources.Load<Material>("Ground/Snow");
		if (material == null)
		{
			material = (lit != null) ? Made(new Material(lit)) : floor;
			if (lit != null)
			{
				material.SetFloat("_Smoothness", 0.1f);
			}
		}
		roadMaterials = new Material[RoadMesh.SubmeshCount];
		for (int i = 0; i < roadMaterials.Length; i++)
		{
			roadMaterials[i] = Resources.Load<Material>("Roads/" + roadMaterialNames[i]);
			if (roadMaterials[i] == null && lit != null)
			{
				roadMaterials[i] = Made(new Material(lit));
				roadMaterials[i].SetColor("_BaseColor", roadColors[i]);
				roadMaterials[i].SetColor("_Color", roadColors[i]);
				roadMaterials[i].SetFloat("_Smoothness", 0.1f);
			}
			else if (roadMaterials[i] == null)
			{
				roadMaterials[i] = floor;
			}
		}
		Material wall = Resources.Load<Material>("Buildings/Wall");
		if (wall == null && lit != null)
		{
			wall = Made(new Material(lit));
			wall.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.62f));
			wall.SetFloat("_Smoothness", 0.1f);
		}
		lotMaterials = new[] { (wall != null) ? wall : floor, roadMaterials[2], LotMaterial("Dark", new Color(0.11f, 0.11f, 0.12f), lit, floor), LotMaterial("Metal", new Color(0.5f, 0.45f, 0.4f), lit, floor) };
		MeasureLandmarks();
#if DUMP_WORLDGEN
		string[] args = System.Environment.GetCommandLineArgs();
		int at = System.Array.IndexOf(args, "-gen2run");
		run = at >= 0;
		if (run && at + 1 < args.Length && float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed))
		{
			runSpeed = speed;
		}
#endif
		// everything in reach before the first frame, like the legacy BuildAround
		Refresh();
		BuildMissing(double.MaxValue);
	}

	private void OnDestroy()
	{
		foreach (Material m in made)
		{
			Destroy(m);
		}
	}

	// Resources/Buildings/<name>, a flat colour if it's missing
	private Material LotMaterial(string name, Color fallback, Shader lit, Material floor)
	{
		Material loaded = Resources.Load<Material>("Buildings/" + name);
		if (loaded != null || lit == null)
		{
			return (loaded != null) ? loaded : floor;
		}
		Material made = Made(new Material(lit));
		made.SetColor("_BaseColor", fallback);
		made.SetFloat("_Smoothness", 0.1f);
		return made;
	}

	private Material Made(Material m)
	{
		made.Add(m);
		return m;
	}

	private static Transform Girl(string name)
	{
		GameObject girl = GameObject.Find(name);
		return (girl != null) ? girl.transform : null;
	}

	private Vector3 Offset()
	{
		return (floatingOrigin != null) ? floatingOrigin.TotalOffset : Vector3.zero;
	}

#if DUMP_WORLDGEN
	private float nextLog;

	private int builtTiles;

	private long roadTriangles;

	private long lotTriangles;

	private double slowestBuild;

	private readonly Stopwatch buildWatch = new Stopwatch();

	private bool run;

	// frames left to log the camera after a shift
	private int probeFrames;

	private Vector3 lastCamera;

	private Vector3 lastChito;

	private Quaternion lastRotation = Quaternion.identity;

	private void OnEnable()
	{
		FloatingOrigin.Shifted += OnShiftedProbe;
		StartCoroutine(CameraProbe());
	}

	private void OnDisable()
	{
		FloatingOrigin.Shifted -= OnShiftedProbe;
	}

	private Quaternion lastLiveRotation = Quaternion.identity;

	private static string Describe(Cinemachine.CinemachineVirtualCameraBase vcam)
	{
		string text = vcam.name + (Cinemachine.CinemachineCore.Instance.IsLive(vcam) ? " LIVE" : "") + " parent=" + (vcam.ParentCamera != null ? vcam.ParentCamera.Name : "-") + " follow=" + (vcam.Follow != null ? vcam.Follow.name : "-") + " lookat=" + (vcam.LookAt != null ? vcam.LookAt.name : "-");
		if (vcam is Cinemachine.CinemachineVirtualCamera v)
		{
			text += " body=" + v.GetCinemachineComponent(Cinemachine.CinemachineCore.Stage.Body)?.GetType().Name + " aim=" + v.GetCinemachineComponent(Cinemachine.CinemachineCore.Stage.Aim)?.GetType().Name;
		}
		return text;
	}

	private void OnShiftedProbe(Vector3 offset)
	{
		Cinemachine.CinemachineCore core = Cinemachine.CinemachineCore.Instance;
		for (int i = 0; i < core.VirtualCameraCount; i++)
		{
			UnityEngine.Debug.Log("ShiftProbe: vcam " + Describe(core.GetVirtualCamera(i)));
		}
		UnityEngine.Debug.Log("ShiftProbe: shift by " + offset + " frame " + Time.frameCount + ", the 30 frames before: avg " + (frameTimes.Count > 0 ? frameTimes.Average() : 0f).ToString("F1") + "ms max " + (frameTimes.Count > 0 ? frameTimes.Max() : 0f).ToString("F1") + "ms");
		probeFrames = 6;
	}

	private readonly Queue<float> frameTimes = new Queue<float>();

	// end of frame, after every LateUpdate: how far the camera and chito moved this frame in layout coords.
	// a jerk shows up as a camera step that doesn't match chito's
	private System.Collections.IEnumerator CameraProbe()
	{
		WaitForEndOfFrame end = new WaitForEndOfFrame();
		while (true)
		{
			yield return end;
			Camera camera = Camera.main;
			if (camera == null || chito == null)
			{
				continue;
			}
			frameTimes.Enqueue(Time.unscaledDeltaTime * 1000f);
			if (frameTimes.Count > 30)
			{
				frameTimes.Dequeue();
			}
			Vector3 cam = camera.transform.position + Offset();
			Vector3 girl = chito.position + Offset();
			float turn = Quaternion.Angle(lastRotation, camera.transform.rotation);
			Cinemachine.CinemachineBrain brain = camera.GetComponent<Cinemachine.CinemachineBrain>();
			Cinemachine.ICinemachineCamera live = (brain != null) ? brain.ActiveVirtualCamera : null;
			Quaternion liveRotation = (live != null) ? live.State.FinalOrientation : Quaternion.identity;
			if (probeFrames > 0)
			{
				probeFrames--;
				UnityEngine.Debug.Log("ShiftProbe: frame " + Time.frameCount + " dt " + (Time.unscaledDeltaTime * 1000f).ToString("F1") + "ms camera step " + (cam - lastCamera).ToString("F3") + " chito step " + (girl - lastChito).ToString("F3") + " camera-chito " + (cam - girl).ToString("F2") + " turn " + turn.ToString("F2") + " live " + (live != null ? live.Name : "-") + " live turn " + Quaternion.Angle(lastLiveRotation, liveRotation).ToString("F2") + " camera vs live " + Quaternion.Angle(liveRotation, camera.transform.rotation).ToString("F2") + " brain " + (brain != null ? "on camera" : "elsewhere"));
			}
			// anything sudden outside the logged frames too
			else if (lastCamera != Vector3.zero && (turn > 5f || ((cam - lastCamera) - (girl - lastChito)).magnitude > 2f))
			{
				UnityEngine.Debug.Log("ShiftProbe: camera jump at frame " + Time.frameCount + " turn " + turn.ToString("F2") + " off chito by " + ((cam - lastCamera) - (girl - lastChito)).ToString("F2"));
			}
			lastCamera = cam;
			lastChito = girl;
			lastRotation = camera.transform.rotation;
			lastLiveRotation = liveRotation;
		}
	}

	private float runSpeed = 20f;

	// -gen2run [speed]: both girls slide along at speed m/s (20 default) on a slow curve, for streaming and floating origin tests
	private void DebugRun()
	{
		if (run && chito != null)
		{
			float heading = Time.time * 15f * Mathf.Deg2Rad;
			Vector3 step = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * (runSpeed * Time.deltaTime);
			chito.position += step;
			if (yuuri != null)
			{
				yuuri.position += step;
			}
			Physics.SyncTransforms();
		}
	}

	private void LateUpdate()
	{
		if (Time.unscaledTime >= nextLog)
		{
			nextLog = Time.unscaledTime + 5f;
			Vector3 p = (chito != null) ? chito.position + Offset() : Vector3.zero;
			UnityEngine.Debug.Log("TileStreamer: tiles=" + tiles.Count + " pooled=" + pool.Count + " queued=" + missing.Count + " chito=" + p + " offset=" + Offset() + " district=" + Plan.Def.Districts[Plan.DistrictAt(p.x, p.z)].Id + " built=" + builtTiles + " road tris=" + roadTriangles + " lot tris=" + lotTriangles + " slowest tile=" + slowestBuild.ToString("F2") + "ms");
			slowestBuild = 0.0;
		}
	}
#endif

	private void Update()
	{
#if DUMP_WORLDGEN
		DebugRun();
#endif
		if (Time.time >= nextRefresh)
		{
			nextRefresh = Time.time + refreshInterval;
			Refresh();
		}
		BuildMissing(budgetMs);
	}

	private static long Key(int x, int z)
	{
		return ((long)x << 32) ^ (uint)z;
	}

	private static int KeyX(long key)
	{
		return (int)(key >> 32);
	}

	private static int KeyZ(long key)
	{
		return (int)(uint)key;
	}

	private void Refresh()
	{
		points.Clear();
		Vector3 offset = Offset();
		if (chito != null && chito.gameObject.activeInHierarchy)
		{
			points.Add(chito.position + offset);
		}
		if (yuuri != null && yuuri.gameObject.activeInHierarchy)
		{
			points.Add(yuuri.position + offset);
		}
		wanted.Clear();
		float size = (float)Gen2.TileSize;
		foreach (Vector3 p in points)
		{
			int x0 = Mathf.FloorToInt((p.x - BuildRadius) / size);
			int x1 = Mathf.FloorToInt((p.x + BuildRadius) / size);
			int z0 = Mathf.FloorToInt((p.z - BuildRadius) / size);
			int z1 = Mathf.FloorToInt((p.z + BuildRadius) / size);
			for (int x = x0; x <= x1; x++)
			{
				for (int z = z0; z <= z1; z++)
				{
					if (TileDistance(x, z, p) <= BuildRadius)
					{
						wanted.Add(Key(x, z));
					}
				}
			}
		}
		leaving.Clear();
		foreach (KeyValuePair<long, GameObject> tile in tiles)
		{
			if (!wanted.Contains(tile.Key) && NearestDistance(KeyX(tile.Key), KeyZ(tile.Key)) > BuildRadius + keepMargin)
			{
				leaving.Add(tile.Key);
			}
		}
		foreach (long key in leaving)
		{
			GameObject tile = tiles[key];
			tiles.Remove(key);
			ClearProps(tile);
			tile.SetActive(false);
			pool.Push(tile);
		}
		missing.Clear();
		foreach (long key in wanted)
		{
			if (!tiles.ContainsKey(key))
			{
				missing.Add(key);
			}
		}
		// nearest last, built from the end
		missing.Sort((a, b) => NearestDistance(KeyX(b), KeyZ(b)).CompareTo(NearestDistance(KeyX(a), KeyZ(a))));
	}

	// from p to the closest point of the tile
	private static float TileDistance(int x, int z, Vector3 p)
	{
		float size = (float)Gen2.TileSize;
		float dx = Mathf.Max(0f, Mathf.Max(x * size - p.x, p.x - (x + 1) * size));
		float dz = Mathf.Max(0f, Mathf.Max(z * size - p.z, p.z - (z + 1) * size));
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	private float NearestDistance(int x, int z)
	{
		float best = float.MaxValue;
		foreach (Vector3 p in points)
		{
			best = Mathf.Min(best, TileDistance(x, z, p));
		}
		return best;
	}

	private void BuildMissing(double budget)
	{
		stopwatch.Restart();
		while (missing.Count > 0 && stopwatch.Elapsed.TotalMilliseconds < budget)
		{
			long key = missing[missing.Count - 1];
			missing.RemoveAt(missing.Count - 1);
			if (!tiles.ContainsKey(key))
			{
				tiles.Add(key, Build(KeyX(key), KeyZ(key)));
			}
		}
	}

	// a pure function of (plan, x, z) so co-op peers get the same tile
	private GameObject Build(int x, int z)
	{
#if DUMP_WORLDGEN
		buildWatch.Restart();
#endif
		GameObject tile = (pool.Count > 0) ? pool.Pop() : NewTile();
		tile.name = "tile " + x + "," + z;
		float size = (float)Gen2.TileSize;
		tile.transform.SetPositionAndRotation(new Vector3(x * size, groundY, z * size) - Offset(), Quaternion.identity);
		// active before the collider gets its mesh: a collider in an inactive pooled tile drops it
		tile.SetActive(true);
		GameObject roads = tile.transform.GetChild(1).gameObject;
		Plan.Roads.Collect(x, z, pieces, patches);
		Mesh mesh = roads.GetComponent<MeshFilter>().sharedMesh;
		bool any = roadMesh.Build(mesh, pieces, patches, x * Gen2.TileSize, z * Gen2.TileSize);
		// set again so the collider cooks the new shape
		roads.SetActive(any);
		MeshCollider collider = roads.GetComponent<MeshCollider>();
		collider.sharedMesh = null;
		if (any)
		{
			collider.sharedMesh = mesh;
		}
		GameObject built = tile.transform.GetChild(2).gameObject;
		Plan.Lots.Collect(x, z, lots);
		// a landmark lot with no landmark that fits is open ground
		placed.Clear();
		pieceOf.Clear();
		for (int i = 0; i < lots.Count; i++)
		{
			if (lots[i].Kind != LotKind.Landmark)
			{
				continue;
			}
			int piece = PickLandmark(lots[i]);
			if (piece >= 0)
			{
				placed.Add(i);
				pieceOf.Add(piece);
			}
			else
			{
				Lot open = lots[i];
				open.Kind = LotKind.Open;
				lots[i] = open;
			}
		}
		Mesh lotsMesh = built.GetComponent<MeshFilter>().sharedMesh;
		lotMesh.Begin();
		lotMesh.AddLots(lots, Plan.Def.Districts, Plan.Def.Ruin, x * Gen2.TileSize, z * Gen2.TileSize);
		Scatter(x, z);
		PlaceCables(x, z);
		PlaceViaducts(x, z);
		bool anyLots = lotMesh.End(lotsMesh);
		built.SetActive(anyLots);
		MeshCollider lotsCollider = built.GetComponent<MeshCollider>();
		lotsCollider.sharedMesh = null;
		if (anyLots)
		{
			lotsCollider.sharedMesh = lotsMesh;
		}
		Transform props = ClearProps(tile);
		for (int i = 0; i < placed.Count; i++)
		{
			PlaceLandmark(lots[placed[i]], pieceOf[i], props);
		}
		int items = 0;
		foreach (Lot lot in lots)
		{
			PlaceItems(lot, x, z, ref items, props);
		}
		PlaceLamps(props);
#if DUMP_WORLDGEN
		builtTiles++;
		roadTriangles += roadMesh.Triangles;
		lotTriangles += anyLots ? lotMesh.Triangles : 0;
		slowestBuild = System.Math.Max(slowestBuild, buildWatch.Elapsed.TotalMilliseconds);
#endif
		return tile;
	}

	// root at the tile's corner on the deck: child 0 the ground slab, child 1 the roads, child 2 the lots, child 3 the props
	private GameObject NewTile()
	{
		float size = (float)Gen2.TileSize;
		GameObject tile = new GameObject();
		tile.layer = groundLayer;
		GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
		ground.name = "ground";
		ground.layer = groundLayer;
		ground.GetComponent<Renderer>().sharedMaterial = material;
		ground.transform.SetParent(tile.transform, false);
		ground.transform.localPosition = new Vector3(size * 0.5f, -thickness * 0.5f - 0.02f, size * 0.5f);
		ground.transform.localScale = new Vector3(size, thickness, size);
		GameObject roads = new GameObject("roads", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
		roads.layer = groundLayer;
		roads.transform.SetParent(tile.transform, false);
		Mesh mesh = new Mesh { name = "roads" };
		mesh.MarkDynamic();
		roads.GetComponent<MeshFilter>().sharedMesh = mesh;
		roads.GetComponent<MeshRenderer>().sharedMaterials = roadMaterials;
		GameObject built = new GameObject("lots", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
		built.layer = groundLayer;
		built.transform.SetParent(tile.transform, false);
		Mesh lotsMesh = new Mesh { name = "lots" };
		lotsMesh.MarkDynamic();
		built.GetComponent<MeshFilter>().sharedMesh = lotsMesh;
		built.GetComponent<MeshRenderer>().sharedMaterials = lotMaterials;
		new GameObject("props").transform.SetParent(tile.transform, false);
		return tile;
	}

	// once at the start: how big each landmark is, standing as its model stands
	private void MeasureLandmarks()
	{
		kit = WorldKit.Load();
		if (kit != null)
		{
			lotMesh.Buildings = kit.Buildings;
			lotMesh.Tanks = kit.Tanks;
			lotMesh.Props = kit.Props;
		}
		int count = (kit != null && kit.Landmarks != null) ? kit.Landmarks.Length : 0;
		landmarkSizes = new Vector3[count];
		landmarkBases = new Vector3[count];
		for (int i = 0; i < count; i++)
		{
			GameObject model = kit.Landmarks[i].Model;
			if (model == null)
			{
				continue;
			}
			GameObject probe = Instantiate(model, Vector3.zero, model.transform.rotation);
			probe.transform.localScale = model.transform.lossyScale;
			Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
			bool any = false;
			foreach (Renderer renderer in probe.GetComponentsInChildren<Renderer>())
			{
				if (any)
				{
					bounds.Encapsulate(renderer.bounds);
				}
				else
				{
					bounds = renderer.bounds;
					any = true;
				}
			}
			landmarkSizes[i] = bounds.size;
			landmarkBases[i] = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
			probe.SetActive(false);
			Destroy(probe);
		}
	}

	// a landmark allowed in the lot's district that fits it either way round, weighted, from the lot's seed. -1 if none
	private int PickLandmark(Lot lot)
	{
		if (kit == null || landmarkSizes.Length == 0)
		{
			return -1;
		}
		string district = Plan.Def.Districts[lot.District].Id;
		float total = 0f;
		for (int i = 0; i < landmarkSizes.Length; i++)
		{
			total += Fits(lot, i, district) ? kit.Landmarks[i].Weight : 0f;
		}
		if (total <= 0f)
		{
			return -1;
		}
		float pick = (float)Gen2.Unit(WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 8uL)) * total;
		for (int i = 0; i < landmarkSizes.Length; i++)
		{
			if (!Fits(lot, i, district))
			{
				continue;
			}
			pick -= kit.Landmarks[i].Weight;
			if (pick < 0f)
			{
				return i;
			}
		}
		return -1;
	}

	private bool Fits(Lot lot, int i, string district)
	{
		WorldKit.Landmark landmark = kit.Landmarks[i];
		if (landmark.Model == null || !landmark.Allows(district))
		{
			return false;
		}
		float u = (float)lot.HalfU * 2f - 2f;
		float v = (float)lot.HalfV * 2f - 2f;
		Vector3 size = landmarkSizes[i];
		return (size.x <= u && size.z <= v) || (size.z <= u && size.x <= v);
	}

	// in the middle of the lot on the deck, along the lot's axis (turned a quarter if it only fits that way, else either way round)
	private void PlaceLandmark(Lot lot, int i, Transform props)
	{
		WorldKit.Landmark landmark = kit.Landmarks[i];
		Vector3 size = landmarkSizes[i];
		float u = (float)lot.HalfU * 2f - 2f;
		float v = (float)lot.HalfV * 2f - 2f;
		bool straight = size.x <= u && size.z <= v;
		bool turned = size.z <= u && size.x <= v;
		ulong choice = WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 9uL);
		float yaw = Mathf.Atan2((float)lot.Axis.Z, (float)lot.Axis.X) * -Mathf.Rad2Deg;
		yaw += (straight && turned) ? (choice % 4uL) * 90f : straight ? (choice % 2uL) * 180f : 90f + (choice % 2uL) * 180f;
		Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
		Vector3 centre = new Vector3((float)lot.Center.X, groundY, (float)lot.Center.Z) - Offset();
		GameObject instance = Instantiate(landmark.Model, props);
		instance.transform.localScale = landmark.Model.transform.lossyScale;
		instance.transform.SetPositionAndRotation(centre - turn * landmarkBases[i], turn * landmark.Model.transform.rotation);
		foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
		{
			if (filter.GetComponent<Collider>() == null && filter.sharedMesh != null)
			{
				filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
			}
			filter.gameObject.layer = groundLayer;
		}
		if (landmark.Extra != null)
		{
			Instantiate(landmark.Extra, instance.transform.TransformPoint(landmark.ExtraPosition), instance.transform.rotation * landmark.ExtraRotation, props);
		}
		GraphicsTuning.Adopt(instance);
	}

	// debris on the ground from the tile's seed: plenty in open districts (slabs, piles, dead trees, broken frames,
	// a leaning pole), a pile or two in town. clear of the tile's roads and lots and the start room
	private void Scatter(int x, int z)
	{
		double size = Gen2.TileSize;
		WorldRandom random = new WorldRandom(WorldRandom.Derive(Gen2.TileSeed(Plan.LayerSeed, x, z), Gen2.StreamLot + 5, 0uL));
		bool open = Plan.Def.Districts[Plan.DistrictAt((x + 0.5) * size, (z + 0.5) * size)].Roads == DistrictRoads.Open;
		int count = open ? 2 + random.Next(6) : random.Next(3);
		for (int i = 0; i < count; i++)
		{
			XZ at = new XZ((x + Gen2.Unit(random.NextULong())) * size, (z + Gen2.Unit(random.NextULong())) * size);
			double pick = Gen2.Unit(random.NextULong());
			ulong seed = random.NextULong();
			if (Blocked(at, 6.0))
			{
				continue;
			}
			LotMesh.Debris kind = LotMesh.Debris.Pile;
			if (Plan.Def.Districts[Plan.DistrictAt(at.X, at.Z)].Roads == DistrictRoads.Open)
			{
				kind = (pick < 0.25) ? LotMesh.Debris.Slab : (pick < 0.5) ? LotMesh.Debris.Pile : (pick < 0.75) ? LotMesh.Debris.Tree : (pick < 0.93) ? LotMesh.Debris.Frame : LotMesh.Debris.Pole;
			}
			lotMesh.AddDebris(kind, new Vector3((float)(at.X - x * size), 0f, (float)(at.Z - z * size)), seed);
		}
	}

	// utility poles along streets (sides taking turns, so the wires cross the street) and along buried roads,
	// each with a wire to the next pole down the road
	private void PlaceCables(int x, int z)
	{
		if (pieceCache.Count > 4096)
		{
			pieceCache.Clear();
		}
		pieceCache[Key(x, z)] = pieces.ToArray();
		chain.Clear();
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				foreach (RoadPiece other in PiecesOf(x + dx, z + dz))
				{
					if (other.Kind != RoadKind.Highway)
					{
						chain[other.A] = other;
					}
				}
			}
		}
		Vector3 corner = new Vector3((float)(x * Gen2.TileSize), 0f, (float)(z * Gen2.TileSize));
		foreach (RoadPiece piece in pieces)
		{
			if (!PoleAt(piece, out Vector3 at, out Vector3 across, out float tall))
			{
				continue;
			}
			lotMesh.AddPole(at - corner, tall, across);
			RoadPiece next = piece;
			bool found = true;
			int every = (piece.Kind == RoadKind.Street) ? 3 : 4;
			for (int k = 0; k < every && found; k++)
			{
				found = chain.TryGetValue(next.B, out next);
			}
			if (found && PoleAt(next, out Vector3 to, out _, out float nextTall))
			{
				float sag = 0.6f + 0.6f * (float)Gen2.Unit(WorldRandom.Derive(Plan.LayerSeed, Gen2.StreamLot + 7, Gen2.Pack((int)System.Math.Round(piece.A.X * 10.0), (int)System.Math.Round(piece.A.Z * 10.0))));
				lotMesh.AddCable(at - corner + Vector3.up * (tall - 0.5f), to - corner + Vector3.up * (nextTall - 0.5f), sag);
			}
		}
	}

	// the bays of every viaduct whose middle is in this tile, and the piers that stand in it. a pier is left out
	// where a road runs, the bays either side of it get a girder instead of an arch
	private void PlaceViaducts(int x, int z)
	{
		double size = Gen2.TileSize;
		Plan.Viaducts.Near((x + 0.5) * size, (z + 0.5) * size, viaducts);
		Vector3 corner = new Vector3((float)(x * size), 0f, (float)(z * size));
		foreach (Viaduct viaduct in viaducts)
		{
			double length = viaduct.Length;
			if (RoadPlan.SegmentDistance(new XZ((x + 0.5) * size, (z + 0.5) * size), viaduct.A, viaduct.B) > size)
			{
				continue;
			}
			Vector3 along = new Vector3((float)((viaduct.B.X - viaduct.A.X) / length), 0f, (float)((viaduct.B.Z - viaduct.A.Z) / length));
			int bays = (int)System.Math.Round(length / ViaductPlan.Bay);
			for (int k = 0; k <= bays; k++)
			{
				XZ pier = viaduct.At(k * ViaductPlan.Bay);
				if (k < bays)
				{
					XZ middle = viaduct.At((k + 0.5) * ViaductPlan.Bay);
					if (Gen2.Cell(middle.X, size) == x && Gen2.Cell(middle.Z, size) == z)
					{
						bool arch = PierStands(pier) && PierStands(viaduct.At((k + 1) * ViaductPlan.Bay));
						lotMesh.AddBay(new Vector3((float)pier.X, 0f, (float)pier.Z) - corner, along, (float)viaduct.Height, arch);
					}
				}
				if (Gen2.Cell(pier.X, size) == x && Gen2.Cell(pier.Z, size) == z && PierStands(pier))
				{
					lotMesh.AddPier(new Vector3((float)pier.X, 0f, (float)pier.Z) - corner, along, (float)viaduct.Height);
				}
			}
		}
	}

	private bool PierStands(XZ p)
	{
		foreach (RoadPiece piece in PiecesOf(Gen2.Cell(p.X, Gen2.TileSize), Gen2.Cell(p.Z, Gen2.TileSize)))
		{
			if (RoadPlan.SegmentDistance(p, piece.A, piece.B) < piece.Width * 0.5 + ViaductPlan.PierWidth)
			{
				return false;
			}
		}
		return true;
	}

	private RoadPiece[] PiecesOf(int x, int z)
	{
		long key = Key(x, z);
		if (!pieceCache.TryGetValue(key, out RoadPiece[] found))
		{
			Plan.Roads.Collect(x, z, otherPieces, otherPatches);
			found = otherPieces.ToArray();
			pieceCache[key] = found;
		}
		return found;
	}

	// whether a pole stands by this piece: every third street piece, every fourth on a buried road, a district's share
	private bool PoleAt(RoadPiece piece, out Vector3 at, out Vector3 across, out float tall)
	{
		at = across = Vector3.zero;
		tall = 0f;
		bool street = piece.Kind == RoadKind.Street;
		int every = street ? 3 : 4;
		if (piece.Kind == RoadKind.Highway || piece.Index % every != 0)
		{
			return false;
		}
		XZ mid = new XZ((piece.A.X + piece.B.X) * 0.5, (piece.A.Z + piece.B.Z) * 0.5);
		ulong roll = WorldRandom.Derive(Plan.LayerSeed, Gen2.StreamLot + 6, Gen2.Pack((int)System.Math.Round(mid.X * 10.0), (int)System.Math.Round(mid.Z * 10.0)));
		if (Gen2.Unit(roll) >= Plan.Def.Districts[Plan.DistrictAt(mid.X, mid.Z)].Cables)
		{
			return false;
		}
		float side = (!street || (piece.Index / every) % 2 == 0) ? 1f : -1f;
		across = new Vector3((float)-piece.TangentA.Z, 0f, (float)piece.TangentA.X);
		at = new Vector3((float)mid.X, 0f, (float)mid.Z) + across * (side * ((float)piece.Width * 0.5f + (street ? 2.5f : 3f)));
		tall = street ? 8.5f : 7f;
		return true;
	}

	private bool Blocked(XZ p, double clear)
	{
		if (Plan.Origin.DistanceTo(p) < RoadPlan.StartClearance + clear)
		{
			return true;
		}
		foreach (RoadPiece piece in pieces)
		{
			if (RoadPlan.SegmentDistance(p, piece.A, piece.B) < piece.Width * 0.5 + clear)
			{
				return true;
			}
		}
		foreach (Lot lot in lots)
		{
			double dx = p.X - lot.Center.X;
			double dz = p.Z - lot.Center.Z;
			double u = dx * lot.Axis.X + dz * lot.Axis.Z;
			double v = -dx * lot.Axis.Z + dz * lot.Axis.X;
			if (System.Math.Abs(u) < lot.HalfU + clear && System.Math.Abs(v) < lot.HalfV + clear)
			{
				return true;
			}
		}
		return false;
	}

	// the tile's props and the items its spawners put out (taken ones are gone already, and stay taken by id)
	private static Transform ClearProps(GameObject tile)
	{
		Transform props = tile.transform.GetChild(3);
		foreach (ItemSpawnManager spawner in props.GetComponentsInChildren<ItemSpawnManager>(true))
		{
			if (spawner.spawned != null)
			{
				Destroy(spawner.spawned);
			}
		}
		for (int i = props.childCount - 1; i >= 0; i--)
		{
			Destroy(props.GetChild(i).gameObject);
		}
		return props;
	}

	// a spawner by one of the lot's long edges, clear of its building or rubble; ids from the tile and a running index
	private void PlaceItems(Lot lot, int x, int z, ref int index, Transform props)
	{
		float chance = (lot.Kind == LotKind.Landmark) ? itemOnLandmark : (lot.Kind == LotKind.Open) ? itemOnOpen : itemOnBuilding;
		ulong roll = WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 10uL);
		if (kit == null || kit.ItemSpawner == null || Gen2.Unit(roll) >= chance)
		{
			return;
		}
		Vector3 u = new Vector3((float)lot.Axis.X, 0f, (float)lot.Axis.Z);
		Vector3 v = new Vector3(-u.z, 0f, u.x);
		float side = ((roll & 1uL) == 0uL) ? 1f : -1f;
		float along = (float)Gen2.Unit(WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 11uL)) * 2f - 1f;
		Vector3 at = new Vector3((float)lot.Center.X, groundY + 0.1f, (float)lot.Center.Z) + v * (side * ((float)lot.HalfV - 0.5f)) + u * (along * (float)lot.HalfU * 0.5f);
		GameObject spawner = Instantiate(kit.ItemSpawner, at - Offset(), Quaternion.identity, props);
		ulong tileSeed = Gen2.TileSeed(Plan.LayerSeed, x, z);
		foreach (ItemSpawnManager manager in spawner.GetComponentsInChildren<ItemSpawnManager>(true))
		{
			manager.random = new WorldRandom(WorldRandom.Derive(tileSeed, Gen2.StreamLot + 3, (ulong)index));
			manager.itemId = Gen2.ItemId(Plan.Layer, x, z, index);
			index++;
		}
	}

	// on the sidewalk by every other street piece, sides taking turns, a district's share of them
	private void PlaceLamps(Transform props)
	{
		if (kit == null || kit.Lamps == null || kit.Lamps.Length == 0)
		{
			return;
		}
		foreach (RoadPiece piece in pieces)
		{
			if (piece.Kind != RoadKind.Street || piece.Index % 2 != 0)
			{
				continue;
			}
			XZ mid = new XZ((piece.A.X + piece.B.X) * 0.5, (piece.A.Z + piece.B.Z) * 0.5);
			ulong roll = WorldRandom.Derive(Plan.LayerSeed, Gen2.StreamLot + 4, Gen2.Pack((int)System.Math.Round(mid.X * 10.0), (int)System.Math.Round(mid.Z * 10.0)));
			if (Gen2.Unit(roll) >= Plan.Def.Districts[Plan.DistrictAt(mid.X, mid.Z)].Lamps)
			{
				continue;
			}
			Vector3 left = new Vector3((float)-piece.TangentA.Z, 0f, (float)piece.TangentA.X);
			float side = ((piece.Index / 2) % 2 == 0) ? 1f : -1f;
			Vector3 at = new Vector3((float)mid.X, groundY, (float)mid.Z) + left * (side * ((float)RoadPlan.StreetWidth * 0.5f + 1.5f));
			GameObject prefab = kit.Lamps[(int)(roll % (ulong)kit.Lamps.Length)];
			GameObject lamp = Instantiate(prefab, at - Offset(), Quaternion.LookRotation(-left * side) * prefab.transform.rotation, props);
			GraphicsTuning.Adopt(lamp);
		}
	}
}
