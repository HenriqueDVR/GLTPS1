using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

// far view of the elevator towers and the level above. meshes-only copies of the pillar pieces in thin fog
// and a broken ceiling
public class DistantPillar : MonoBehaviour
{
	// tower parts in nodeCOLUMN
	private static readonly string[] towerParts = { "Cylinder", "Elevator" };

	// ring round the column's lower part with panels on it. its wall never drew so the panels floated, off everywhere
	private const string hiddenPart = "Thingys";

	private const float swapDistance = 250f;

	// cameras get their far clip pushed out to this, so the upper level is gone into the haze before it's cut off.
	// map pieces hide past 700m anyway so it's only this stuff drawing further. a ceiling tile, 3x3 of them always cover it
	private const float drawDistance = 7000f;

	// past this copies get pulled in and shrunk, in order, so lined up towers keep in front of each other
	private const float farthest = 5500f;

	// ceiling's share of the world's fog and the most it ever shows. hazier than the towers so it reads soft and far
	private const float ceilingFogShare = 0.1f;

	private const float ceilingStrength = 0.55f;

	// how much of the underside's panels and beams show
	private const float ceilingDetail = 0.6f;

	// the upper level sits this much higher than the column's own slabs. the real slabs go up with it and the
	// towers are drawn taller to meet it
	private const float ceilingLift = 80f;

	// towers get less still, they should read from a long way off
	private const float towerFogShare = 0.06f;

	// ceiling sits this far under the column's top, if the piece has no slabs of its own
	private const float ceilingSink = 45f;

	// anything of the piece this far up besides the tower is the next level's slabs
	private const float highUp = 300f;

	// anything of the piece reaching this high is hidden past the swap, tower and walls and all.
	// the real thing goes fog white out there and stood in front of the ceiling
	private const float tallUp = 200f;

	// clouds clear round this many towers at most
	private const int cloudGaps = 16;

	// ours sits just over the real bottom slab, so up close the real one covers it without fighting
	private const float slabGap = 2f;

	// the ceiling tiles every this many metres, so it goes on wherever you walk
	private const float ceilingSize = 7000f;

	// share of the ceiling the deck covers, the rest is open sky
	private const float ceilingSolid = 0.5f;

	// noise cells across a tile and how much each counts, roughly 1000, 390, 175, 70 and 47 m. holes of a few hundred metres
	// lead so any couple of km you walk gets about the same mix, not all solid or all open by luck
	private static readonly int[] ceilingPeriods = { 7, 18, 40, 100, 150 };

	private static readonly float[] ceilingWeights = { 0.25f, 1f, 0.35f, 0.12f, 0.05f };

	// steel truss under the slab's broken edges, this deep
	private const float trussDepth = 16f;

	// thin bits narrower than about twice this many cells are worn away
	private const int ceilingWear = 2;

	// how fine the broken edges are
	private const float ceilingCell = 30f;

	// solid stretches merge into blocks this many cells wide
	private const int ceilingBlock = 8;

	// concrete slab and the deck on it, from the underside up: bottom, thickness. the slab lines up with the pillar piece's
	private static readonly Vector2[] storeys = { new Vector2(0f, 24f), new Vector2(40f, 14f) };

	// the deck reaches out a little past the slab, so looking up the edge is layered
	private static readonly float[] storeyShare = { 0.86f, 1f };

	// bits of slab with fewer grid points than this are dropped, alone in a gap they float
	private const int smallestIsland = 60;

	// clouds thin out this far round the tower so it doesn't cut a line through them
	private const float cloudClearing = 220f;

	// if the real tower's material can't be read
	private static readonly Color fallbackAlbedo = new Color(0.6f, 0.61f, 0.63f);

	private static DistantPillar instance;

	private sealed class Distant
	{
		public Transform Copy;

		public Material Material;

		public WorldLayout.PlacedNode Node;

		// the real one's tower meshes, off while the copy stands in
		public GameObject RealInstance;

		public readonly List<MeshRenderer> RealParts = new List<MeshRenderer>();
	}

	private readonly List<Distant> pillars = new List<Distant>();

	private WorldGenerator world;

	private WorldLayout.PlacedNode pillar;

	// nearest nine tiles round the eye
	private readonly List<Transform> ceilings = new List<Transform>();

	private Material ceilingMaterial;

	// fog coloured dome round the eye, fills in behind the ceiling where the sky would show
	private Transform sky;

	private Material skyMaterial;

	// the scene's own skybox, a mesh that follows the girl about 1.5 km out and hid everything past it. ours does its job
	private MeshRenderer[] originalSky = new MeshRenderer[0];

	// cloud layers under the ceiling
	private readonly List<Transform> clouds = new List<Transform>();

	private readonly List<Material> cloudMaterials = new List<Material>();

	private readonly List<Vector4> gaps = new List<Vector4>();

	// depth under the ceiling, cover
	private static readonly Vector2[] cloudLayers = { new Vector2(100f, 0.4f) };

	// foot and height of the column in the copy
	private float foot;

	private float height;

	// widest the column gets from its middle
	private float girth;

	// copies are drawn this much taller so they reach the lifted ceiling
	private float stretch = 1f;

	// the real tower's colour, lit the same way in the copy's shader
	private Color albedo = fallbackAlbedo;

	// underside of the piece's own bottom slab over its foot, nan if it has none
	private float slab = float.NaN;

	private bool hidden;

	private Camera[] cameras = new Camera[8];


	// how far the world camera really drew last
	private float farClip = drawDistance;

	private void OnEnable()
	{
		Application.onBeforeRender += Push;
		RenderPipelineManager.beginCameraRendering += Reach;
	}

	private void OnDisable()
	{
		Application.onBeforeRender -= Push;
		RenderPipelineManager.beginCameraRendering -= Reach;
	}

	// world cameras only, not the psx screen quad or the map
	private static bool Sees(Camera camera)
	{
		return !camera.orthographic && camera.farClipPlane >= 1000f;
	}

