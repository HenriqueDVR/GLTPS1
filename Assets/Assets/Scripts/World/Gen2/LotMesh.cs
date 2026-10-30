using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// buildings and rubble for one tile's lots, in the district's style (13-content-pipeline, look of the source):
// monoliths with raised panels and few small windows, office slabs with window bands, ruins and skeleton frames,
// industrial tanks with pipes that reach the ground. one mesh, four submeshes (office walls, concrete, dark, metal).
// tile-local like RoadMesh
public class LotMesh
{
	public const int SubmeshCount = 4;

	private const int office = 0;

	private const int concrete = 1;

	private const int dark = 2;

	private const int metal = 3;

	public const float FloorHeight = 3.5f;

	private const float module = 4f;

	// buildings sit this deep so they don't float on uneven ground later
	private const float sink = 0.5f;

	private const int tankSides = 12;

	private readonly List<Vector3> vertices = new List<Vector3>();

	private readonly List<Vector2> uvs = new List<Vector2>();

	private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

	private WorldRandom random;

	public int Triangles { get; private set; }

	// lots as they'll be drawn: a landmark lot that got no landmark comes in as Open
	public bool Build(Mesh mesh, List<Lot> lots, DistrictDef[] districts, double ruin, double originX, double originZ)
	{
		vertices.Clear();
		uvs.Clear();
		foreach (List<int> list in triangles)
		{
			list.Clear();
		}
		foreach (Lot lot in lots)
		{
			random = new WorldRandom(lot.Seed);
			Vector3 centre = new Vector3((float)(lot.Center.X - originX), 0f, (float)(lot.Center.Z - originZ));
			Vector3 u = new Vector3((float)lot.Axis.X, 0f, (float)lot.Axis.Z);
			float hu = (float)lot.HalfU;
			float hv = (float)lot.HalfV;
			if (lot.Kind == LotKind.Open)
			{
				Rubble(centre, u, hu, hv);
				continue;
			}
			if (lot.Kind != LotKind.Building)
			{
				continue;
			}
			switch (districts[lot.District].Style)
			{
				case BuildingStyle.Slab:
					Slab(centre, u, hu, hv, lot.Floors);
					break;
				case BuildingStyle.Ruin:
					if (Unit() < 0.5f)
					{
						Skeleton(centre, u, hu, hv, lot.Floors);
					}
					else
					{
						Monolith(centre, u, hu, hv, lot.Floors, 1.0);
					}
					break;
				case BuildingStyle.Industrial:
					Industrial(centre, u, hu, hv, lot.Floors);
					break;
				default:
					Monolith(centre, u, hu, hv, lot.Floors, ruin);
					break;
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

	private float Unit()
	{
		return (float)Gen2.Unit(random.NextULong());
	}

	private float Range(float min, float max)
	{
		return min + (max - min) * Unit();
	}

	private static Vector3 Left(Vector3 axis)
	{
		return new Vector3(-axis.z, 0f, axis.x);
	}

	// ---- styles

	// a concrete mass, maybe a set-back upper block, raised panels and a few small windows on its faces,
	// boxes jutting out higher up, something on the roof. a ruin loses part of its top
	private void Monolith(Vector3 centre, Vector3 u, float hu, float hv, int floors, double ruin)
	{
		float margin = Range(1f, 4f);
		hu = Mathf.Max(3f, hu - margin);
		hv = Mathf.Max(3f, hv - margin);
		Vector3 v = Left(u);
		float height = floors * FloorHeight + Range(0f, 2f);
		bool broken = floors >= 2 && Unit() < ruin;
		if (broken)
		{
			float at = Range(0.4f, 0.7f);
			float lost = Mathf.Max(1, Mathf.FloorToInt(Unit() * floors)) * FloorHeight + Range(0f, 1.2f);
			float a = hu * at;
			float b = hu - a;
			Box(centre - u * (hu - a), u, a, hv, -sink, height, concrete, concrete, false);
			Box(centre + u * (hu - b), u, b, hv, -sink, Mathf.Max(1.5f, height - lost), concrete, concrete, false);
		}
		else
		{
			Box(centre, u, hu, hv, -sink, height, concrete, concrete, false);
			if (Unit() < 0.6f)
			{
				// a narrower block on top, pushed towards one corner
				float su = hu * Range(0.45f, 0.85f);
				float sv = hv * Range(0.45f, 0.85f);
				Vector3 top = centre + u * ((hu - su) * Range(-1f, 1f)) + v * ((hv - sv) * Range(-1f, 1f));
				Box(top, u, su, sv, height, height + Mathf.FloorToInt(Range(1f, 5f)) * FloorHeight, concrete, concrete, false);
			}
		}
		for (int side = 0; side < 4; side++)
		{
			Vector3 normal = (side == 0) ? u : (side == 1) ? -u : (side == 2) ? v : -v;
			float half = (side < 2) ? hv : hu;
			float depth = (side < 2) ? hu : hv;
			Vector3 face = centre + normal * depth;
			FaceDetail(face, normal, half, broken ? height * 0.6f : height);
		}
		if (!broken && Unit() < 0.3f)
		{
			// a stair house or a tank on the roof
			Box(centre + u * (hu * Range(-0.5f, 0.5f)) + v * (hv * Range(-0.5f, 0.5f)), u, Range(1.5f, 3f), Range(1.5f, 3f), height, height + Range(2.5f, 4f), concrete, concrete, false);
		}
	}

	// panels standing out of a face, a few small square windows, sometimes a cantilevered box
	private void FaceDetail(Vector3 face, Vector3 normal, float half, float height)
	{
		Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;
		int panels = 2 + random.Next(4);
		for (int i = 0; i < panels; i++)
		{
			float w = Range(1.5f, Mathf.Min(8f, half));
			float h = Range(2f, Mathf.Max(2.1f, Mathf.Min(12f, height * 0.6f)));
			float x = Range(-half + w * 0.5f, half - w * 0.5f);
			float y = Range(0.5f, Mathf.Max(0.6f, height - h - 0.5f));
			float d = Range(0.2f, 0.5f);
			Box(face + along * x + normal * (d * 0.5f), along, w * 0.5f, d * 0.5f, y, y + h, concrete, concrete, true);
		}
		// dark traces cut into the face, straight or bent once, like the source's walls
		int traces = 2 + random.Next(5);
		for (int i = 0; i < traces; i++)
		{
			float width = Range(0.25f, 0.6f);
			float x = Range(-half + 0.5f, half - 0.5f);
			float y = Range(0.8f, Mathf.Max(0.9f, height - 1f));
			float run = Range(3f, Mathf.Max(3.1f, Mathf.Min(16f, height - y)));
			bool up = Unit() < 0.5f;
			Trace(face, along, normal, half, height, x, y, up ? 0f : run, up ? run : 0f, width);
			if (Unit() < 0.5f)
			{
				// the bend
				float turn = Range(2f, 8f) * (Unit() < 0.5f ? -1f : 1f);
				Trace(face, along, normal, half, height, up ? x : x + run, up ? y + run : y, up ? turn : 0f, up ? 0f : -Mathf.Abs(turn), width);
			}
		}
		int windows = random.Next(5);
		for (int i = 0; i < windows; i++)
		{
			float s = Range(0.7f, 1.3f);
			int row = 1 + random.Next(4);
			float x = Range(-half + 1f, Mathf.Max(-half + 1.1f, half - 1f - row * s * 1.6f));
			float y = Mathf.Floor(Range(1f, Mathf.Max(1.1f, height - 2f)) / FloorHeight) * FloorHeight + 1.2f;
			for (int j = 0; j < row; j++)
			{
				Vector3 at = face + along * (x + j * s * 1.6f) + normal * 0.04f + Vector3.up * y;
				Quad(dark, at, at + along * s, at + along * s + Vector3.up * s, at + Vector3.up * s, normal);
			}
		}
		if (height > 10f && Unit() < 0.35f)
		{
			float w = Range(3f, Mathf.Max(3.1f, Mathf.Min(9f, half * 1.6f)));
			float d = Range(2f, 5f);
			float y = Range(height * 0.35f, height - 4f);
			float x = Range(-half + w * 0.5f, half - w * 0.5f);
			Box(face + along * x + normal * (d * 0.5f), along, w * 0.5f, d * 0.5f, y, y + Range(3f, 7f), concrete, concrete, true);
		}
	}

	// a dark strip from (x, y) running dx along the face and dy up, clipped to the face
	private void Trace(Vector3 face, Vector3 along, Vector3 normal, float half, float height, float x, float y, float dx, float dy, float width)
	{
		float x0 = Mathf.Clamp(Mathf.Min(x, x + dx) - width * 0.5f, -half, half);
		float x1 = Mathf.Clamp(Mathf.Max(x, x + dx) + width * 0.5f, -half, half);
		float y0 = Mathf.Clamp(Mathf.Min(y, y + dy) - width * 0.5f, 0f, height);
		float y1 = Mathf.Clamp(Mathf.Max(y, y + dy) + width * 0.5f, 0f, height);
		if (x1 - x0 < 0.1f || y1 - y0 < 0.1f)
		{
			return;
		}
		Vector3 o = face + normal * 0.03f;
		Quad(dark, o + along * x0 + Vector3.up * y0, o + along * x1 + Vector3.up * y0, o + along * x1 + Vector3.up * y1, o + along * x0 + Vector3.up * y1, normal);
	}

	// a tall office slab along the lot's long side, a dark window band on every floor of its long faces
	private void Slab(Vector3 centre, Vector3 u, float hu, float hv, int floors)
	{
		if (hv > hu)
		{
			u = Left(u);
			float h = hu;
			hu = hv;
			hv = h;
		}
		hu = Mathf.Max(4f, hu - Range(1f, 2.5f));
		hv = Mathf.Max(4f, Mathf.Min(hv - 1.5f, Range(5f, 8f)));
		Vector3 v = Left(u);
		int storeys = floors + 3;
		float height = storeys * FloorHeight;
		Box(centre, u, hu, hv, -sink, height, office, concrete, false);
		for (int side = -1; side <= 1; side += 2)
		{
			Vector3 normal = v * side;
			Vector3 face = centre + normal * (hv + 0.04f);
			for (int f = 0; f < storeys; f++)
			{
				Vector3 a = face - u * (hu - 0.6f) + Vector3.up * (f * FloorHeight + 1.1f);
				Vector3 b = face + u * (hu - 0.6f) + Vector3.up * (f * FloorHeight + 1.1f);
				Quad(dark, a, b, b + Vector3.up * 1.3f, a + Vector3.up * 1.3f, normal);
			}
		}
		// a parapet along the long edges of the roof
		Box(centre + v * (hv - 0.15f), u, hu, 0.15f, height, height + 1f, concrete, concrete, false);
		Box(centre - v * (hv - 0.15f), u, hu, 0.15f, height, height + 1f, concrete, concrete, false);
	}

	// bare columns and beams, broken off at different heights, a slab or two left
	private void Skeleton(Vector3 centre, Vector3 u, float hu, float hv, int floors)
	{
		hu = Mathf.Max(3f, hu - Range(1f, 3f));
		hv = Mathf.Max(3f, hv - Range(1f, 3f));
		Vector3 v = Left(u);
		int nu = Mathf.Max(2, Mathf.RoundToInt(hu * 2f / Range(4f, 6f)) + 1);
		int nv = Mathf.Max(2, Mathf.RoundToInt(hv * 2f / Range(4f, 6f)) + 1);
		int storeys = Mathf.Max(2, floors + 1);
		int[,] tops = new int[nu, nv];
		for (int i = 0; i < nu; i++)
		{
			for (int j = 0; j < nv; j++)
			{
				tops[i, j] = Unit() < 0.15f ? 0 : 1 + random.Next(storeys);
			}
		}
		for (int i = 0; i < nu; i++)
		{
			for (int j = 0; j < nv; j++)
			{
				Vector3 at = Grid(centre, u, v, hu, hv, nu, nv, i, j);
				if (tops[i, j] > 0)
				{
					Box(at, u, 0.3f, 0.3f, -sink, tops[i, j] * FloorHeight, concrete, concrete, false);
				}
				// beams to the next column along each axis, on the floors both still reach
				for (int axis = 0; axis < 2; axis++)
				{
					int ni = i + (axis == 0 ? 1 : 0);
					int nj = j + (axis == 1 ? 1 : 0);
					if (ni >= nu || nj >= nv)
					{
						continue;
					}
					Vector3 to = Grid(centre, u, v, hu, hv, nu, nv, ni, nj);
					int reach = Mathf.Min(tops[i, j], tops[ni, nj]);
					for (int f = 1; f <= reach; f++)
					{
						if (Unit() < 0.8f)
						{
							float y = f * FloorHeight - 0.25f;
							Beam(at + Vector3.up * y, to + Vector3.up * y, 0.25f, concrete);
						}
					}
				}
			}
		}
		if (Unit() < 0.6f)
		{
			// what's left of a floor slab
			Box(centre + u * (hu * Range(-0.4f, 0.4f)), u, hu * Range(0.3f, 0.6f), hv * Range(0.4f, 0.9f), FloorHeight - 0.3f, FloorHeight, concrete, concrete, true);
		}
	}

	private static Vector3 Grid(Vector3 centre, Vector3 u, Vector3 v, float hu, float hv, int nu, int nv, int i, int j)
	{
		return centre + u * (-hu + hu * 2f * i / (nu - 1)) + v * (-hv + hv * 2f * j / (nv - 1));
	}

	// one to three tanks on a concrete pad, banded, a pipe from each down to the ground and between neighbours
	private void Industrial(Vector3 centre, Vector3 u, float hu, float hv, int floors)
	{
		// not every lot is a tank farm: sheds and blocks between them
		if (Unit() < 0.4f)
		{
			Monolith(centre, u, hu, hv, Mathf.Max(1, floors - 1), 0.2);
			return;
		}
		Vector3 v = Left(u);
		if (hv > hu)
		{
			u = Left(u);
			v = Left(u);
			float h = hu;
			hu = hv;
			hv = h;
		}
		hu = Mathf.Max(4f, hu - 1.5f);
		hv = Mathf.Max(4f, hv - 1.5f);
		Box(centre, u, hu, hv, -sink, 0.3f, concrete, concrete, false);
		int count = 1 + random.Next(Mathf.Clamp(Mathf.FloorToInt(hu * hv / 120f), 1, 3));
		Vector3[] at = new Vector3[count];
		float[] radius = new float[count];
		float[] height = new float[count];
		float slot = hu * 2f / count;
		for (int i = 0; i < count; i++)
		{
			// side by side along the long axis
			radius[i] = Mathf.Min(Range(2.5f, 7f), slot * 0.4f, hv * 0.8f);
			height[i] = Range(6f, 10f + floors * 3f);
			at[i] = centre + u * (-hu + slot * (i + 0.5f)) + v * (Range(-1f, 1f) * (hv - radius[i]) * 0.5f);
			Cylinder(at[i], radius[i], 0.3f, height[i], metal, true);
			for (float y = 3f; y < height[i] - 1f; y += Range(3f, 5f))
			{
				Cylinder(at[i], radius[i] + 0.12f, y, y + 0.3f, metal, false);
			}
			// out of the side and down into the ground
			Vector3 dir = (i % 2 == 0) ? v : -v;
			float py = height[i] * Range(0.4f, 0.8f);
			float pipe = Range(0.25f, 0.45f);
			Vector3 start = at[i] + dir * radius[i] + Vector3.up * py;
			Vector3 elbow = start + dir * Range(1.5f, 3f);
			Beam(start, elbow + dir * pipe, pipe, metal);
			Beam(elbow + Vector3.up * pipe, elbow - Vector3.up * (py + sink), pipe, metal);
		}
		for (int i = 0; i + 1 < count; i++)
		{
			float y = Mathf.Min(height[i], height[i + 1]) * Range(0.5f, 0.85f);
			Vector3 dir = (at[i + 1] - at[i]).normalized;
			Beam(at[i] + dir * radius[i] + Vector3.up * y, at[i + 1] - dir * radius[i + 1] + Vector3.up * y, Range(0.3f, 0.6f), metal);
		}
	}

	// a few low slabs and heaps, turned every which way
	private void Rubble(Vector3 centre, Vector3 u, float hu, float hv)
	{
		Vector3 v = Left(u);
		int count = 3 + random.Next(5);
		for (int i = 0; i < count; i++)
		{
			Vector3 at = centre + u * (Range(-1f, 1f) * hu * 0.7f) + v * (Range(-1f, 1f) * hv * 0.7f);
			Vector3 axis = new Vector3(1f, 0f, Range(-1f, 1f)).normalized;
			Box(at, axis, Range(1f, 4f), Range(1f, 4f), -sink, Range(0.4f, 1.8f), concrete, concrete, false);
		}
	}

	// ---- shapes

	// four walls and a top, a bottom too if it hangs in the air
	private void Box(Vector3 centre, Vector3 u, float hu, float hv, float y0, float y1, int sides, int top, bool bottom)
	{
		Vector3 v = Left(u);
		Vector3 lo = Vector3.up * y0;
		Vector3 hi = Vector3.up * y1;
		Vector3 c00 = centre - u * hu - v * hv;
		Vector3 c10 = centre + u * hu - v * hv;
		Vector3 c11 = centre + u * hu + v * hv;
		Vector3 c01 = centre - u * hu + v * hv;
		Quad(sides, c10 + lo, c11 + lo, c11 + hi, c10 + hi, u);
		Quad(sides, c01 + lo, c00 + lo, c00 + hi, c01 + hi, -u);
		Quad(sides, c11 + lo, c01 + lo, c01 + hi, c11 + hi, v);
		Quad(sides, c00 + lo, c10 + lo, c10 + hi, c00 + hi, -v);
		Quad(top, c00 + hi, c10 + hi, c11 + hi, c01 + hi, Vector3.up);
		if (bottom)
		{
			Quad(top, c00 + lo, c01 + lo, c11 + lo, c10 + lo, Vector3.down);
		}
	}

	// a square-section bar from a to b
	private void Beam(Vector3 a, Vector3 b, float half, int submesh)
	{
		Vector3 d = b - a;
		if (d.sqrMagnitude < 1e-6f)
		{
			return;
		}
		d.Normalize();
		Vector3 s = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized * half;
		Vector3 t = Vector3.Cross(d, s).normalized * half;
		Vector3 c0 = s + t;
		Vector3 c1 = -s + t;
		Vector3 c2 = -s - t;
		Vector3 c3 = s - t;
		Quad(submesh, a + c0, a + c1, b + c1, b + c0, (c0 + c1).normalized);
		Quad(submesh, a + c1, a + c2, b + c2, b + c1, (c1 + c2).normalized);
		Quad(submesh, a + c2, a + c3, b + c3, b + c2, (c2 + c3).normalized);
		Quad(submesh, a + c3, a + c0, b + c0, b + c3, (c3 + c0).normalized);
		Quad(submesh, a + c0, a + c1, a + c2, a + c3, -d);
		Quad(submesh, b + c0, b + c1, b + c2, b + c3, d);
	}

	// an upright many-sided cylinder, capped on top if asked
	private void Cylinder(Vector3 centre, float radius, float y0, float y1, int submesh, bool cap)
	{
		for (int i = 0; i < tankSides; i++)
		{
			float a0 = i * Mathf.PI * 2f / tankSides;
			float a1 = (i + 1) * Mathf.PI * 2f / tankSides;
			Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
			Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
			Vector3 p0 = centre + d0 * radius;
			Vector3 p1 = centre + d1 * radius;
			Quad(submesh, p0 + Vector3.up * y0, p1 + Vector3.up * y0, p1 + Vector3.up * y1, p0 + Vector3.up * y1, (d0 + d1).normalized);
			if (cap)
			{
				Triangle(submesh, centre + Vector3.up * y1, p0 + Vector3.up * y1, p1 + Vector3.up * y1, Vector3.up);
			}
		}
	}

	// ---- triangles

	// four corners in order round the face, wound to face outward whichever way round they came.
	// tops get planar uvs, walls run along the wall and up a floor per repeat
	private void Quad(int submesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
	{
		int first = vertices.Count;
		Add(p0, outward);
		Add(p1, outward);
		Add(p2, outward);
		Add(p3, outward);
		List<int> list = triangles[submesh];
		bool facing = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) > 0f;
		list.Add(first);
		list.Add(facing ? first + 1 : first + 2);
		list.Add(facing ? first + 2 : first + 1);
		list.Add(first);
		list.Add(facing ? first + 2 : first + 3);
		list.Add(facing ? first + 3 : first + 2);
	}

	private void Triangle(int submesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 outward)
	{
		int first = vertices.Count;
		Add(p0, outward);
		Add(p1, outward);
		Add(p2, outward);
		List<int> list = triangles[submesh];
		bool facing = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) > 0f;
		list.Add(first);
		list.Add(facing ? first + 1 : first + 2);
		list.Add(facing ? first + 2 : first + 1);
	}

	private void Add(Vector3 p, Vector3 outward)
	{
		vertices.Add(p);
		if (Mathf.Abs(outward.y) > 0.7f)
		{
			uvs.Add(new Vector2(p.x, p.z) / module);
		}
		else
		{
			Vector3 along = Vector3.Cross(Vector3.up, outward).normalized;
			uvs.Add(new Vector2(Vector3.Dot(p, along) / module, p.y / FloorHeight));
		}
	}
}
