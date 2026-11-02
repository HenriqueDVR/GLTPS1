using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// world gen v2: 128m tiles around both girls, nearest first, built in stages a couple of ms a frame, pooled when out of reach.
// out to BuildRadius they're whole (colliders, landmarks, lamps, items); out from there to FarRadius the same tiles are
// only drawn, in thinner fog so the city goes on into the haze, and they're made whole when a girl comes near.
// a tile is its deck with the voids cut out (DeckMesh), its roads (RoadMesh), its lots' buildings and rubble (LotMesh)
// and its props from the WorldKit: small landmarks, street lamps, item spawners.
// everything is in layout coords (world + TotalOffset)
public class TileStreamer : MonoBehaviour
{
	public const float BuildRadius = 700f;

	// drawn only, out to here
	public const float FarRadius = 3000f;

	// how far this layer builds now: less while a ride is on its way to it (LayerTransition), the deck above hides most
	// of it going up and the fog going down, and building all 3 km of it in the ride's few seconds was the boarding hitch
	public float FarReach
	{
		get => farReach;
		set
		{
			if (farReach != value)
			{
				farReach = value;
				nextRefresh = 0f;
				rescan = true;
			}
		}
	}

	private float farReach = FarRadius;

	// a far tile's share of the world's fog far out, eased in from BuildRadius over this: the horizon still goes into
	// the haze, the city before it shows
	private const float farFogShare = 0.5f;

	private const float farFogEase = 900f;

	// the camera this high over the deck (or under it) thins the far tiles' fog to farFogShare by farFogHigh
	private const float farFogLow = 40f;

	private const float farFogHigh = 300f;

	// tiles stay until this much further out, so they don't flicker at the edge
	private const float keepMargin = 128f;

	private const double budgetMs = 2.0;

	private const float refreshInterval = 0.25f;

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

	private readonly DeckMesh deckMesh = new DeckMesh();

	private readonly List<Chasm> chasms = new List<Chasm>();

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

	// in the entry of a building entered from a raised street
	private const float itemInEntry = 0.3f;

	// lamps on raised streets: decks at least this high (not up their ramps), standing on the parapet's top
	// (RoadMesh: the street's lift and the parapet's height) in the middle of its width
	private const double lampDeck = 6.0;

	private const float parapetTop = 0.06f + 0.9f;

	private const float parapetMiddle = 0.175f;

	private readonly List<RoadPiece> pieces = new List<RoadPiece>();

	private readonly List<RoadPatch> patches = new List<RoadPatch>();

	// road pieces by tile, so a wire can reach the next pole in the neighbouring tile (pure data, cleared when big)
	private readonly Dictionary<long, RoadPiece[]> pieceCache = new Dictionary<long, RoadPiece[]>();

	// street and buried-road pieces around the tile by their start, to walk along a road
	private readonly Dictionary<XZ, RoadPiece> chain = new Dictionary<XZ, RoadPiece>();

	private readonly List<RoadPiece> otherPieces = new List<RoadPiece>();

	private readonly List<Viaduct> viaducts = new List<Viaduct>();

	// stairs up to raised streets from the blocks centred in the tile being built
	private readonly List<Stair> stairs = new List<Stair>();

	// the underground's halls and gallery pieces the tile being built owns, and the shaft holes in its deck
	private readonly List<Hall> halls = new List<Hall>();

	private readonly List<GalleryPiece> galleries = new List<GalleryPiece>();

	private readonly List<Area> holes = new List<Area>();

	// the elevator shafts among them, through the deck's underside too
	private readonly List<Area> shafts = new List<Area>();

	private readonly List<RoadPatch> otherPatches = new List<RoadPatch>();

	// made here at runtime, destroyed with the streamer (never the loaded assets)
	private readonly List<Material> made = new List<Material>();

	private readonly Dictionary<long, GameObject> tiles = new Dictionary<long, GameObject>();

	private readonly Stack<GameObject> pool = new Stack<GameObject>();

	private readonly List<long> missing = new List<long>();

	private readonly List<long> leaving = new List<long>();

	private readonly HashSet<long> wanted = new HashSet<long>();

	// within BuildRadius of a girl, and the tiles that are whole
	private readonly HashSet<long> wantedNear = new HashSet<long>();

	private readonly HashSet<long> near = new HashSet<long>();

	private readonly List<long> downgrading = new List<long>();

	// each material's thin-fog twin for far tiles (Resources/FarTile)
	private readonly Dictionary<Material, Material> farMaterials = new Dictionary<Material, Material>();

	private Shader farShader;

	private MaterialPropertyBlock born;

	// far tiles fading in, and when they were built: the block comes off after, a renderer with one isn't SRP batched
	private readonly List<KeyValuePair<GameObject, float>> fading = new List<KeyValuePair<GameObject, float>>();

	private const float fadeTime = 0.8f;

	private readonly List<Vector3> points = new List<Vector3>();

	private readonly Stopwatch stopwatch = new Stopwatch();

	// this frame's build budget, a stage that goes a lot at a time stops when it's spent
	private double frameBudget;

	private Transform chito;

	private Transform yuuri;

	private float nextRefresh;

	// layout y of the deck when the layer's reached by a ride, NaN at the start (from the floor under Chito)
	private float deck = float.NaN;

	// a layer reached by a ride builds while they're still in the elevator, nearest first; what's left by the end goes on
	// at the usual budget (6 ms had the ride at 45-50 fps)
	private const double rideBudgetMs = 3.0;

	// more holes in a tile's deck, besides the underground's shafts: the elevators' shafts (PillarStreamer)
	public System.Action<int, int, List<Area>> MoreHoles;

	// where Chito stood as the scene loaded, on layer 0's floor, before a join or a save moves her
	private Vector3 startAt;

	// everything in reach at once, not over the ride: a layer reached without one (a co-op friend that missed it)
	private bool buildAll;

	public void Init(LayerPlan plan, FloatingOrigin origin, float deck = float.NaN, int groundLayer = 0, bool buildAll = false)
	{
		Plan = plan;
		floatingOrigin = origin;
		this.deck = deck;
		this.groundLayer = groundLayer;
		this.buildAll = buildAll;
		if (!float.IsNaN(deck))
		{
			groundY = deck;
		}
		startAt = (Girls.Chito != null) ? Girls.Chito.transform.position : transform.position;
	}

	public int GroundLayer => groundLayer;