	// cinemachine copies its lens onto the camera every frame, so the far clip goes on the lens
	private void Update()
	{
		CinemachineCore core = CinemachineCore.Instance;
		for (int i = 0; i < core.VirtualCameraCount; i++)
		{
			CinemachineVirtualCameraBase vcam = core.GetVirtualCamera(i);
			if (vcam is CinemachineVirtualCamera single && single.m_Lens.FarClipPlane < drawDistance)
			{
				single.m_Lens.FarClipPlane = drawDistance;
			}
			else if (vcam is CinemachineFreeLook free)
			{
				if (free.m_Lens.FarClipPlane < drawDistance)
				{
					free.m_Lens.FarClipPlane = drawDistance;
				}
				for (int rig = 0; rig < 3; rig++)
				{
					CinemachineVirtualCamera part = free.GetRig(rig);
					if (part != null && part.m_Lens.FarClipPlane < drawDistance)
					{
						part.m_Lens.FarClipPlane = drawDistance;
					}
				}
			}
		}
	}

	// cameras cinemachine doesn't drive
	private void Push()
	{
		if (hidden)
		{
			return;
		}
		if (cameras.Length < Camera.allCamerasCount)
		{
			cameras = new Camera[Camera.allCamerasCount];
		}
		int count = Camera.GetAllCameras(cameras);
		for (int i = 0; i < count; i++)
		{
			if (Sees(cameras[i]) && cameras[i].farClipPlane < drawDistance)
			{
				cameras[i].farClipPlane = drawDistance;
			}
		}
	}

	// fade out by wherever the camera really stops, in case something set it back
	private void Reach(ScriptableRenderContext context, Camera camera)
	{
		if (hidden || !Sees(camera) || ceilingMaterial == null)
		{
			return;
		}
		farClip = Mathf.Min(camera.farClipPlane, drawDistance);
		ceilingMaterial.SetFloat("_FarFade", farClip);
		foreach (Material material in cloudMaterials)
		{
			material.SetFloat("_FarFade", farClip);
		}
	}

	private void Start()
	{
		world = GetComponent<WorldGenerator>();
		pillar = world.Layout != null ? world.Pillar() : null;
		if (pillar == null)
		{
			Destroy(this);
			return;
		}
		instance = this;
		Shader shader = Resources.Load<Shader>("DistantSilhouette");
		albedo = TowerAlbedo(pillar.Def.Prefab);
		// every real pillar gets the same treatment, not just the one the map points at
		List<Vector3> towers = new List<Vector3>();
		foreach (WorldLayout.PlacedNode node in world.Pillars())
		{
			pillars.Add(Make(shader, node));
			towers.Add(node.Pose.position - pillar.Pose.position);
		}
		stretch = Level() / BaseLevel();
		// same holes every time for a seed
		System.Random random = new System.Random((int)(world.Seed & 0x7fffffff));
		ceilingMaterial = new Material(Resources.Load<Shader>("DistantCeiling"));
		Mesh ceilingMesh = Ceiling(towers, random);
		for (int i = 0; i < 9; i++)
		{
			GameObject tile = new GameObject("DistantCeiling", typeof(MeshFilter), typeof(MeshRenderer));
			tile.GetComponent<MeshFilter>().sharedMesh = ceilingMesh;
			MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
			tileRenderer.sharedMaterial = ceilingMaterial;
			tileRenderer.shadowCastingMode = ShadowCastingMode.Off;
			tileRenderer.receiveShadows = false;
			ceilings.Add(tile.transform);
		}
		GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		Destroy(dome.GetComponent<Collider>());
		dome.name = "DistantSky";
		dome.transform.localScale = Vector3.one * 400f;
		skyMaterial = new Material(Resources.Load<Shader>("DistantSky"));
		MeshRenderer domeRenderer = dome.GetComponent<MeshRenderer>();
		domeRenderer.sharedMaterial = skyMaterial;
		domeRenderer.shadowCastingMode = ShadowCastingMode.Off;
		domeRenderer.receiveShadows = false;
		sky = dome.transform;
		GameObject original = GameObject.Find("skybox");
		if (original != null)
		{
			originalSky = original.GetComponentsInChildren<MeshRenderer>();
			ShowOriginalSky(false);
		}
		Shader cloudShader = Resources.Load<Shader>("DistantCloud");
		for (int i = 0; i < cloudLayers.Length; i++)
		{
			GameObject layer = GameObject.CreatePrimitive(PrimitiveType.Quad);
			Destroy(layer.GetComponent<Collider>());
			layer.name = "DistantCloud";
			layer.transform.localScale = Vector3.one * drawDistance * 2.2f;
			Material material = new Material(cloudShader);
			material.SetFloat("_Cover", cloudLayers[i].y);
			material.SetFloat("_Scale", 380f + i * 220f);
			material.SetVector("_Drift", new Vector4(3f + i * 2f, 0f, 1.5f - i, 0f));
			MeshRenderer layerRenderer = layer.GetComponent<MeshRenderer>();
			layerRenderer.sharedMaterial = material;
			layerRenderer.shadowCastingMode = ShadowCastingMode.Off;
			layerRenderer.receiveShadows = false;
			clouds.Add(layer.transform);
			cloudMaterials.Add(material);
		}
	}

	private Distant Make(Shader shader, WorldLayout.PlacedNode node)
	{
		Distant distant = new Distant
		{
			Material = new Material(shader),
			Node = node
		};
		distant.Copy = Copy(pillar.Def.Prefab, distant.Material).transform;
		distant.Material.SetColor("_Albedo", albedo);
		return distant;
	}

