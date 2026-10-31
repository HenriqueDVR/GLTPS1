using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

// world gen v2: the city past the tiles, out to the haze. a box per building lot of the layer's plan (LotPlan), its
// floors and maybe a block on top, no detail. built a tile at a time in 512 m chunks, nearest first, a millisecond or so
// a frame on the main thread (the plans keep caches, so no worker), drawn in thin fog like the towers. inside the
// tiles' reach the shader cuts it away, the real tiles stand there
public class FarCity : MonoBehaviour
{
	private const double chunkSize = 512.0;

	// out to this from either girl
	public const float Reach = 3600f;

	private const float hideRadius = TileStreamer.BuildRadius - 20f;

	private const float refreshInterval = 0.5f;

	// ms a frame, more while a ride builds the layer it goes to
	private const double budgetMs = 1.0;

	private const double rideBudgetMs = 3.0;

	// LotMesh's floor, and how far a building stands in from its lot's edge
	private const double floor = 3.5;

	private const double inset = 1.5;

	// no stack goes higher (LotMesh)
	private const double tallest = 160.0;

	// the world's fog far out
	private const float fogShare = 0.22f;

	private LayerPlan plan;

	private TileStreamer tiles;

	private FloatingOrigin floatingOrigin;

	private float deck;

	private Material material;

	private readonly Dictionary<long, GameObject> chunks = new Dictionary<long, GameObject>();

	private readonly HashSet<long> wanted = new HashSet<long>();

	private readonly List<long> leaving = new List<long>();

	private float nextRefresh;

	private bool shown = true;

	private readonly List<long> queue = new List<long>();

	private readonly List<Lot> lots = new List<Lot>();

	private readonly Stopwatch stopwatch = new Stopwatch();

	// the chunk being built, and how many of its tiles are in
	private Result building;

	private sealed class Result
	{
		public long Key;

		public int Tile;

		public readonly List<Vector3> Vertices = new List<Vector3>();

		public readonly List<Color32> Colours = new List<Color32>();

		public readonly List<int> Triangles = new List<int>();
	}

	public void Init(LayerPlan plan, TileStreamer tiles, FloatingOrigin origin)
	{
		this.plan = plan;
		this.tiles = tiles;
		floatingOrigin = origin;
	}

	// riding up, the layer above's city stays hidden till they're over its deck: from under it the boxes would hang
	public void Show(bool show)
	{
		if (shown == show)
		{
			return;
		}
		shown = show;
		foreach (GameObject chunk in chunks.Values)
		{
			if (chunk != null)
			{
				chunk.SetActive(show);
			}
		}
	}

	private void Start()
	{
		material = new Material(Resources.Load<Shader>("FarCity"));
		Material wall = Resources.Load<Material>("Buildings/Wall");
		material.SetColor("_Albedo", (wall != null) ? DistantPillar.Albedo(wall) : new Color(0.6f, 0.6f, 0.62f));
		material.SetFloat("_Share", fogShare);
	}

	private void OnDestroy()
	{
		foreach (GameObject chunk in chunks.Values)
		{
			if (chunk != null)
			{
				Destroy(chunk.GetComponent<MeshFilter>().sharedMesh);
				Destroy(chunk);
			}
		}
		if (material != null)
		{
			Destroy(material);
		}
	}

	private Vector3 Offset()
	{
		return (floatingOrigin != null) ? floatingOrigin.TotalOffset : Vector3.zero;
	}

	private static long Key(int x, int z)
	{
		return ((long)x << 32) ^ (uint)z;
	}

	private void Update()
	{
		if (plan == null || tiles == null || tiles.Points.Count == 0)
		{
			return;
		}
		deck = tiles.GroundY;
		if (Time.time >= nextRefresh)
		{
			nextRefresh = Time.time + refreshInterval;
			Refresh();
		}
		Build(LayerTransition.Busy ? rideBudgetMs : budgetMs);
	}

