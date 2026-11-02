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

		// its tower rode off to another layer, none here till this layer's gone
		public bool Gone;

		// the lined hole a car comes up through, the layer's not the tower's: it stays when the tower rides off and is
		// there before one rides in
		public GameObject Head;
	}

	public readonly List<Spot> Spots = new List<Spot>();

	// goes up whenever Spots changes
	public int Version { get; private set; }

	public GameObject Prefab { get; private set; }

	public LayerPlan Plan => plan;

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

	// which way its elevator goes from here: 1 up, -1 down, 0 none
	public static int Way(PillarState state)
	{
		return LayerStack.Way(state);
	}

	// layout coords of the middle of the cell's elevator car standing on the deck
	public Vector3 CarPoint(int cellX, int cellZ)
	{
		Pose root = RootPose(cellX, cellZ, plan.Pillar(cellX, cellZ));
		Vector2 middle = (carMin + carMax) * 0.5f;
		return root.position + root.rotation * new Vector3(middle.x, 0f, middle.y);
	}

	// out of the car's door, along the deck
	public Vector3 Outward(int cellX, int cellZ)
	{
		return RootPose(cellX, cellZ, plan.Pillar(cellX, cellZ)).rotation * Vector3.right;
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
		spot.Gone = true;
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
			if (spot.Head != null)
			{
				Destroy(spot.Head);
			}
		}
		if (headMesh != null)
		{
			Destroy(headMesh);
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
			if (spot.Head != null)
			{
				Destroy(spot.Head);
			}
			byCell.Remove(key);
			Spots.Remove(spot);
			changed = true;
		}
		for (int x = cx - farCells; x <= cx + farCells; x++)
		{
			for (int z = cz - farCells; z <= cz + farCells; z++)
			{
				// only towers that go somewhere stand (LayerPlan.HasTower)
				if (!byCell.ContainsKey(Key(x, z)) && plan.HasTower(x, z))
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
			Head(spot, near < realRadius);
			if (spot.Gone)
			{
				continue;
			}
			if (arrived != null && spot.Instance == null && spot.CellX == arrived.CellX && spot.CellZ == arrived.CellZ)
			{
				// held for the tower that's riding here, till it comes
				if (arrived.Instance == null)
				{
					continue;
				}
				spot.Instance = arrived.Instance;
				arrived = null;
				changed = true;
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
		// made from the ride's animation events, where nothing can be destroyed right away: off now, gone after
		Transform scene = tower.transform.GetChild(0);
		for (int i = scene.childCount - 1; i >= 0; i--)
		{
			GameObject part = scene.GetChild(i).gameObject;
			if (part.name != towerParts[0])
			{
				part.SetActive(false);
				Destroy(part);
			}
		}
		foreach (Collider collider in tower.GetComponentsInChildren<Collider>(true))
		{
			collider.enabled = false;
			Destroy(collider);
		}
		tower.GetComponent<Node>().MarkPlaced();
		Reach(tower);
		GraphicsTuning.Adopt(tower);
		return tower;
	}

	private GameObject Real(Spot spot)
	{
		GameObject tower = Instantiate(Prefab, spot.Pose.position - Offset(), spot.Pose.rotation);
		tower.name = "pillar " + spot.CellX + "," + spot.CellZ;
		Transform scene = tower.transform.GetChild(0);
		// an elevator only where it goes up or down from this layer (LayerTransition)
		bool working = Way(spot.State) != 0;
		for (int i = scene.childCount - 1; i >= 0; i--)
		{
			GameObject part = scene.GetChild(i).gameObject;
			if (System.Array.IndexOf(towerParts, part.name) < 0 || (!working && part.name == "Elevator"))
			{
				DestroyImmediate(part);
			}
		}
		// the root's footprint box was the old yard's
		foreach (BoxCollider box in tower.GetComponents<BoxCollider>())
		{
			Destroy(box);
		}
		tower.GetComponent<Node>().MarkPlaced();
		Reach(tower);
		DistantPillar.HideBrokenParts(tower);
		GraphicsTuning.Adopt(tower);
		return tower;
	}

	private const string headName = "shaft head";

	// how high the kerb is, the railings over it, the steel frame over the car's door side
	private const float kerb = 0.25f;

	private const float rail = 1.1f;

	private const float frame = 5.5f;

	private static Material[] headMaterials;

	// one for every head of the layer, in the tower's space
	private Mesh headMesh;

	// where the car comes up from the layer below, near the girls, whether its tower's here or not
	private void Head(Spot spot, bool near)
	{
		bool wanted = near && (spot.State & PillarState.Down) != 0 && carMax.x > carMin.x;
		if (spot.Head != null && !wanted)
		{
			Destroy(spot.Head);
			spot.Head = null;
		}
		if (spot.Head != null || !wanted)
		{
			return;
		}
		if (headMesh == null)
		{
			headMesh = HeadMesh();
		}
		GameObject head = new GameObject(headName + " " + spot.CellX + "," + spot.CellZ, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
		head.transform.SetPositionAndRotation(spot.Pose.position - Offset(), spot.Pose.rotation);
		head.GetComponent<MeshFilter>().sharedMesh = headMesh;
		head.GetComponent<MeshRenderer>().sharedMaterials = headMaterials;
		head.GetComponent<MeshCollider>().sharedMesh = headMesh;
		spot.Head = head;
	}

	// the hole lined with concrete down through the deck to its underside, a kerb and railings along its sides, a steel
	// frame over the side the car opens on (+x, out from the column)
	private Mesh HeadMesh()
	{
		if (headMaterials == null)
		{
			Material concrete = Resources.Load<Material>("Roads/Concrete");
			Material metal = Resources.Load<Material>("Buildings/Metal");
			headMaterials = new[] { concrete, (metal != null) ? metal : concrete };
		}
		List<Vector3> vertices = new List<Vector3>();
		List<int>[] triangles = { new List<int>(), new List<int>() };
		float x0 = carMin.x - carMargin;
		float x1 = carMax.x + carMargin;
		float z0 = carMin.y - carMargin;
		float z1 = carMax.y + carMargin;
		float depth = DeckMesh.UndersideDepth;
		// the lining, seen from in the shaft and from out of it
		Lining(vertices, triangles[0], new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), depth);
		Lining(vertices, triangles[0], new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), depth);
		Lining(vertices, triangles[0], new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), depth);
		Lining(vertices, triangles[0], new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), depth);
		// kerbs along the two sides, the column closes the third, the door side stays open
		HeadBox(vertices, triangles[0], new Vector3(x0, 0f, z0 - 0.5f), new Vector3(x1, kerb, z0));
		HeadBox(vertices, triangles[0], new Vector3(x0, 0f, z1), new Vector3(x1, kerb, z1 + 0.5f));
		foreach (float z in new[] { z0 - 0.25f, z1 + 0.25f })
		{
			for (float x = x0 + 0.2f; x <= x1 + 0.01f; x += (x1 - x0 - 0.4f) / 3f)
			{
				HeadBox(vertices, triangles[1], new Vector3(x - 0.04f, kerb, z - 0.04f), new Vector3(x + 0.04f, kerb + rail, z + 0.04f));
			}
			HeadBox(vertices, triangles[1], new Vector3(x0, kerb + rail - 0.06f, z - 0.05f), new Vector3(x1, kerb + rail, z + 0.05f));
			HeadBox(vertices, triangles[1], new Vector3(x0, kerb + rail * 0.5f - 0.03f, z - 0.03f), new Vector3(x1, kerb + rail * 0.5f + 0.03f, z + 0.03f));
		}
		// a sill over the gap between the car's floor and the deck on the door side, just outside the car so it still
		// goes down through: the ket's wheels dropped in the 20 cm slot and it stuck there
		HeadBox(vertices, triangles[1], new Vector3(carMax.x + 0.01f, -0.06f, carMin.y), new Vector3(x1 + 0.05f, 0f, carMax.y));
		// the frame over the way out
		HeadBox(vertices, triangles[1], new Vector3(x1 + 0.3f, 0f, z0 - 0.7f), new Vector3(x1 + 0.6f, frame, z0 - 0.4f));
		HeadBox(vertices, triangles[1], new Vector3(x1 + 0.3f, 0f, z1 + 0.4f), new Vector3(x1 + 0.6f, frame, z1 + 0.7f));
		HeadBox(vertices, triangles[1], new Vector3(x1 + 0.25f, frame - 0.4f, z0 - 0.75f), new Vector3(x1 + 0.65f, frame, z1 + 0.75f));
		Mesh mesh = new Mesh { name = headName };
		mesh.SetVertices(vertices);
		mesh.subMeshCount = 2;
		mesh.SetTriangles(triangles[0], 0);
		mesh.SetTriangles(triangles[1], 1);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	// a wall from a to b going down, both faces
	private static void Lining(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, float depth)
	{
		int at = vertices.Count;
		vertices.Add(a);
		vertices.Add(b);
		vertices.Add(b + Vector3.down * depth);
		vertices.Add(a + Vector3.down * depth);
		triangles.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
		at = vertices.Count;
		vertices.Add(a);
		vertices.Add(b);
		vertices.Add(b + Vector3.down * depth);
		vertices.Add(a + Vector3.down * depth);
		triangles.AddRange(new[] { at, at + 2, at + 1, at, at + 3, at + 2 });
	}

	// an axis box from min to max, faces out
	private static void HeadBox(List<Vector3> vertices, List<int> triangles, Vector3 min, Vector3 max)
	{
		Vector3[] c =
		{
			new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
			new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
		};
		int[][] faces = { new[] { 4, 7, 6, 5 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
		foreach (int[] f in faces)
		{
			int at = vertices.Count;
			foreach (int k in f)
			{
				vertices.Add(c[k]);
			}
			// faces out of the box: unity's front is Cross(b - a, c - a)
			Vector3 normal = Vector3.Cross(c[f[1]] - c[f[0]], c[f[2]] - c[f[0]]);
			Vector3 outward = (c[f[0]] + c[f[2]]) * 0.5f - (min + max) * 0.5f;
			if (Vector3.Dot(normal, outward) > 0f)
			{
				triangles.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
			}
			else
			{
				triangles.AddRange(new[] { at, at + 2, at + 1, at, at + 3, at + 2 });
			}
		}
	}

	// the column goes on up into the deck above: the piece's is 571 m, a layer is LayerHeight
	private static void Reach(GameObject tower)
	{
		foreach (MeshFilter part in tower.GetComponentsInChildren<MeshFilter>(true))
		{
			if (part.name != towerParts[0] || part.sharedMesh == null)
			{
				continue;
			}
			Bounds b = part.sharedMesh.bounds;
			float low = float.MaxValue;
			float high = float.MinValue;
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
				float y = tower.transform.InverseTransformPoint(part.transform.TransformPoint(corner)).y;
				low = Mathf.Min(low, y);
				high = Mathf.Max(high, y);
			}
			float height = high - low;
			if (height <= 1f || height >= (float)Gen2.LayerHeight)
			{
				continue;
			}
			// along whichever of its own axes points up
			Vector3 up = part.transform.InverseTransformDirection(tower.transform.up);
			Vector3 scale = part.transform.localScale;
			float stretch = (float)Gen2.LayerHeight / height;
			if (Mathf.Abs(up.x) > Mathf.Abs(up.y) && Mathf.Abs(up.x) > Mathf.Abs(up.z))
			{
				scale.x *= stretch;
			}
			else if (Mathf.Abs(up.y) > Mathf.Abs(up.z))
			{
				scale.y *= stretch;
			}
			else
			{
				scale.z *= stretch;
			}
			part.transform.localScale = scale;
		}
	}
}
