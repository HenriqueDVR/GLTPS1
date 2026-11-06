using System.Collections.Generic;
using NUnit.Framework;

// which faces get a back (Core/MeshSides): only what could be seen from behind
public class MeshSidesTests
{
	private class Shape
	{
		public readonly List<float> Positions = new List<float>();

		public readonly List<int> Triangles = new List<int>();

		private int Corner(float x, float y, float z)
		{
			Positions.Add(x);
			Positions.Add(y);
			Positions.Add(z);
			return Positions.Count / 3 - 1;
		}

		// a quad a b c d, its own corners (a mesh splits them by normal), split on a-c or b-d
		public void Quad(float[] a, float[] b, float[] c, float[] d, bool otherDiagonal = false)
		{
			int i = Corner(a[0], a[1], a[2]);
			int j = Corner(b[0], b[1], b[2]);
			int k = Corner(c[0], c[1], c[2]);
			int l = Corner(d[0], d[1], d[2]);
			if (otherDiagonal)
			{
				Triangles.AddRange(new[] { i, j, l, j, k, l });
			}
			else
			{
				Triangles.AddRange(new[] { i, j, k, i, k, l });
			}
		}

		// a box, faces out (or in), any of its six sides left off
		public void Box(float x, float y, float z, float s, bool inward = false, int without = -1)
		{
			float[][] p =
			{
				new[] { x, y, z }, new[] { x + s, y, z }, new[] { x + s, y + s, z }, new[] { x, y + s, z },
				new[] { x, y, z + s }, new[] { x + s, y, z + s }, new[] { x + s, y + s, z + s }, new[] { x, y + s, z + s }
			};
			int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
			for (int f = 0; f < faces.Length; f++)
			{
				if (f == without)
				{
					continue;
				}
				int[] q = faces[f];
				if (inward)
				{
					Quad(p[q[3]], p[q[2]], p[q[1]], p[q[0]]);
				}
				else
				{
					Quad(p[q[0]], p[q[1]], p[q[2]], p[q[3]]);
				}
			}
		}

		public int Backs()
		{
			int n = 0;
			foreach (bool b in MeshSides.Backs(Positions.ToArray(), Triangles.ToArray()))
			{
				n += b ? 1 : 0;
			}
			return n;
		}
	}

	[Test]
	public void ClosedBoxFacingOutNeedsNone()
	{
		Shape shape = new Shape();
		shape.Box(0f, 0f, 0f, 1f);
		Assert.AreEqual(0, shape.Backs());
	}

	[Test]
	public void FarAwayBoxNeedsNone()
	{
		Shape shape = new Shape();
		shape.Box(5000f, 20f, -7000f, 0.3f);
		Assert.AreEqual(0, shape.Backs());
	}

	[Test]
	public void BoxTurnedInsideOutNeedsAll()
	{
		Shape shape = new Shape();
		shape.Box(0f, 0f, 0f, 1f, true);
		Assert.AreEqual(12, shape.Backs());
	}

	[Test]
	public void BoxWithNoBottomNeedsAll()
	{
		Shape shape = new Shape();
		shape.Box(0f, 0f, 0f, 2f, false, 2);
		Assert.AreEqual(10, shape.Backs());
	}

	[Test]
	public void SheetNeedsItsBack()
	{
		Shape shape = new Shape();
		shape.Quad(new[] { 0f, 0f, 0f }, new[] { 1f, 0f, 0f }, new[] { 1f, 1f, 0f }, new[] { 0f, 1f, 0f });
		Assert.AreEqual(2, shape.Backs());
	}

	[Test]
	public void SheetWithItsTwinNeedsNone()
	{
		Shape shape = new Shape();
		shape.Quad(new[] { 0f, 0f, 0f }, new[] { 1f, 0f, 0f }, new[] { 1f, 1f, 0f }, new[] { 0f, 1f, 0f });
		// the twin split along the other diagonal
		shape.Quad(new[] { 0f, 1f, 0f }, new[] { 1f, 1f, 0f }, new[] { 1f, 0f, 0f }, new[] { 0f, 0f, 0f }, true);
		Assert.AreEqual(0, shape.Backs());
	}

	[Test]
	public void OnlyTheOpenPieceGetsBacks()
	{
		Shape shape = new Shape();
		shape.Box(0f, 0f, 0f, 1f);
		shape.Box(3f, 0f, 0f, 1f, false, 3);
		Assert.AreEqual(10, shape.Backs());
	}

	[Test]
	public void BoxesTouchingAtACornerAreTheirOwnPieces()
	{
		Shape shape = new Shape();
		shape.Box(0f, 0f, 0f, 1f);
		shape.Box(1f, 1f, 1f, 1f, true);
		Assert.AreEqual(12, shape.Backs());
	}
}