	private void LateUpdate()
	{
		if (material == null || tiles == null || tiles.Points.Count == 0)
		{
			return;
		}
		Vector3 offset = Offset();
		Vector3 a = tiles.Points[0] - offset;
		Vector3 b = (tiles.Points.Count > 1) ? tiles.Points[1] - offset : a;
		material.SetVector("_Hide", new Vector4(a.x, a.z, b.x, b.z));
		// where the tiles are still building (a layer just reached) the boxes stand in right up close
		material.SetFloat("_HideRadius", Mathf.Min(hideRadius, tiles.BuiltRadius - 20f));
		material.SetColor("_FogColor", RenderSettings.fogColor);
		material.SetFloat("_Density", RenderSettings.fog ? RenderSettings.fogDensity : 0f);
	}

	private void Refresh()
	{
		List<Vector3> points = tiles.Points;
		wanted.Clear();
		foreach (Vector3 p in points)
		{
			int x0 = (int)System.Math.Floor((p.x - Reach) / chunkSize);
			int x1 = (int)System.Math.Floor((p.x + Reach) / chunkSize);
			int z0 = (int)System.Math.Floor((p.z - Reach) / chunkSize);
			int z1 = (int)System.Math.Floor((p.z + Reach) / chunkSize);
			for (int x = x0; x <= x1; x++)
			{
				for (int z = z0; z <= z1; z++)
				{
					if (Nearest(points, x, z) < Reach)
					{
						wanted.Add(Key(x, z));
					}
				}
			}
		}
		leaving.Clear();
		foreach (KeyValuePair<long, GameObject> chunk in chunks)
		{
			if (!wanted.Contains(chunk.Key) && Nearest(points, (int)(chunk.Key >> 32), (int)(uint)chunk.Key) > Reach + chunkSize)
			{
				leaving.Add(chunk.Key);
			}
		}
		foreach (long key in leaving)
		{
			GameObject chunk = chunks[key];
			chunks.Remove(key);
			queue.Remove(key);
			if (building != null && building.Key == key)
			{
				building = null;
			}
			if (chunk != null)
			{
				Destroy(chunk.GetComponent<MeshFilter>().sharedMesh);
				Destroy(chunk);
			}
		}
		foreach (long key in wanted)
		{
			if (!chunks.ContainsKey(key))
			{
				chunks.Add(key, null);
				queue.Add(key);
			}
		}
		// nearest last, taken from the end
		Vector3 focus = points[0];
		queue.Sort((a, b) => Centre(b, focus).CompareTo(Centre(a, focus)));
	}

	private static float Centre(long key, Vector3 focus)
	{
		float dx = (float)(((int)(key >> 32) + 0.5) * chunkSize) - focus.x;
		float dz = (float)(((int)(uint)key + 0.5) * chunkSize) - focus.z;
		return dx * dx + dz * dz;
	}

	// a tile at a time while the budget lasts, a chunk goes up when its last tile's in
	private void Build(double budget)
	{
		int per = (int)(chunkSize / Gen2.TileSize);
		stopwatch.Restart();
		while (stopwatch.Elapsed.TotalMilliseconds < budget)
		{
			if (building == null)
			{
				if (queue.Count == 0)
				{
					return;
				}
				building = new Result { Key = queue[queue.Count - 1] };
				queue.RemoveAt(queue.Count - 1);
			}
			Tile(plan, (int)(building.Key >> 32), (int)(uint)building.Key, building.Tile, lots, building);
			building.Tile++;
			if (building.Tile == per * per)
			{
				Upload(building);
				building = null;
			}
		}
	}

	// flat distance from the nearest girl to the chunk's nearest point
	private static float Nearest(List<Vector3> points, int x, int z)
	{
		float best = float.MaxValue;
		foreach (Vector3 p in points)
		{
			float dx = Mathf.Max(0f, Mathf.Max((float)(x * chunkSize) - p.x, p.x - (float)((x + 1) * chunkSize)));
			float dz = Mathf.Max(0f, Mathf.Max((float)(z * chunkSize) - p.z, p.z - (float)((z + 1) * chunkSize)));
			best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
		}
		return best;
	}

