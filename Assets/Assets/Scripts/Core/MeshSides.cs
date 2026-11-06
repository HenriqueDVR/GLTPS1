using System;
using System.Collections.Generic;

// which triangles of a mesh can be seen from behind, where a culled back would let you see through. a piece (triangles
// joined by their edges) is fine as it is when every edge in it runs both ways, so it's closed, and it holds its volume
// faces out. anything else shows its backs somewhere: a sheet, a box with no bottom, a solid turned inside out, a room
// made to be seen from in. each triangle of such a piece needs a back face, unless one already lies back to back with
// it (a twin, however it was split into triangles)
public static class MeshSides
{
	private struct Key : IEquatable<Key>
	{
		public long X;
		public long Y;
		public long Z;

		public long W;

		public long V;

		public bool Equals(Key other)
		{
			return X == other.X && Y == other.Y && Z == other.Z && W == other.W && V == other.V;
		}

		public override bool Equals(object obj)
		{
			return obj is Key other && Equals(other);
		}

		public override int GetHashCode()
		{
			return Mixed.Hash(Mixed.Mix(Mixed.Mix(Mixed.Mix(Mixed.Mix((ulong)X) ^ (ulong)Y) ^ (ulong)Z) ^ (ulong)W) ^ (ulong)V);
		}
	}

	// a long's own hash is its halves xor'd: an edge between corners n and n+1 hashes the same for every n
	private sealed class Mixed : IEqualityComparer<long>
	{
		public static readonly Mixed Comparer = new Mixed();

		public static ulong Mix(ulong z)
		{
			unchecked
			{
				z += 0x9E3779B97F4A7C15UL;
				z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
				z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
				return z ^ (z >> 31);
			}
		}

		public static int Hash(ulong z)
		{
			return (int)(z ^ (z >> 32));
		}

		public bool Equals(long a, long b)
		{
			return a == b;
		}

		public int GetHashCode(long value)
		{
			return Hash(Mix((ulong)value));
		}
	}

