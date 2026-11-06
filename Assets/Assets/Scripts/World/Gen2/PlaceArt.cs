using System.Collections.Generic;
using UnityEngine;

// the places' models (Resources/Places, built in blender by Art/Scripts/place_models.py): low poly, in psx textures
// made from the project's own (AMOGAAS's concrete and rust, the ket's paint, the ration crates' planks, the fuel
// station's atlas), the tiger and the ket's drum reused. a spawned model takes the shared materials by name and its
// col_ boxes become colliders. what changes size with the room (a pump house's walls, a dark room's lining) is built
// here in the same textures. place frame: x along the place's axis (u), z across (v), y up, origin at the middle
public static class PlaceArt
{
	private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();

	private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

	// the deck's own snow, for what lies on things. set by the streamer
	public static Material Snow;

	// a pump house's walls: 4 m to the roof, a 0.4 m parapet, 0.3 thick, a 2 x 2.4 m doorway (PlacePlan's room)
	public const float PumpHeight = 4f;

	public const float Wall = 0.3f;

	private const float parapet = 0.4f;

	private const float roofThick = 0.2f;

	public const float PumpDoorWidth = 2f;

	public const float PumpDoorHeight = 2.4f;

	// a dark room's doorway (LotMesh.AddPlace's Room)
	public const float RoomDoorWidth = 1.6f;

	public const float RoomDoorHeight = 2.2f;

	// the tank hull's rear plate, and where its lamp hangs (place_models.py's tankhull)
	private const float tankRear = 5.58f;

	// the stack on a pump house's roof and the top of its rain cap (ph_stack)
	public const float StackTop = 5.2f;

	// the depot's tower: the kit's fuel station landmark, the game's own
	public const string DepotTower = "fuel_station";

	// how big the tower stands in a depot and where: the end away from the door, a little over half the room's length
	// and most of its width, turned a quarter if that fits it bigger. gives its scale, u is its middle along the axis
	public static float DepotFit(Place place, Vector3 size, out float u, out bool turned)
	{
		float fitU = (float)place.RoomU * 1.2f;
		float fitV = (float)place.RoomV * 1.6f;
		float straight = Mathf.Min(1f, Mathf.Min(fitU / Mathf.Max(0.1f, size.x), fitV / Mathf.Max(0.1f, size.z)));
		float quarter = Mathf.Min(1f, Mathf.Min(fitU / Mathf.Max(0.1f, size.z), fitV / Mathf.Max(0.1f, size.x)));
		turned = quarter > straight;
		float away = (place.Door == 0) ? -1f : 1f;
		u = away * ((float)place.RoomU - fitU * 0.5f);
		return Mathf.Max(straight, quarter);
	}

	public static GameObject Model(string name)
	{
		if (!models.TryGetValue(name, out GameObject model))
		{
			model = Resources.Load<GameObject>("Places/" + name);
			models[name] = model;
		}
		return model;
	}

	public static Material Material(string name)
	{
		if (name == "snow" && Snow != null)
		{
			return Snow;
		}
		if (!materials.TryGetValue(name, out Material material))
		{
			material = Resources.Load<Material>("Places/Materials/" + name);
			materials[name] = material;
		}
		return material;
	}

	// a model, or one child of it (a relic), at a spot
	public static GameObject Spawn(string name, Transform parent, Vector3 at, Quaternion turn, int layer, string child = null)
	{
		GameObject source = Model(name);
		if (source != null && child != null)
		{
			Transform part = source.transform.Find(child);
			source = (part != null) ? part.gameObject : null;
		}
		if (source == null)
		{
			Debug.LogWarning("PlaceArt: no model " + name + (child != null ? "/" + child : ""));
			return null;
		}
		GameObject made = Object.Instantiate(source, at, turn, parent);
		made.name = child ?? name;
		Dress(made, layer);
		return made;
	}