	private void Upload(Result result)
	{
		if (!chunks.ContainsKey(result.Key) || chunks[result.Key] != null)
		{
			return;
		}
		int x = (int)(result.Key >> 32);
		int z = (int)(uint)result.Key;
		GameObject chunk = new GameObject("far city " + x + "," + z, typeof(MeshFilter), typeof(MeshRenderer));
		chunk.transform.position = new Vector3((float)(x * chunkSize), deck, (float)(z * chunkSize)) - Offset();
		Mesh mesh = new Mesh { name = "far city", indexFormat = (result.Vertices.Count > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16 };
		mesh.SetVertices(result.Vertices);
		mesh.SetColors(result.Colours);
		mesh.SetTriangles(result.Triangles, 0);
		mesh.RecalculateBounds();
		chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer renderer = chunk.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = material;
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		chunk.SetActive(shown);
		chunks[result.Key] = chunk;
	}

	// one tile of a chunk, in chunk space
	private static void Tile(LayerPlan plan, int chunkX, int chunkZ, int index, List<Lot> lots, Result result)
	{
		int per = (int)(chunkSize / Gen2.TileSize);
		double x0 = chunkX * chunkSize;
		double z0 = chunkZ * chunkSize;
		lots.Clear();
		plan.Lots.Collect(chunkX * per + index % per, chunkZ * per + index / per, lots);
		foreach (Lot lot in lots)
		{
			if (lot.Kind != LotKind.Building)
			{
				continue;
			}
			DistrictDef district = plan.Def.Districts[lot.District];
			double height = lot.Floors * floor;
			double bottom = lot.Base;
			double shade = 0.92 + 0.16 * Gen2.Unit(WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 0xFA00uL));
			XZ centre = new XZ(lot.Center.X - x0, lot.Center.Z - z0);
			Box(centre, lot.Axis, lot.HalfU - inset, lot.HalfV - inset, bottom, bottom + height, shade, result);
			// a block on top, like LotMesh's stacking, roughly
			if (Gen2.Unit(WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 0xFA01uL)) < district.Stacking)
			{
				double share = 0.45 + 0.4 * Gen2.Unit(WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 0xFA02uL));
				double more = (district.FloorsMin + (district.FloorsMax - district.FloorsMin) * Gen2.Unit(WorldRandom.Derive(lot.Seed, Gen2.StreamLot, 0xFA03uL))) * floor;
				double top = System.Math.Min(bottom + height + more, bottom + tallest);
				if (top > bottom + height)
				{
					Box(centre, lot.Axis, (lot.HalfU - inset) * share, (lot.HalfV - inset) * share, bottom + height, top, shade, result);
				}
			}
		}
	}

	// four walls and a roof, chunk space
	private static void Box(XZ centre, XZ axis, double halfU, double halfV, double bottom, double top, double shade, Result result)
	{
		if (halfU <= 0.5 || halfV <= 0.5 || top <= bottom)
		{
			return;
		}
		Vector3 u = new Vector3((float)(axis.X * halfU), 0f, (float)(axis.Z * halfU));
		Vector3 v = new Vector3((float)(-axis.Z * halfV), 0f, (float)(axis.X * halfV));
		Vector3 c = new Vector3((float)centre.X, 0f, (float)centre.Z);
		Vector3 low = Vector3.up * (float)bottom;
		Vector3 high = Vector3.up * (float)top;
		Vector3[] corners = { c - u - v, c + u - v, c + u + v, c - u + v };
		for (int k = 0; k < 4; k++)
		{
			Vector3 a = corners[k];
			Vector3 b = corners[(k + 1) % 4];
			Vector3 side = Vector3.Cross(Vector3.up, b - a).normalized;
			// faces towards x a little lighter than towards z, so the boxes read
			float lit = (float)shade * (0.62f + 0.16f * Mathf.Abs(side.x));
			Quad(a + low, b + low, b + high, a + high, lit, result);
		}
		Quad(corners[0] + high, corners[1] + high, corners[2] + high, corners[3] + high, (float)shade * 0.95f, result);
	}

	private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float lit, Result result)
	{
		int at = result.Vertices.Count;
		result.Vertices.Add(a);
		result.Vertices.Add(b);
		result.Vertices.Add(c);
		result.Vertices.Add(d);
		byte g = (byte)Mathf.Clamp(lit * 255f, 0f, 255f);
		Color32 colour = new Color32(g, g, g, 255);
		for (int i = 0; i < 4; i++)
		{
			result.Colours.Add(colour);
		}
		result.Triangles.Add(at);
		result.Triangles.Add(at + 2);
		result.Triangles.Add(at + 1);
		result.Triangles.Add(at);
		result.Triangles.Add(at + 3);
		result.Triangles.Add(at + 2);
	}
}
