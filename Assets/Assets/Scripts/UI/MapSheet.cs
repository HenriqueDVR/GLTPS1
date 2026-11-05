using System.Collections.Generic;
using UnityEngine;

// what's been seen (12): tiles either girl has been near, one set per layer, saved and handed to a joining friend. each
// peer reveals from both girls as it sees them. a tile's sketch comes from the plan, one a frame, so the paper never
// waits on it. only what's been seen is ever drawn, and places show up marked once their tile has been
public class MapSheet : MonoBehaviour
{
	public enum MarkKind
	{
		Fuel,
		Water,
		Shelter,
		Tower,
		TowerUp
	}

	public struct Mark
	{
		public MarkKind Kind;

		public Vector2 At;
	}

	public sealed class Tile
	{
		public int X;

		public int Z;

		// road pieces: a, b (layout xz), half width in z of a third pair
		public readonly List<Vector4> Roads = new List<Vector4>();

		public readonly List<float> RoadWidths = new List<float>();

		// lots: centre, u axis, half sizes
		public readonly List<Vector4> Lots = new List<Vector4>();

		public readonly List<Vector2> LotHalves = new List<Vector2>();

		// 8 x 8 of the tile: true where it's void
		public readonly List<Vector2> Voids = new List<Vector2>();

		public readonly List<Mark> Marks = new List<Mark>();
	}

	public const float VoidCell = 16f;

	private const float chitoSees = 260f;

	private const float yuuriSees = 180f;

	private const float checkEvery = 0.5f;

	private static readonly Dictionary<int, HashSet<long>> revealed = new Dictionary<int, HashSet<long>>();

	public static int Version { get; private set; }

	private static MapSheet instance;

	private readonly Dictionary<long, Tile> sketches = new Dictionary<long, Tile>();

	private readonly Queue<long> todo = new Queue<long>();

	private readonly List<RoadPiece> pieces = new List<RoadPiece>();

	private readonly List<RoadPatch> patches = new List<RoadPatch>();

	private readonly List<Lot> lots = new List<Lot>();

	private int sketchLayer = -1;

	private float nextCheck;

	public static void ResetLevel()
	{
		revealed.Clear();
		Version++;
	}

	private static long Key(int x, int z)
	{
		return ((long)x << 32) ^ (uint)z;
	}

	public static bool Seen(int layer, int x, int z)
	{
		return revealed.TryGetValue(layer, out HashSet<long> tiles) && tiles.Contains(Key(x, z));
	}

	public static IEnumerable<Tile> Tiles => (instance != null) ? instance.sketches.Values : System.Linq.Enumerable.Empty<Tile>();

	private void Awake()
	{
		instance = this;
	}

	private void OnDestroy()
	{
		if (instance == this)
		{
			instance = null;
		}
	}

	private void Update()
	{
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		if (world == null || world.Plan == null || LayerTransition.Busy)
		{
			return;
		}
		if (sketchLayer != world.Layer)
		{
			sketchLayer = world.Layer;
			sketches.Clear();
			todo.Clear();
			Version++;
			if (revealed.TryGetValue(sketchLayer, out HashSet<long> known))
			{
				foreach (long key in known)
				{
					todo.Enqueue(key);
				}
			}
		}
		if (Time.time >= nextCheck)
		{
			nextCheck = Time.time + checkEvery;
			Vector3 offset = SaveScene.Offset();
			if (Girls.Chito != null)
			{
				Reveal(Girls.Chito.transform.position + offset, chitoSees);
			}
			if (Girls.Yuuri != null)
			{
				Reveal(Girls.Yuuri.transform.position + offset, yuuriSees);
			}
		}
		if (todo.Count > 0)
		{
			long key = todo.Dequeue();
			if (!sketches.ContainsKey(key))
			{
				sketches[key] = Sketch(world.Plan, (int)(key >> 32), (int)(uint)key);
				Version++;
			}
		}
	}