	private void Start()
	{
		// no girl registry yet (WP-ARCH-2), same lookup as CoopPlayers
		chito = Girl("ChitoParent");
		yuuri = Girl("YuuriParent");
		Vector3 start = startAt;
		// a co-op friend joining on a layer over the first: its deck is that much over the start's floor
		float rise = (float)LayerStack.Rise(Plan.Layer);
		groundY = float.IsNaN(deck) ? start.y + Offset().y + rise : deck;
		Material floor = null;
		if (float.IsNaN(deck) && Physics.Raycast(start + Vector3.up, Vector3.down, out RaycastHit hit, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
		{
			groundY = hit.point.y + Offset().y + rise;
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
		lotMaterials = new[] { (wall != null) ? wall : floor, roadMaterials[2], LotMaterial("Dark", new Color(0.11f, 0.11f, 0.12f), lit, floor), LotMaterial("Metal", new Color(0.5f, 0.45f, 0.4f), lit, floor), material };
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
		// everything in reach before the first frame, like the legacy BuildAround. not mid-ride
		Refresh();
		if (float.IsNaN(deck) || buildAll)
		{
			BuildMissing(double.MaxValue, true);
		}
	}

	private void OnDestroy()
	{
		if (job != null && job.Stage == baking)
		{
			job.Bake.Complete();
			job.Meshes.Dispose();
		}
		// a layer left by the elevator: its tiles go with it
		if (job != null && job.Tile != null && !tiles.ContainsValue(job.Tile))
		{
			Scrap(job.Tile);
		}
		foreach (GameObject tile in tiles.Values)
		{
			Scrap(tile);
		}
		foreach (GameObject tile in pool)
		{
			Scrap(tile);
		}
		tiles.Clear();
		pool.Clear();
		foreach (Material m in made)
		{
			Destroy(m);
		}
	}

	// out of sight now, destroyed a few a frame (GameSession), all at once froze the frame for half a second
	private static void Scrap(GameObject tile)
	{
		if (tile == null)
		{
			return;
		}
		tile.SetActive(false);
		if (GameSession.Current != null)
		{
			GameSession.Current.Later(() => DestroyTile(tile));
		}
		else
		{
			DestroyTile(tile);
		}
	}

	private static void DestroyTile(GameObject tile)
	{
		if (tile == null)
		{
			return;
		}
		ClearProps(tile);
		foreach (MeshFilter filter in tile.GetComponentsInChildren<MeshFilter>(true))
		{
			// ~1.7 ms each (the gpu's side). now, or the time budget in Later doesn't see it
			Mesh mesh = filter.sharedMesh;
			if (mesh != null && GameSession.Current != null)
			{
				GameSession.Current.Later(() => DestroyImmediate(mesh));
			}
			else if (mesh != null)
			{
				Destroy(mesh);
			}
		}
		if (GameSession.Current != null)
		{
			DestroyImmediate(tile);
		}
		else
		{
			Destroy(tile);
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
			if (Moved())
			{
				Refresh();
			}
		}
		BuildMissing(LayerTransition.Busy ? rideBudgetMs : budgetMs, false);
		Faded();
		FarFog();
		Underneath();
	}

	// the camera under the deck's underside (riding up to it, or down from it) sees only that: the roads and the
	// buildings aren't drawn, a ride drew two layers whole
	private bool under;

	private void Underneath()
	{
		Camera camera = Camera.main;
		bool now = camera != null && camera.transform.position.y + Offset().y < groundY - DeckMesh.UndersideDepth - 1f;
		if (now == under)
		{
			return;
		}
		under = now;
		foreach (GameObject tile in tiles.Values)
		{
			Above(tile);
		}
	}

	private void Above(GameObject tile)
	{
		tile.transform.GetChild(1).GetComponent<MeshRenderer>().enabled = !under;
		tile.transform.GetChild(2).GetComponent<MeshRenderer>().enabled = !under;
	}

	private void Faded()
	{
		float now = Time.timeSinceLevelLoad;
		int kept = 0;
		for (int i = 0; i < fading.Count; i++)
		{
			if (now - fading[i].Value < fadeTime + 0.1f)
			{
				fading[kept++] = fading[i];
				continue;
			}
			// pooled or made whole since, it's had its block taken off already
			GameObject tile = fading[i].Key;
			if (tile != null)
			{
				for (int k = 0; k < 3; k++)
				{
					tile.transform.GetChild(k).GetComponent<MeshRenderer>().SetPropertyBlock(null);
				}
			}
		}
		fading.RemoveRange(kept, fading.Count - kept);
	}

	// the far tiles' fog, the same for every far material, set by the layer the girls are on. on the ground they take
	// all the fog like the whole tiles (they showed as white shapes past it); it thins only as the camera rises over
	// the deck, a ride's view going out over the city
	private void FarFog()
	{
		if (GameSession.Get<TileStreamer>() != this)
		{
			return;
		}
		Camera camera = Camera.main;
		float height = (camera != null) ? Mathf.Abs(camera.transform.position.y + Offset().y - groundY) : 0f;
		float share = Mathf.Lerp(1f, farFogShare, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(farFogLow, farFogHigh, height)));
		Vector3 offset = Offset();
		Vector3 a = (points.Count > 0) ? points[0] - offset : Vector3.zero;
		Vector3 b = (points.Count > 1) ? points[1] - offset : a;
		Shader.SetGlobalVector("_FarHide", new Vector4(a.x, a.z, b.x, b.z));
		Shader.SetGlobalFloat("_FarNear", BuildRadius);
		Shader.SetGlobalFloat("_FarEase", farFogEase);
		Shader.SetGlobalFloat("_FarShare", share);
		Shader.SetGlobalFloat("_FarEnd", FarRadius);
		// linear like URP's own fog colour and the sky dome's, as a vector so nothing converts it again: fogged out, a far
		// tile is exactly the haze behind it (it came out whiter, a pale skyline over the horizon)
		Shader.SetGlobalVector("_FarFogColor", RenderSettings.fogColor.linear);
		Shader.SetGlobalFloat("_FarDensity", RenderSettings.fog ? RenderSettings.fogDensity : 0f);
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

	// what's wanted only changes with the girls, a scan is thousands of cells
	private bool rescan;

	private bool Moved()
	{
		if (rescan)
		{
			return true;
		}
		Vector3 offset = Offset();
		int at = 0;
		return Moved(chito, offset, ref at) || Moved(yuuri, offset, ref at) || at != points.Count;
	}

	private bool Moved(Transform girl, Vector3 offset, ref int at)
	{
		if (girl == null || !girl.gameObject.activeInHierarchy)
		{
			return false;
		}
		at++;
		return at > points.Count || (girl.position + offset - points[at - 1]).sqrMagnitude >= 1f;
	}

	private void Refresh()
	{
		rescan = false;
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
		wantedNear.Clear();
		float size = (float)Gen2.TileSize;
		foreach (Vector3 p in points)
		{
			int x0 = Mathf.FloorToInt((p.x - farReach) / size);
			int x1 = Mathf.FloorToInt((p.x + farReach) / size);
			int z0 = Mathf.FloorToInt((p.z - farReach) / size);
			int z1 = Mathf.FloorToInt((p.z + farReach) / size);
			for (int x = x0; x <= x1; x++)
			{
				for (int z = z0; z <= z1; z++)
				{
					float d = TileDistance(x, z, p);
					if (d <= farReach)
					{
						wanted.Add(Key(x, z));
						if (d <= BuildRadius)
						{
							wantedNear.Add(Key(x, z));
						}
					}
				}
			}
		}
		leaving.Clear();
		downgrading.Clear();
		foreach (KeyValuePair<long, GameObject> tile in tiles)
		{
			float d = NearestDistance(KeyX(tile.Key), KeyZ(tile.Key));
			if (!wanted.Contains(tile.Key) && d > farReach + keepMargin)
			{
				leaving.Add(tile.Key);
			}
			else if (near.Contains(tile.Key) && !wantedNear.Contains(tile.Key) && d > BuildRadius + keepMargin)
			{
				downgrading.Add(tile.Key);
			}
		}
		foreach (long key in leaving)
		{
			GameObject tile = tiles[key];
			tiles.Remove(key);
			near.Remove(key);
			ClearProps(tile);
			tile.SetActive(false);
			pool.Push(tile);
		}
		foreach (long key in downgrading)
		{
			near.Remove(key);
			Far(tiles[key]);
		}
		missing.Clear();
		foreach (long key in wanted)
		{
			// not built, or built far and now wanted whole
			if (!tiles.ContainsKey(key) || (wantedNear.Contains(key) && !near.Contains(key)))
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

	// tiles go one at a time, a stage or more a frame while the budget lasts. budget MaxValue (the first frame, a debug
	// teleport) finishes everything now
	// nearOnly: just the whole tiles (the first frame), the far ones come after
	private void BuildMissing(double budget, bool nearOnly = true)
	{
		stopwatch.Restart();
		frameBudget = budget;
		bool now = budget == double.MaxValue;
		while (stopwatch.Elapsed.TotalMilliseconds < budget)
		{
			if (job == null)
			{
				if (missing.Count == 0)
				{
					return;
				}
				long key = missing[missing.Count - 1];
				bool whole = wantedNear.Contains(key);
				if (nearOnly && !whole)
				{
					return;
				}
				missing.RemoveAt(missing.Count - 1);
				if (tiles.ContainsKey(key))
				{
					// a far tile come near is built again, whole (a far one keeps no cpu copy of its meshes to cook colliders
					// from); it stays showing meanwhile
					if (near.Contains(key) || !whole)
					{
						continue;
					}
					job = new Job { Key = key, X = KeyX(key), Z = KeyZ(key), Tile = tiles[key], Upgrade = true, Near = true };
				}
				else
				{
					job = new Job { Key = key, X = KeyX(key), Z = KeyZ(key), Near = whole };
				}
			}
			if (job.Stage == baking && !now && !job.Bake.IsCompleted)
			{
				return;
			}
#if DUMP_WORLDGEN
			buildWatch.Restart();
			int stage = job.Stage;
#endif
			bool done = Step(job);
#if DUMP_WORLDGEN
			double took = buildWatch.Elapsed.TotalMilliseconds;
			slowestBuild = System.Math.Max(slowestBuild, took);
			if (took > 4.0)
			{
				UnityEngine.Debug.Log("TileStreamer: slow stage " + stage + " " + took.ToString("F1") + "ms tile " + job.X + "," + job.Z + " district " + Plan.Def.Districts[Plan.DistrictAt((job.X + 0.5) * Gen2.TileSize, (job.Z + 0.5) * Gen2.TileSize)].Id + " lot tris " + lotMesh.Triangles);
			}
#endif
			if (done)
			{
				tiles[job.Key] = job.Tile;
				if (job.Near)
				{
					near.Add(job.Key);
				}
				job = null;
			}
		}
	}

	// a tile being built over several frames, so no frame pays for a whole tile (dense districts took up to ~37 ms).
	// it stays switched off until it's done; its mesh colliders are cooked on worker threads meanwhile
	private sealed class Job
	{
		public long Key;

		public int X;

		public int Z;

		public GameObject Tile;

		public int Stage;

		public bool AnyDeck;

		public bool AnyRoads;

		public bool AnyLots;

		// made whole (colliders, props), else only drawn; a far tile being built again whole, in place
		public bool Near;

		public bool Upgrade;

		// lots added to the mesh so far
		public int LotAt;

		public bool Laned;

		public NativeArray<int> Meshes;

		public JobHandle Bake;
	}

	private struct BakeColliders : IJobParallelFor
	{
		[ReadOnly]
		public NativeArray<int> Meshes;

		public void Execute(int i)
		{
			Physics.BakeMesh(Meshes[i], false, Cooking);
		}
	}

	private Job job;

	private const int baking = 7;

	// a far tile's last: its meshes packed small
	private const int packing = 20;

	// 16 bytes a vertex for far tiles, 32 as built: half positions and uvs (6 cm at 128 m, under a pixel that far), a
	// byte normal. the far tiles were ~1.1 GB of a layer's gpu memory
	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct Packed
	{
		public ushort X, Y, Z, W;

		public sbyte NX, NY, NZ, NW;

		public ushort U, V;
	}

	private static readonly UnityEngine.Rendering.VertexAttributeDescriptor[] packedLayout =
	{
		new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttributeFormat.Float16, 4),
		new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Normal, UnityEngine.Rendering.VertexAttributeFormat.SNorm8, 4),
		new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.TexCoord0, UnityEngine.Rendering.VertexAttributeFormat.Float16, 2)
	};

	private static readonly List<Vector3> packPositions = new List<Vector3>();

	private static readonly List<Vector3> packNormals = new List<Vector3>();

	private static readonly List<Vector2> packUvs = new List<Vector2>();

	private static void Pack(GameObject tile)
	{
		for (int part = 0; part < 3; part++)
		{
			Mesh mesh = tile.transform.GetChild(part).GetComponent<MeshFilter>().sharedMesh;
			if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0)
			{
				continue;
			}
			int count = mesh.vertexCount;
			mesh.GetVertices(packPositions);
			mesh.GetNormals(packNormals);
			mesh.GetUVs(0, packUvs);
			bool uvs = packUvs.Count == count;
			NativeArray<Packed> packed = new NativeArray<Packed>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			for (int i = 0; i < count; i++)
			{
				Vector3 p = packPositions[i];
				Vector3 n = packNormals[i];
				Vector2 uv = uvs ? packUvs[i] : Vector2.zero;
				packed[i] = new Packed
				{
					X = Mathf.FloatToHalf(p.x),
					Y = Mathf.FloatToHalf(p.y),
					Z = Mathf.FloatToHalf(p.z),
					W = Mathf.FloatToHalf(1f),
					NX = (sbyte)Mathf.RoundToInt(n.x * 127f),
					NY = (sbyte)Mathf.RoundToInt(n.y * 127f),
					NZ = (sbyte)Mathf.RoundToInt(n.z * 127f),
					U = Mathf.FloatToHalf(uv.x),
					V = Mathf.FloatToHalf(uv.y)
				};
			}
			Bounds bounds = mesh.bounds;
			const UnityEngine.Rendering.MeshUpdateFlags quiet = UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices;
			mesh.SetVertexBufferParams(count, packedLayout);
			mesh.SetVertexBufferData(packed, 0, 0, count, 0, quiet);
			mesh.bounds = bounds;
			packed.Dispose();
		}
	}

	// the colliders are given the same, or they'd cook the mesh again on the main thread
	private const MeshColliderCookingOptions Cooking = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;

	// one stage of a tile, a pure function of (plan, x, z) all told, so co-op peers get the same tile. true when done
	private bool Step(Job job)
	{
		int x = job.X;
		int z = job.Z;
		GameObject tile = job.Tile;
		switch (job.Stage++)
		{
			case 0:
			{
				if (!job.Upgrade)
				{
					tile = job.Tile = (pool.Count > 0) ? pool.Pop() : NewTile();
					tile.SetActive(false);
				}
				Writable(tile, 0, !job.Near);
				tile.name = "tile " + x + "," + z;
				float size = (float)Gen2.TileSize;
				tile.transform.SetPositionAndRotation(new Vector3(x * size, groundY, z * size) - Offset(), Quaternion.identity);
				ClearProps(tile);
				Plan.Underground.Holes(x, z, holes);
				shafts.Clear();
				MoreHoles?.Invoke(x, z, shafts);
				holes.AddRange(shafts);
				deckMesh.Build(tile.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh, Plan.Chasms, x, z, holes, shafts);
				return false;
			}
			case 1:
				Plan.Roads.Collect(x, z, pieces, patches);
				roadMesh.Voids = Plan.Chasms;
				Writable(tile, 1, !job.Near);
				job.AnyRoads = roadMesh.Build(tile.transform.GetChild(1).GetComponent<MeshFilter>().sharedMesh, pieces, patches, x * Gen2.TileSize, z * Gen2.TileSize);
				return false;
			case 2:
				Plan.Lots.Collect(x, z, lots, stairs);
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
				return false;
			case 3:
			{
				// the lanes on their own, then a lot at a time while the frame's budget lasts (half a dense tile's lots
				// took 4-10 ms)
				if (!job.Laned)
				{
					lotMesh.Far = !job.Near;
					lotMesh.Begin();
					Lanes(x, z);
					job.Laned = true;
					job.Stage = 3;
					return false;
				}
				do
				{
					int to = Mathf.Min(job.LotAt + 1, lots.Count);
					lotMesh.AddLots(lots, job.LotAt, to, Plan.Def.Districts, Plan.Def.Ruin, x * Gen2.TileSize, z * Gen2.TileSize);
					job.LotAt = to;
				}
				while (job.LotAt < lots.Count && stopwatch.Elapsed.TotalMilliseconds < frameBudget);
				job.Stage = (job.LotAt < lots.Count) ? 3 : 5;
				return false;
			}
			case 5:
				Scatter(x, z);
				// too fine to see far out, and the underground's under the deck
				if (job.Near)
				{
					PlaceCables(x, z);
					Drifts(x, z);
				}
				PlaceViaducts(x, z);
				PlaceRaised(x, z);
				DressVoids(x, z);
				if (job.Near)
				{
					PlaceUnderground(x, z);
				}
				foreach (Stair stair in stairs)
				{
					Vector3 corner = new Vector3((float)(x * Gen2.TileSize), 0f, (float)(z * Gen2.TileSize));
					lotMesh.AddStair(Local(stair.Foot, corner), new Vector3((float)stair.Dir.X, 0f, (float)stair.Dir.Z), new Vector3((float)stair.Out.X, 0f, (float)stair.Out.Z), (float)stair.Deck);
				}
				Writable(tile, 2, !job.Near);
				job.AnyLots = lotMesh.End(tile.transform.GetChild(2).GetComponent<MeshFilter>().sharedMesh);
				if (!job.Near)
				{
					job.Stage = packing;
				}
				return false;
			case 6:
			{
				// cooked off the main thread; the colliders pick the cooked data up when they get the mesh
				// a tile all in a collapsed section has no deck at all
				Mesh deck = tile.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
				job.AnyDeck = deck.vertexCount > 0;
				List<int> ids = new List<int>();
				if (job.AnyDeck)
				{
					ids.Add(deck.GetInstanceID());
				}
				if (job.AnyRoads)
				{
					ids.Add(tile.transform.GetChild(1).GetComponent<MeshFilter>().sharedMesh.GetInstanceID());
				}
				if (job.AnyLots)
				{
					ids.Add(tile.transform.GetChild(2).GetComponent<MeshFilter>().sharedMesh.GetInstanceID());
				}
				job.Meshes = new NativeArray<int>(ids.ToArray(), Allocator.Persistent);
				job.Bake = new BakeColliders { Meshes = job.Meshes }.Schedule(job.Meshes.Length, 1);
				JobHandle.ScheduleBatchedJobs();
				return false;
			}
			case packing:
				Pack(tile);
				Far(tile, true);
				Drop(tile);
				return true;
			case baking:
			{
				job.Bake.Complete();
				job.Meshes.Dispose();
				// active before the colliders get their meshes: a collider in an inactive pooled tile drops it
				tile.SetActive(true);
				Look(tile, false);
				Collide(tile.transform.GetChild(0).gameObject, job.AnyDeck);
				Collide(tile.transform.GetChild(1).gameObject, job.AnyRoads);
				Collide(tile.transform.GetChild(2).gameObject, job.AnyLots);
				return false;
			}
			default:
			{
				Transform props = tile.transform.GetChild(3);
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
				PlaceUnderLamps(x, z, props);
#if DUMP_WORLDGEN
				builtTiles++;
				roadTriangles += roadMesh.Triangles;
				lotTriangles += job.AnyLots ? lotMesh.Triangles : 0;
#endif
				return true;
			}
		}
	}

	// drawn only: showing, nothing to stand on, no props, the thin-fog materials. just built, it fades in
	private void Far(GameObject tile, bool fresh = false)
	{
		ClearProps(tile);
		tile.SetActive(true);
		for (int i = 0; i < 3; i++)
		{
			GameObject part = tile.transform.GetChild(i).gameObject;
			Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
			part.SetActive(mesh != null && mesh.vertexCount > 0);
			MeshCollider collider = part.GetComponent<MeshCollider>();
			collider.sharedMesh = null;
			collider.enabled = false;
		}
		Look(tile, true);
		if (!fresh)
		{
			return;
		}
		if (born == null)
		{
			born = new MaterialPropertyBlock();
		}
		born.SetFloat("_Born", Time.timeSinceLevelLoad);
		for (int i = 0; i < 3; i++)
		{
			tile.transform.GetChild(i).GetComponent<MeshRenderer>().SetPropertyBlock(born);
		}
		fading.Add(new KeyValuePair<GameObject, float>(tile, Time.timeSinceLevelLoad));
	}

	// a far tile's meshes go to the gpu alone and can't be written again: a new one, swapped in the step that fills it,
	// so a tile built again whole in place never shows half done. a far one gets a plain static mesh, a dynamic one is
	// kept in system memory too
	private static void Writable(GameObject tile, int part, bool far)
	{
		MeshFilter filter = tile.transform.GetChild(part).GetComponent<MeshFilter>();
		Mesh old = filter.sharedMesh;
		if (old != null && old.isReadable && !far)
		{
			return;
		}
		filter.sharedMesh = new Mesh { name = (old != null) ? old.name : "tile" };
		if (!far)
		{
			filter.sharedMesh.MarkDynamic();
		}
		// a frame stalls on it, so it waits its turn with the others
		if (old != null && GameSession.Current != null)
		{
			GameSession.Current.Later(() => DestroyImmediate(old));
		}
		else if (old != null)
		{
			Destroy(old);
		}
	}

	// built far: its meshes to the gpu and no copy kept, there's a couple of thousand of them
	private static void Drop(GameObject tile)
	{
		for (int i = 0; i < 3; i++)
		{
			Mesh mesh = tile.transform.GetChild(i).GetComponent<MeshFilter>().sharedMesh;
			if (mesh != null && mesh.isReadable)
			{
				mesh.UploadMeshData(true);
			}
		}
	}


	// near: the real materials and shadows; far: their thin-fog twins, no shadows
	private void Look(GameObject tile, bool far)
	{
		for (int i = 0; i < 3; i++)
		{
			MeshRenderer renderer = tile.transform.GetChild(i).GetComponent<MeshRenderer>();
			Material[] near = (i == 0) ? new[] { material, roadMaterials[2], lotMaterials[2] } : (i == 1) ? roadMaterials : lotMaterials;
			if (far)
			{
				Material[] twins = new Material[near.Length];
				for (int k = 0; k < near.Length; k++)
				{
					twins[k] = FarOf(near[k]);
				}
				renderer.sharedMaterials = twins;
			}
			else
			{
				renderer.sharedMaterials = near;
				renderer.SetPropertyBlock(null);
			}
			renderer.shadowCastingMode = far ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
		}
		Above(tile);
	}

	private Material FarOf(Material source)
	{
		if (source == null)
		{
			return null;
		}
		if (farMaterials.TryGetValue(source, out Material twin))
		{
			return twin;
		}
		if (farShader == null)
		{
			farShader = Resources.Load<Shader>("FarTile");
		}
		twin = Made(new Material(farShader));
		Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
		twin.SetTexture("_BaseMap", texture);
		if (source.HasProperty("_BaseMap"))
		{
			twin.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
			twin.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
		}
		twin.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color);
		twin.SetColor("_Average", DistantPillar.Albedo(source));
		farMaterials.Add(source, twin);
		return twin;
	}