	// positions as x, y, z per vertex; triangles three indices each. weld: how near two corners are the same corner
	public static bool[] Backs(float[] positions, int[] triangles, double weld = 1e-4)
	{
		int count = triangles.Length / 3;
		bool[] backs = new bool[count];
		// corners welded by position: a mesh splits them by normal and uv
		Dictionary<Key, int> welded = new Dictionary<Key, int>();
		int[] corner = new int[positions.Length / 3];
		for (int i = 0; i < corner.Length; i++)
		{
			Key key = new Key { X = (long)Math.Round(positions[i * 3] / weld), Y = (long)Math.Round(positions[i * 3 + 1] / weld), Z = (long)Math.Round(positions[i * 3 + 2] / weld) };
			if (!welded.TryGetValue(key, out int id))
			{
				id = welded.Count;
				welded[key] = id;
			}
			corner[i] = id;
		}
		int[] w = new int[triangles.Length];
		bool[] live = new bool[count];
		for (int t = 0; t < count; t++)
		{
			w[t * 3] = corner[triangles[t * 3]];
			w[t * 3 + 1] = corner[triangles[t * 3 + 1]];
			w[t * 3 + 2] = corner[triangles[t * 3 + 2]];
			live[t] = w[t * 3] != w[t * 3 + 1] && w[t * 3 + 1] != w[t * 3 + 2] && w[t * 3 + 2] != w[t * 3];
		}
		// edges each way, and pieces by shared edges
		Dictionary<long, int> directed = new Dictionary<long, int>(Mixed.Comparer);
		Dictionary<long, int> firstOnEdge = new Dictionary<long, int>(Mixed.Comparer);
		int[] parent = new int[count];
		for (int t = 0; t < count; t++)
		{
			parent[t] = t;
		}
		for (int t = 0; t < count; t++)
		{
			if (!live[t])
			{
				continue;
			}
			for (int e = 0; e < 3; e++)
			{
				int a = w[t * 3 + e];
				int b = w[t * 3 + (e + 1) % 3];
				long d = Edge(a, b);
				directed[d] = directed.TryGetValue(d, out int n) ? n + 1 : 1;
				long u = Edge(Math.Min(a, b), Math.Max(a, b));
				if (firstOnEdge.TryGetValue(u, out int other))
				{
					Join(parent, t, other);
				}
				else
				{
					firstOnEdge[u] = t;
				}
			}
		}
		// volume about the piece's own first corner, so a far piece's sums don't drown in rounding; its size, to judge
		// a volume that's only rounding (a sheet with its twin holds none)
		Dictionary<int, bool> open = new Dictionary<int, bool>();
		Dictionary<int, double> volume = new Dictionary<int, double>();
		Dictionary<int, int> origin = new Dictionary<int, int>();
		Dictionary<int, double> extent = new Dictionary<int, double>();
		for (int t = 0; t < count; t++)
		{
			if (!live[t])
			{
				continue;
			}
			int root = Find(parent, t);
			bool unbalanced = open.TryGetValue(root, out bool was) && was;
			for (int e = 0; e < 3 && !unbalanced; e++)
			{
				int a = w[t * 3 + e];
				int b = w[t * 3 + (e + 1) % 3];
				directed.TryGetValue(Edge(b, a), out int back);
				unbalanced = back != directed[Edge(a, b)];
			}
			open[root] = unbalanced;
			if (!origin.TryGetValue(root, out int o))
			{
				o = triangles[t * 3] * 3;
				origin[root] = o;
			}
			int i0 = triangles[t * 3] * 3;
			int i1 = triangles[t * 3 + 1] * 3;
			int i2 = triangles[t * 3 + 2] * 3;
			double ax = positions[i0] - positions[o], ay = positions[i0 + 1] - positions[o + 1], az = positions[i0 + 2] - positions[o + 2];
			double bx = positions[i1] - positions[o], by = positions[i1 + 1] - positions[o + 1], bz = positions[i1 + 2] - positions[o + 2];
			double cx = positions[i2] - positions[o], cy = positions[i2 + 1] - positions[o + 1], cz = positions[i2 + 2] - positions[o + 2];
			double six = ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
			volume[root] = (volume.TryGetValue(root, out double v) ? v : 0.0) + six / 6.0;
			double far = Math.Max(Math.Max(Math.Abs(ax) + Math.Abs(ay) + Math.Abs(az), Math.Abs(bx) + Math.Abs(by) + Math.Abs(bz)), Math.Abs(cx) + Math.Abs(cy) + Math.Abs(cz));
			extent[root] = Math.Max(extent.TryGetValue(root, out double x) ? x : 0.0, far);
		}
		// back to back: a face on the same plane, facing the other way, under this one's middle. found by plane and by
		// grid cells: a face goes in the finest grid (1 m, 2 m, 4 m... cells) where it spans few
		Dictionary<Key, List<int>> planes = new Dictionary<Key, List<int>>();
		double[] normal = new double[count * 4];
		for (int t = 0; t < count; t++)
		{
			if (!live[t] || !Plane(positions, triangles, t, normal))
			{
				live[t] = false;
				continue;
			}
			Key plane = PlaneKey(normal, t, 1.0);
			int level = 0;
			long x0, y0, z0, x1, y1, z1;
			while (true)
			{
				Bounds(positions, triangles, t, level, out x0, out y0, out z0, out x1, out y1, out z1);
				if ((x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) <= fewCells || level == levels - 1)
				{
					break;
				}
				level++;
			}
			for (long x = x0; x <= x1; x++)
			{
				for (long y = y0; y <= y1; y++)
				{
					for (long z = z0; z <= z1; z++)
					{
						plane.V = Cell(level, x, y, z);
						Add(planes, plane, t);
					}
				}
			}
		}
		for (int t = 0; t < count; t++)
		{
			if (!live[t])
			{
				continue;
			}
			int root = Find(parent, t);
			double reach = extent[root];
			if (!open[root] && volume[root] >= -1e-7 * reach * reach * reach)
			{
				continue;
			}
			backs[t] = !Twinned(positions, triangles, normal, planes, t);
		}
		return backs;
	}

	private const int fewCells = 8;

	private const int levels = 14;

	private static double Size(int level)
	{
		return Math.Pow(2.0, level);
	}

	private static long Cell(int level, long x, long y, long z)
	{
		return ((long)level << 57) | ((x & 0x7FFFF) << 38) | ((y & 0x7FFFF) << 19) | (z & 0x7FFFF);
	}

	private static void Add(Dictionary<Key, List<int>> map, Key key, int t)
	{
		if (!map.TryGetValue(key, out List<int> list))
		{
			list = new List<int>();
			map[key] = list;
		}
		list.Add(t);
	}

	private static void Bounds(float[] p, int[] triangles, int t, int level, out long x0, out long y0, out long z0, out long x1, out long y1, out long z1)
	{
		double cell = Size(level);
		double ax = double.MaxValue, ay = double.MaxValue, az = double.MaxValue;
		double bx = double.MinValue, by = double.MinValue, bz = double.MinValue;
		for (int k = 0; k < 3; k++)
		{
			int i = triangles[t * 3 + k] * 3;
			ax = Math.Min(ax, p[i]);
			ay = Math.Min(ay, p[i + 1]);
			az = Math.Min(az, p[i + 2]);
			bx = Math.Max(bx, p[i]);
			by = Math.Max(by, p[i + 1]);
			bz = Math.Max(bz, p[i + 2]);
		}
		x0 = (long)Math.Floor(ax / cell - 1e-6);
		y0 = (long)Math.Floor(ay / cell - 1e-6);
		z0 = (long)Math.Floor(az / cell - 1e-6);
		x1 = (long)Math.Floor(bx / cell + 1e-6);
		y1 = (long)Math.Floor(by / cell + 1e-6);
		z1 = (long)Math.Floor(bz / cell + 1e-6);
	}

