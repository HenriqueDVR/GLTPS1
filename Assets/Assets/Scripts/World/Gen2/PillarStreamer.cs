using System.Collections.Generic;
using UnityEngine;

// world gen v2: the megastructure towers on the layer's pillar grid (01, megastructure). the real nodeCOLUMN tower
// stands near the girls, DistantPillar draws copies out to the horizon and the deck above. only the tower and its
// elevator: the old piece's walled yard is 400 m across, roads and lots leave room for the tower alone
public class PillarStreamer : MonoBehaviour
{
	// real towers within this of either girl, gone again past it plus the margin
	private const float realRadius = 1100f;

	private const float keepMargin = 200f;

	// region cells each way round the eye that get a far copy, about the far clip
	private const int farCells = 4;

	private const float refreshInterval = 0.5f;

	// the parts of nodeCOLUMN kept in v2, the rest is the old walled yard and the next level's slabs (the ceiling does those)
	private static readonly string[] towerParts = { "Cylinder", "Thingys", "Elevator", "Stones", "Scraps" };

	public sealed class Spot
	{
		public int CellX;

		public int CellZ;

		// root of the piece, layout coords
		public Pose Pose;

		public PillarState State;

		// the real one, null when far
		public GameObject Instance;
	}

	public readonly List<Spot> Spots = new List<Spot>();

	// goes up whenever Spots changes
	public int Version { get; private set; }

	public GameObject Prefab { get; private set; }

	public float GroundY => tiles.GroundY;

	private LayerPlan plan;

	private TileStreamer tiles;

	private FloatingOrigin floatingOrigin;

	// from the root to the middle of the tower's foot, so the tower stands on the grid point
	private Vector3 towerCentre;

	// the elevator car's footprint from the root, flat: a working elevator from below comes up through a hole this size
	private Vector2 carMin;

	private Vector2 carMax;

	private const float carMargin = 0.2f;

	// the tower the elevator came up or down with, standing on here
	private Spot arrived;

	private readonly Dictionary<long, Spot> byCell = new Dictionary<long, Spot>();

	private readonly HashSet<long> wanted = new HashSet<long>();

	private readonly List<long> leaving = new List<long>();

	private float nextRefresh;