	// set again so the collider takes the new shape
	private static void Collide(GameObject part, bool any)
	{
		part.SetActive(any);
		MeshCollider collider = part.GetComponent<MeshCollider>();
		collider.enabled = true;
		collider.cookingOptions = Cooking;
		collider.sharedMesh = null;
		if (any)
		{
			collider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
		}
	}

	// root at the tile's corner on the deck: child 0 the deck, child 1 the roads, child 2 the lots, child 3 the props
	private GameObject NewTile()
	{
		GameObject tile = new GameObject();
		tile.layer = groundLayer;
		GameObject ground = new GameObject("ground", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
		ground.layer = groundLayer;
		ground.transform.SetParent(tile.transform, false);
		// a hair under the roads' shoulders
		ground.transform.localPosition = Vector3.down * 0.02f;
		Mesh groundMesh = new Mesh { name = "deck" };
		groundMesh.MarkDynamic();
		ground.GetComponent<MeshFilter>().sharedMesh = groundMesh;
		ground.GetComponent<MeshRenderer>().sharedMaterials = new[] { material, roadMaterials[2], lotMaterials[2] };
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

	// gentle snow drifts on open ground, whole inside their tile, clear of what Blocked keeps things off and of towers.
	// their own stream, the rest of the tile's the same with or without them. near only: snow on snow, nothing far out
	private void Drifts(int x, int z)
	{
		double size = Gen2.TileSize;
		if (Plan.Def.Districts[Plan.DistrictAt((x + 0.5) * size, (z + 0.5) * size)].Roads != DistrictRoads.Open)
		{
			return;
		}
		WorldRandom random = new WorldRandom(WorldRandom.Derive(Gen2.TileSeed(Plan.LayerSeed, x, z), Gen2.StreamLot + 8, 0uL));
		int count = 2 + random.Next(4);
		for (int i = 0; i < count; i++)
		{
			double a = 8.0 + Gen2.Unit(random.NextULong()) * 16.0;
			double b = a * (0.45 + Gen2.Unit(random.NextULong()) * 0.4);
			double height = 0.2 + Gen2.Unit(random.NextULong()) * 0.45;
			double turn = Gen2.Unit(random.NextULong()) * System.Math.PI;
			// its most with the wobble (LotMesh.AddDrift)
			double reach = a * 1.25;
			XZ at = new XZ((x + Gen2.Unit(random.NextULong())) * size, (z + Gen2.Unit(random.NextULong())) * size);
			ulong seed = random.NextULong();
			double u = at.X - x * size;
			double v = at.Z - z * size;
			if (u < reach || v < reach || u > size - reach || v > size - reach || Blocked(at, reach + 2.0) || (Plan.TowerNear(at.X, at.Z, out XZ tower) && tower.DistanceTo(at) < RoadPlan.PillarClearance + reach))
			{
				continue;
			}
			lotMesh.AddDrift(new Vector3((float)u, 0f, (float)v), (float)a, (float)b, (float)height, (float)turn, seed);
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
	// the underground's halls (with their ways in) and gallery pieces this tile owns
	private void PlaceUnderground(int x, int z)
	{
		Plan.Underground.Collect(x, z, halls, galleries);
		Vector3 corner = new Vector3((float)(x * Gen2.TileSize), 0f, (float)(z * Gen2.TileSize));
		float[] gaps = new float[4];
		foreach (Hall hall in halls)
		{
			double[] at = { hall.GalleryPX - hall.Center.Z, -(hall.GalleryNX - hall.Center.Z), -(hall.GalleryPZ - hall.Center.X), hall.GalleryNZ - hall.Center.X };
			int door = -1;
			if (hall.Entrance)
			{
				door = (hall.Out.X > 0.5) ? 0 : (hall.Out.X < -0.5) ? 1 : (hall.Out.Z > 0.5) ? 2 : 3;
				at[door] = hall.EntranceAt;
				Hall way = hall;
				lotMesh.AddWayIn((s, across) => Local(way.PassagePoint(s, across), corner), new Vector3((float)hall.Out.X, 0f, (float)hall.Out.Z));
			}
			for (int side = 0; side < 4; side++)
			{
				gaps[side] = (float)at[side];
			}
			lotMesh.AddHall(Local(hall.Center, corner), (float)hall.HalfX, (float)hall.HalfZ, gaps, hall.Seed, FanSide(hall, gaps));
		}
		foreach (GalleryPiece piece in galleries)
		{
			lotMesh.AddGallery(Local(piece.A, corner), Local(piece.B, corner), WorldRandom.Derive(Plan.LayerSeed, Gen2.StreamUnder + 3, Gen2.Pack((int)System.Math.Round(piece.A.X), (int)System.Math.Round(piece.A.Z))));
		}
		Fixtures(x, z, fixtures);
		foreach (Fixture fixture in fixtures)
		{
			lotMesh.AddFixture(Local(fixture.At, corner) + Vector3.up * fixture.Height, new Vector3((float)fixture.Facing.X, 0f, (float)fixture.Facing.Z));
		}
		// the start tunnel's chamber and its gallery to the hall, with the tile the chamber's in
		if (StartChamberIn(x, z, out StartWay start))
		{
			lotMesh.AddChamber(Local(start.Chamber, corner), new Vector3((float)-start.Dir.X, 0f, (float)-start.Dir.Z), new Vector3((float)start.Out.X, 0f, (float)start.Out.Z));
			lotMesh.AddGallery(Local(start.GalleryA, corner), Local(start.GalleryB, corner), start.Hall.Seed);
		}
	}

	private bool StartChamberIn(int x, int z, out StartWay start)
	{
		return Plan.Underground.Start(out start) && Gen2.Cell(start.Chamber.X, Gen2.TileSize) == x && Gen2.Cell(start.Chamber.Z, Gen2.TileSize) == z;
	}

	// a fan in one of the hall's end walls with no mouth in it, some halls
	private static Vector3 FanSide(Hall hall, float[] gaps)
	{
		ulong roll = WorldRandom.Derive(hall.Seed, Gen2.StreamUnder, 3uL);
		if (Gen2.Unit(roll) >= 0.5)
		{
			return Vector3.zero;
		}
		// the ends: across the hall's long way
		int first = (hall.HalfX >= hall.HalfZ) ? 0 : 2;
		for (int n = 0; n < 2; n++)
		{
			int side = first + (int)(((roll >> 8) + (ulong)n) % 2uL);
			if (float.IsNaN(gaps[side]))
			{
				return (side == 0) ? Vector3.right : (side == 1) ? Vector3.left : (side == 2) ? Vector3.forward : Vector3.back;
			}
		}
		return Vector3.zero;
	}

	// a light fitting on an underground wall: where (layout), how high over the deck, which way it faces (out of the wall)
	private struct Fixture
	{
		public XZ At;

		public float Height;

		public XZ Facing;
	}

	private readonly List<Fixture> fixtures = new List<Fixture>();

	// the tile's light fittings down there: a few on each hall's walls above the ledge (not the fan's), one on every
	// other stretch of gallery, at the foot and the top of each way in, one in the start's chamber. a pure function of
	// the tile, so the housings (lot mesh) and the lights (props) agree
	private void Fixtures(int x, int z, List<Fixture> into)
	{
		into.Clear();
		Plan.Underground.Collect(x, z, halls, galleries);
		float level = -(float)UndergroundPlan.Level;
		float wallTop = -(float)UndergroundPlan.Slab;
		float[] gaps = new float[4];
		foreach (Hall hall in halls)
		{
			WorldRandom random = new WorldRandom(WorldRandom.Derive(hall.Seed, Gen2.StreamUnder, 2uL));
			double[] at = { hall.GalleryPX - hall.Center.Z, -(hall.GalleryNX - hall.Center.Z), -(hall.GalleryPZ - hall.Center.X), hall.GalleryNZ - hall.Center.X };
			if (hall.Entrance)
			{
				at[(hall.Out.X > 0.5) ? 0 : (hall.Out.X < -0.5) ? 1 : (hall.Out.Z > 0.5) ? 2 : 3] = hall.EntranceAt;
			}
			for (int side = 0; side < 4; side++)
			{
				gaps[side] = (float)at[side];
			}
			Vector3 fan = FanSide(hall, gaps);
			for (int side = 0; side < 4; side++)
			{
				XZ out_ = (side == 0) ? new XZ(1.0, 0.0) : (side == 1) ? new XZ(-1.0, 0.0) : (side == 2) ? new XZ(0.0, 1.0) : new XZ(0.0, -1.0);
				if (fan.x * (float)out_.X + fan.z * (float)out_.Z > 0.5f)
				{
					continue;
				}
				double halfAlong = (side < 2) ? hall.HalfZ : hall.HalfX;
				double halfOut = (side < 2) ? hall.HalfX : hall.HalfZ;
				// every 36 m or so along the wall, clear of a mouth in it
				int count = System.Math.Max(1, (int)(halfAlong * 2.0 / 36.0));
				double shift = (Gen2.Unit(random.NextULong()) - 0.5) * 6.0;
				for (int i = 0; i < count; i++)
				{
					double along = -halfAlong + halfAlong * 2.0 * (i + 0.5) / count + shift;
					if (!double.IsNaN(at[side]) && System.Math.Abs(along - at[side]) < UndergroundPlan.GalleryWidth)
					{
						continue;
					}
					XZ point = new XZ(hall.Center.X + out_.X * (halfOut - 0.1) - out_.Z * along, hall.Center.Z + out_.Z * (halfOut - 0.1) + out_.X * along);
					into.Add(new Fixture { At = point, Height = level + 4.5f, Facing = new XZ(-out_.X, -out_.Z) });
				}
			}
			if (hall.Entrance)
			{
				double half = UndergroundPlan.GalleryWidth * 0.5 - 0.1;
				XZ back = new XZ(-hall.Out.X, -hall.Out.Z);
				XZ left = new XZ(-hall.Out.Z, hall.Out.X);
				into.Add(new Fixture { At = hall.PassagePoint(UndergroundPlan.Flat * 0.5, half), Height = level + 4.5f, Facing = new XZ(-left.X, -left.Z) });
				double s = UndergroundPlan.PassageLength - 6.0;
				into.Add(new Fixture { At = hall.PassagePoint(s, -half), Height = (float)UndergroundPlan.PassageFloor(s) + 4.5f, Facing = left });
			}
		}
		foreach (GalleryPiece piece in galleries)
		{
			ulong roll = WorldRandom.Derive(Plan.LayerSeed, Gen2.StreamUnder + 3, Gen2.Pack((int)System.Math.Round(piece.A.X), (int)System.Math.Round(piece.A.Z)));
			if ((roll >> 8) % 2uL != 0uL)
			{
				continue;
			}
			XZ along = new XZ(piece.B.X - piece.A.X, piece.B.Z - piece.A.Z);
			double length = System.Math.Sqrt(along.X * along.X + along.Z * along.Z);
			XZ side = new XZ(-along.Z / length, along.X / length);
			double s = ((roll & 1uL) == 0uL) ? 1.0 : -1.0;
			double reach = UndergroundPlan.GalleryWidth * 0.5 - 0.1;
			into.Add(new Fixture { At = new XZ((piece.A.X + piece.B.X) * 0.5 + side.X * s * reach, (piece.A.Z + piece.B.Z) * 0.5 + side.Z * s * reach), Height = wallTop - 1.2f, Facing = new XZ(-side.X * s, -side.Z * s) });
		}
		if (StartChamberIn(x, z, out StartWay start))
		{
			XZ left = new XZ(-start.Out.Z, start.Out.X);
			double r = UndergroundPlan.ChamberRadius - 0.1;
			into.Add(new Fixture { At = new XZ(start.Chamber.X + left.X * r, start.Chamber.Z + left.Z * r), Height = level + 4.5f, Facing = new XZ(-left.X, -left.Z) });
		}
	}

	// the lights under the tile's fittings: warm, a wide cone down and out from the wall, a dim glow on the housing
	private void PlaceUnderLamps(int x, int z, Transform props)
	{
		Fixtures(x, z, fixtures);
		foreach (Fixture fixture in fixtures)
		{
			Vector3 facing = new Vector3((float)fixture.Facing.X, 0f, (float)fixture.Facing.Z);
			Vector3 at = new Vector3((float)fixture.At.X, groundY + fixture.Height - 0.45f, (float)fixture.At.Z) + facing * 0.6f;
			GameObject lamp = new GameObject("under light");
			lamp.transform.SetParent(props, false);
			lamp.transform.SetPositionAndRotation(at - Offset(), Quaternion.LookRotation(facing * 0.6f + Vector3.down));
			Light light = lamp.AddComponent<Light>();
			light.type = LightType.Spot;
			light.spotAngle = 110f;
			light.range = 22f;
			light.intensity = 30f;
			light.color = new Color(1f, 0.8f, 0.55f);
			light.shadows = LightShadows.Soft;
			GraphicsTuning.Adopt(lamp);
		}
	}

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
						lotMesh.AddBay(new Vector3((float)pier.X, 0f, (float)pier.Z) - corner, along, (float)viaduct.HeightAt(k * ViaductPlan.Bay), (float)viaduct.HeightAt((k + 1) * ViaductPlan.Bay), arch);
					}
				}
				// up the ramps the deck stands on a wall, not piers
				float height = (float)viaduct.HeightAt(k * ViaductPlan.Bay);
				if (Gen2.Cell(pier.X, size) == x && Gen2.Cell(pier.Z, size) == z && height >= RoadPlan.ArchFrom && PierStands(pier))
				{
					lotMesh.AddPier(new Vector3((float)pier.X, 0f, (float)pier.Z) - corner, along, height);
				}
			}
		}
	}

	// the decks round the tile buildings mustn't reach into: raised street pieces and viaducts, tile-local
	private void Lanes(int x, int z)
	{
		lotMesh.Lanes.Clear();
		Vector3 corner = new Vector3((float)(x * Gen2.TileSize), 0f, (float)(z * Gen2.TileSize));
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				foreach (RoadPiece piece in PiecesOf(x + dx, z + dz))
				{
					if (piece.HeightA > 0.3 || piece.HeightB > 0.3)
					{
						lotMesh.Lanes.Add(new LotMesh.Lane { A = Local(piece.A, corner), B = Local(piece.B, corner), Half = (float)piece.Width * 0.5f, HA = (float)piece.HeightA, HB = (float)piece.HeightB });
					}
				}
			}
		}
		XZ middle = new XZ((x + 0.5) * Gen2.TileSize, (z + 0.5) * Gen2.TileSize);
		Plan.Viaducts.Near(middle.X, middle.Z, viaducts);
		foreach (Viaduct viaduct in viaducts)
		{
			if (RoadPlan.SegmentDistance(middle, viaduct.A, viaduct.B) > Gen2.TileSize * 1.5)
			{
				continue;
			}
			for (double s = 0.0; s < viaduct.Length; s += ViaductPlan.Bay * 2.0)
			{
				double e = System.Math.Min(viaduct.Length, s + ViaductPlan.Bay * 2.0);
				XZ a = viaduct.At(s);
				XZ b = viaduct.At(e);
				if (RoadPlan.SegmentDistance(middle, a, b) < Gen2.TileSize * 1.2)
				{
					lotMesh.Lanes.Add(new LotMesh.Lane { A = Local(a, corner), B = Local(b, corner), Half = (float)ViaductPlan.Width * 0.5f, HA = (float)viaduct.HeightAt(s), HB = (float)viaduct.HeightAt(e) });
				}
			}
		}
	}

	// arches, piers, girders and walls under the tile's raised street pieces. over a void it's a girder, nothing stands there
	private void PlaceRaised(int x, int z)
	{
		Vector3 corner = new Vector3((float)(x * Gen2.TileSize), 0f, (float)(z * Gen2.TileSize));
		foreach (RoadPiece piece in pieces)
		{
			if (piece.Arch == RoadPlan.ArchNone)
			{
				continue;
			}
			Vector3 a = Local(piece.A, corner);
			Vector3 b = Local(piece.B, corner);
			Vector3 along = (b - a).normalized;
			float half = (float)piece.Width * 0.5f;
			int mode = piece.Arch;
			if (mode != RoadPlan.ArchGirder && (Plan.Chasms.Inside(piece.A.X, piece.A.Z) > -2.0 || Plan.Chasms.Inside(piece.B.X, piece.B.Z) > -2.0))
			{
				mode = RoadPlan.ArchGirder;
			}
			lotMesh.AddStreetUnder(a, b, (float)piece.HeightA, (float)piece.HeightB, half, mode);
			if (mode == RoadPlan.ArchPier || mode == RoadPlan.ArchPiers)
			{
				lotMesh.AddStreetPier(a, along, (float)piece.HeightA, half);
			}
			if (mode == RoadPlan.ArchPiers)
			{
				lotMesh.AddStreetPier(b, along, (float)piece.HeightB, half);
			}
		}
	}

	// what stands in and over the voids near this tile, each owned by the tile its middle is in: blocks on stilts
	// rising out of the dark, footbridges across the rifts. from the void's seed, so peers agree
	private void DressVoids(int x, int z)
	{
		double size = Gen2.TileSize;
		Plan.Chasms.Near((x + 0.5) * size, (z + 0.5) * size, chasms);
		Vector3 corner = new Vector3((float)(x * size), 0f, (float)(z * size));
		foreach (Chasm chasm in chasms)
		{
			WorldRandom random = new WorldRandom(chasm.Seed);
			if (chasm.Shaft)
			{
				float half = (float)System.Math.Min(chasm.HalfU, chasm.HalfV) * 0.45f;
				if (half >= 7f && Gen2.Unit(random.NextULong()) < 0.6 && Owns(chasm.Center, x, z))
				{
					Vector3 axis = new Vector3((float)chasm.Axis.X, 0f, (float)chasm.Axis.Z);
					lotMesh.AddStiltTower(Local(chasm.Center, corner), axis, Mathf.Min(half, 14f), Range(random, -8f, 40f), Range(random, 25f, 60f), random.NextULong());
				}
				continue;
			}
			double length = chasm.A.DistanceTo(chasm.M) + chasm.M.DistanceTo(chasm.B);
			for (double s = 90.0; s < length - 60.0; s += 180.0)
			{
				XZ at = Along(chasm, s, out XZ dir);
				XZ side = new XZ(-dir.Z, dir.X);
				double roll = Gen2.Unit(random.NextULong());
				double shift = (Gen2.Unit(random.NextULong()) * 2.0 - 1.0) * chasm.Half * 0.25;
				ulong seed = random.NextULong();
				float top = Range(random, -8f, 40f);
				float body = Range(random, 25f, 60f);
				if (chasm.Half >= 14.0 && roll < 0.45)
				{
					XZ centre = new XZ(at.X + side.X * shift, at.Z + side.Z * shift);
					if (Owns(centre, x, z))
					{
						lotMesh.AddStiltTower(Local(centre, corner), new Vector3((float)dir.X, 0f, (float)dir.Z), Mathf.Min((float)chasm.Half * 0.6f, 12f), top, body, seed);
					}
				}
				else if (roll > 0.6 && Owns(at, x, z))
				{
					float reach = (float)(chasm.Half + ChasmPlan.Ragged);
					lotMesh.AddFootbridge(Local(at, corner), new Vector3((float)side.X, 0f, (float)side.Z), reach * 2f + 10f, reach * 2f);
				}
			}
		}
	}

	private static bool Owns(XZ p, int x, int z)
	{
		return Gen2.Cell(p.X, Gen2.TileSize) == x && Gen2.Cell(p.Z, Gen2.TileSize) == z;
	}

	private static Vector3 Local(XZ p, Vector3 corner)
	{
		return new Vector3((float)p.X, 0f, (float)p.Z) - corner;
	}

	private static float Range(WorldRandom random, float a, float b)
	{
		return a + (float)Gen2.Unit(random.NextULong()) * (b - a);
	}

	// s metres along a rift's middle line, and its direction there
	private static XZ Along(Chasm chasm, double s, out XZ dir)
	{
		double first = chasm.A.DistanceTo(chasm.M);
		XZ from = (s < first) ? chasm.A : chasm.M;
		XZ to = (s < first) ? chasm.M : chasm.B;
		double t = (s < first) ? s / first : (s - first) / chasm.M.DistanceTo(chasm.B);
		double length = from.DistanceTo(to);
		dir = new XZ((to.X - from.X) / length, (to.Z - from.Z) / length);
		return new XZ(from.X + (to.X - from.X) * t, from.Z + (to.Z - from.Z) * t);
	}

	private bool PierStands(XZ p)
	{
		if (Plan.Chasms.Inside(p.X, p.Z) > -ViaductPlan.PierWidth)
		{
			return false;
		}
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
		if (piece.Kind == RoadKind.Highway || piece.Index % every != 0 || piece.HeightA > 0.3 || piece.HeightB > 0.3)
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
		float edge = street ? Mathf.Min(2.5f, (float)Plan.Def.Districts[Plan.DistrictAt(mid.X, mid.Z)].Sidewalk * 0.5f) : 3f;
		at = new Vector3((float)mid.X, 0f, (float)mid.Z) + across * (side * ((float)piece.Width * 0.5f + edge));
		tall = street ? 8.5f : 7f;
		return Plan.Chasms.Inside(at.x, at.z) < -1.0 && !Plan.Underground.Blocks(new XZ(at.x, at.z), 1.0);
	}

	private bool Blocked(XZ p, double clear)
	{
		if (Plan.Origin.DistanceTo(p) < RoadPlan.StartClearance + clear || Plan.Chasms.Inside(p.X, p.Z) > -clear || Plan.Underground.Blocks(p, clear))
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
		foreach (Stair stair in stairs)
		{
			XZ top = new XZ(stair.Foot.X + stair.Dir.X * stair.Length, stair.Foot.Z + stair.Dir.Z * stair.Length);
			if (RoadPlan.SegmentDistance(p, stair.Foot, top) < LotPlan.StairWidth * 0.5 + clear)
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
		float chance = (lot.Door != 0) ? itemInEntry : (lot.Kind == LotKind.Landmark) ? itemOnLandmark : (lot.Kind == LotKind.Open) ? itemOnOpen : itemOnBuilding;
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
		if (lot.Door != 0)
		{
			// in the dark of the entry, the building itself stands on its plinth
			at = new Vector3((float)lot.Center.X, groundY + (float)lot.Deck + 0.16f, (float)lot.Center.Z) + v * (lot.Door * ((float)lot.HalfV - (float)LotPlan.PorchDepth * 0.6f)) + u * (float)lot.DoorAt;
		}
		GameObject spawner = Instantiate(kit.ItemSpawner, at - Offset(), Quaternion.identity, props);
		ulong tileSeed = Gen2.TileSeed(Plan.LayerSeed, x, z);
		foreach (ItemSpawnManager manager in spawner.GetComponentsInChildren<ItemSpawnManager>(true))
		{
			manager.random = new WorldRandom(WorldRandom.Derive(tileSeed, Gen2.StreamLot + 3, (ulong)index));
			manager.itemId = Gen2.ItemId(Plan.Layer, x, z, index);
			index++;
		}
	}

	// on the sidewalk by every other street piece, sides taking turns, a district's share of them. on a raised
	// street they stand on the parapet, out of the way, and not by an opening in it
	private void PlaceLamps(Transform props)
	{
		if (kit == null || kit.Lamps == null || kit.Lamps.Length == 0)
		{
			return;
		}
		foreach (RoadPiece piece in pieces)
		{
			bool raised = piece.HeightA > 0.3 || piece.HeightB > 0.3;
			if (piece.Kind != RoadKind.Street || piece.Index % 2 != 0 || (raised && System.Math.Min(piece.HeightA, piece.HeightB) < lampDeck))
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
			if (raised)
			{
				bool open = (side > 0f) ? (piece.LeftTo > piece.LeftFrom || piece.LeftTo2 > piece.LeftFrom2) : (piece.RightTo > piece.RightFrom || piece.RightTo2 > piece.RightFrom2);
				if (open)
				{
					continue;
				}
				float deck = (float)(piece.HeightA + piece.HeightB) * 0.5f + parapetTop;
				Vector3 onParapet = new Vector3((float)mid.X, groundY + deck, (float)mid.Z) + left * (side * ((float)piece.Width * 0.5f - parapetMiddle));
				GameObject high = kit.Lamps[(int)(roll % (ulong)kit.Lamps.Length)];
				GraphicsTuning.Adopt(Instantiate(high, onParapet - Offset(), Quaternion.LookRotation(-left * side) * high.transform.rotation, props));
				continue;
			}
			// on the sidewalk, or against the wall where there's hardly one
			float sidewalk = (float)Plan.Def.Districts[Plan.DistrictAt(mid.X, mid.Z)].Sidewalk;
			Vector3 at = new Vector3((float)mid.X, groundY, (float)mid.Z) + left * (side * ((float)piece.Width * 0.5f + Mathf.Min(1.5f, sidewalk * 0.5f)));
			if (Plan.Chasms.Inside(at.x, at.z) > -1.0 || Plan.Underground.Blocks(new XZ(at.x, at.z), 1.0))
			{
				continue;
			}
			GameObject prefab = kit.Lamps[(int)(roll % (ulong)kit.Lamps.Length)];
			GameObject lamp = Instantiate(prefab, at - Offset(), Quaternion.LookRotation(-left * side) * prefab.transform.rotation, props);
			GraphicsTuning.Adopt(lamp);
		}
	}
}
