using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// blocks in the gaps between map pieces for the ending ride, so from up there it reads as a city all the way out.
// nothing walkable, only there for the ride
public static class EndingCity
{
	// out to here, one per cell at most
	private const float fillerInner = 250f;

	private const float fillerOuter = 3000f;

	private const float fillerCell = 70f;

	private const float fillerChance = 0.7f;

	// clear of the real pieces' footprints by this much
	private const float fillerMargin = 25f;

	// going down into the haze under the deck, so they never show a bottom
	private const float fillerDepth = 220f;

	public static void Build(Transform parent, Transform column)
	{
		Material buildings = null;
		foreach (MeshRenderer part in column.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
		{
			if (part.name.StartsWith("Buildings") && part.sharedMaterial != null)
			{
				buildings = part.sharedMaterial;
				break;
			}
		}
		Color albedo = buildings != null ? DistantPillar.Albedo(buildings) : new Color(0.6f, 0.61f, 0.63f);
		Filler(parent, column, albedo, new Material(Resources.Load<Shader>("DistantFiller")));
	}

	private sealed class Piece
	{
		public Vector3 Position;

		public Quaternion Inverse;

		public List<WorldLayout.LocalBox> Boxes;

		public float Reach;
	}

	private static void Filler(Transform parent, Transform column, Color albedo, Material material)
	{
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		if (world == null || world.Layout == null)
		{
			return;
		}
		List<Piece> pieces = new List<Piece>();
		foreach (WorldLayout.PlacedNode node in world.Layout.Nodes)
		{
			pieces.Add(MakePiece(world, node.Pose, node.Def.Boxes));
		}
		foreach (WorldLayout.PlacedEdge edge in world.Layout.Edges)
		{
			pieces.Add(MakePiece(world, edge.Pose, edge.Def.Boxes));
		}
		// same city every time for a seed, both players see the same one
		System.Random random = new System.Random((int)(world.Seed & 0x7fffffff) ^ 0x51a7);
		Quaternion grid = Quaternion.Euler(0f, (float)random.NextDouble() * 90f, 0f);
		Vector3 centre = column.position;
		Blocks blocks = new Blocks();
		int reach = Mathf.CeilToInt(fillerOuter / fillerCell);
		for (int x = -reach; x <= reach; x++)
		{
			for (int z = -reach; z <= reach; z++)
			{
				// rolled for every cell either way so one change doesn't move all the rest
				double chance = random.NextDouble();
				float jitterX = (float)random.NextDouble() - 0.5f;
				float jitterZ = (float)random.NextDouble() - 0.5f;
				float width = Mathf.Lerp(18f, 45f, (float)random.NextDouble());
				float depth = Mathf.Lerp(18f, 45f, (float)random.NextDouble());
				float tall = 8f + Mathf.Pow((float)random.NextDouble(), 2.2f) * 140f;
				float shade = Mathf.Lerp(0.8f, 1.1f, (float)random.NextDouble());
				Vector3 flat = grid * new Vector3((x + jitterX * 0.4f) * fillerCell, 0f, (z + jitterZ * 0.4f) * fillerCell);
				float distance = flat.magnitude;
				if (chance > fillerChance || distance < fillerInner || distance > fillerOuter)
				{
					continue;
				}
				Vector3 foot = centre + flat;
				if (Blocked(pieces, world, foot, Mathf.Max(width, depth) * 0.5f))
				{
					continue;
				}
				Color colour = albedo * shade;
				colour.a = 1f;
				blocks.Building(foot, grid, new Vector3(width, tall, depth), fillerDepth, colour);
			}
		}
		Spawn("EndingFiller", blocks, parent, material);
	}

	private static Piece MakePiece(WorldGenerator world, Pose pose, List<WorldLayout.LocalBox> boxes)
	{
		float reach = 0f;
		foreach (WorldLayout.LocalBox box in boxes)
		{
			reach = Mathf.Max(reach, new Vector2(box.Center.x, box.Center.z).magnitude + new Vector2(box.Size.x, box.Size.z).magnitude * 0.5f);
		}
		return new Piece { Position = world.ToWorld(pose.position), Inverse = Quaternion.Inverse(pose.rotation), Boxes = boxes, Reach = reach };
	}

	// inside any piece's footprint, grown by the margin and the block's own size, height doesn't count
	private static bool Blocked(List<Piece> pieces, WorldGenerator world, Vector3 point, float half)
	{
		float clear = fillerMargin + half;
		foreach (Piece piece in pieces)
		{
			Vector3 apart = point - piece.Position;
			if (new Vector2(apart.x, apart.z).magnitude > piece.Reach + clear)
			{
				continue;
			}
			Vector3 local = piece.Inverse * apart;
			foreach (WorldLayout.LocalBox box in piece.Boxes)
			{
				if (Mathf.Abs(local.x - box.Center.x) < Mathf.Abs(box.Size.x) * 0.5f + clear && Mathf.Abs(local.z - box.Center.z) < Mathf.Abs(box.Size.z) * 0.5f + clear)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static void Spawn(string name, Blocks blocks, Transform parent, Material material)
	{
		Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
		mesh.SetVertices(blocks.Vertices);
		mesh.SetNormals(blocks.Normals);
		mesh.SetColors(blocks.Colours);
		mesh.SetUVs(0, blocks.Uvs);
		mesh.SetTriangles(blocks.Triangles, 0);
		mesh.RecalculateBounds();
		GameObject holder = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
		holder.transform.SetParent(parent, worldPositionStays: false);
		holder.GetComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer renderer = holder.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = material;
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
	}

	// quads with a colour, a normal, and facade coordinates for the windows: across the face and height above the deck,
	// y under zero means no windows
	private sealed class Blocks
	{
		public readonly List<Vector3> Vertices = new List<Vector3>();

		public readonly List<Vector3> Normals = new List<Vector3>();

		public readonly List<Color> Colours = new List<Color>();

		public readonly List<Vector2> Uvs = new List<Vector2>();

		public readonly List<int> Triangles = new List<int>();

		// a block standing on the deck, going on down into the haze
		public void Building(Vector3 foot, Quaternion turn, Vector3 size, float below, Color colour)
		{
			Vector3 along = turn * Vector3.right * size.x * 0.5f;
			Vector3 forward = turn * Vector3.forward * size.z * 0.5f;
			Vector3 down = Vector3.down * below;
			Vector3 up = Vector3.up * size.y;
			Vector3[] corners = { foot - along - forward, foot + along - forward, foot + along + forward, foot - along + forward };
			for (int i = 0; i < 4; i++)
			{
				Vector3 a = corners[i];
				Vector3 b = corners[(i + 1) % 4];
				Vector3 normal = Vector3.Cross(Vector3.up, b - a).normalized;
				float width = (b - a).magnitude;
				Quad(a + down, a + up, b + up, b + down, normal, colour, new Vector2(0f, -below), new Vector2(width, size.y));
			}
			Quad(corners[0] + up, corners[3] + up, corners[2] + up, corners[1] + up, Vector3.up, colour, Vector2.one * -1f, Vector2.one * -1f);
		}

		// a b c d round the face, clockwise seen from outside. uv from the corner at a to the one at c
		private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color colour, Vector2 uvLow, Vector2 uvHigh)
		{
			int first = Vertices.Count;
			Vertices.Add(a);
			Vertices.Add(b);
			Vertices.Add(c);
			Vertices.Add(d);
			Uvs.Add(new Vector2(uvLow.x, uvLow.y));
			Uvs.Add(new Vector2(uvLow.x, uvHigh.y));
			Uvs.Add(new Vector2(uvHigh.x, uvHigh.y));
			Uvs.Add(new Vector2(uvHigh.x, uvLow.y));
			for (int i = 0; i < 4; i++)
			{
				Normals.Add(normal);
				Colours.Add(colour.linear);
			}
			Triangles.Add(first);
			Triangles.Add(first + 1);
			Triangles.Add(first + 2);
			Triangles.Add(first);
			Triangles.Add(first + 2);
			Triangles.Add(first + 3);
		}
	}
}