	private static void Dress(GameObject made, int layer)
	{
		foreach (Transform part in made.GetComponentsInChildren<Transform>(true))
		{
			part.gameObject.layer = layer;
			MeshFilter filter = part.GetComponent<MeshFilter>();
			MeshRenderer renderer = part.GetComponent<MeshRenderer>();
			if (part.name.StartsWith("col_"))
			{
				if (filter != null && filter.sharedMesh != null)
				{
					Bounds bounds = filter.sharedMesh.bounds;
					BoxCollider box = part.gameObject.AddComponent<BoxCollider>();
					box.center = bounds.center;
					box.size = bounds.size;
				}
				Object.Destroy(renderer);
				Object.Destroy(filter);
				continue;
			}
			if (renderer == null)
			{
				continue;
			}
			Material[] shared = renderer.sharedMaterials;
			for (int i = 0; i < shared.Length; i++)
			{
				if (shared[i] == null)
				{
					continue;
				}
				string name = shared[i].name;
				int cut = name.IndexOf(' ');
				Material ours = Material((cut > 0) ? name.Substring(0, cut) : name);
				if (ours != null)
				{
					shared[i] = ours;
				}
			}
			renderer.sharedMaterials = shared;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
		}
		GraphicsTuning.Adopt(made);
	}

	public static Transform Find(GameObject made, string name)
	{
		if (made == null)
		{
			return null;
		}
		foreach (Transform part in made.GetComponentsInChildren<Transform>(true))
		{
			if (part.name == name)
			{
				return part;
			}
		}
		return null;
	}

	// the outward normal of a side of a place's room (PlacePlan's Door: 0 +u, 1 -u, 2 +v, 3 -v), in the place frame
	public static Vector3 Out(int side)
	{
		return (side == 0) ? Vector3.right : (side == 1) ? Vector3.left : (side == 2) ? Vector3.forward : Vector3.back;
	}

	// a side's wall's half length, and how far out its outer face is
	private static void Side(int side, float hu, float hv, out float half, out float reach)
	{
		half = (side < 2) ? hv : hu;
		reach = (side < 2) ? hu : hv;
	}

	// where a place's lamp hangs and which way it faces, place frame; the bulb's 0.3 m out and 8 cm down from it.
	// the depot's is on its fuel tower, worked out by whoever has the tower
	public static void Lamp(Place place, out Vector3 mount, out Vector3 facing)
	{
		float hu = (float)place.RoomU;
		float hv = (float)place.RoomV;
		facing = Out(place.Door);
		switch (place.Kind)
		{
		case PlaceKind.TankHull:
			mount = facing * tankRear + Vector3.up * 2.3f + Vector3.forward * 0.5f;
			break;
		case PlaceKind.PumpHouse:
			Side(place.Door, hu, hv, out _, out float reach);
			mount = facing * reach + Vector3.up * 3.5f;
			break;
		default:
			Side(place.Door, hu, hv, out _, out float far);
			mount = facing * far + Vector3.up * 2.95f;
			break;
		}
	}

	public static Vector3 Bulb(Vector3 mount, Vector3 facing)
	{
		return mount + facing * 0.3f - Vector3.up * 0.08f;
	}

	// ---- built to size

	// quads into submeshes; a quad's facing comes from its hint, its uvs are given
	private class Builder
	{
		public readonly List<Vector3> Vertices = new List<Vector3>();

		public readonly List<Vector2> Uvs = new List<Vector2>();

		public readonly List<int>[] Triangles;

		public Builder(int submeshes)
		{
			Triangles = new List<int>[submeshes];
			for (int i = 0; i < submeshes; i++)
			{
				Triangles[i] = new List<int>();
			}
		}

		public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 facing)
		{
			int i = Vertices.Count;
			Vertices.Add(a);
			Vertices.Add(b);
			Vertices.Add(c);
			Vertices.Add(d);
			Uvs.Add(ua);
			Uvs.Add(ub);
			Uvs.Add(uc);
			Uvs.Add(ud);
			// unity draws the side its winding's clockwise from: the cross of the first two edges points at the eye
			bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f;
			List<int> t = Triangles[sub];
			if (flip)
			{
				t.Add(i); t.Add(i + 2); t.Add(i + 1);
				t.Add(i); t.Add(i + 3); t.Add(i + 2);
			}
			else
			{
				t.Add(i); t.Add(i + 1); t.Add(i + 2);
				t.Add(i); t.Add(i + 2); t.Add(i + 3);
			}
		}

