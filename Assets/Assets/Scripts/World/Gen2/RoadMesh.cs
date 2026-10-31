using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// placeholder road geometry for one tile from the road plan: one mesh, three submeshes (asphalt, buried road, concrete).
// tile-local: x/z from the tile corner, y from the deck surface
public class RoadMesh
{
	public const int SubmeshCount = 3;

	private const int asphalt = 0;

	private const int buried = 1;

	private const int concrete = 2;

	// the low slope at a road's edges, so the ket and the girls drive on and off it
	private const float shoulder = 0.6f;

	private const float deckThickness = 1.2f;

	private const float parapetHeight = 0.9f;

	private const float parapetWidth = 0.35f;

	// decks this high get parapets
	private const float parapetFrom = 0.5f;

	// decks this high get a column under every other piece
	private const float supportFrom = 3f;

	private const float supportSize = 2.4f;

	private const int roundSegments = 24;

	private readonly List<Vector3> vertices = new List<Vector3>();

	private readonly List<Vector2> uvs = new List<Vector2>();

	private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };

	public int Triangles { get; private set; }

	// how far the surface sits above the deck: highways over streets over buried roads, so crossings don't flicker
	private static float Lift(RoadKind kind)
	{
		switch (kind)
		{
			case RoadKind.Highway:
				return 0.1f;
			case RoadKind.Street:
				return 0.06f;
			default:
				return 0.025f;
		}
	}

	private static int Surface(RoadKind kind)
	{
		return (kind == RoadKind.Track) ? buried : asphalt;
	}

	// false when there's nothing to draw
	// how deep a road's slab is where it crosses a void
	private const float spanDepth = 2.2f;

	// voids (ChasmPlan): roads over them are drawn as bridges. null for none
	public ChasmPlan Voids;

	private bool OverVoid(XZ p)
	{
		return Voids != null && Voids.Inside(p.X, p.Z) > -3.0;
	}

	public bool Build(Mesh mesh, List<RoadPiece> pieces, List<RoadPatch> patches, double originX, double originZ)
	{
		vertices.Clear();
		uvs.Clear();
		foreach (List<int> list in triangles)
		{
			list.Clear();
		}
		foreach (RoadPiece piece in pieces)
		{
			Vector3 a = Local(piece.A, originX, originZ);
			Vector3 b = Local(piece.B, originX, originZ);
			Vector3 ta = Flat(piece.TangentA);
			Vector3 tb = Flat(piece.TangentB);
			float half = (float)piece.Width * 0.5f;
			bool span = OverVoid(piece.A) || OverVoid(piece.B) || OverVoid(new XZ((piece.A.X + piece.B.X) * 0.5, (piece.A.Z + piece.B.Z) * 0.5));
			// the foot of a ramp keeps the ground cross-section, so the ket meets no seam where the climb starts
			if (!span && piece.HeightA <= parapetFrom && piece.HeightB <= parapetFrom)
			{
				Ground(a, b, ta, tb, half, Lift(piece.Kind) + (float)piece.HeightA, Lift(piece.Kind) + (float)piece.HeightB, Surface(piece.Kind));
			}
			else
			{
				Deck(piece, a, b, ta, tb, half, span);
			}
		}
		foreach (RoadPatch patch in patches)
		{
			Vector3 centre = Local(patch.Center, originX, originZ);
			float lift = Lift(patch.Kind);
			if (patch.Round)
			{
				Disc(centre, (float)patch.Size, lift, Surface(patch.Kind));
			}
			else
			{
				Square(centre, Flat(patch.Axis), (float)patch.Size, lift, Surface(patch.Kind));
			}
			if (OverVoid(patch.Center))
			{
				// a crossing over the void sits on a slab
				float size = (float)patch.Size;
				Box(centre + Vector3.up * (lift - spanDepth * 0.5f), Flat(patch.Axis), new Vector3(size, spanDepth * 0.5f - 0.02f, size));
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

	private static Vector3 Local(XZ p, double originX, double originZ)
	{
		return new Vector3((float)(p.X - originX), 0f, (float)(p.Z - originZ));
	}

	private static Vector3 Flat(XZ d)
	{
		return new Vector3((float)d.X, 0f, (float)d.Z);
	}

	private static Vector3 Left(Vector3 tangent)
	{
		return new Vector3(-tangent.z, 0f, tangent.x);
	}

	// a road on the deck: flat top at liftA/liftB, sloped shoulders down to the deck
	private void Ground(Vector3 a, Vector3 b, Vector3 ta, Vector3 tb, float half, float liftA, float liftB, int submesh)
	{
		Vector3 la = Left(ta);
		Vector3 lb = Left(tb);
		Vector3 upA = Vector3.up * liftA;
		Vector3 upB = Vector3.up * liftB;
		float inner = half - shoulder;
		Quad(submesh, a + la * half, a + la * inner + upA, b + lb * inner + upB, b + lb * half);
		Quad(submesh, a + la * inner + upA, a - la * inner + upA, b - lb * inner + upB, b + lb * inner + upB);
		Quad(submesh, a - la * inner + upA, a - la * half, b - lb * half, b - lb * inner + upB);
	}

	// one side of a deck from a to b (side +1 left, -1 right), underside ba..bb up to the top ha..hb; where a cross
	// street joins (from..to) it stops a little under the top, or the ket's wheels catch on its edge crossing over
	private void Side(Vector3 a, Vector3 b, Vector3 la, Vector3 lb, float half, float side, float ba, float bb, float ha, float hb, float from, float to)
	{
		if (to > from)
		{
			Side(a, b, la, lb, half, side, ba, bb, ha, hb, 0f, from, 0f);
			Side(a, b, la, lb, half, side, ba, bb, ha, hb, from, to, 0.3f);
			Side(a, b, la, lb, half, side, ba, bb, ha, hb, to, 1f, 0f);
			return;
		}
		Side(a, b, la, lb, half, side, ba, bb, ha, hb, 0f, 1f, 0f);
	}

	private void Side(Vector3 a, Vector3 b, Vector3 la, Vector3 lb, float half, float side, float ba, float bb, float ha, float hb, float t0, float t1, float drop)
	{
		if (t1 - t0 < 0.0005f)
		{
			return;
		}
		Vector3 p = Vector3.Lerp(a, b, t0);
		Vector3 q = Vector3.Lerp(a, b, t1);
		Vector3 lp = Vector3.Lerp(la, lb, t0).normalized * (side * half);
		Vector3 lq = Vector3.Lerp(la, lb, t1).normalized * (side * half);
		float bp = Mathf.Lerp(ba, bb, t0);
		float bq = Mathf.Lerp(ba, bb, t1);
		float tp = Mathf.Lerp(ha, hb, t0) - drop;
		float tq = Mathf.Lerp(ha, hb, t1) - drop;
		Sided(side < 0f, p + lp + Vector3.up * bp, p + lp + Vector3.up * tp, q + lq + Vector3.up * tq, q + lq + Vector3.up * bq);
	}

	// one side's parapet along a deck from topA to topB (side +1 left, -1 right), left out between the shares from
	// and to and between from2 and to2 (each none where to <= from)
	private void Parapet(Vector3 topA, Vector3 topB, Vector3 la, Vector3 lb, float half, float rise, float side, float from, float to, float from2, float to2)
	{
		bool one = to > from;
		bool two = to2 > from2;
		if (!one && !two)
		{
			Parapet(topA, topB, la, lb, half, rise, side, 0f, 1f, false);
			return;
		}
		if (!one || (two && from2 < from))
		{
			float f = from;
			float t = to;
			from = from2;
			to = to2;
			from2 = f;
			to2 = t;
			one = true;
			two = to2 > from2;
		}
		float at = 0f;
		if (from > at + 0.001f)
		{
			Parapet(topA, topB, la, lb, half, rise, side, at, from, true);
		}
		at = Mathf.Max(at, to);
		if (two)
		{
			if (from2 > at + 0.001f)
			{
				Parapet(topA, topB, la, lb, half, rise, side, at, from2, true);
			}
			at = Mathf.Max(at, to2);
		}
		if (at < 0.999f)
		{
			Parapet(topA, topB, la, lb, half, rise, side, at, 1f, true);
		}
	}

	// the stretch of it from share t0 to t1: its outer face, top, inner face, and ends where it's cut for an opening
	private void Parapet(Vector3 topA, Vector3 topB, Vector3 la, Vector3 lb, float half, float rise, float side, float t0, float t1, bool ends)
	{
		Vector3 p = Vector3.Lerp(topA, topB, t0);
		Vector3 q = Vector3.Lerp(topB, topA, 1f - t1);
		Vector3 lp = Vector3.Lerp(la, lb, t0).normalized * side;
		Vector3 lq = Vector3.Lerp(la, lb, t1).normalized * side;
		float inner = half - parapetWidth;
		Vector3 up = Vector3.up * rise;
		bool flip = side < 0f;
		Sided(flip, p + lp * half, p + lp * half + up, q + lq * half + up, q + lq * half);
		Sided(flip, p + lp * half + up, p + lp * inner + up, q + lq * inner + up, q + lq * half + up);
		Sided(flip, p + lp * inner + up, p + lp * inner, q + lq * inner, q + lq * inner + up);
		if (ends)
		{
			// seen from either way
			Sided(false, p + lp * inner, p + lp * inner + up, p + lp * half + up, p + lp * half);
			Sided(true, p + lp * inner, p + lp * inner + up, p + lp * half + up, p + lp * half);
			Sided(false, q + lq * half, q + lq * half + up, q + lq * inner + up, q + lq * inner);
			Sided(true, q + lq * half, q + lq * half + up, q + lq * inner + up, q + lq * inner);
		}
	}

	// a concrete quad, wound the other way round if flip (a right-hand face mirrored from the left)
	private void Sided(bool flip, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
	{
		if (flip)
		{
			Quad(concrete, p0, p3, p2, p1);
		}
		else
		{
			Quad(concrete, p0, p1, p2, p3);
		}
	}

	// an elevated piece or a ramp: top, sides down to the underside (or the ground on a low ramp), parapets,
	// a column under every other piece, a wall across a broken end
	private void Deck(RoadPiece piece, Vector3 a, Vector3 b, Vector3 ta, Vector3 tb, float half, bool span)
	{
		float ha = (float)piece.HeightA + Lift(piece.Kind);
		float hb = (float)piece.HeightB + Lift(piece.Kind);
		// over a void the slab hangs below the deck's level
		float ba = span ? (float)piece.HeightA - spanDepth : Mathf.Max(0f, (float)piece.HeightA - deckThickness);
		float bb = span ? (float)piece.HeightB - spanDepth : Mathf.Max(0f, (float)piece.HeightB - deckThickness);
		bool parapets = span || piece.HeightA > parapetFrom || piece.HeightB > parapetFrom;
		float rise = parapets ? parapetHeight : 0f;
		Vector3 la = Left(ta);
		Vector3 lb = Left(tb);
		Vector3 topA = a + Vector3.up * ha;
		Vector3 topB = b + Vector3.up * hb;
		Quad(Surface(piece.Kind), topA + la * half, topA - la * half, topB - lb * half, topB + lb * half);
		// outer sides up to the deck, the parapets on them (open where a door's walkway comes in)
		Side(a, b, la, lb, half, 1f, ba, bb, ha, hb, (float)piece.LeftFrom2, (float)piece.LeftTo2);
		Side(a, b, la, lb, half, -1f, ba, bb, ha, hb, (float)piece.RightFrom2, (float)piece.RightTo2);
		if (ba != 0f || bb != 0f)
		{
			Quad(concrete, a + la * half + Vector3.up * ba, b + lb * half + Vector3.up * bb, b - lb * half + Vector3.up * bb, a - la * half + Vector3.up * ba);
		}
		if (parapets)
		{
			Parapet(topA, topB, la, lb, half, rise, 1f, (float)piece.LeftFrom, (float)piece.LeftTo, (float)piece.LeftFrom2, (float)piece.LeftTo2);
			Parapet(topA, topB, la, lb, half, rise, -1f, (float)piece.RightFrom, (float)piece.RightTo, (float)piece.RightFrom2, (float)piece.RightTo2);
		}
		// raised streets stand on arches (TileStreamer), not columns
		if (!span && piece.Kind != RoadKind.Street && piece.Index % 2 == 0 && piece.HeightA > supportFrom)
		{
			Box(a + Vector3.up * (ba * 0.5f), ta, new Vector3(supportSize, ba, supportSize) * 0.5f);
		}
		if (piece.BrokenA)
		{
			// the torn end facing back along the road, and a wall a step in from it
			Quad(concrete, a - la * half + Vector3.up * ba, a - la * half + Vector3.up * (ha + rise), a + la * half + Vector3.up * (ha + rise), a + la * half + Vector3.up * ba);
			Box(topA + ta * 1.5f + Vector3.up * 0.5f, ta, new Vector3(half, 0.5f, 0.4f));
		}
		if (piece.BrokenB)
		{
			Quad(concrete, b + lb * half + Vector3.up * bb, b + lb * half + Vector3.up * (hb + rise), b - lb * half + Vector3.up * (hb + rise), b - lb * half + Vector3.up * bb);
			Box(topB - tb * 1.5f + Vector3.up * 0.5f, tb, new Vector3(half, 0.5f, 0.4f));
		}
	}

	// a square crossing, its sides sloping down like a road's shoulders
	private void Square(Vector3 centre, Vector3 axis, float half, float lift, int submesh)
	{
		Vector3 u = axis;
		Vector3 v = Left(axis);
		Vector3 up = Vector3.up * lift;
		Vector3[] corners = { centre + (u + v) * half, centre + (-u + v) * half, centre + (-u - v) * half, centre + (u - v) * half };
		Quad(submesh, corners[0] + up, corners[1] + up, corners[2] + up, corners[3] + up);
		for (int i = 0; i < 4; i++)
		{
			Vector3 c0 = corners[i];
			Vector3 c1 = corners[(i + 1) % 4];
			Vector3 out0 = c0 + (c0 - centre).normalized * shoulder;
			Vector3 out1 = c1 + (c1 - centre).normalized * shoulder;
			Quad(submesh, c1 + up, c0 + up, out0, out1);
		}
	}

	// a round junction with a sloped rim
	private void Disc(Vector3 centre, float radius, float lift, int submesh)
	{
		Vector3 up = Vector3.up * lift;
		int first = vertices.Count;
		Add(centre + up);
		for (int i = 0; i < roundSegments; i++)
		{
			float angle = i * Mathf.PI * 2f / roundSegments;
			Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
			Add(centre + dir * radius + up);
		}
		for (int i = 0; i < roundSegments; i++)
		{
			int next = (i + 1) % roundSegments;
			triangles[submesh].Add(first);
			triangles[submesh].Add(first + 1 + next);
			triangles[submesh].Add(first + 1 + i);
		}
		for (int i = 0; i < roundSegments; i++)
		{
			float a0 = i * Mathf.PI * 2f / roundSegments;
			float a1 = (i + 1) * Mathf.PI * 2f / roundSegments;
			Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
			Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
			Quad(submesh, centre + d1 * radius + up, centre + d0 * radius + up, centre + d0 * (radius + shoulder), centre + d1 * (radius + shoulder));
		}
	}

	// a concrete box around centre, turned to face along forward. size is half extents (across, up, along)
	private void Box(Vector3 centre, Vector3 forward, Vector3 size)
	{
		Vector3 f = forward * size.z;
		Vector3 r = -Left(forward) * size.x;
		Vector3 u = Vector3.up * size.y;
		Vector3 p000 = centre - r - u - f;
		Vector3 p100 = centre + r - u - f;
		Vector3 p010 = centre - r + u - f;
		Vector3 p110 = centre + r + u - f;
		Vector3 p001 = centre - r - u + f;
		Vector3 p101 = centre + r - u + f;
		Vector3 p011 = centre - r + u + f;
		Vector3 p111 = centre + r + u + f;
		Quad(concrete, p100, p110, p010, p000);
		Quad(concrete, p001, p011, p111, p101);
		Quad(concrete, p000, p010, p011, p001);
		Quad(concrete, p101, p111, p110, p100);
		Quad(concrete, p110, p111, p011, p010);
	}

	// corners counter-clockwise seen from the side that shows, from above for a top
	private void Quad(int submesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
	{
		int first = vertices.Count;
		Add(p0);
		Add(p1);
		Add(p2);
		Add(p3);
		List<int> list = triangles[submesh];
		list.Add(first);
		list.Add(first + 2);
		list.Add(first + 1);
		list.Add(first);
		list.Add(first + 3);
		list.Add(first + 2);
	}

	private void Add(Vector3 p)
	{
		vertices.Add(p);
		// planar, 4 m a repeat, for when the kit gets textures
		uvs.Add(new Vector2(p.x + p.y, p.z + p.y) * 0.25f);
	}
}
