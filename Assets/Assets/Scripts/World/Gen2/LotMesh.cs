using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// building kit v0 for one tile's lots: boxes with sc_building walls (a 4 m module, one floor high, per texture repeat),
// concrete roofs, broken stepped tops on ruins, rubble on open lots. one mesh, two submeshes (walls, concrete).
// tile-local like RoadMesh
public class LotMesh
{
	public const int SubmeshCount = 2;

	private const int walls = 0;

	private const int concrete = 1;

	public const float FloorHeight = 3.5f;

	private const float module = 4f;

	// buildings sit this deep so they don't float on uneven ground later
	private const float sink = 0.5f;

	private readonly List<Vector3> vertices = new List<Vector3>();

	private readonly List<Vector2> uvs = new List<Vector2>();

	private readonly List<int>[] triangles = { new List<int>(), new List<int>() };

	public int Triangles { get; private set; }

	// lots as they'll be drawn: a landmark lot that got no landmark comes in as Open
	public bool Build(Mesh mesh, List<Lot> lots, double ruin, double originX, double originZ)
	{
		vertices.Clear();
		uvs.Clear();
		foreach (List<int> list in triangles)
		{
			list.Clear();
		}
		foreach (Lot lot in lots)
		{
			WorldRandom random = new WorldRandom(lot.Seed);
			Vector3 centre = new Vector3((float)(lot.Center.X - originX), 0f, (float)(lot.Center.Z - originZ));
			Vector3 axis = new Vector3((float)lot.Axis.X, 0f, (float)lot.Axis.Z);
			if (lot.Kind == LotKind.Building)
			{
				Building(random, centre, axis, (float)lot.HalfU, (float)lot.HalfV, lot.Floors, ruin);
			}
			else if (lot.Kind == LotKind.Open)
			{
				Rubble(random, centre, axis, (float)lot.HalfU, (float)lot.HalfV);
			}
		}
		mesh.Clear();
		Triangles = 0;
		if (vertices.Count == 0)
		{
			return false;
		}
		mesh.indexFormat = (vertices.Count > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16;
		mesh.SetVertices(vertices);
		mesh.SetUVs(0, uvs);
		mesh.subMeshCount = SubmeshCount;
		for (int i = 0; i < SubmeshCount; i++)
		{
			mesh.SetTriangles(triangles[i], i, false);
			Triangles += triangles[i].Count / 3;
		}
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return true;
	}

	private static float Unit(WorldRandom random)
	{
		return (float)Gen2.Unit(random.NextULong());
	}

	private static Vector3 Left(Vector3 axis)
	{
		return new Vector3(-axis.z, 0f, axis.x);
	}

	// set back from the lot's edges; a ruin loses part of its top floors
	private void Building(WorldRandom random, Vector3 centre, Vector3 u, float hu, float hv, int floors, double ruin)
	{
		float margin = 1f + 3f * Unit(random);
		hu = Mathf.Max(3f, hu - margin);
		hv = Mathf.Max(3f, hv - margin);
		float height = floors * FloorHeight;
		if (Unit(random) >= ruin || floors < 2)
		{
			Box(centre, u, hu, hv, -sink, height, walls);
			return;
		}
		// the standing part, and a lower broken part beside it whose top doesn't land on a floor line
		float at = 0.4f + 0.3f * Unit(random);
		float lost = Mathf.Max(1, Mathf.FloorToInt(Unit(random) * floors)) * FloorHeight + 1.2f * Unit(random);
		float a = hu * at;
		float b = hu - a;
		Box(centre - u * (hu - a), u, a, hv, -sink, height, walls);
		Box(centre + u * (hu - b), u, b, hv, -sink, Mathf.Max(1.5f, height - lost), walls);
	}

	// a few low slabs and heaps, turned every which way
	private void Rubble(WorldRandom random, Vector3 centre, Vector3 u, float hu, float hv)
	{
		Vector3 v = Left(u);
		int count = 3 + random.Next(5);
		for (int i = 0; i < count; i++)
		{
			Vector3 at = centre + u * ((Unit(random) * 2f - 1f) * hu * 0.7f) + v * ((Unit(random) * 2f - 1f) * hv * 0.7f);
			float tilt = Unit(random) * 2f - 1f;
			Vector3 axis = new Vector3(1f, 0f, tilt).normalized;
			Box(at, axis, 1f + 3f * Unit(random), 1f + 3f * Unit(random), -sink, 0.4f + 1.4f * Unit(random), concrete);
		}
	}

	// walls in the given submesh, a concrete top, no bottom
	private void Box(Vector3 centre, Vector3 u, float hu, float hv, float y0, float y1, int side)
	{
		Vector3 v = Left(u);
		Face(centre + u * hu, u, hv, y0, y1, side);
		Face(centre - u * hu, -u, hv, y0, y1, side);
		Face(centre + v * hv, v, hu, y0, y1, side);
		Face(centre - v * hv, -v, hu, y0, y1, side);
		Vector3 top = Vector3.up * y1;
		Quad(concrete, centre - u * hu - v * hv + top, centre + u * hu - v * hv + top, centre + u * hu + v * hv + top, centre - u * hu + v * hv + top);
	}

	// a wall facing out along normal, half as wide as half, from y0 to y1
	private void Face(Vector3 middle, Vector3 normal, float half, float y0, float y1, int submesh)
	{
		// the right hand of someone looking at the wall from outside
		Vector3 right = Vector3.Cross(normal, Vector3.up);
		Vector3 left = middle - right * half;
		Vector3 rightEnd = middle + right * half;
		int first = vertices.Count;
		float width = half * 2f / module;
		vertices.Add(left + Vector3.up * y0);
		vertices.Add(rightEnd + Vector3.up * y0);
		vertices.Add(rightEnd + Vector3.up * y1);
		vertices.Add(left + Vector3.up * y1);
		uvs.Add(new Vector2(0f, y0 / FloorHeight));
		uvs.Add(new Vector2(width, y0 / FloorHeight));
		uvs.Add(new Vector2(width, y1 / FloorHeight));
		uvs.Add(new Vector2(0f, y1 / FloorHeight));
		AddQuad(submesh, first);
	}

	// corners counter-clockwise seen from the side that shows, from above for a top
	private void Quad(int submesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
	{
		int first = vertices.Count;
		Add(p0);
		Add(p1);
		Add(p2);
		Add(p3);
		AddQuad(submesh, first);
	}

	// planar on top, a module a repeat
	private void Add(Vector3 p)
	{
		vertices.Add(p);
		uvs.Add(new Vector2(p.x, p.z) / module);
	}

	private void AddQuad(int submesh, int first)
	{
		List<int> list = triangles[submesh];
		list.Add(first);
		list.Add(first + 2);
		list.Add(first + 1);
		list.Add(first);
		list.Add(first + 3);
		list.Add(first + 2);
	}
}
