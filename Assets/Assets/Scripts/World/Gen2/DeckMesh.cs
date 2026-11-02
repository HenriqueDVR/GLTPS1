using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// a tile's ground: the deck's top with the voids (ChasmPlan) cut out of it (marching squares over 8 m cells, so the
// ragged edges run smooth), the cut faces going down into the dark with girders sticking out, a dark floor far down
// under the void. three submeshes (snow, concrete, dark).
// tile-local like RoadMesh
public class DeckMesh
{
	public const int SubmeshCount = 3;

	public const float Cell = 8f;

	private const int snow = 0;

	private const int concrete = 1;

	private const int dark = 2;

	private const float rim = 2.5f;

	private const int cells = 16;

	// at the cell corners, positive on the deck
	private readonly float[,] solid = new float[cells + 1, cells + 1];

	// at the cell corners, in a collapsed section (ChasmPlan.Collapse): no floor down there and no underside, it's open
	// right through to the layer below. a cell with a corner on deck keeps both, a lip at the hole's foot
	private readonly bool[,] open = new bool[cells + 1, cells + 1];

	private readonly double[,] collapsed = new double[cells + 1, cells + 1];

	private bool anyOpen;

	// the deck's underside, the ceiling seen from the layer below: a flat slab this far down, whole but for the elevator
	// shafts. under the underground's deepest pits (UndergroundPlan), and a few metres under where DistantPillar draws the
	// ceiling, so riding up the real layer above wins over it
	public const float UndersideDepth = 60f;

	// the dark floor the voids end on, and how far down their cut edges go: on the underside, so from below a void
	// doesn't show
	public const float Abyss = UndersideDepth;

	private readonly List<Vector3> polygon = new List<Vector3>();

	private readonly List<bool> crossing = new List<bool>();

	private readonly List<Vector3> vertices = new List<Vector3>();

	private readonly List<Vector2> uvs = new List<Vector2>();