	private static long Edge(int a, int b)
	{
		return ((long)a << 32) | (uint)b;
	}

	private static int Find(int[] parent, int t)
	{
		while (parent[t] != t)
		{
			parent[t] = parent[parent[t]];
			t = parent[t];
		}
		return t;
	}

	private static void Join(int[] parent, int a, int b)
	{
		a = Find(parent, a);
		b = Find(parent, b);
		if (a != b)
		{
			parent[a] = b;
		}
	}

	// unit normal and the plane's distance from the origin, false for a sliver with no area
	private static bool Plane(float[] p, int[] triangles, int t, double[] normal)
	{
		int i0 = triangles[t * 3] * 3;
		int i1 = triangles[t * 3 + 1] * 3;
		int i2 = triangles[t * 3 + 2] * 3;
		double ux = p[i1] - p[i0], uy = p[i1 + 1] - p[i0 + 1], uz = p[i1 + 2] - p[i0 + 2];
		double vx = p[i2] - p[i0], vy = p[i2 + 1] - p[i0 + 1], vz = p[i2 + 2] - p[i0 + 2];
		double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
		double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
		if (length < 1e-12)
		{
			return false;
		}
		normal[t * 4] = nx / length;
		normal[t * 4 + 1] = ny / length;
		normal[t * 4 + 2] = nz / length;
		normal[t * 4 + 3] = (normal[t * 4] * p[i0] + normal[t * 4 + 1] * p[i0 + 1] + normal[t * 4 + 2] * p[i0 + 2]);
		return true;
	}

	private static Key PlaneKey(double[] normal, int t, double sign)
	{
		// coarse enough that one plane's triangles share a key, its own twins the flipped key
		return new Key
		{
			X = (long)Math.Round(sign * normal[t * 4] * 200.0),
			Y = (long)Math.Round(sign * normal[t * 4 + 1] * 200.0),
			Z = (long)Math.Round(sign * normal[t * 4 + 2] * 200.0),
			W = (long)Math.Round(sign * normal[t * 4 + 3] * 200.0)
		};
	}

	private static bool Twinned(float[] p, int[] triangles, double[] normal, Dictionary<Key, List<int>> planes, int t)
	{
		int i0 = triangles[t * 3] * 3;
		int i1 = triangles[t * 3 + 1] * 3;
		int i2 = triangles[t * 3 + 2] * 3;
		double mx = (p[i0] + p[i1] + p[i2]) / 3.0;
		double my = (p[i0 + 1] + p[i1 + 1] + p[i2 + 1]) / 3.0;
		double mz = (p[i0 + 2] + p[i1 + 2] + p[i2 + 2]) / 3.0;
		Key key = PlaneKey(normal, t, -1.0);
		for (int level = 0; level < levels; level++)
		{
			double cell = Size(level);
			key.V = Cell(level, (long)Math.Floor(mx / cell), (long)Math.Floor(my / cell), (long)Math.Floor(mz / cell));
			if (TwinIn(p, triangles, normal, planes, key, t, mx, my, mz))
			{
				return true;
			}
		}
		return false;
	}

	private static bool TwinIn(float[] p, int[] triangles, double[] normal, Dictionary<Key, List<int>> planes, Key key, int t, double mx, double my, double mz)
	{
		if (!planes.TryGetValue(key, out List<int> others))
		{
			return false;
		}
		foreach (int s in others)
		{
			double facing = normal[t * 4] * normal[s * 4] + normal[t * 4 + 1] * normal[s * 4 + 1] + normal[t * 4 + 2] * normal[s * 4 + 2];
			if (facing < -0.999 && Inside(p, triangles, normal, s, mx, my, mz))
			{
				return true;
			}
		}
		return false;
	}

	// a point on the triangle's plane inside it, by which side of each edge it lies
	private static bool Inside(float[] p, int[] triangles, double[] normal, int s, double mx, double my, double mz)
	{
		double nx = normal[s * 4], ny = normal[s * 4 + 1], nz = normal[s * 4 + 2];
		if (Math.Abs(nx * mx + ny * my + nz * mz - normal[s * 4 + 3]) > 1e-3)
		{
			return false;
		}
		for (int e = 0; e < 3; e++)
		{
			int a = triangles[s * 3 + e] * 3;
			int b = triangles[s * 3 + (e + 1) % 3] * 3;
			double ex = p[b] - p[a], ey = p[b + 1] - p[a + 1], ez = p[b + 2] - p[a + 2];
			double qx = mx - p[a], qy = my - p[a + 1], qz = mz - p[a + 2];
			double cx = ey * qz - ez * qy, cy = ez * qx - ex * qz, cz = ex * qy - ey * qx;
			if (cx * nx + cy * ny + cz * nz < -1e-9)
			{
				return false;
			}
		}
		return true;
	}
}
