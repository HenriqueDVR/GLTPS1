using System;
using System.Collections.Generic;

public enum RoadKind
{
	Highway,
	Street,
	// an old road buried under the snow, in open districts
	Track
}

// one straight stretch of road, at most about PieceLength long.
// the tangents at its ends are shared with the next piece, so pieces join without gaps
public struct RoadPiece
{
	public RoadKind Kind;

	public XZ A;

	public XZ B;

	// unit direction of the road at A and B
	public XZ TangentA;

	public XZ TangentB;

	// deck height above the ground at A and B (elevated highways and their ramps)
	public double HeightA;

	public double HeightB;

	public double Width;

	// a collapsed span starts right before A / right after B
	public bool BrokenA;

	public bool BrokenB;

	// sample index along its road, supports go under the even ones
	public int Index;
}

// a flat junction: a square street crossing, or the round apron around a pillar
public struct RoadPatch
{
	public RoadKind Kind;

	public XZ Center;

	// half the side of a square, the radius of a round one
	public double Size;

	// unit, the square's first axis
	public XZ Axis;

	public bool Round;
}

// a highway link from a junction to its east or north neighbour junction, as a cubic bezier
public struct HighwayLink
{
	public int CellX;

	public int CellZ;

	public bool North;

	public XZ P0;

	public XZ C1;

	public XZ C2;

	public XZ P1;

	public bool Elevated;

	public bool Broken;

	// the elevated stretch as shares of the link's length, ramps included
	public double ElevatedFrom;

	public double ElevatedTo;
}

// the roads of one layer (01-world-layers, roads): an expressway network of its own on a coarse grid, a rotated street
// grid per street district, buried roads between sites in open districts. the pillars are the city's columns, not
// destinations: roads bend around them and keep their footprint clear. pure like LayerPlan, so a tile asks for its own
// pieces in any order. not thread safe (shared sample buffers).
// everything here is part of the genVersion 2 output once a v2 release ships
public sealed class RoadPlan
{
	public const double PieceLength = 16.0;

	public const double HighwayWidth = 14.0;

	// a district's own is DistrictDef.StreetWidth, this is the widest
	public const double StreetWidth = 11.0;

	public const double TrackWidth = 5.0;

	// one junction per cell, jittered, linked to the east and north neighbours
	public const double HighwayCell = 1400.0;

	public const double HighwayMargin = 250.0;

	public const double HighwayChance = 0.75;

	// bezier control points sideways, share of the link length
	public const double HighwayBend = 0.12;

	public const double DeckHeight = 9.0;

	public const double RampLength = 128.0;

	// share of the ramp at each end that eases in and out, so the slope doesn't start in one step.
	// the steepest part is DeckHeight / (RampLength * (1 - RampEase)), 9.4%
	public const double RampEase = 0.25;

	public const double GapLength = 24.0;

	// kept clear around each pillar: the tower is 112 m across (nodeCOLUMN's Cylinder), plus room to pass
	public const double PillarClearance = 72.0;

	// a road passing a pillar shifts sideways over this distance either side
	public const double Detour = 200.0;

	// junctions and sites stay this far from pillars, so a detour never moves a road's ends
	public const double PillarGap = 300.0;

	// nothing around the layer origin, the start tunnel's embankment stands there (OriginTunnel, about 120 by 160 m)
	public const double StartClearance = 110.0;

	// streets: blocks of 64-128 m, the grid tilted up to about 20 degrees per district
	public const double StreetSpacing = 96.0;

	public const double StreetJitter = 16.0;

	public const double StreetTilt = 0.36;

	// one site per cell, landmarks will stand on them (GEN2-4)
	public const double SiteCell = 512.0;

	public const double SiteMargin = 64.0;

	public const double TrackChance = 0.55;

	public const double TrackBend = 0.15;

	private struct Rect
	{
		public double X0;

		public double Z0;

		public double X1;

		public double Z1;

		public bool Contains(XZ p)
		{
			return p.X >= X0 && p.X < X1 && p.Z >= Z0 && p.Z < Z1;
		}

		public bool Overlaps(double x0, double z0, double x1, double z1)
		{
			return x0 < X1 && x1 >= X0 && z0 < Z1 && z1 >= Z0;
		}
	}

	private struct Grid
	{
		public ulong Seed;

		public XZ Origin;

		public XZ U;

		public XZ V;
	}

	private readonly LayerPlan plan;

	private readonly ulong seed;

	private readonly Grid[] grids;

	private readonly List<XZ> points = new List<XZ>();