		// an upright face: centre of its foot, facing (level), half width, from y0 to y1, metres per tile su x sv
		public void Upright(int sub, Vector3 foot, Vector3 facing, float half, float y0, float y1, float su, float sv)
		{
			Vector3 along = Vector3.Cross(Vector3.up, facing).normalized;
			float s = Vector3.Dot(foot, along);
			Vector3 a = foot - along * half + Vector3.up * y0;
			Vector3 b = foot + along * half + Vector3.up * y0;
			Vector3 c = foot + along * half + Vector3.up * y1;
			Vector3 d = foot - along * half + Vector3.up * y1;
			Quad(sub, a, b, c, d, new Vector2((s - half) / su, y0 / sv), new Vector2((s + half) / su, y0 / sv), new Vector2((s + half) / su, y1 / sv), new Vector2((s - half) / su, y1 / sv), facing);
		}

		// a level face at height y, x0..x1 by z0..z1, 2 m tiles
		public void Level(int sub, float x0, float x1, float z0, float z1, float y, bool up, float s = 2f)
		{
			Quad(sub, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), new Vector2(x0 / s, z0 / s), new Vector2(x1 / s, z0 / s), new Vector2(x1 / s, z1 / s), new Vector2(x0 / s, z1 / s), up ? Vector3.up : Vector3.down);
		}

		// a box: centre, half sizes on x, y, z; faces by submesh (-1 leaves it out): +x -x +y -y +z -z
		public void Box(Vector3 c, Vector3 h, int px, int nx, int py, int ny, int pz, int nz)
		{
			float y0 = c.y - h.y;
			float y1 = c.y + h.y;
			if (px >= 0) Upright(px, c + Vector3.right * h.x - Vector3.up * c.y, Vector3.right, h.z, y0, y1, 2f, 2f);
			if (nx >= 0) Upright(nx, c - Vector3.right * h.x - Vector3.up * c.y, Vector3.left, h.z, y0, y1, 2f, 2f);
			if (pz >= 0) Upright(pz, c + Vector3.forward * h.z - Vector3.up * c.y, Vector3.forward, h.x, y0, y1, 2f, 2f);
			if (nz >= 0) Upright(nz, c - Vector3.forward * h.z - Vector3.up * c.y, Vector3.back, h.x, y0, y1, 2f, 2f);
			if (py >= 0) Level(py, c.x - h.x, c.x + h.x, c.z - h.z, c.z + h.z, y1, true);
			if (ny >= 0) Level(ny, c.x - h.x, c.x + h.x, c.z - h.z, c.z + h.z, y0, false);
		}

		public GameObject Build(string name, Transform parent, Vector3 at, Quaternion turn, int layer, string[] materialNames)
		{
			Mesh mesh = new Mesh { name = name };
			mesh.SetVertices(Vertices);
			mesh.SetUVs(0, Uvs);
			mesh.subMeshCount = Triangles.Length;
			for (int i = 0; i < Triangles.Length; i++)
			{
				mesh.SetTriangles(Triangles[i], i, false);
			}
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			GameObject made = new GameObject(name);
			made.layer = layer;
			made.transform.SetParent(parent, false);
			made.transform.SetPositionAndRotation(at, turn);
			made.AddComponent<MeshFilter>().sharedMesh = mesh;
			Material[] shared = new Material[materialNames.Length];
			for (int i = 0; i < shared.Length; i++)
			{
				shared[i] = Material(materialNames[i]);
			}
			made.AddComponent<MeshRenderer>().sharedMaterials = shared;
			made.AddComponent<Owned>().Mesh = mesh;
			return made;
		}
	}

	// a built mesh goes with its object
	private class Owned : MonoBehaviour
	{
		public Mesh Mesh;

		private void OnDestroy()
		{
			if (Mesh != null)
			{
				Object.Destroy(Mesh);
			}
		}
	}

	private const int outside = 0;

	private const int inside = 1;

	private const int concrete = 2;

	private const int floor = 3;

	private static readonly string[] shellMaterials = { "pl_wall_out", "pl_wall_in", "pl_concrete", "pl_floor" };

	// a pump house's windows (ph_window): their opening in the wall's outer face, half its width and its sill to lintel
	private const float windowHalf = 0.45f;

	private const float windowLow = 2.65f;

	private const float windowHigh = 3.35f;

	// of the bays clear for one, how many get a window
	private const float windowShare = 0.6f;

	// a pump house: walls round the room with the doorway in its side, a pilaster at every bay, a cornice, a roof, a
	// floor; a collider for each piece of wall and the roof. windows go in some bays' middles, clear of the corners and
	// the doorway, with their openings cut in the outer face: gives them as x, side, z, along the side
	public static GameObject PumpShell(Place place, Transform parent, Vector3 at, Quaternion turn, int layer, List<Vector4> windows)
	{
		float hu = (float)place.RoomU;
		float hv = (float)place.RoomV;
		float h = PumpHeight;
		float top = h + parapet;
		float gap = PumpDoorWidth * 0.5f;
		Builder b = new Builder(4);
		List<Vector3> boxes = new List<Vector3>();
		int bayIndex = 0;
		for (int side = 0; side < 4; side++)
		{
			Vector3 n = Out(side);
			Vector3 along = Vector3.Cross(Vector3.up, n);
			Side(side, hu, hv, out float half, out float reach);
			int bays = Mathf.Max(1, Mathf.RoundToInt(half));
			float bay = half * 2f / bays;
			for (int k = 0; k < bays; k++)
			{
				float s = -half + (k + 0.5f) * bay;
				if (Mathf.Abs(s) > half - 0.9f || (side == place.Door && Mathf.Abs(s) < gap + 0.9f))
				{
					continue;
				}
				if (Gen2.Unit(WorldRandom.Derive(place.Seed, Gen2.StreamPlace, 20uL + (ulong)bayIndex++)) < windowShare)
				{
					windows.Add(new Vector4(n.x * reach + along.x * s, side, n.z * reach + along.z * s, s));
				}
			}
		}
		List<float> holes = new List<float>();
		for (int side = 0; side < 4; side++)
		{
			Vector3 n = Out(side);
			Vector3 along = Vector3.Cross(Vector3.up, n);
			Side(side, hu, hv, out float half, out float reach);
			// the short walls sit between the long ones
			float length = (side < 2) ? half - Wall : half;
			Vector3 middle = n * (reach - Wall * 0.5f);
			List<Vector3> pieces = new List<Vector3>();
			if (side == place.Door)
			{
				pieces.Add(new Vector3(-length, -gap, 0f));
				pieces.Add(new Vector3(gap, length, 0f));
			}
			else
			{
				pieces.Add(new Vector3(-length, length, 0f));
			}
			holes.Clear();
			foreach (Vector4 window in windows)
			{
				if ((int)window.y == side)
				{
					holes.Add(window.w);
				}
			}
			foreach (Vector3 piece in pieces)
			{
				WallPiece(b, middle, n, along, piece.x, piece.y, 0f, top, h, boxes, holes);
			}
			if (side == place.Door)
			{
				WallPiece(b, middle, n, along, -gap, gap, PumpDoorHeight, top, h, boxes, null);
			}
			// the cornice, a shadow line along the top
			Vector3 band = n * (reach + 0.05f) + Vector3.up * (top - 0.15f);
			BoxAlong(b, band, n, along, half + 0.1f, 0.15f, 0.15f, concrete);
			// bays: a pilaster between each, windows maybe in their middles
			int bays = Mathf.Max(1, Mathf.RoundToInt(half));
			float bay = half * 2f / bays;
			for (int k = 1; k < bays; k++)
			{
				float s = -half + k * bay;
				if (side == place.Door && Mathf.Abs(s) < gap + 0.5f)
				{
					continue;
				}
				Vector3 foot = n * (reach + 0.06f) + along * s;
				b.Upright(outside, foot + n * 0.06f, n, 0.2f, 0f, top - 0.3f, 2f, top);
				b.Upright(concrete, foot + along * 0.2f, along, 0.06f, 0f, top - 0.3f, 2f, 2f);
				b.Upright(concrete, foot - along * 0.2f, -along, 0.06f, 0f, top - 0.3f, 2f, 2f);
			}
		}
		// the roof's slab, the ceiling under it, the floor
		float ix = hu - Wall;
		float iz = hv - Wall;
		b.Level(concrete, -ix, ix, -iz, iz, h + roofThick, true);
		b.Level(concrete, -ix, ix, -iz, iz, h, false);
		b.Level(floor, -ix, ix, -iz, iz, 0.02f, true);
		GameObject shell = b.Build("pump house", parent, at, turn, layer, shellMaterials);
		// boxes went in as centre, size pairs
		for (int i = 0; i + 1 < boxes.Count; i += 2)
		{
			BoxCollider collider = shell.AddComponent<BoxCollider>();
			collider.center = boxes[i];
			collider.size = boxes[i + 1];
		}
		BoxCollider roof = shell.AddComponent<BoxCollider>();
		roof.center = new Vector3(0f, h + roofThick * 0.5f, 0f);
		roof.size = new Vector3(ix * 2f, roofThick, iz * 2f);
		return shell;
	}

	// a piece of wall from s0 to s1 along it, y0 to y1: rendered outside with the windows' openings (holes, their
	// middles along it) cut out, inside up to the ceiling, concrete on top, ends and the parapet's inner face; its
	// collider's box
	private static void WallPiece(Builder b, Vector3 middle, Vector3 n, Vector3 along, float s0, float s1, float y0, float y1, float ceiling, List<Vector3> boxes, List<float> holes)
	{
		float half = (s1 - s0) * 0.5f;
		Vector3 centre = middle + along * ((s0 + s1) * 0.5f);
		Vector3 outer = centre + n * (Wall * 0.5f);
		Vector3 inner = centre - n * (Wall * 0.5f);
		bool cut = false;
		if (holes != null && y0 < windowLow && y1 > windowHigh)
		{
			float from = s0;
			foreach (float s in holes)
			{
				if (s - windowHalf < s0 || s + windowHalf > s1)
				{
					continue;
				}
				if (!cut)
				{
					b.Upright(outside, outer, n, half, y0, windowLow, 2f, y1);
					b.Upright(outside, outer, n, half, windowHigh, y1, 2f, y1);
					cut = true;
				}
				Band(b, middle, n, along, from, s - windowHalf, y1);
				from = s + windowHalf;
				// the opening's sill, under the model's own
				Vector3 sill = middle + along * s + n * (Wall * 0.5f) + Vector3.up * windowLow;
				b.Quad(concrete, sill - along * windowHalf - n * 0.1f, sill + along * windowHalf - n * 0.1f, sill + along * windowHalf - n * 0.02f, sill - along * windowHalf - n * 0.02f, Vector2.zero, new Vector2(windowHalf, 0f), new Vector2(windowHalf, 0.05f), new Vector2(0f, 0.05f), Vector3.up);
			}
			if (cut)
			{
				Band(b, middle, n, along, from, s1, y1);
			}
		}
		if (!cut)
		{
			b.Upright(outside, outer, n, half, y0, y1, 2f, y1);
		}
		if (y0 < ceiling)
		{
			b.Upright(inside, inner, -n, half, y0, Mathf.Min(y1, ceiling), 2f, 4f);
		}
		if (y1 > ceiling)
		{
			b.Upright(concrete, inner, -n, half, Mathf.Max(y0, ceiling), y1, 2f, 2f);
		}
		// the top, and the ends (the doorway's sides show them)
		Vector3 c0 = centre + Vector3.up * y1;
		b.Quad(concrete, c0 - along * half - n * (Wall * 0.5f), c0 + along * half - n * (Wall * 0.5f), c0 + along * half + n * (Wall * 0.5f), c0 - along * half + n * (Wall * 0.5f), Vector2.zero, new Vector2(half, 0f), new Vector2(half, 0.15f), new Vector2(0f, 0.15f), Vector3.up);
		b.Upright(concrete, centre + along * half, along, Wall * 0.5f, y0, y1, 2f, 2f);
		b.Upright(concrete, centre - along * half, -along, Wall * 0.5f, y0, y1, 2f, 2f);
		if (y0 > 0f)
		{
			Vector3 c1 = centre + Vector3.up * y0;
			b.Quad(concrete, c1 - along * half - n * (Wall * 0.5f), c1 + along * half - n * (Wall * 0.5f), c1 + along * half + n * (Wall * 0.5f), c1 - along * half + n * (Wall * 0.5f), Vector2.zero, new Vector2(half, 0f), new Vector2(half, 0.15f), new Vector2(0f, 0.15f), Vector3.down);
		}
		Vector3 size = new Vector3(Mathf.Abs(along.x) * half * 2f + Mathf.Abs(n.x) * Wall, y1 - y0, Mathf.Abs(along.z) * half * 2f + Mathf.Abs(n.z) * Wall);
		boxes.Add(centre + Vector3.up * ((y0 + y1) * 0.5f));
		boxes.Add(size);
	}

	// the outer face between two windows' openings, sill to lintel
	private static void Band(Builder b, Vector3 middle, Vector3 n, Vector3 along, float s0, float s1, float top)
	{
		if (s1 - s0 > 0.001f)
		{
			b.Upright(outside, middle + n * (Wall * 0.5f) + along * ((s0 + s1) * 0.5f), n, (s1 - s0) * 0.5f, windowLow, windowHigh, 2f, top);
		}
	}

	// a box lying along a wall: centre, its half length along, half depth out, half height
	private static void BoxAlong(Builder b, Vector3 c, Vector3 n, Vector3 along, float half, float depth, float height, int sub)
	{
		Vector3 foot = c - Vector3.up * c.y;
		float y0 = c.y - height;
		float y1 = c.y + height;
		b.Upright(sub, foot + n * depth, n, half, y0, y1, 2f, 2f);
		b.Upright(sub, foot + along * half, along, depth, y0, y1, 2f, 2f);
		b.Upright(sub, foot - along * half, -along, depth, y0, y1, 2f, 2f);
		Vector3 t = foot + Vector3.up * y1;
		b.Quad(sub, t - along * half - n * depth, t + along * half - n * depth, t + along * half + n * depth, t - along * half + n * depth, Vector2.zero, new Vector2(half, 0f), new Vector2(half, depth), new Vector2(0f, depth), Vector3.up);
		Vector3 u = foot + Vector3.up * y0;
		b.Quad(sub, u - along * half - n * depth, u + along * half - n * depth, u + along * half + n * depth, u - along * half + n * depth, Vector2.zero, new Vector2(half, 0f), new Vector2(half, depth), new Vector2(0f, depth), Vector3.down);
	}

	// a dark room's lining, a hair inside LotMesh's walls: painted plaster with its dado, a dirty floor, a concrete
	// ceiling with beams across it every so often. drawn only, LotMesh's room is what stops you
	public static GameObject RoomLining(Place place, Transform parent, Vector3 at, Quaternion turn, int layer)
	{
		float hu = (float)place.RoomU;
		float hv = (float)place.RoomV;
		float h = (float)place.Height;
		float ix = hu - Wall - 0.01f;
		float iz = hv - Wall - 0.01f;
		float gap = RoomDoorWidth * 0.5f;
		Builder b = new Builder(4);
		for (int side = 0; side < 4; side++)
		{
			Vector3 n = Out(side);
			Vector3 along = Vector3.Cross(Vector3.up, n);
			float half = (side < 2) ? iz : ix;
			float reach = (side < 2) ? ix : iz;
			Vector3 foot = n * reach;
			if (side == place.Door)
			{
				float piece = (half - gap) * 0.5f;
				b.Upright(inside, foot + along * (gap + piece), -n, piece, 0f, h - 0.01f, 2f, 4f);
				b.Upright(inside, foot - along * (gap + piece), -n, piece, 0f, h - 0.01f, 2f, 4f);
				b.Upright(inside, foot, -n, gap, RoomDoorHeight, h - 0.01f, 2f, 4f);
			}
			else
			{
				b.Upright(inside, foot, -n, half, 0f, h - 0.01f, 2f, 4f);
			}
		}
		b.Level(floor, -ix, ix, -iz, iz, 0.02f, true);
		b.Level(concrete, -ix, ix, -iz, iz, h - 0.01f, false);
		// beams across the short way
		bool acrossZ = hu >= hv;
		float span = acrossZ ? ix : iz;
		int beams = Mathf.Max(1, Mathf.FloorToInt(span * 2f / 2.6f));
		for (int k = 1; k < beams; k++)
		{
			float s = -span + k * (span * 2f / beams);
			Vector3 c = acrossZ ? new Vector3(s, h - 0.19f, 0f) : new Vector3(0f, h - 0.19f, s);
			Vector3 size = acrossZ ? new Vector3(0.15f, 0.18f, iz) : new Vector3(ix, 0.18f, 0.15f);
			b.Box(c, size, concrete, concrete, -1, concrete, concrete, concrete);
		}
		return b.Build("lining", parent, at, turn, layer, shellMaterials);
	}
}
