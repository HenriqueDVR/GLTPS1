using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// world gen v2: 128m tiles around both girls, nearest first, a couple of ms a frame, pooled when out of reach.
// tiles are placeholder flat ground tinted by district for now. everything is in layout coords (world + TotalOffset)
public class TileStreamer : MonoBehaviour
{
	public const float BuildRadius = 700f;

	// tiles stay until this much further out, so they don't flicker at the edge
	private const float keepMargin = 128f;

	private const double budgetMs = 2.0;

	private const float refreshInterval = 0.25f;

	private const float thickness = 2f;

	private static readonly Color[] districtColors =
	{
		new Color(0.78f, 0.80f, 0.82f),
		new Color(0.55f, 0.52f, 0.45f),
		new Color(0.45f, 0.55f, 0.60f),
		new Color(0.62f, 0.58f, 0.70f),
		new Color(0.70f, 0.62f, 0.48f),
		new Color(0.50f, 0.62f, 0.50f)
	};

	public LayerPlan Plan { get; private set; }

	public int TileCount => tiles.Count;

	private FloatingOrigin floatingOrigin;

	// layout y of the deck surface, taken from the floor under Chito at the start
	private float groundY;

	private int groundLayer;

	private Material material;

	private MaterialPropertyBlock block;

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
		material = (lit != null) ? new Material(lit) : floor;
		block = new MaterialPropertyBlock();
#if DUMP_WORLDGEN
		run = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-gen2run") >= 0;
#endif
		// everything in reach before the first frame, like the legacy BuildAround
		Refresh();
		BuildMissing(double.MaxValue);
	}

	private void OnDestroy()
	{
		if (material != null && material.shader != null && material.shader.name == "Universal Render Pipeline/Lit")
		{
			Destroy(material);
		}
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

	private bool run;

	private void LateUpdate()
	{
		// -gen2run: both girls slide along +x at 60 m/s, for streaming and floating origin tests
		if (run && chito != null)
		{
			Vector3 step = new Vector3(60f * Time.deltaTime, 0f, 0f);
			chito.position += step;
			if (yuuri != null)
			{
				yuuri.position += step;
			}
			Physics.SyncTransforms();
		}
		if (Time.unscaledTime >= nextLog)
		{
			nextLog = Time.unscaledTime + 5f;
			Vector3 p = (chito != null) ? chito.position + Offset() : Vector3.zero;
			UnityEngine.Debug.Log("TileStreamer: tiles=" + tiles.Count + " pooled=" + pool.Count + " queued=" + missing.Count + " chito=" + p + " offset=" + Offset() + " district=" + Plan.Def.Districts[Plan.DistrictAt(p.x, p.z)].Id);
		}
	}
#endif

	private void Update()
	{
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
		GameObject tile;
		if (pool.Count > 0)
		{
			tile = pool.Pop();
		}
		else
		{
			tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
			tile.GetComponent<Renderer>().sharedMaterial = material;
		}
		tile.name = "tile " + x + "," + z;
		tile.layer = groundLayer;
		float size = (float)Gen2.TileSize;
		Vector3 layout = new Vector3((x + 0.5f) * size, groundY - thickness * 0.5f - 0.02f, (z + 0.5f) * size);
		tile.transform.SetPositionAndRotation(layout - Offset(), Quaternion.identity);
		tile.transform.localScale = new Vector3(size, thickness, size);
		int district = Plan.DistrictAt((x + 0.5) * Gen2.TileSize, (z + 0.5) * Gen2.TileSize);
		Color color = districtColors[district % districtColors.Length];
		block.SetColor("_BaseColor", color);
		block.SetColor("_Color", color);
		tile.GetComponent<Renderer>().SetPropertyBlock(block);
		tile.SetActive(true);
		return tile;
	}
}