	private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };

	public int Triangles { get; private set; }

	// whether any of the tile is void
	public bool Broken { get; private set; }

	// holes: the underground's shafts reaching into the tile (UndergroundPlan.Holes) and the elevator shafts, cut out of
	// the top exactly; shafts: the elevators' alone, cut through the underside too. both keep far from voids, so a tile
	// has one or the other
	public void Build(Mesh mesh, ChasmPlan chasms, int tileX, int tileZ, List<Area> holes = null, List<Area> shafts = null)
	{
		vertices.Clear();
		uvs.Clear();
		foreach (List<int> list in triangles)
		{
			list.Clear();
		}
		double size = Gen2.TileSize;
		double x0 = tileX * size;
		double z0 = tileZ * size;
		// nothing near: one quad. a collapse's value runs shallow far from it, so it's looked for over the tile
		bool collapse = chasms.CollapseNear(x0, z0, size, Cell + ChasmPlan.Ragged + 2.0);
		double rift = chasms.Rifts(x0 + size * 0.5, z0 + size * 0.5);
		Broken = collapse || (rift != double.MinValue && rift + ChasmPlan.Ragged > -(size * 0.75 + Cell + ChasmPlan.Ragged + 2.0));
		anyOpen = false;
		for (int i = 0; i <= cells && collapse; i++)
		{
			for (int j = 0; j <= cells; j++)
			{
				collapsed[i, j] = chasms.Collapse(x0 + i * Cell, z0 + j * Cell);
				open[i, j] = collapsed[i, j] > 0.0;
				anyOpen |= open[i, j];
			}
		}
		if (!Broken && (holes == null || holes.Count == 0))
		{
			Top(Vector3.zero, new Vector3((float)size, 0f, (float)size));
		}
		else if (!Broken)
		{
			// the tile minus each hole, as rectangles
			Pieces(x0, z0, holes);
			foreach (Area r in pieces)
			{
				Top(new Vector3((float)(r.X0 - x0), 0f, (float)(r.Z0 - z0)), new Vector3((float)(r.X1 - x0), 0f, (float)(r.Z1 - z0)));
			}
		}
		else
		{
			// how solid each corner is (minus how far into a void), marching squares over the cells
			for (int i = 0; i <= cells; i++)
			{
				for (int j = 0; j <= cells; j++)
				{
					solid[i, j] = (float)-chasms.Inside(x0 + i * Cell, z0 + j * Cell, collapse ? collapsed[i, j] : double.MinValue);
				}
			}
			for (int j = 0; j < cells; j++)
			{
				int i = 0;
				while (i < cells)
				{
					// whole solid cells along x as one quad
					int start = i;
					while (i < cells && Full(i, j))
					{
						i++;
					}
					if (i > start)
					{
						Top(new Vector3(start * Cell, 0f, j * Cell), new Vector3(i * Cell, 0f, (j + 1) * Cell));
						continue;
					}
					March(i, j, tileX * cells + i, tileZ * cells + j);
					i++;
				}
			}
		}
		if (anyOpen)
		{
			OpenUnderside();
		}
		else
		{
			Underside(x0, z0, shafts);
		}
		mesh.Clear();
		mesh.indexFormat = (vertices.Count > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16;
		mesh.SetVertices(vertices);
		mesh.SetUVs(0, uvs);
		mesh.subMeshCount = SubmeshCount;
		Triangles = 0;
		for (int i = 0; i < SubmeshCount; i++)
		{
			mesh.SetTriangles(triangles[i], i, false);
			Triangles += triangles[i].Count / 3;
		}
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
	}

	// the tile as rectangles, minus the holes
	private void Pieces(double x0, double z0, List<Area> holes)
	{
		double size = Gen2.TileSize;
		pieces.Clear();
		pieces.Add(new Area { X0 = x0, Z0 = z0, X1 = x0 + size, Z1 = z0 + size });
		if (holes == null)
		{
			return;
		}
		foreach (Area hole in holes)
		{
			for (int i = pieces.Count - 1; i >= 0; i--)
			{
				Area r = pieces[i];
				if (hole.X1 <= r.X0 || hole.X0 >= r.X1 || hole.Z1 <= r.Z0 || hole.Z0 >= r.Z1)
				{
					continue;
				}
				pieces.RemoveAt(i);
				Keep(r.X0, r.Z0, hole.X0, r.Z1);
				Keep(hole.X1, r.Z0, r.X1, r.Z1);
				Keep(System.Math.Max(r.X0, hole.X0), r.Z0, System.Math.Min(r.X1, hole.X1), hole.Z0);
				Keep(System.Math.Max(r.X0, hole.X0), hole.Z1, System.Math.Min(r.X1, hole.X1), r.Z1);
			}
		}
	}

	// flat and whole under voids and the underground's holes too: from below the layer is one slab
	private void Underside(double x0, double z0, List<Area> shafts)
	{
		Pieces(x0, z0, shafts);
		foreach (Area r in pieces)
		{
			float ax = (float)(r.X0 - x0);
			float az = (float)(r.Z0 - z0);
			float bx = (float)(r.X1 - x0);
			float bz = (float)(r.Z1 - z0);
			int first = vertices.Count;
			Add(new Vector3(ax, -UndersideDepth, az), new Vector2(ax, az) / (float)Gen2.TileSize);
			Add(new Vector3(ax, -UndersideDepth, bz), new Vector2(ax, bz) / (float)Gen2.TileSize);
			Add(new Vector3(bx, -UndersideDepth, bz), new Vector2(bx, bz) / (float)Gen2.TileSize);
			Add(new Vector3(bx, -UndersideDepth, az), new Vector2(bx, az) / (float)Gen2.TileSize);
			Face(dark, first, Vector3.down);
		}
	}

	// the underside but under the collapse, a run of cells at a time. collapses keep far from the pillars, so no
	// elevator shaft shares a tile with one
	private void OpenUnderside()
	{
		for (int j = 0; j < cells; j++)
		{
			int i = 0;
			while (i < cells)
			{
				if (Gone(i, j))
				{
					i++;
					continue;
				}
				int start = i;
				while (i < cells && !Gone(i, j))
				{
					i++;
				}
				int first = vertices.Count;
				Add(new Vector3(start * Cell, -UndersideDepth, j * Cell), Vector2.zero);
				Add(new Vector3(start * Cell, -UndersideDepth, (j + 1) * Cell), Vector2.zero);
				Add(new Vector3(i * Cell, -UndersideDepth, (j + 1) * Cell), Vector2.zero);
				Add(new Vector3(i * Cell, -UndersideDepth, j * Cell), Vector2.zero);
				Face(dark, first, Vector3.down);
			}
		}
	}

	// the cell's all in a collapse
	private bool Gone(int i, int j)
	{
		return anyOpen && open[i, j] && open[i + 1, j] && open[i, j + 1] && open[i + 1, j + 1];
	}

	private bool Full(int i, int j)
	{
		return solid[i, j] > 0f && solid[i + 1, j] > 0f && solid[i, j + 1] > 0f && solid[i + 1, j + 1] > 0f;
	}

	// one cell cut by a void's edge: the solid part's top, the cut faces down into the void, the dark floor under it
	private void March(int i, int j, int cellX, int cellZ)
	{
		polygon.Clear();
		crossing.Clear();
		int[] ci = { i, i + 1, i + 1, i };
		int[] cj = { j, j, j + 1, j + 1 };
		for (int k = 0; k < 4; k++)
		{
			float a = solid[ci[k], cj[k]];
			float b = solid[ci[(k + 1) % 4], cj[(k + 1) % 4]];
			Vector3 pa = new Vector3(ci[k] * Cell, 0f, cj[k] * Cell);
			Vector3 pb = new Vector3(ci[(k + 1) % 4] * Cell, 0f, cj[(k + 1) % 4] * Cell);
			if (a > 0f)
			{
				polygon.Add(pa);
				crossing.Add(false);
			}
			if ((a > 0f) != (b > 0f))
			{
				polygon.Add(Vector3.Lerp(pa, pb, a / (a - b)));
				crossing.Add(true);
			}
		}
		// the floor far down under the whole cell, it's under deck where the cell's solid anyway. none in a collapse
		if (!Gone(i, j))
		{
			Vector3 c0 = new Vector3(i * Cell, -Abyss, j * Cell);
			int floor = vertices.Count;
			Add(c0, Vector2.zero);
			Add(c0 + new Vector3(Cell, 0f, 0f), Vector2.zero);
			Add(c0 + new Vector3(Cell, 0f, Cell), Vector2.zero);
			Add(c0 + new Vector3(0f, 0f, Cell), Vector2.zero);
			Face(dark, floor, Vector3.up);
		}
		if (polygon.Count < 3)
		{
			return;
		}
		for (int k = 1; k + 1 < polygon.Count; k++)
		{
			int first = vertices.Count;
			Add(polygon[0], Uv(polygon[0]));
			Add(polygon[k], Uv(polygon[k]));
			Add(polygon[k + 1], Uv(polygon[k + 1]));
			Tri(snow, first, Vector3.up);
		}
		// a cut runs from a crossing to the next one round the polygon
		Vector3 middle = Vector3.zero;
		foreach (Vector3 p in polygon)
		{
			middle += p;
		}
		middle /= polygon.Count;
		for (int k = 0; k < polygon.Count; k++)
		{
			int next = (k + 1) % polygon.Count;
			if (crossing[k] && crossing[next])
			{
				Vector3 p = polygon[k];
				Vector3 q = polygon[next];
				Vector3 into = Vector3.Cross(Vector3.up, q - p).normalized;
				if (Vector3.Dot(into, middle - (p + q) * 0.5f) > 0f)
				{
					into = -into;
				}
				Edge(p, q, into, cellX, cellZ, k);
			}
		}
	}

	private static Vector2 Uv(Vector3 p)
	{
		return new Vector2(p.x, p.z) / (float)Gen2.TileSize;
	}

	// snow, uv over the whole tile (the material repeats it)
	private readonly List<Area> pieces = new List<Area>();

	private void Keep(double x0, double z0, double x1, double z1)
	{
		if (x1 - x0 > 1e-6 && z1 - z0 > 1e-6)
		{
			pieces.Add(new Area { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1 });
		}
	}

	private void Top(Vector3 a, Vector3 b)
	{
		int first = vertices.Count;
		Add(new Vector3(a.x, 0f, a.z), new Vector2(a.x, a.z) / (float)Gen2.TileSize);
		Add(new Vector3(a.x, 0f, b.z), new Vector2(a.x, b.z) / (float)Gen2.TileSize);
		Add(new Vector3(b.x, 0f, b.z), new Vector2(b.x, b.z) / (float)Gen2.TileSize);
		Add(new Vector3(b.x, 0f, a.z), new Vector2(b.x, a.z) / (float)Gen2.TileSize);
		Face(snow, first, Vector3.up);
	}

	// a cut edge from p to q (the void on the side `into` points at): the rim, the slab under it in shadow, now and
	// then a girder snapped off and sticking out. which cell and side picks the girder, so peers agree
	private void Edge(Vector3 p, Vector3 q, Vector3 into, int cellX, int cellZ, int side)
	{
		Wall(concrete, p, q, 0f, -rim, into);
		Wall(dark, p, q, -rim, -Abyss, into);
		ulong roll = WorldRandom.Derive((ulong)(uint)cellX, Gen2.StreamChasm + 2, Gen2.Pack(cellZ, side));
		if (roll % 100uL < 18uL)
		{
			Vector3 middle = (p + q) * 0.5f + (q - p) * ((roll >> 8) % 60uL / 100f - 0.3f);
			float length = 2f + (roll >> 16) % 7uL;
			float y = -rim - 1.5f - (roll >> 24) % 6uL;
			Beam(middle + into * (length * 0.5f) + Vector3.up * y, into, length);
		}
	}

	private void Wall(int submesh, Vector3 p, Vector3 q, float top, float bottom, Vector3 into)
	{
		int first = vertices.Count;
		float along = (q - p).magnitude;
		Vector2 at = new Vector2(p.x + p.z, 0f) * 0.25f;
		Add(p + Vector3.up * bottom, at + new Vector2(0f, bottom * 0.25f));
		Add(p + Vector3.up * top, at + new Vector2(0f, top * 0.25f));
		Add(q + Vector3.up * top, at + new Vector2(along * 0.25f, top * 0.25f));
		Add(q + Vector3.up * bottom, at + new Vector2(along * 0.25f, bottom * 0.25f));
		Face(submesh, first, into);
	}

	// a 0.8 m steel beam along `along`, no ends
	private void Beam(Vector3 centre, Vector3 along, float length)
	{
		Vector3 across = new Vector3(-along.z, 0f, along.x) * 0.4f;
		Vector3 up = Vector3.up * 0.4f;
		Vector3 f = along * (length * 0.5f);
		Vector3[] ring = { -across - up, -across + up, across + up, across - up };
		for (int k = 0; k < 4; k++)
		{
			int first = vertices.Count;
			Vector3 a = centre + ring[k];
			Vector3 b = centre + ring[(k + 1) % 4];
			Add(a - f, Vector2.zero);
			Add(b - f, new Vector2(0f, 0.2f));
			Add(b + f, new Vector2(length * 0.25f, 0.2f));
			Add(a + f, new Vector2(length * 0.25f, 0f));
			Face(dark, first, (ring[k] + ring[(k + 1) % 4]).normalized);
		}
		int cap = vertices.Count;
		for (int k = 3; k >= 0; k--)
		{
			Add(centre + f + ring[k], Vector2.zero);
		}
		Face(dark, cap, along);
	}

	private void Tri(int submesh, int first, Vector3 outward)
	{
		List<int> list = triangles[submesh];
		Vector3 a = vertices[first];
		bool facing = Vector3.Dot(Vector3.Cross(vertices[first + 1] - a, vertices[first + 2] - a), outward) > 0f;
		list.Add(first);
		list.Add(facing ? first + 1 : first + 2);
		list.Add(facing ? first + 2 : first + 1);
	}

	// the quad just added, wound to show on the side `outward` points at
	private void Face(int submesh, int first, Vector3 outward)
	{
		List<int> list = triangles[submesh];
		Vector3 a = vertices[first];
		bool facing = Vector3.Dot(Vector3.Cross(vertices[first + 1] - a, vertices[first + 2] - a), outward) > 0f;
		list.Add(first);
		list.Add(facing ? first + 1 : first + 2);
		list.Add(facing ? first + 2 : first + 1);
		list.Add(first);
		list.Add(facing ? first + 2 : first + 3);
		list.Add(facing ? first + 3 : first + 2);
	}

	private void Add(Vector3 p, Vector2 uv)
	{
		vertices.Add(p);
		uvs.Add(uv);
	}
}