	private void Reveal(Vector3 layout, float radius)
	{
		if (!revealed.TryGetValue(sketchLayer, out HashSet<long> tiles))
		{
			tiles = new HashSet<long>();
			revealed[sketchLayer] = tiles;
		}
		float size = (float)Gen2.TileSize;
		int x0 = Mathf.FloorToInt((layout.x - radius) / size);
		int x1 = Mathf.FloorToInt((layout.x + radius) / size);
		int z0 = Mathf.FloorToInt((layout.z - radius) / size);
		int z1 = Mathf.FloorToInt((layout.z + radius) / size);
		for (int x = x0; x <= x1; x++)
		{
			for (int z = z0; z <= z1; z++)
			{
				float dx = (x + 0.5f) * size - layout.x;
				float dz = (z + 0.5f) * size - layout.z;
				if (dx * dx + dz * dz <= radius * radius && tiles.Add(Key(x, z)))
				{
					todo.Enqueue(Key(x, z));
				}
			}
		}
	}

	private Tile Sketch(LayerPlan plan, int x, int z)
	{
		Tile tile = new Tile { X = x, Z = z };
		plan.Roads.Collect(x, z, pieces, patches);
		foreach (RoadPiece piece in pieces)
		{
			tile.Roads.Add(new Vector4((float)piece.A.X, (float)piece.A.Z, (float)piece.B.X, (float)piece.B.Z));
			tile.RoadWidths.Add((float)piece.Width);
		}
		plan.Lots.Collect(x, z, lots);
		foreach (Lot lot in lots)
		{
			tile.Lots.Add(new Vector4((float)lot.Center.X, (float)lot.Center.Z, (float)lot.Axis.X, (float)lot.Axis.Z));
			tile.LotHalves.Add(new Vector2((float)lot.HalfU, (float)lot.HalfV));
		}
		float size = (float)Gen2.TileSize;
		for (int i = 0; i < 8; i++)
		{
			for (int j = 0; j < 8; j++)
			{
				Vector2 at = new Vector2((x + (i + 0.5f) / 8f) * size, (z + (j + 0.5f) / 8f) * size);
				if (plan.Chasms.Inside(at.x, at.y) > 0.0)
				{
					tile.Voids.Add(at);
				}
			}
		}
		if (plan.Places.InTile(x, z, out Place place))
		{
			MarkKind kind = (place.Kind == PlaceKind.FuelDepot) ? MarkKind.Fuel : (place.Kind == PlaceKind.PumpHouse) ? MarkKind.Water : MarkKind.Shelter;
			tile.Marks.Add(new Mark { Kind = kind, At = new Vector2((float)place.Center.X, (float)place.Center.Z) });
		}
		double middleX = (x + 0.5) * Gen2.TileSize;
		double middleZ = (z + 0.5) * Gen2.TileSize;
		if (plan.TowerNear(middleX, middleZ, out XZ tower) && Gen2.Cell(tower.X, Gen2.TileSize) == x && Gen2.Cell(tower.Z, Gen2.TileSize) == z)
		{
			bool up = (plan.PillarNear(middleX, middleZ).State & PillarState.Up) != 0;
			tile.Marks.Add(new Mark { Kind = up ? MarkKind.TowerUp : MarkKind.Tower, At = new Vector2((float)tower.X, (float)tower.Z) });
		}
		return tile;
	}

	public class State : IStateful
	{
		public const string SectionKey = "map";

		public string Key => SectionKey;

		public int Version => 1;

		public void Write(StateWriter writer)
		{
			writer.Put(revealed.Count);
			foreach (KeyValuePair<int, HashSet<long>> layer in revealed)
			{
				writer.Put(layer.Key);
				writer.Put(layer.Value.Count);
				foreach (long tile in layer.Value)
				{
					writer.Put((int)(tile >> 32));
					writer.Put((int)(uint)tile);
				}
			}
		}

		public void Read(StateReader reader, int version)
		{
			revealed.Clear();
			int layers = reader.GetInt();
			for (int l = 0; l < layers; l++)
			{
				int layer = reader.GetInt();
				int count = reader.GetInt();
				HashSet<long> tiles = new HashSet<long>();
				for (int i = 0; i < count; i++)
				{
					int x = reader.GetInt();
					tiles.Add(MapSheet.Key(x, reader.GetInt()));
				}
				revealed[layer] = tiles;
			}
			MapSheet.Version++;
			if (instance != null)
			{
				instance.sketchLayer = -1;
			}
		}
	}
}
