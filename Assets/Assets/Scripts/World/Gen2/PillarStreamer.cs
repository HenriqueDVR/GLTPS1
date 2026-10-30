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

	private readonly Dictionary<long, Spot> byCell = new Dictionary<long, Spot>();

	private readonly HashSet<long> wanted = new HashSet<long>();

	private readonly List<long> leaving = new List<long>();

	private float nextRefresh;

	public void Init(LayerPlan plan, TileStreamer tiles, FloatingOrigin origin, GameObject prefab)
	{
		this.plan = plan;
		this.tiles = tiles;
		floatingOrigin = origin;
		Prefab = prefab;
		towerCentre = Vector3.zero;
		foreach (MeshFilter part in prefab.GetComponentsInChildren<MeshFilter>(true))
		{
			if (part.name == towerParts[0] && part.sharedMesh != null)
			{
				Vector3 centre = prefab.transform.InverseTransformPoint(part.transform.TransformPoint(part.sharedMesh.bounds.center));
				towerCentre = new Vector3(centre.x, 0f, centre.z);
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
		Quaternion turn = Quaternion.Euler(0f, (float)LayerPlan.PillarYaw(plan.WorldSeed, x, z), 0f);
		Vector3 foot = new Vector3((float)info.Position.X, tiles.GroundY, (float)info.Position.Z);
		return new Spot
		{
			CellX = x,
			CellZ = z,
			Pose = new Pose(foot - turn * towerCentre, turn),
			State = info.State
		};
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
		// not a working elevator unless it goes up from this layer (WP-WORLD-3 takes it from there)
		if ((spot.State & PillarState.Up) == 0)
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