	public void Init(LayerPlan plan, TileStreamer tiles, FloatingOrigin origin, GameObject prefab, Spot arrived = null)
	{
		this.plan = plan;
		this.tiles = tiles;
		floatingOrigin = origin;
		Prefab = prefab;
		this.arrived = arrived;
		towerCentre = Vector3.zero;
		carMin = Vector2.zero;
		carMax = Vector2.zero;
		foreach (MeshFilter part in prefab.GetComponentsInChildren<MeshFilter>(true))
		{
			if (part.name == towerParts[0] && part.sharedMesh != null)
			{
				Vector3 centre = prefab.transform.InverseTransformPoint(part.transform.TransformPoint(part.sharedMesh.bounds.center));
				towerCentre = new Vector3(centre.x, 0f, centre.z);
			}
			if (part.name == "Elevator" && part.sharedMesh != null)
			{
				Bounds b = part.sharedMesh.bounds;
				carMin = new Vector2(float.MaxValue, float.MaxValue);
				carMax = new Vector2(float.MinValue, float.MinValue);
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
					Vector3 local = prefab.transform.InverseTransformPoint(part.transform.TransformPoint(corner));
					carMin = Vector2.Min(carMin, new Vector2(local.x, local.z));
					carMax = Vector2.Max(carMax, new Vector2(local.x, local.z));
				}
			}
		}
		tiles.MoreHoles = Shafts;
	}

	// which way its elevator goes from here: 1 up, -1 down, 0 none. co-op rides keep the old ending until WP-WORLD-4
	public static int Way(PillarState state)
	{
		if (CoopSession.InGame)
		{
			return ((state & PillarState.Up) != 0) ? 1 : 0;
		}
		return LayerStack.Way(state);
	}

	public Spot SpotOf(GameObject tower)
	{
		foreach (Spot spot in Spots)
		{
			if (spot.Instance == tower)
			{
				return spot;
			}
		}
		return null;
	}

	// the riding tower leaves with the elevator, it isn't this layer's to clear away
	public void Release(Spot spot)
	{
		spot.Instance = null;
		Version++;
	}

	// where the tower's root stands, a pure function of (world seed, cell) and the deck
	private Pose RootPose(int x, int z, PillarInfo info)
	{
		Quaternion turn = Quaternion.Euler(0f, (float)LayerPlan.PillarYaw(plan.WorldSeed, x, z), 0f);
		Vector3 foot = new Vector3((float)info.Position.X, tiles.GroundY, (float)info.Position.Z);
		return new Pose(foot - turn * towerCentre, turn);
	}

	// a hole in the deck under each car that comes up from the layer below, so it comes up through it.
	// layout coords, the same on every peer
	private void Shafts(int tileX, int tileZ, List<Area> holes)
	{
		if (carMax.x <= carMin.x)
		{
			return;
		}
		double size = Gen2.TileSize;
		Area tile = new Area { X0 = tileX * size, Z0 = tileZ * size, X1 = (tileX + 1) * size, Z1 = (tileZ + 1) * size };
		for (int x = Gen2.Cell(tile.X0, Gen2.RegionSize); x <= Gen2.Cell(tile.X1, Gen2.RegionSize); x++)
		{
			for (int z = Gen2.Cell(tile.Z0, Gen2.RegionSize); z <= Gen2.Cell(tile.Z1, Gen2.RegionSize); z++)
			{
				PillarInfo info = plan.Pillar(x, z);
				if ((info.State & PillarState.Down) == 0)
				{
					continue;
				}
				Pose root = RootPose(x, z, info);
				Vector3 a = root.position + root.rotation * new Vector3(carMin.x - carMargin, 0f, carMin.y - carMargin);
				Vector3 b = root.position + root.rotation * new Vector3(carMax.x + carMargin, 0f, carMax.y + carMargin);
				Area hole = new Area { X0 = Mathf.Min(a.x, b.x), Z0 = Mathf.Min(a.z, b.z), X1 = Mathf.Max(a.x, b.x), Z1 = Mathf.Max(a.z, b.z) };
				if (hole.X1 > tile.X0 && hole.X0 < tile.X1 && hole.Z1 > tile.Z0 && hole.Z0 < tile.Z1)
				{
					holes.Add(hole);
				}
			}
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
		if (plan == null || Prefab == null || tiles == null || Time.time < nextRefresh)
		{
			return;
		}
		nextRefresh = Time.time + refreshInterval;
		Refresh();
	}

	private void OnDestroy()
	{
		foreach (Spot spot in Spots)
		{
			if (spot.Instance != null)
			{
				Destroy(spot.Instance);
			}
		}
	}

	private void Refresh()
	{
		Camera camera = Camera.main;
		Vector3 eye = (camera != null) ? camera.transform.position + Offset() : (tiles.Points.Count > 0 ? tiles.Points[0] : Vector3.zero);
		int cx = Gen2.Cell(eye.x, Gen2.RegionSize);
		int cz = Gen2.Cell(eye.z, Gen2.RegionSize);
		wanted.Clear();
		for (int x = cx - farCells; x <= cx + farCells; x++)
		{
			for (int z = cz - farCells; z <= cz + farCells; z++)
			{
				wanted.Add(Key(x, z));
			}
		}
		bool changed = false;
		leaving.Clear();
		foreach (KeyValuePair<long, Spot> pair in byCell)
		{
			if (!wanted.Contains(pair.Key))
			{
				leaving.Add(pair.Key);
			}
		}
		foreach (long key in leaving)
		{
			Spot spot = byCell[key];
			if (spot.Instance != null)
			{
				Destroy(spot.Instance);
			}
			byCell.Remove(key);
			Spots.Remove(spot);
			changed = true;
		}
		for (int x = cx - farCells; x <= cx + farCells; x++)
		{
			for (int z = cz - farCells; z <= cz + farCells; z++)
			{
				if (!byCell.ContainsKey(Key(x, z)))
				{
					Spot spot = Make(x, z);
					byCell.Add(Key(x, z), spot);
					Spots.Add(spot);
					changed = true;
				}
			}
		}
		// real towers round both girls
		foreach (Spot spot in Spots)
		{
			if (arrived != null && spot.Instance == null && spot.CellX == arrived.CellX && spot.CellZ == arrived.CellZ && arrived.Instance != null)
			{
				spot.Instance = arrived.Instance;
				arrived = null;
				changed = true;
			}
			float near = float.MaxValue;
			foreach (Vector3 p in tiles.Points)
			{
				near = Mathf.Min(near, new Vector2(p.x - spot.Pose.position.x, p.z - spot.Pose.position.z).magnitude);
			}
			if (spot.Instance == null && near < realRadius)
			{
				spot.Instance = Real(spot);
				changed = true;
			}
			else if (spot.Instance != null && near > realRadius + keepMargin)
			{
				Destroy(spot.Instance);
				spot.Instance = null;
				changed = true;
			}
		}
		if (changed)
		{
			Version++;
		}
	}

	// a pure function of (world seed, layer, cell), so co-op peers get the same tower
	private Spot Make(int x, int z)
	{
		PillarInfo info = plan.Pillar(x, z);
		return new Spot
		{
			CellX = x,
			CellZ = z,
			Pose = RootPose(x, z, info),
			State = info.State
		};
	}

	// just the tower, nothing to stand on or use: what an elevator riding down goes past before the layer under is built
	public GameObject Bare(Pose pose)
	{
		GameObject tower = Instantiate(Prefab, pose.position - Offset(), pose.rotation);
		tower.name = "pillar below";
		Transform scene = tower.transform.GetChild(0);
		for (int i = scene.childCount - 1; i >= 0; i--)
		{
			GameObject part = scene.GetChild(i).gameObject;
			if (part.name != towerParts[0])
			{
				DestroyImmediate(part);
			}
		}
		foreach (Collider collider in tower.GetComponentsInChildren<Collider>(true))
		{
			Destroy(collider);
		}
		tower.GetComponent<Node>().MarkPlaced();
		GraphicsTuning.Adopt(tower);
		return tower;
	}

	private GameObject Real(Spot spot)
	{
		GameObject tower = Instantiate(Prefab, spot.Pose.position - Offset(), spot.Pose.rotation);
		tower.name = "pillar " + spot.CellX + "," + spot.CellZ;
		Transform scene = tower.transform.GetChild(0);
		for (int i = scene.childCount - 1; i >= 0; i--)
		{
			GameObject part = scene.GetChild(i).gameObject;
			if (System.Array.IndexOf(towerParts, part.name) < 0)
			{
				DestroyImmediate(part);
			}
		}
		// a working elevator where it goes up or down from this layer (LayerTransition)
		if (Way(spot.State) == 0)
		{
			foreach (Collider collider in tower.GetComponentsInChildren<Collider>(true))
			{
				if (collider.CompareTag("Elevator") || collider.CompareTag("ElevatorTrigger"))
				{
					collider.tag = "Untagged";
				}
				if (collider.isTrigger)
				{
					Destroy(collider);
				}
			}
			foreach (PillarNodeAnimationEvents events in tower.GetComponentsInChildren<PillarNodeAnimationEvents>(true))
			{
				events.enabled = false;
			}
		}
		// the root's footprint box was the old yard's
		foreach (BoxCollider box in tower.GetComponents<BoxCollider>())
		{
			Destroy(box);
		}
		tower.GetComponent<Node>().MarkPlaced();
		DistantPillar.HideBrokenParts(tower);
		GraphicsTuning.Adopt(tower);
		return tower;
	}
}