	// the tower's base colour times its texture's average, so the copy is the colour of the real one
	private static Color TowerAlbedo(GameObject prefab)
	{
		foreach (MeshRenderer part in prefab.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
		{
			if (part.name == towerParts[0] && part.sharedMaterial != null)
			{
				return Albedo(part.sharedMaterial);
			}
		}
		return fallbackAlbedo;
	}

	// off as soon as a piece is made
	public static void HideBrokenParts(GameObject piece)
	{
		foreach (MeshRenderer part in piece.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
		{
			if (part.name == hiddenPart)
			{
				part.enabled = false;
			}
		}
	}

	// a material's base colour times its texture's average, in gamma like SetColor takes it
	public static Color Albedo(Material material)
	{
		Color tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
		Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
		Color colour = tint * (texture != null ? Average(texture).gamma : Color.white);
		colour.a = 1f;
		return colour;
	}

	// read back through the gpu, textures aren't cpu readable. linear
	private static Color Average(Texture texture)
	{
		int width = Mathf.Clamp(texture.width, 1, 256);
		int height = Mathf.Clamp(texture.height, 1, 256);
		RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		Graphics.Blit(texture, target);
		RenderTexture previous = RenderTexture.active;
		RenderTexture.active = target;
		Texture2D pixels = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
		pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
		RenderTexture.active = previous;
		RenderTexture.ReleaseTemporary(target);
		Color sum = Color.black;
		foreach (Color pixel in pixels.GetPixels())
		{
			sum += pixel;
		}
		Destroy(pixels);
		return sum / (width * height);
	}

	private void OnDestroy()
	{
		foreach (Distant distant in pillars)
		{
			if (distant.Copy != null)
			{
				Destroy(distant.Copy.gameObject);
			}
		}
		foreach (Transform tile in ceilings)
		{
			if (tile != null)
			{
				Destroy(tile.gameObject);
			}
		}
		foreach (Transform layer in clouds)
		{
			if (layer != null)
			{
				Destroy(layer.gameObject);
			}
		}
		if (sky != null)
		{
			Destroy(sky.gameObject);
		}
		ShowOriginalSky(true);
		foreach (Distant distant in pillars)
		{
			ShowReal(distant, true);
		}
	}

	// keep them out of the map picture
	public static void Hide(bool hide)
	{
		if (instance == null)
		{
			return;
		}
		instance.hidden = hide;
		foreach (Distant distant in instance.pillars)
		{
			distant.Copy.gameObject.SetActive(!hide && distant.Copy.gameObject.activeSelf);
		}
		foreach (Transform tile in instance.ceilings)
		{
			tile.gameObject.SetActive(!hide);
		}
		foreach (Transform layer in instance.clouds)
		{
			layer.gameObject.SetActive(!hide);
		}
		instance.sky.gameObject.SetActive(!hide);
		// the map picture gets the scene as it always was
		instance.ShowOriginalSky(hide);
		if (hide)
		{
			foreach (Distant distant in instance.pillars)
			{
				ShowReal(distant, true);
			}
		}
	}

	// underside of the ceiling over the column's foot
	private float Level()
	{
		return BaseLevel() + ceilingLift;
	}

	// where the column's own slabs put it, before the lift
	private float BaseLevel()
	{
		return float.IsNaN(slab) ? height - ceilingSink : slab + slabGap;
	}

	private void ShowOriginalSky(bool show)
	{
		foreach (MeshRenderer part in originalSky)
		{
			if (part != null)
			{
				part.enabled = show;
			}
		}
	}

	private static void ShowReal(Distant distant, bool show)
	{
		if (distant.Node.Instance != distant.RealInstance)
		{
			distant.RealInstance = distant.Node.Instance;
			distant.RealParts.Clear();
			if (distant.RealInstance != null)
			{
				float bottom = distant.RealInstance.transform.position.y;
				List<Transform> slabs = new List<Transform>();
				foreach (MeshRenderer part in distant.RealInstance.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
				{
					if (part.name == hiddenPart)
					{
						part.enabled = false;
						continue;
					}
					// from the mesh, a renderer's bounds are empty while the piece is still switched off
					MeshFilter filter = part.GetComponent<MeshFilter>();
					bool tower = System.Array.IndexOf(towerParts, part.name) >= 0;
					bool tall = filter != null && filter.sharedMesh != null && Highest(filter.sharedMesh.bounds, part.transform) > bottom + tallUp;
					if (tower || tall)
					{
						distant.RealParts.Add(part);
					}
					if (!tower && filter != null && filter.sharedMesh != null && Lowest(filter.sharedMesh.bounds, part.transform) > bottom + highUp)
					{
						slabs.Add(part.transform);
					}
				}
				Lift(slabs);
			}
		}
		foreach (MeshRenderer part in distant.RealParts)
		{
			if (part != null)
			{
				part.enabled = show;
			}
		}
	}

	// the real piece's next level slabs go up with the ceiling, once. children of a lifted part already went with it
	private static void Lift(List<Transform> slabs)
	{
		foreach (Transform slabPart in slabs)
		{
			bool carried = false;
			for (Transform parent = slabPart.parent; parent != null && !carried; parent = parent.parent)
			{
				carried = slabs.Contains(parent);
			}
			if (!carried)
			{
				slabPart.position += Vector3.up * ceilingLift;
			}
		}
	}

	private static float Lowest(Bounds bounds, Transform part)
	{
		float lowest = float.MaxValue;
		foreach (Vector3 corner in Corners(bounds))
		{
			lowest = Mathf.Min(lowest, part.TransformPoint(corner).y);
		}
		return lowest;
	}

	private static float Highest(Bounds bounds, Transform part)
	{
		float highest = float.MinValue;
		foreach (Vector3 corner in Corners(bounds))
		{
			highest = Mathf.Max(highest, part.TransformPoint(corner).y);
		}
		return highest;
	}

	// under an inactive parent so its scripts never wake, then strip to meshes
	private GameObject Copy(GameObject prefab, Material material)
	{
		GameObject holder = new GameObject("DistantPillar");
		holder.SetActive(false);
		GameObject copy = Instantiate(prefab, holder.transform);
		copy.transform.localPosition = Vector3.zero;
		copy.transform.localRotation = Quaternion.identity;
		// a few passes, some components need others gone first
		for (int pass = 0; pass < 4; pass++)
		{
			foreach (Component component in copy.GetComponentsInChildren<Component>(includeInactive: true))
			{
				if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer))
				{
					DestroyImmediate(component);
				}
			}
		}
		// untag, and turn the parts on (the piece's scripts normally do)
		foreach (Transform part in copy.GetComponentsInChildren<Transform>(includeInactive: true))
		{
			part.gameObject.tag = "Untagged";
			part.gameObject.layer = 0;
			part.gameObject.SetActive(true);
		}
		bool any = false;
		Bounds bounds = default;
		float lowest = float.NaN;
		foreach (MeshRenderer meshRenderer in copy.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
		{
			// just the tower, not the walls or the next level's slabs. the ceiling lines up with the bottom slab
			if (System.Array.IndexOf(towerParts, meshRenderer.name) < 0)
			{
				MeshFilter other = meshRenderer.GetComponent<MeshFilter>();
				if (other != null && other.sharedMesh != null)
				{
					Bounds part = new Bounds(holder.transform.InverseTransformPoint(meshRenderer.transform.TransformPoint(other.sharedMesh.bounds.center)), Vector3.zero);
					foreach (Vector3 corner in Corners(other.sharedMesh.bounds))
					{
						part.Encapsulate(holder.transform.InverseTransformPoint(meshRenderer.transform.TransformPoint(corner)));
					}
					if (part.min.y > highUp && !(part.min.y >= lowest))
					{
						lowest = part.min.y;
					}
				}
				Destroy(meshRenderer.gameObject);
				continue;
			}
			Material[] materials = new Material[meshRenderer.sharedMaterials.Length];
			for (int i = 0; i < materials.Length; i++)
			{
				materials[i] = material;
			}
			meshRenderer.sharedMaterials = materials;
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = false;
			MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
			if (filter != null && filter.sharedMesh != null)
			{
				// in the holder's space, it sits at the origin
				foreach (Vector3 corner in Corners(filter.sharedMesh.bounds))
				{
					Vector3 point = holder.transform.InverseTransformPoint(meshRenderer.transform.TransformPoint(corner));
					if (!any)
					{
						bounds = new Bounds(point, Vector3.zero);
						any = true;
					}
					bounds.Encapsulate(point);
				}
			}
		}
		foot = bounds.min.y;
		slab = lowest - foot;
		girth = Mathf.Max(Mathf.Max(-bounds.min.x, bounds.max.x), Mathf.Max(-bounds.min.z, bounds.max.z));
		height = Mathf.Max(1f, bounds.size.y);
		holder.SetActive(true);
		return holder;
	}

	private static Vector3[] Corners(Bounds b)
	{
		Vector3 min = b.min;
		Vector3 max = b.max;
		return new Vector3[8]
		{
			new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
			new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z)
		};
	}

	// slabs with holes, solid round the pillars, wraps at the tile's sides. rim walls marked by vertex colour
	private Mesh Ceiling(List<Vector3> towers, System.Random random)
	{
		int count = Mathf.CeilToInt(ceilingSize / ceilingCell / ceilingBlock) * ceilingBlock;
		float cell = ceilingSize / count;
		float half = ceilingSize * 0.5f;
		int seed = random.Next();
		float[,] field = new float[count + 1, count + 1];
		for (int x = 0; x <= count; x++)
		{
			for (int y = 0; y <= count; y++)
			{
				float value = 0f;
				for (int layer = 0; layer < ceilingPeriods.Length; layer++)
				{
					int period = ceilingPeriods[layer];
					value += Wrapped((float)x * period / count, (float)y * period / count, period, seed + layer) * ceilingWeights[layer];
				}
				field[x, y] = value;
			}
		}
		field = Wear(Wear(field, count, true), count, false);
		// levels that leave the right share standing for each storey
		float[] sorted = new float[count * count];
		for (int x = 0; x < count; x++)
		{
			for (int y = 0; y < count; y++)
			{
				sorted[x * count + y] = field[x, y];
			}
		}
		System.Array.Sort(sorted);
		float[] levels = new float[storeys.Length];
		for (int i = 0; i < levels.Length; i++)
		{
			levels[i] = sorted[Mathf.Clamp(Mathf.RoundToInt((1f - ceilingSolid * storeyShare[i]) * sorted.Length), 0, sorted.Length - 1)];
		}
		// the outer tier needs the least, the bottom one the most
		float iso = levels[levels.Length - 1];
		float top = levels[0];
		for (int x = 0; x <= count; x++)
		{
			for (int y = 0; y <= count; y++)
			{
				// solid round the pillars, every storey, a cell's width to spare. ragged so it isn't a circle
				Vector3 point = new Vector3(x * cell - half, 0f, y * cell - half);
				float reach = Mathf.Max(0f, Wrapped(x * 18f / count, y * 18f / count, 18, seed + 9)) * 250f;
				float solid = 0f;
				foreach (Vector3 tower in towers)
				{
					solid = Mathf.Max(solid, Mathf.InverseLerp(560f + reach, 450f + reach, Apart(point, tower)));
				}
				if (solid > 0f)
				{
					field[x, y] = Mathf.Max(field[x, y], Mathf.Lerp(iso - 0.3f, top + 0.05f, solid));
				}
			}
		}
		DropIslands(field, count, iso);
		Slabs slabs = new Slabs(field, count, cell, half, random.Next());
		for (int i = 0; i < storeys.Length; i++)
		{
			slabs.Storey(i, levels[i], true);
		}
		Chunks(slabs, random);
		Mesh mesh = new Mesh { name = "DistantCeiling", indexFormat = IndexFormat.UInt32 };
		mesh.SetVertices(slabs.Vertices);
		mesh.SetColors(slabs.Colours);
		mesh.SetTriangles(slabs.Triangles, 0);
		mesh.RecalculateBounds();
		return mesh;
	}

	// flat distance the short way round the tiling
	private static float Apart(Vector3 point, Vector3 spot)
	{
		float x = point.x - spot.x;
		float z = point.z - spot.z;
		x -= Mathf.Round(x / ceilingSize) * ceilingSize;
		z -= Mathf.Round(z / ceilingSize) * ceilingSize;
		return Mathf.Sqrt(x * x + z * z);
	}

	// lowest or highest round each point, wrapping. low then high wears thin bits off and keeps the chunky ones
	private static float[,] Wear(float[,] field, int count, bool lowest)
	{
		float[,] worn = new float[count + 1, count + 1];
		for (int x = 0; x < count; x++)
		{
			for (int y = 0; y < count; y++)
			{
				float pick = field[x, y];
				for (int dx = -ceilingWear; dx <= ceilingWear; dx++)
				{
					for (int dy = -ceilingWear; dy <= ceilingWear; dy++)
					{
						if (dx * dx + dy * dy > ceilingWear * ceilingWear)
						{
							continue;
						}
						float other = field[(x + dx + count) % count, (y + dy + count) % count];
						pick = lowest ? Mathf.Min(pick, other) : Mathf.Max(pick, other);
					}
				}
				worn[x, y] = pick;
			}
		}
		for (int i = 0; i <= count; i++)
		{
			worn[count, i] = worn[0, i % count];
			worn[i, count] = worn[i % count, 0];
		}
		return worn;
	}

	// gradient noise repeating every period cells
	private static float Wrapped(float x, float y, int period, int seed)
	{
		int x0 = Mathf.FloorToInt(x);
		int y0 = Mathf.FloorToInt(y);
		float fx = x - x0;
		float fy = y - y0;
		float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
		float v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
		float a = Slope(x0, y0, fx, fy, period, seed);
		float b = Slope(x0 + 1, y0, fx - 1f, fy, period, seed);
		float c = Slope(x0, y0 + 1, fx, fy - 1f, period, seed);
		float d = Slope(x0 + 1, y0 + 1, fx - 1f, fy - 1f, period, seed);
		return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
	}

	private static float Slope(int x, int y, float dx, float dy, int period, int seed)
	{
		x = (x % period + period) % period;
		y = (y % period + period) % period;
		unchecked
		{
			uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
			h *= 0x9E3779B1u;
			h ^= h >> 15;
			h *= 0x85EBCA77u;
			h ^= h >> 13;
			float angle = (h & 0xffff) / 65536f * Mathf.PI * 2f;
			return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
		}
	}

	// 8 way like the saddle cells join them, across the tile's sides too
	private static void DropIslands(float[,] field, int count, float iso)
	{
		bool[,] seen = new bool[count, count];
		List<Vector2Int> island = new List<Vector2Int>();
		Stack<Vector2Int> open = new Stack<Vector2Int>();
		for (int x = 0; x < count; x++)
		{
			for (int y = 0; y < count; y++)
			{
				if (seen[x, y] || field[x, y] < iso)
				{
					continue;
				}
				island.Clear();
				seen[x, y] = true;
				open.Push(new Vector2Int(x, y));
				while (open.Count > 0)
				{
					Vector2Int at = open.Pop();
					island.Add(at);
					for (int dx = -1; dx <= 1; dx++)
					{
						for (int dy = -1; dy <= 1; dy++)
						{
							int nx = (at.x + dx + count) % count;
							int ny = (at.y + dy + count) % count;
							if (seen[nx, ny] || field[nx, ny] < iso)
							{
								continue;
							}
							seen[nx, ny] = true;
							open.Push(new Vector2Int(nx, ny));
						}
					}
				}
				if (island.Count < smallestIsland)
				{
					foreach (Vector2Int at in island)
					{
						field[at.x, at.y] = iso - 0.05f;
					}
				}
			}
		}
		for (int i = 0; i <= count; i++)
		{
			field[count, i] = field[0, i % count];
			field[i, count] = field[i % count, 0];
		}
	}

	// what's left at the broken edges: bent slabs, girders and rebar sticking out, and buildings standing on the deck
	private static void Chunks(Slabs slabs, System.Random random)
	{
		int hanging = 0;
		int girders = 0;
		int buildings = 0;
		Vector2 slab = storeys[0];
		Vector2 deck = storeys[storeys.Length - 1];
		foreach (Slabs.Edge edge in slabs.Edges)
		{
			float roll = (float)random.NextDouble();
			if (edge.Tier == 0 && roll < 0.025f && hanging < 160)
			{
				hanging++;
				float bend = Mathf.Lerp(20f, 65f, (float)random.NextDouble()) * Mathf.Deg2Rad;
				Vector3 forward = edge.Out * Mathf.Cos(bend) - Vector3.up * Mathf.Sin(bend);
				Vector3 up = edge.Out * Mathf.Sin(bend) + Vector3.up * Mathf.Cos(bend);
				float reach = Mathf.Lerp(15f, 45f, (float)random.NextDouble());
				Vector3 size = new Vector3(Mathf.Lerp(15f, 40f, (float)random.NextDouble()), slab.y * Mathf.Lerp(0.6f, 0.9f, (float)random.NextDouble()), reach + 12f);
				// hinged half way up the slab's rim, starts well inside so it never looks loose on a ragged bit
				Vector3 hinge = edge.Middle + Vector3.up * slab.y * 0.5f;
				slabs.Box(hinge + forward * (reach - 12f) * 0.5f, edge.Along, up, forward, size, false);
			}
			else if (edge.Tier == 0 && roll < 0.09f && girders < 400)
			{
				// a few beams side by side, snapped off and sagging a little
				int beams = 1 + random.Next(3);
				float droop = Mathf.Lerp(0f, 30f, (float)random.NextDouble()) * Mathf.Deg2Rad;
				float reach = Mathf.Lerp(20f, 70f, (float)random.NextDouble());
				for (int i = 0; i < beams && girders < 400; i++)
				{
					girders++;
					float side = (i - (beams - 1) * 0.5f) * 9f;
					float drop = droop + Mathf.Lerp(-0.1f, 0.1f, (float)random.NextDouble());
					Vector3 forward = edge.Out * Mathf.Cos(drop) - Vector3.up * Mathf.Sin(drop);
					Vector3 up = edge.Out * Mathf.Sin(drop) + Vector3.up * Mathf.Cos(drop);
					float length = reach * Mathf.Lerp(0.6f, 1.1f, (float)random.NextDouble());
					Vector3 root = edge.Middle + edge.Along * side + Vector3.up * Mathf.Lerp(3f, slab.y - 3f, (float)random.NextDouble());
					slabs.Box(root + forward * (length * 0.5f - 6f), edge.Along, up, forward, new Vector3(3f, 3.5f, length + 6f), true);
				}
			}
			else if (edge.Tier == storeys.Length - 1 && roll < 0.06f && buildings < 260)
			{
				// stands back from the deck's edge, on solid deck
				float width = Mathf.Lerp(25f, 70f, (float)random.NextDouble());
				float depth = Mathf.Lerp(25f, 70f, (float)random.NextDouble());
				Vector3 centre = edge.Middle - edge.Out * Mathf.Lerp(depth * 0.5f + 10f, depth * 0.5f + 80f, (float)random.NextDouble());
				if (!slabs.Solid(centre, storeys.Length - 1) || !slabs.Solid(centre - edge.Out * depth * 0.5f, storeys.Length - 1))
				{
					continue;
				}
				buildings++;
				float tall = Mathf.Lerp(30f, 160f, Mathf.Pow((float)random.NextDouble(), 1.6f));
				slabs.Building(centre + Vector3.up * (deck.x + deck.y), edge.Along, edge.Out, new Vector3(width, tall, depth));
			}
		}
	}

	// marching squares over the hole noise. the solid fills in blocks where it can, edges get torn up.
	// every tier gets an underside, the higher ones reach out past the lower
	private sealed class Slabs
	{
		public struct Edge
		{
			public Vector3 Middle;

			// flat, away from the solid side
			public Vector3 Out;

			public Vector3 Along;

			public int Tier;
		}

		private struct Point
		{
			public long Key;

			public Vector3 Position;

			public bool Crossing;

			// crossing from inside to out, walking round the cell
			public bool Exit;

			// inside corner before an exit
			public Vector3 Inside;
		}

		private static readonly int[] cornerX = { 0, 1, 1, 0 };

		private static readonly int[] cornerY = { 0, 0, 1, 1 };

		public readonly List<Vector3> Vertices = new List<Vector3>();

		public readonly List<Color> Colours = new List<Color>();

		public readonly List<int> Triangles = new List<int>();

		// broken edges of the outer tier
		public readonly List<Edge> Edges = new List<Edge>();

		private readonly Dictionary<long, int> shared = new Dictionary<long, int>();

		private readonly List<Point> points = new List<Point>();

		private readonly float[,] field;

		private readonly int count;

		private readonly float cell;

		private readonly float half;

		private readonly int seed;

		private float iso;

		private readonly float[] levels = new float[storeys.Length];

		// the tier being built: its underside height and first vertex slot
		private float floor;

		private int slot;

		public Slabs(float[,] field, int count, float cell, float half, int seed)
		{
			this.field = field;
			this.count = count;
			this.cell = cell;
			this.half = half;
			this.seed = seed;
		}

		// every tier has its own underside, the ones above reach out past the ones below
		public void Storey(int index, float level, bool edges)
		{
			iso = level;
			levels[index] = level;
			Vector2 storey = storeys[index];
			floor = storey.x;
			slot = index * 5;
			for (int x = 0; x < count; x += ceilingBlock)
			{
				for (int y = 0; y < count; y += ceilingBlock)
				{
					Fill(x, y, ceilingBlock);
				}
			}
			for (int x = 0; x < count; x++)
			{
				for (int y = 0; y < count; y++)
				{
					Walk(x, y);
					for (int i = 0; i < points.Count; i++)
					{
						if (points[i].Crossing && points[i].Exit)
						{
							Point p = points[i];
							Point q = points[(i + 1) % points.Count];
							Wall(p, q, slot + 1, slot + 2, storey.x, storey.x + storey.y, Color.red);
							if (index + 1 < storeys.Length)
							{
								// dark recess up to the tier above, under its overhang, marked green
								Wall(p, q, slot + 3, slot + 4, storey.x + storey.y, storeys[index + 1].x, Color.green);
							}
							if (index == 0)
							{
								// steel truss hanging under the slab's edge, marked blue, the shader cuts the lattice out
								Wall(p, q, 30, 31, -trussDepth, 0f, Color.blue);
							}
							if (!edges)
							{
								continue;
							}
							Vector3 along = (q.Position - p.Position).normalized;
							Vector3 middle = (p.Position + q.Position) * 0.5f;
							Vector3 outward = new Vector3(along.z, 0f, -along.x);
							if (Vector3.Dot(outward, p.Inside - middle) > 0f)
							{
								outward = -outward;
							}
							Edges.Add(new Edge { Middle = middle, Out = outward, Along = along, Tier = index });
						}
					}
				}
			}
		}

		// no top, nothing sees it from below. steel is dark all over, concrete has pale broken sides
		public void Box(Vector3 centre, Vector3 along, Vector3 up, Vector3 forward, Vector3 size, bool steel)
		{
			Color sides = steel ? Color.black : Color.red;
			Vector3 a = along * size.x * 0.5f;
			Vector3 u = up * size.y * 0.5f;
			Vector3 f = forward * size.z * 0.5f;
			Vector3 c0 = centre - a - u - f;
			Vector3 c1 = centre + a - u - f;
			Vector3 c2 = centre + a - u + f;
			Vector3 c3 = centre - a - u + f;
			Vector3 h = u * 2f;
			Quad(Vertices, Colours, Triangles, c0, c1, c2, c3, Color.black);
			Quad(Vertices, Colours, Triangles, c0, c1, c1 + h, c0 + h, sides);
			Quad(Vertices, Colours, Triangles, c1, c2, c2 + h, c1 + h, sides);
			Quad(Vertices, Colours, Triangles, c2, c3, c3 + h, c2 + h, sides);
			Quad(Vertices, Colours, Triangles, c3, c0, c0 + h, c3 + h, sides);
		}

		// a block on the deck, its facades marked by alpha for the shader's floors and windows
		public void Building(Vector3 foot, Vector3 along, Vector3 forward, Vector3 size)
		{
			Color facade = new Color(0f, 0f, 0f, 0.5f);
			Vector3 a = along * size.x * 0.5f;
			Vector3 f = forward * size.z * 0.5f;
			Vector3 h = Vector3.up * size.y;
			Vector3 c0 = foot - a - f;
			Vector3 c1 = foot + a - f;
			Vector3 c2 = foot + a + f;
			Vector3 c3 = foot - a + f;
			Quad(Vertices, Colours, Triangles, c0, c1, c1 + h, c0 + h, facade);
			Quad(Vertices, Colours, Triangles, c1, c2, c2 + h, c1 + h, facade);
			Quad(Vertices, Colours, Triangles, c2, c3, c3 + h, c2 + h, facade);
			Quad(Vertices, Colours, Triangles, c3, c0, c0 + h, c3 + h, facade);
			Quad(Vertices, Colours, Triangles, c0 + h, c1 + h, c2 + h, c3 + h, facade);
		}

		// that tier is standing here
		public bool Solid(Vector3 position, int tier)
		{
			int x = Mathf.RoundToInt((position.x + half) / cell);
			int y = Mathf.RoundToInt((position.z + half) / cell);
			x = (x % count + count) % count;
			y = (y % count + count) % count;
			return field[x, y] >= levels[tier];
		}

		private void Fill(int x, int y, int size)
		{
			int inside = 0;
			for (int i = x; i <= x + size; i++)
			{
				for (int j = y; j <= y + size; j++)
				{
					if (Inside(i, j))
					{
						inside++;
					}
				}
			}
			if (inside == 0)
			{
				return;
			}
			if (inside == (size + 1) * (size + 1))
			{
				Vector3 up = Vector3.up * floor;
				int a = Shared(CornerKey(x, y) * 32 + slot, Grid(x, y) + up, Color.black);
				int b = Shared(CornerKey(x + size, y) * 32 + slot, Grid(x + size, y) + up, Color.black);
				int c = Shared(CornerKey(x + size, y + size) * 32 + slot, Grid(x + size, y + size) + up, Color.black);
				int d = Shared(CornerKey(x, y + size) * 32 + slot, Grid(x, y + size) + up, Color.black);
				Triangles.Add(a);
				Triangles.Add(b);
				Triangles.Add(c);
				Triangles.Add(a);
				Triangles.Add(c);
				Triangles.Add(d);
				return;
			}
			if (size > 1)
			{
				int h = size / 2;
				Fill(x, y, h);
				Fill(x + h, y, h);
				Fill(x, y + h, h);
				Fill(x + h, y + h, h);
				return;
			}
			// cut corners off a square, always convex so a fan does
			Walk(x, y);
			Vector3 lift = Vector3.up * floor;
			int first = Shared(points[0].Key * 32 + slot, points[0].Position + lift, Color.black);
			for (int i = 1; i + 1 < points.Count; i++)
			{
				Triangles.Add(first);
				Triangles.Add(Shared(points[i].Key * 32 + slot, points[i].Position + lift, Color.black));
				Triangles.Add(Shared(points[i + 1].Key * 32 + slot, points[i + 1].Position + lift, Color.black));
			}
		}

		// round the cell's corners, the inside ones plus where the edge crosses
		private void Walk(int x, int y)
		{
			points.Clear();
			for (int i = 0; i < 4; i++)
			{
				int ax = x + cornerX[i];
				int ay = y + cornerY[i];
				int bx = x + cornerX[(i + 1) % 4];
				int by = y + cornerY[(i + 1) % 4];
				bool a = Inside(ax, ay);
				if (a)
				{
					points.Add(new Point { Key = CornerKey(ax, ay), Position = Grid(ax, ay) });
				}
				if (a != Inside(bx, by))
				{
					points.Add(Crossing(ax, ay, bx, by, a));
				}
			}
		}

		// same point from both cells sharing the side, slid along it so the edge looks torn
		private Point Crossing(int ax, int ay, int bx, int by, bool exit)
		{
			Vector3 inside = exit ? Grid(ax, ay) : Grid(bx, by);
			if (bx < ax || by < ay)
			{
				(ax, bx) = (bx, ax);
				(ay, by) = (by, ay);
			}
			long n = (long)(count + 1) * (count + 1);
			long key = (ay == by ? n : n * 2) + CornerKey(ax, ay);
			float t = (iso - field[ax, ay]) / (field[bx, by] - field[ax, ay]);
			// same slide on both sides of the tile's seam
			long wrapped = (ay == by ? n : n * 2) + CornerKey(ax % count, ay % count);
			t = Mathf.Clamp(t + (Hash(wrapped * 7 + Mathf.RoundToInt(iso * 1000f)) - 0.5f) * 0.7f, 0.08f, 0.92f);
			return new Point { Key = key, Position = Vector3.Lerp(Grid(ax, ay), Grid(bx, by), t), Crossing = true, Exit = exit, Inside = inside };
		}

		private void Wall(Point p, Point q, int low, int high, float bottom, float top, Color colour)
		{
			int pb = Shared(p.Key * 32 + low, p.Position + Vector3.up * bottom, colour);
			int qb = Shared(q.Key * 32 + low, q.Position + Vector3.up * bottom, colour);
			int qt = Shared(q.Key * 32 + high, q.Position + Vector3.up * top, colour);
			int pt = Shared(p.Key * 32 + high, p.Position + Vector3.up * top, colour);
			Triangles.Add(pb);
			Triangles.Add(qb);
			Triangles.Add(qt);
			Triangles.Add(pb);
			Triangles.Add(qt);
			Triangles.Add(pt);
		}

		private int Shared(long key, Vector3 position, Color colour)
		{
			if (!shared.TryGetValue(key, out int index))
			{
				index = Vertices.Count;
				Vertices.Add(position);
				Colours.Add(colour);
				shared.Add(key, index);
			}
			return index;
		}

		private bool Inside(int x, int y)
		{
			return field[x, y] >= iso;
		}

		private long CornerKey(int x, int y)
		{
			return (long)x * (count + 1) + y;
		}

		private Vector3 Grid(int x, int y)
		{
			return new Vector3(x * cell - half, 0f, y * cell - half);
		}

		private float Hash(long n)
		{
			unchecked
			{
				uint h = (uint)n ^ (uint)(n >> 32) ^ (uint)seed;
				h *= 0x9E3779B1u;
				h ^= h >> 15;
				h *= 0x85EBCA77u;
				h ^= h >> 13;
				return (h & 0xffffff) / 16777216f;
			}
		}
	}

	private static void Quad(List<Vector3> vertices, List<Color> colours, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
	{
		int first = vertices.Count;
		vertices.Add(a);
		vertices.Add(b);
		vertices.Add(c);
		vertices.Add(d);
		for (int i = 0; i < 4; i++)
		{
			colours.Add(colour);
		}
		triangles.Add(first);
		triangles.Add(first + 1);
		triangles.Add(first + 2);
		triangles.Add(first);
		triangles.Add(first + 2);
		triangles.Add(first + 3);
	}

	private void LateUpdate()
	{
		Camera camera = Camera.main;
		if (camera == null || hidden)
		{
			return;
		}
		Vector3 eye = camera.transform.position;
		Vector3 goal = world.ToWorld(pillar.Pose.position);
		float density = RenderSettings.fog ? RenderSettings.fogDensity : 0f;
		Color fog = RenderSettings.fogColor;
		Vector3 centre = goal + Vector3.up * (foot + Level());
		// the tile under the eye and the ring round it
		float tileX = Mathf.Round((eye.x - centre.x) / ceilingSize);
		float tileZ = Mathf.Round((eye.z - centre.z) / ceilingSize);
		for (int i = 0; i < ceilings.Count; i++)
		{
			ceilings[i].position = centre + new Vector3((tileX + i % 3 - 1) * ceilingSize, 0f, (tileZ + i / 3 - 1) * ceilingSize);
		}
		ceilingMaterial.SetColor("_FogColor", fog);
		sky.position = eye;
		skyMaterial.SetColor("_FogColor", fog);
		ceilingMaterial.SetFloat("_Density", density * ceilingFogShare);
		ceilingMaterial.SetFloat("_Strength", ceilingStrength);
		ceilingMaterial.SetFloat("_Detail", ceilingDetail);
		// here too, not only per camera, or it sits on the shader's default and fades out far too close
		ceilingMaterial.SetFloat("_FarFade", farClip);
		foreach (Material material in cloudMaterials)
		{
			material.SetFloat("_FarFade", farClip);
		}
		// clouds clear round every tower
		gaps.Clear();
		foreach (Distant distant in pillars)
		{
			Vector3 real = world.ToWorld(distant.Node.Pose.position);
			Quaternion turn = distant.Node.Pose.rotation;
			float distance = Vector3.Distance(eye, real);
			// flat, so riding the elevator up never swaps the real one out from under her
			float flat = new Vector2(real.x - eye.x, real.z - eye.z).magnitude;
			bool near = distant.Node.Instance != null && distant.Node.Instance.activeInHierarchy && flat < swapDistance;
			// up close the real tower stops short of the lifted ceiling, the copy only fills in above it
			float realTop = real.y + foot + height;
			float ceilingY = real.y + foot + Level();
			distant.Copy.gameObject.SetActive(!near || realTop < ceilingY);
			distant.Material.SetFloat("_From", near ? realTop - 1f : -100000f);
			// past the swap the real one is fogged near white, the copy stands in. not once she's up on the next level
			ShowReal(distant, flat < swapDistance || eye.y > real.y + foot + Level() - slabGap);
			float seems = distance;
			float drawn = seems <= farthest ? seems : farthest + (drawDistance * 0.92f - farthest) * (1f - Mathf.Exp((farthest - seems) / 4000f));
			float pull = drawn / Mathf.Max(distance, 1f);
			float shrink = drawn / seems;
			// taller from its foot, the foot stays where it was
			distant.Copy.SetPositionAndRotation(eye + (real - eye) * pull + Vector3.up * foot * shrink * (1f - stretch), turn);
			distant.Copy.localScale = new Vector3(shrink, shrink * stretch, shrink);
			if (gaps.Count < cloudGaps)
			{
				gaps.Add(new Vector4(distant.Copy.position.x, distant.Copy.position.z, girth * shrink, (girth + cloudClearing) * shrink));
			}
			// thin fog far off, world fog up close so the swap doesn't pop
			float thin = Mathf.Exp(-density * towerFogShare * seems);
			float worldFog = Mathf.Exp(-density * seems);
			float visibility = Mathf.Lerp(worldFog, thin, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(swapDistance, 800f, seems)));
			distant.Material.SetColor("_FogColor", fog);
			distant.Material.SetFloat("_Visibility", visibility);
			distant.Material.SetFloat("_Base", distant.Copy.position.y + foot * shrink * stretch);
			distant.Material.SetFloat("_Height", height * shrink * stretch);
			// cut off where the ceiling meets it, unless she's up above the ceiling. the top goes into the ceiling's look there
			distant.Material.SetFloat("_Meet", eye.y > centre.y ? 1f : Level() / (height * stretch));
			distant.Material.SetFloat("_Pull", seems / Mathf.Max(drawn, 1f));
			distant.Material.SetColor("_CeilingTint", ceilingMaterial.GetColor("_Tint"));
			distant.Material.SetFloat("_CeilingDensity", density * ceilingFogShare);
			distant.Material.SetFloat("_CeilingFade", farClip);
			distant.Material.SetFloat("_CeilingStrength", ceilingStrength);
		}
		int used = gaps.Count;
		while (gaps.Count < cloudGaps)
		{
			gaps.Add(Vector4.zero);
		}
		for (int i = 0; i < clouds.Count; i++)
		{
			cloudMaterials[i].SetVectorArray("_Gaps", gaps);
			cloudMaterials[i].SetFloat("_GapCount", used);
			// under the eye so they never end, the pattern stays put relative to the tower
			cloudMaterials[i].SetVector("_Origin", new Vector4(goal.x, goal.z, 0f, 0f));
			clouds[i].SetPositionAndRotation(new Vector3(eye.x, centre.y - cloudLayers[i].x, eye.z), Quaternion.Euler(90f, 0f, 0f));
			cloudMaterials[i].SetColor("_FogColor", fog);
		}
	}
}