	private readonly List<double> along = new List<double>();

	private readonly List<XZ> shifts = new List<XZ>();

	private sealed class Curve
	{
		public XZ[] Points;

		public double[] Along;
	}

	// sampled roads by (kind: 0 expressway 1 buried road, cell x, cell z, direction)
	private readonly Dictionary<(int, int, int, int), Curve> curves = new Dictionary<(int, int, int, int), Curve>();

	private readonly HashSet<int> districts = new HashSet<int>();

	public RoadPlan(LayerPlan plan)
	{
		this.plan = plan;
		seed = plan.LayerSeed;
		grids = new Grid[plan.Def.Districts.Length];
		for (int i = 0; i < grids.Length; i++)
		{
			grids[i] = MakeGrid(i);
		}
	}

	// everything owned by tile (x, z): pieces whose midpoint and patches whose centre lie in it
	public void Collect(int tileX, int tileZ, List<RoadPiece> pieces, List<RoadPatch> patches)
	{
		pieces.Clear();
		patches.Clear();
		Rect tile = new Rect
		{
			X0 = tileX * Gen2.TileSize,
			Z0 = tileZ * Gen2.TileSize,
			X1 = (tileX + 1) * Gen2.TileSize,
			Z1 = (tileZ + 1) * Gen2.TileSize
		};
		CollectHighways(tile, pieces, patches);
		CollectStreets(tile, pieces, patches);
		CollectTracks(tile, pieces);
	}

	// ---- highways

