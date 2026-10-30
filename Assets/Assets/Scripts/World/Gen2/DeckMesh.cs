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

	// the dark floor the voids end on, and how far down their cut edges go
	public const float Abyss = 90f;

	private readonly List<Vector3> polygon = new List<Vector3>();

	private readonly List<bool> crossing = new List<bool>();

	private readonly List<Vector3> vertices = new List<Vector3>();

	private readonly List<Vector2> uvs = new List<Vector2>();

	private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };

	public int Triangles { get; private set; }

	// whether any of the tile is void
	public bool Broken { get; private set; }

	public void Build(Mesh mesh, ChasmPlan chasms, int tileX, int tileZ)
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
		// nothing near: one quad
		Broken = chasms.Inside(x0 + size * 0.5, z0 + size * 0.5) > -(size * 0.75 + Cell + ChasmPlan.Ragged + 2.0);
		if (!Broken)
		{
			Top(Vector3.zero, new Vector3((float)size, 0f, (float)size));
		}
		else
		{
			// how solid each corner is (minus how far into a void), marching squares over the cells
			for (int i = 0; i <= cells; i++)
			{
				for (int j = 0; j <= cells; j++)
				{
					solid[i, j] = (float)-chasms.Inside(x0 + i * Cell, z0 + j * Cell);
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
		// the floor far down under the whole cell, it's under deck where the cell's solid anyway
		Vector3 c0 = new Vector3(i * Cell, -Abyss, j * Cell);
		int floor = vertices.Count;
		Add(c0, Vector2.zero);
		Add(c0 + new Vector3(Cell, 0f, 0f), Vector2.zero);
		Add(c0 + new Vector3(Cell, 0f, Cell), Vector2.zero);
		Add(c0 + new Vector3(0f, 0f, Cell), Vector2.zero);
		Face(dark, floor, Vector3.up);
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