	public bool Highway(int cellX, int cellZ, bool north, out HighwayLink link)
	{
		link = default;
		ulong key = Gen2.Pack(cellX, cellZ);
		ulong dir = north ? 1uL : 0uL;
		if (Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamHighway + dir, key)) >= HighwayChance)
		{
			return false;
		}
		link.CellX = cellX;
		link.CellZ = cellZ;
		link.North = north;
		link.P0 = Junction(cellX, cellZ);
		link.P1 = north ? Junction(cellX, cellZ + 1) : Junction(cellX + 1, cellZ);
		double b1 = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamHighway + 2 + dir * 2, key)) * 2.0 - 1.0;
		double b2 = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamHighway + 3 + dir * 2, key)) * 2.0 - 1.0;
		Bend(link.P0, link.P1, b1 * HighwayBend, b2 * HighwayBend, out link.C1, out link.C2);
		ulong elevated = WorldRandom.Derive(seed, Gen2.StreamElevated + dir, key);
		if (Gen2.Unit(elevated) < plan.Def.ElevatedChance)
		{
			double length = link.P0.DistanceTo(link.P1);
			double from = 0.2 + 0.2 * Gen2.Unit(WorldRandom.Derive(elevated, Gen2.StreamElevated, 0uL));
			double to = 0.6 + 0.2 * Gen2.Unit(WorldRandom.Derive(elevated, Gen2.StreamElevated, 1uL));
			// room for both ramps and a flat top with a gap and a couple of pieces either side of it
			if ((to - from) * length >= 2.0 * RampLength + GapLength + 4.0 * PieceLength)
			{
				link.Elevated = true;
				link.ElevatedFrom = from;
				link.ElevatedTo = to;
				link.Broken = Gen2.Unit(WorldRandom.Derive(elevated, Gen2.StreamElevated, 2uL)) < plan.Def.BrokenChance;
			}
		}
		return true;
	}

	// a junction's position, pushed out to PillarGap if a pillar is too close
	public XZ Junction(int cellX, int cellZ)
	{
		ulong key = Gen2.Pack(cellX, cellZ);
		double span = HighwayCell - 2.0 * HighwayMargin;
		XZ p = new XZ(cellX * HighwayCell + HighwayMargin + Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamHighway + 6, key)) * span, cellZ * HighwayCell + HighwayMargin + Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamHighway + 7, key)) * span);
		XZ pillar = PillarOf(p);
		double d = pillar.DistanceTo(p);
		if (d >= PillarGap)
		{
			return p;
		}
		if (d <= 0.0)
		{
			return new XZ(pillar.X + PillarGap, pillar.Z);
		}
		return new XZ(pillar.X + (p.X - pillar.X) / d * PillarGap, pillar.Z + (p.Z - pillar.Z) / d * PillarGap);
	}

	public bool HasAnyHighway(int cellX, int cellZ)
	{
		return Highway(cellX, cellZ, false, out _) || Highway(cellX, cellZ, true, out _) || Highway(cellX - 1, cellZ, false, out _) || Highway(cellX, cellZ - 1, true, out _);
	}

	private void CollectHighways(Rect tile, List<RoadPiece> pieces, List<RoadPatch> patches)
	{
		int cx0 = Gen2.Cell(tile.X0, HighwayCell);
		int cx1 = Gen2.Cell(tile.X1, HighwayCell);
		int cz0 = Gen2.Cell(tile.Z0, HighwayCell);
		int cz1 = Gen2.Cell(tile.Z1, HighwayCell);
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				XZ junction = Junction(cx, cz);
				if (tile.Contains(junction) && !InClearance(junction) && HasAnyHighway(cx, cz))
				{
					patches.Add(new RoadPatch { Kind = RoadKind.Highway, Center = junction, Size = HighwayWidth, Axis = new XZ(1.0, 0.0), Round = true });
				}
			}
		}
		// links start in this cell or the ones west and south of it
		for (int cx = cx0 - 1; cx <= cx1; cx++)
		{
			for (int cz = cz0 - 1; cz <= cz1; cz++)
			{
				for (int d = 0; d < 2; d++)
				{
					if (!Highway(cx, cz, d == 1, out HighwayLink link) || !CurveTouches(tile, link.P0, link.C1, link.C2, link.P1, HighwayWidth + PillarClearance))
					{
						continue;
					}
					Sample(0, link.CellX, link.CellZ, link.North ? 1 : 0, link.P0, link.C1, link.C2, link.P1, PillarClearance + HighwayWidth * 0.5);
					double length = along[along.Count - 1];
					double from = link.ElevatedFrom * length;
					double to = link.ElevatedTo * length;
					double gapFrom = double.MaxValue;
					double gapTo = double.MaxValue;
					if (link.Broken)
					{
						gapFrom = (from + to - GapLength) * 0.5;
						gapTo = gapFrom + GapLength;
					}
					for (int i = 0; i + 1 < points.Count; i++)
					{
						XZ a = points[i];
						XZ b = points[i + 1];
						if (!tile.Contains(Mid(a, b)) || (along[i + 1] > gapFrom && along[i] < gapTo))
						{
							continue;
						}
						RoadPiece piece = Piece(RoadKind.Highway, HighwayWidth, i);
						if (link.Elevated)
						{
							piece.HeightA = Deck(along[i], from, to);
							piece.HeightB = Deck(along[i + 1], from, to);
							piece.BrokenB = i + 2 < points.Count && along[i + 1] <= gapFrom && along[i + 2] > gapFrom;
							piece.BrokenA = i > 0 && along[i] >= gapTo && along[i - 1] < gapTo;
						}
						// stop at the junctions' round patches
						if (Clip(ref piece, link.P0, HighwayWidth) && Clip(ref piece, link.P1, HighwayWidth) && !InClearance(Mid(piece.A, piece.B)))
						{
							pieces.Add(piece);
						}
					}
				}
			}
		}
	}

	private static double Deck(double s, double from, double to)
	{
		if (s <= from || s >= to)
		{
			return 0.0;
		}
		double u = Math.Min((s - from) / RampLength, (to - s) / RampLength);
		if (u >= 1.0)
		{
			return DeckHeight;
		}
		// slope rises evenly over the ease, steady in the middle, eases off at the top
		double m = 1.0 / (1.0 - RampEase);
		if (u < RampEase)
		{
			return DeckHeight * m * u * u / (2.0 * RampEase);
		}
		if (u <= 1.0 - RampEase)
		{
			return DeckHeight * m * (u - RampEase * 0.5);
		}
		double rest = 1.0 - u;
		return DeckHeight * (1.0 - m * rest * rest / (2.0 * RampEase));
	}

	// false when the piece lies inside the circle, else moves the end that does onto its edge
	private static bool Clip(ref RoadPiece piece, XZ centre, double radius)
	{
		bool insideA = piece.A.DistanceTo(centre) < radius;
		bool insideB = piece.B.DistanceTo(centre) < radius;
		if (insideA && insideB)
		{
			return false;
		}
		if (insideA)
		{
			piece.A = Exit(centre, radius, piece.A, piece.B);
			piece.TangentA = Direction(piece.A, piece.B);
		}
		else if (insideB)
		{
			piece.B = Exit(centre, radius, piece.B, piece.A);
			piece.TangentB = Direction(piece.A, piece.B);
		}
		return true;
	}

	// the point between inside and outside at radius from the centre
	private static XZ Exit(XZ centre, double radius, XZ inside, XZ outside)
	{
		double dx = outside.X - inside.X;
		double dz = outside.Z - inside.Z;
		double fx = inside.X - centre.X;
		double fz = inside.Z - centre.Z;
		double a = dx * dx + dz * dz;
		double b = 2.0 * (fx * dx + fz * dz);
		double c = fx * fx + fz * fz - radius * radius;
		double t = (-b + Math.Sqrt(Math.Max(0.0, b * b - 4.0 * a * c))) / (2.0 * a);
		return new XZ(inside.X + dx * t, inside.Z + dz * t);
	}

	// ---- streets

	private Grid MakeGrid(int district)
	{
		ulong gridSeed = WorldRandom.Derive(seed, Gen2.StreamStreets, (ulong)district);
		double tilt = (Gen2.Unit(WorldRandom.Derive(gridSeed, Gen2.StreamStreets, 0uL)) * 2.0 - 1.0) * StreetTilt;
		double norm = Math.Sqrt(1.0 + tilt * tilt);
		return new Grid
		{
			Seed = gridSeed,
			Origin = new XZ(Gen2.Unit(WorldRandom.Derive(gridSeed, Gen2.StreamStreets, 1uL)) * StreetSpacing, Gen2.Unit(WorldRandom.Derive(gridSeed, Gen2.StreamStreets, 2uL)) * StreetSpacing),
			U = new XZ(1.0 / norm, tilt / norm),
			V = new XZ(-tilt / norm, 1.0 / norm)
		};
	}

	// position of grid line k, axis 0 = lines across U, 1 = across V
	private static double Line(Grid grid, int axis, int k)
	{
		double jitter = Gen2.Unit(WorldRandom.Derive(grid.Seed, Gen2.StreamStreetLine + (ulong)axis, (ulong)(uint)k)) * 2.0 - 1.0;
		return k * StreetSpacing + jitter * StreetJitter;
	}

	private static XZ Corner(Grid grid, int k, int j)
	{
		double u = Line(grid, 0, k);
		double v = Line(grid, 1, j);
		return new XZ(grid.Origin.X + grid.U.X * u + grid.V.X * v, grid.Origin.Z + grid.U.Z * u + grid.V.Z * v);
	}

	// the street from corner (k, j) to (k + 1, j) for axis 0, to (k, j + 1) for axis 1
	public bool Street(int district, int k, int j, int axis)
	{
		DistrictDef def = plan.Def.Districts[district];
		if (def.Roads != DistrictRoads.Streets)
		{
			return false;
		}
		Grid grid = grids[district];
		if (Gen2.Unit(WorldRandom.Derive(grid.Seed, Gen2.StreamStreetKeep + (ulong)axis, Gen2.Pack(k, j))) >= def.StreetKeep)
		{
			return false;
		}
		XZ a = Corner(grid, k, j);
		XZ b = (axis == 0) ? Corner(grid, k + 1, j) : Corner(grid, k, j + 1);
		XZ mid = Mid(a, b);
		return plan.DistrictAt(mid.X, mid.Z) == district && !NearPillar(mid, PillarClearance + StreetSpacing * 0.5) && !InClearance(mid);
	}

	private void CollectStreets(Rect tile, List<RoadPiece> pieces, List<RoadPatch> patches)
	{
		// a district that owns a midpoint in the tile is one sampled here or next to one
		districts.Clear();
		int count = plan.Def.Districts.Length;
		for (int i = 0; i <= 2; i++)
		{
			for (int j = 0; j <= 2; j++)
			{
				int d = plan.DistrictAt(tile.X0 + (tile.X1 - tile.X0) * i * 0.5, tile.Z0 + (tile.Z1 - tile.Z0) * j * 0.5);
				for (int n = Math.Max(0, d - 1); n <= Math.Min(count - 1, d + 1); n++)
				{
					districts.Add(n);
				}
			}
		}
		foreach (int district in districts)
		{
			if (plan.Def.Districts[district].Roads != DistrictRoads.Streets)
			{
				continue;
			}
			Grid grid = grids[district];
			// the tile in grid space, grown so every street with its midpoint inside is in range
			double reach = StreetSpacing + StreetJitter;
			double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
			for (int c = 0; c < 4; c++)
			{
				double x = ((c & 1) == 0 ? tile.X0 : tile.X1) - grid.Origin.X;
				double z = ((c & 2) == 0 ? tile.Z0 : tile.Z1) - grid.Origin.Z;
				double u = x * grid.U.X + z * grid.U.Z;
				double v = x * grid.V.X + z * grid.V.Z;
				u0 = Math.Min(u0, u);
				u1 = Math.Max(u1, u);
				v0 = Math.Min(v0, v);
				v1 = Math.Max(v1, v);
			}
			int k0 = (int)Math.Floor((u0 - reach) / StreetSpacing);
			int k1 = (int)Math.Ceiling((u1 + reach) / StreetSpacing);
			int j0 = (int)Math.Floor((v0 - reach) / StreetSpacing);
			int j1 = (int)Math.Ceiling((v1 + reach) / StreetSpacing);
			for (int k = k0; k <= k1; k++)
			{
				for (int j = j0; j <= j1; j++)
				{
					XZ corner = Corner(grid, k, j);
					double width = plan.Def.Districts[district].StreetWidth;
					bool onU = Street(district, k, j, 0);
					bool onV = Street(district, k, j, 1);
					if (onU)
					{
						StreetPieces(tile, district, corner, Corner(grid, k + 1, j), grid.U, width, pieces);
					}
					if (onV)
					{
						StreetPieces(tile, district, corner, Corner(grid, k, j + 1), grid.V, width, pieces);
					}
					if (tile.Contains(corner) && !NearPillar(corner, PillarClearance + width) && Inside(district, new XZ(corner.X - grid.U.X * width * 0.5, corner.Z - grid.U.Z * width * 0.5), new XZ(corner.X + grid.U.X * width * 0.5, corner.Z + grid.U.Z * width * 0.5), grid.U, width * 0.5) && (onU || onV || Street(district, k - 1, j, 0) || Street(district, k, j - 1, 1)))
					{
						patches.Add(new RoadPatch { Kind = RoadKind.Street, Center = corner, Size = width * 0.5, Axis = grid.U });
					}
				}
			}
		}
	}

	// a straight street between two crossings, stopping at their squares, cut into pieces.
	// a piece poking out of its district is left out: streets stop at the edge, where another grid starts
	private void StreetPieces(Rect tile, int district, XZ from, XZ to, XZ dir, double width, List<RoadPiece> pieces)
	{
		double half = width * 0.5;
		XZ a = new XZ(from.X + dir.X * half, from.Z + dir.Z * half);
		XZ b = new XZ(to.X - dir.X * half, to.Z - dir.Z * half);
		double length = a.DistanceTo(b);
		int n = Math.Max(1, (int)Math.Ceiling(length / PieceLength));
		for (int i = 0; i < n; i++)
		{
			double t0 = (double)i / n;
			double t1 = (double)(i + 1) / n;
			XZ pa = new XZ(a.X + (b.X - a.X) * t0, a.Z + (b.Z - a.Z) * t0);
			XZ pb = new XZ(a.X + (b.X - a.X) * t1, a.Z + (b.Z - a.Z) * t1);
			XZ mid = Mid(pa, pb);
			if (tile.Contains(mid) && !NearPillar(mid, PillarClearance + width * 0.5) && Inside(district, pa, pb, dir, half))
			{
				pieces.Add(new RoadPiece { Kind = RoadKind.Street, A = pa, B = pb, TangentA = dir, TangentB = dir, Width = width, Index = i });
			}
		}
	}

	// the four corners of the strip from a to b, half wide either side, all in the district
	private bool Inside(int district, XZ a, XZ b, XZ dir, double half)
	{
		double sx = -dir.Z * half;
		double sz = dir.X * half;
		return plan.DistrictAt(a.X + sx, a.Z + sz) == district && plan.DistrictAt(a.X - sx, a.Z - sz) == district && plan.DistrictAt(b.X + sx, b.Z + sz) == district && plan.DistrictAt(b.X - sx, b.Z - sz) == district;
	}

	// the street grid of a district, for LotPlan's blocks
	internal void GridFrame(int district, out XZ origin, out XZ u, out XZ v, out ulong gridSeed)
	{
		Grid grid = grids[district];
		origin = grid.Origin;
		u = grid.U;
		v = grid.V;
		gridSeed = grid.Seed;
	}

	internal double GridLine(int district, int axis, int k)
	{
		return Line(grids[district], axis, k);
	}

	// ---- buried roads

	public bool Site(int cellX, int cellZ, out XZ position)
	{
		ulong key = Gen2.Pack(cellX, cellZ);
		double span = SiteCell - 2.0 * SiteMargin;
		position = new XZ(cellX * SiteCell + SiteMargin + Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamSite, key)) * span, cellZ * SiteCell + SiteMargin + Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamSite + 1, key)) * span);
		return plan.Def.Districts[plan.DistrictAt(position.X, position.Z)].Roads == DistrictRoads.Open && !NearPillar(position, PillarGap) && !InClearance(position);
	}

	// a buried road from site (x, z) to its east or north neighbour
	public bool Track(int cellX, int cellZ, bool north, out XZ p0, out XZ c1, out XZ c2, out XZ p1)
	{
		c1 = c2 = p1 = default;
		ulong key = Gen2.Pack(cellX, cellZ);
		ulong dir = north ? 1uL : 0uL;
		if (Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamTrack + dir, key)) >= TrackChance || !Site(cellX, cellZ, out p0) || !Site(north ? cellX : cellX + 1, north ? cellZ + 1 : cellZ, out p1))
		{
			p0 = default;
			return false;
		}
		double b1 = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamTrack + 2 + dir * 2, key)) * 2.0 - 1.0;
		double b2 = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamTrack + 3 + dir * 2, key)) * 2.0 - 1.0;
		Bend(p0, p1, b1 * TrackBend, b2 * TrackBend, out c1, out c2);
		return true;
	}

	private void CollectTracks(Rect tile, List<RoadPiece> pieces)
	{
		int cx0 = Gen2.Cell(tile.X0, SiteCell) - 2;
		int cx1 = Gen2.Cell(tile.X1, SiteCell) + 1;
		int cz0 = Gen2.Cell(tile.Z0, SiteCell) - 2;
		int cz1 = Gen2.Cell(tile.Z1, SiteCell) + 1;
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				for (int d = 0; d < 2; d++)
				{
					if (!Track(cx, cz, d == 1, out XZ p0, out XZ c1, out XZ c2, out XZ p1) || !CurveTouches(tile, p0, c1, c2, p1, TrackWidth + PillarClearance))
					{
						continue;
					}
					Sample(1, cx, cz, d, p0, c1, c2, p1, PillarClearance + TrackWidth * 0.5);
					for (int i = 0; i + 1 < points.Count; i++)
					{
						XZ mid = Mid(points[i], points[i + 1]);
						if (tile.Contains(mid) && !InClearance(mid))
						{
							pieces.Add(Piece(RoadKind.Track, TrackWidth, i));
						}
					}
				}
			}
		}
	}

	// ---- shared

	// an expressway's edge within distance of p
	public bool HighwayNear(XZ p, double distance)
	{
		double reach = distance + HighwayWidth * 0.5;
		Rect around = new Rect { X0 = p.X - reach, Z0 = p.Z - reach, X1 = p.X + reach, Z1 = p.Z + reach };
		int cx = Gen2.Cell(p.X, HighwayCell);
		int cz = Gen2.Cell(p.Z, HighwayCell);
		for (int x = cx - 2; x <= cx + 1; x++)
		{
			for (int z = cz - 2; z <= cz + 1; z++)
			{
				for (int d = 0; d < 2; d++)
				{
					if (!Highway(x, z, d == 1, out HighwayLink link) || !CurveTouches(around, link.P0, link.C1, link.C2, link.P1, PillarClearance + HighwayWidth))
					{
						continue;
					}
					Sample(0, link.CellX, link.CellZ, link.North ? 1 : 0, link.P0, link.C1, link.C2, link.P1, PillarClearance + HighwayWidth * 0.5);
					for (int i = 0; i + 1 < points.Count; i++)
					{
						if (SegmentDistance(p, points[i], points[i + 1]) < reach)
						{
							return true;
						}
					}
				}
			}
		}
		return false;
	}

	// every expressway centreline segment that may come within reach of the box, as pairs (a, b)
	public void HighwaySegments(double x0, double z0, double x1, double z1, List<XZ> segments)
	{
		segments.Clear();
		Rect around = new Rect { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1 };
		for (int x = Gen2.Cell(x0, HighwayCell) - 2; x <= Gen2.Cell(x1, HighwayCell) + 1; x++)
		{
			for (int z = Gen2.Cell(z0, HighwayCell) - 2; z <= Gen2.Cell(z1, HighwayCell) + 1; z++)
			{
				for (int d = 0; d < 2; d++)
				{
					if (!Highway(x, z, d == 1, out HighwayLink link) || !CurveTouches(around, link.P0, link.C1, link.C2, link.P1, PillarClearance + HighwayWidth))
					{
						continue;
					}
					Sample(0, link.CellX, link.CellZ, link.North ? 1 : 0, link.P0, link.C1, link.C2, link.P1, PillarClearance + HighwayWidth * 0.5);
					for (int i = 0; i + 1 < points.Count; i++)
					{
						segments.Add(points[i]);
						segments.Add(points[i + 1]);
					}
				}
			}
		}
	}

	public static double SegmentDistance(XZ p, XZ a, XZ b)
	{
		double dx = b.X - a.X;
		double dz = b.Z - a.Z;
		double length = dx * dx + dz * dz;
		double t = (length > 0.0) ? Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length)) : 0.0;
		return p.DistanceTo(new XZ(a.X + dx * t, a.Z + dz * t));
	}

	// the pillar of p's own region cell: pillars sit PillarMargin inside their cell, so it's the only one that close
	private XZ PillarOf(XZ p)
	{
		return LayerPlan.PillarPosition(plan.WorldSeed, Gen2.Cell(p.X, Gen2.RegionSize), Gen2.Cell(p.Z, Gen2.RegionSize));
	}

	private bool NearPillar(XZ p, double distance)
	{
		return PillarOf(p).DistanceTo(p) < distance;
	}

	private bool InClearance(XZ p)
	{
		return plan.Origin.DistanceTo(p) < StartClearance;
	}

	// an s-bend: the control points at a third and two thirds, pushed sideways by bend1/bend2 of the length
	private static void Bend(XZ p0, XZ p1, double bend1, double bend2, out XZ c1, out XZ c2)
	{
		double dx = p1.X - p0.X;
		double dz = p1.Z - p0.Z;
		c1 = new XZ(p0.X + dx / 3.0 - dz * bend1, p0.Z + dz / 3.0 + dx * bend1);
		c2 = new XZ(p0.X + dx * 2.0 / 3.0 - dz * bend2, p0.Z + dz * 2.0 / 3.0 + dx * bend2);
	}

	// the curve stays inside its control points' box
	private static bool CurveTouches(Rect tile, XZ p0, XZ c1, XZ c2, XZ p1, double width)
	{
		double x0 = Math.Min(Math.Min(p0.X, c1.X), Math.Min(c2.X, p1.X)) - width;
		double x1 = Math.Max(Math.Max(p0.X, c1.X), Math.Max(c2.X, p1.X)) + width;
		double z0 = Math.Min(Math.Min(p0.Z, c1.Z), Math.Min(c2.Z, p1.Z)) - width;
		double z1 = Math.Max(Math.Max(p0.Z, c1.Z), Math.Max(c2.Z, p1.Z)) + width;
		return tile.Overlaps(x0, z0, x1, z1);
	}

	// the whole curve into points/along, about PieceLength apart, shifted sideways around any pillar it passes
	// closer than clear. the shift fades out over Detour along the road, so it never reaches the ends (PillarGap)
	private void Sample(int kind, int cellX, int cellZ, int dir, XZ p0, XZ c1, XZ c2, XZ p1, double clear)
	{
		// a road crosses a dozen tiles and every lot near it asks too: keep the samples
		(int, int, int, int) key = (kind, cellX, cellZ, dir);
		if (curves.TryGetValue(key, out Curve curve))
		{
			points.Clear();
			points.AddRange(curve.Points);
			along.Clear();
			along.AddRange(curve.Along);
			return;
		}
		SampleCurve(p0, c1, c2, p1, clear);
		if (curves.Count >= 1024)
		{
			curves.Clear();
		}
		curves[key] = new Curve { Points = points.ToArray(), Along = along.ToArray() };
	}

	private void SampleCurve(XZ p0, XZ c1, XZ c2, XZ p1, double clear)
	{
		points.Clear();
		along.Clear();
		double estimate = (p0.DistanceTo(c1) + c1.DistanceTo(c2) + c2.DistanceTo(p1) + p0.DistanceTo(p1)) * 0.5;
		int n = Math.Max(4, (int)Math.Ceiling(estimate / PieceLength));
		double total = 0.0;
		for (int i = 0; i <= n; i++)
		{
			double t = (double)i / n;
			double s = 1.0 - t;
			double w0 = s * s * s;
			double w1 = 3.0 * s * s * t;
			double w2 = 3.0 * s * t * t;
			double w3 = t * t * t;
			XZ p = new XZ(w0 * p0.X + w1 * c1.X + w2 * c2.X + w3 * p1.X, w0 * p0.Z + w1 * c1.Z + w2 * c2.Z + w3 * p1.Z);
			if (i > 0)
			{
				total += points[i - 1].DistanceTo(p);
			}
			points.Add(p);
			along.Add(total);
		}
		shifts.Clear();
		for (int i = 0; i <= n; i++)
		{
			shifts.Add(new XZ(0.0, 0.0));
		}
		int cx0 = Gen2.Cell(Math.Min(Math.Min(p0.X, c1.X), Math.Min(c2.X, p1.X)), Gen2.RegionSize);
		int cx1 = Gen2.Cell(Math.Max(Math.Max(p0.X, c1.X), Math.Max(c2.X, p1.X)), Gen2.RegionSize);
		int cz0 = Gen2.Cell(Math.Min(Math.Min(p0.Z, c1.Z), Math.Min(c2.Z, p1.Z)), Gen2.RegionSize);
		int cz1 = Gen2.Cell(Math.Max(Math.Max(p0.Z, c1.Z), Math.Max(c2.Z, p1.Z)), Gen2.RegionSize);
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				XZ pillar = LayerPlan.PillarPosition(plan.WorldSeed, cx, cz);
				int closest = 0;
				double best = double.MaxValue;
				for (int i = 0; i <= n; i++)
				{
					double d = points[i].DistanceTo(pillar);
					if (d < best)
					{
						best = d;
						closest = i;
					}
				}
				if (best >= clear + PieceLength * 0.5)
				{
					continue;
				}
				// away from the side the pillar is on, judged at the closest point
				XZ tangent = Direction(points[Math.Max(0, closest - 1)], points[Math.Min(n, closest + 1)]);
				double side = tangent.X * (pillar.Z - points[closest].Z) - tangent.Z * (pillar.X - points[closest].X);
				double away = (side > 0.0) ? -1.0 : 1.0;
				// half a piece more: the true closest point can lie between two samples
				double need = clear + PieceLength * 0.5 - best;
				for (int i = 0; i <= n; i++)
				{
					double f = 1.0 - Math.Abs(along[i] - along[closest]) / Detour;
					if (f <= 0.0)
					{
						continue;
					}
					f = f * f * (3.0 - 2.0 * f);
					XZ dir = Direction(points[Math.Max(0, i - 1)], points[Math.Min(n, i + 1)]);
					// the road's left is (-dir.Z, dir.X)
					shifts[i] = new XZ(shifts[i].X - dir.Z * away * need * f, shifts[i].Z + dir.X * away * need * f);
				}
			}
		}
		total = 0.0;
		for (int i = 0; i <= n; i++)
		{
			points[i] = new XZ(points[i].X + shifts[i].X, points[i].Z + shifts[i].Z);
			if (i > 0)
			{
				total += points[i - 1].DistanceTo(points[i]);
			}
			along[i] = total;
		}
	}

	// piece i of the sampled curve, tangents from the neighbouring points
	private RoadPiece Piece(RoadKind kind, double width, int i)
	{
		return new RoadPiece
		{
			Kind = kind,
			A = points[i],
			B = points[i + 1],
			TangentA = Direction(points[Math.Max(0, i - 1)], points[i + 1]),
			TangentB = Direction(points[i], points[Math.Min(points.Count - 1, i + 2)]),
			Width = width,
			Index = i
		};
	}

	private static XZ Mid(XZ a, XZ b)
	{
		return new XZ((a.X + b.X) * 0.5, (a.Z + b.Z) * 0.5);
	}

	private static XZ Direction(XZ from, XZ to)
	{
		double d = from.DistanceTo(to);
		return (d > 0.0) ? new XZ((to.X - from.X) / d, (to.Z - from.Z) / d) : new XZ(1.0, 0.0);
	}

	// co-op check: highway junctions and links around the centre (with their elevated stretches) and every street grid
	public ulong Hash(XZ center, int radius)
	{
		ulong hash = 14695981039346656037uL;
		int cx = Gen2.Cell(center.X, HighwayCell);
		int cz = Gen2.Cell(center.Z, HighwayCell);
		for (int x = cx - radius; x <= cx + radius; x++)
		{
			for (int z = cz - radius; z <= cz + radius; z++)
			{
				XZ junction = Junction(x, z);
				hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(junction.X));
				hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(junction.Z));
				for (int d = 0; d < 2; d++)
				{
					bool exists = Highway(x, z, d == 1, out HighwayLink link);
					hash = LayerPlan.Mix(hash, exists ? 1 : 0);
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(link.C1.X + link.C2.Z));
					hash = LayerPlan.Mix(hash, (link.Elevated ? 1 : 0) | (link.Broken ? 2 : 0));
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(link.ElevatedFrom * 1000.0 + link.ElevatedTo));
				}
			}
		}
		foreach (Grid grid in grids)
		{
			hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(grid.Origin.X));
			hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(grid.Origin.Z));
			hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(grid.U.Z * 1000.0));
		}
		return hash;
	}
}
