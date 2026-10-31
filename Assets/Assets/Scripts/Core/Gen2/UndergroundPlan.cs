using System;
using System.Collections.Generic;

// an axis-aligned rectangle, layout coords
public struct Area
{
	public double X0;

	public double Z0;

	public double X1;

	public double Z1;

	public bool Contains(double x, double z)
	{
		return x >= X0 && x < X1 && z >= Z0 && z < Z1;
	}

	// from p to the rectangle, 0 inside
	public double Distance(double x, double z)
	{
		double dx = Math.Max(0.0, Math.Max(X0 - x, x - X1));
		double dz = Math.Max(0.0, Math.Max(Z0 - z, z - Z1));
		return Math.Sqrt(dx * dx + dz * dz);
	}

	public Area Grown(double margin)
	{
		return new Area { X0 = X0 - margin, Z0 = Z0 - margin, X1 = X1 + margin, Z1 = Z1 + margin };
	}

	public bool Overlaps(Area other)
	{
		return other.X0 < X1 && other.X1 > X0 && other.Z0 < Z1 && other.Z1 > Z0;
	}
}

// a hall of the underground, layout coords, its sides along x and z. a deep dark pit with a ledge round its walls
// at the underground's level (UndergroundPlan.Level under the deck) and a bridge across, the galleries' mouths on
// the ledge. where a gallery comes in on a side, the gallery's middle line (z on an x side, x on a z side), NaN
// where none. maybe a way in: a passage from one side up a ramp to a ramp house on the deck
public struct Hall
{
	public int CellX;

	public int CellZ;

	public XZ Center;

	public double HalfX;

	public double HalfZ;

	public double GalleryPX;

	public double GalleryNX;

	public double GalleryPZ;

	public double GalleryNZ;

	public bool Entrance;

	// unit, from the hall to the passage (along x or z), and where the passage's middle is along that side, from the
	// hall's middle along Along
	public XZ Out;

	public double EntranceAt;

	// from the ramp house's doorway to the middle of the road it opens onto
	public double Apron;

	public ulong Seed;

	public XZ Along => new XZ(-Out.Z, Out.X);

	public double HalfOut => (Math.Abs(Out.X) > 0.5) ? HalfX : HalfZ;

	public double HalfAlong => (Math.Abs(Out.X) > 0.5) ? HalfZ : HalfX;

	public Area Inner => new Area { X0 = Center.X - HalfX, Z0 = Center.Z - HalfZ, X1 = Center.X + HalfX, Z1 = Center.Z + HalfZ };

	// s along the passage from the hall's wall, across it to the left (Along)
	public XZ PassagePoint(double s, double across)
	{
		double o = HalfOut + UndergroundPlan.Wall + s;
		double a = EntranceAt + across;
		return new XZ(Center.X + Out.X * o - Out.Z * a, Center.Z + Out.Z * o + Out.X * a);
	}

	// the passage's stretch from s0 to s1, half wide either side
	public Area PassageArea(double s0, double s1, double half)
	{
		XZ a = PassagePoint(s0, -half);
		XZ b = PassagePoint(s1, half);
		return new Area { X0 = Math.Min(a.X, b.X), Z0 = Math.Min(a.Z, b.Z), X1 = Math.Max(a.X, b.X), Z1 = Math.Max(a.Z, b.Z) };
	}
}

// a stretch of gallery (a ket-wide tunnel between two halls), along x or z, layout coords
public struct GalleryPiece
{
	public XZ A;

	public XZ B;
}

// world gen v2: the underground (01, the raised city and the underground; episode 1's dark industrial interior:
// Yuuri wants to go into a building, they get lost inside, and leave by a doorway at the end of a passage).
// one level Level under the deck: halls on a coarse grid of cells, a district's share of cells
// (DistrictDef.Underground), joined to the next cell's hall by straight galleries. the ways in for now: ramp houses,
// a concrete building by a road with a big doorway and a straight ramp down a passage to a hall.
// halls keep clear of voids, pillars and the start; ramp houses of everything on the deck.
// a pure function of (layer seed, cell), cached
public sealed class UndergroundPlan
{
	public const double Cell = 256.0;

	// the level the ket drives on this far under the deck; the ceilings under a slab this thick; the halls' pits
	// go down this far
	public const double Level = 11.0;

	public const double Slab = 4.0;

	public const double Bottom = 45.0;

	public const double Wall = 1.0;

	public const double MinHalf = 16.0;

	public const double MaxHalf = 60.0;

	// the ledge round a hall's walls; a hall too small for a pit is all floor
	public const double Ledge = 9.0;

	public const double MinPit = 6.0;

	public const double BridgeWidth = 8.0;

	// from a hall to its cell's edge
	public const double CellMargin = 10.0;

	public const double GalleryWidth = 10.0;

	public const double GalleryHeight = Level - Slab;

	public const double GalleryChance = 0.8;

	// the passage of a way in: flat out of the hall, then up a ramp (11% but its short eased ends), its roof a thin
	// slab just under the deck, the last of it in the ramp house where the deck's open over it
	public const double Flat = 8.0;

	public const double Grade = 0.11;

	public const double RampEase = 0.1;

	public static readonly double RampLength = Level / (Grade * (1.0 - RampEase));

	public static readonly double PassageLength = Flat + RampLength;

	public const double Roof = 0.6;

	// a ket and its riders under the roof
	public const double Headroom = 3.2;

	public const double HouseHeight = 8.0;

	public const double EntranceChance = 0.6;

	public const double MaxApron = 30.0;

	// kept from voids, pillars, the start; a ramp house from roads and lots
	public const double VoidGap = 16.0;

	public const double PillarGap = 120.0;

	public const double RoadGap = 6.0;

	private readonly LayerPlan plan;

	private readonly Dictionary<long, Hall> halls = new Dictionary<long, Hall>();

	private readonly Dictionary<long, bool> have = new Dictionary<long, bool>();

	public UndergroundPlan(LayerPlan plan)
	{
		this.plan = plan;
	}

	// the passage's floor s metres from the hall's wall, from -Level up to the deck
	public static double PassageFloor(double s)
	{
		double u = (s - Flat) / RampLength;
		if (u <= 0.0)
		{
			return -Level;
		}
		if (u >= 1.0)
		{
			return 0.0;
		}
		double m = 1.0 / (1.0 - RampEase);
		double share;
		if (u < RampEase)
		{
			share = m * u * u / (2.0 * RampEase);
		}
		else if (u <= 1.0 - RampEase)
		{
			share = m * (u - RampEase * 0.5);
		}
		else
		{
			double rest = 1.0 - u;
			share = 1.0 - m * rest * rest / (2.0 * RampEase);
		}
		return -Level + Level * share;
	}

	private static double holeFrom = -1.0;

	// the passage's roof: a gallery's height over its floor, at most up to the thin slab under the deck
	public static double PassageRoof(double s)
	{
		return Math.Min(-Roof, PassageFloor(s) + GalleryHeight);
	}

	// where the passage's floor comes up within Headroom of its roof under the deck: from there on the deck is open
	// over it, under the ramp house's roof
	public static double HoleFrom
	{
		get
		{
			if (holeFrom < 0.0)
			{
				double lo = Flat;
				double hi = PassageLength;
				for (int i = 0; i < 60; i++)
				{
					double mid = (lo + hi) * 0.5;
					if (PassageFloor(mid) < -(Roof + Headroom))
					{
						lo = mid;
					}
					else
					{
						hi = mid;
					}
				}
				holeFrom = lo;
			}
			return holeFrom;
		}
	}

	public static double HouseFrom => HoleFrom - 2.0;

	// the hall in this cell, if there is one, with its galleries and way in
	public bool HallAt(int cellX, int cellZ, out Hall hall)
	{
		long key = ((long)cellX << 32) ^ (uint)cellZ;
		if (have.TryGetValue(key, out bool known))
		{
			hall = known ? halls[key] : default(Hall);
			return known;
		}
		if (have.Count > 4096)
		{
			have.Clear();
			halls.Clear();
		}
		bool found = Room(cellX, cellZ, out hall);
		if (found)
		{
			hall.GalleryPX = Gallery(cellX, cellZ, false, out GalleryPiece east) ? east.A.Z : double.NaN;
			hall.GalleryNX = Gallery(cellX - 1, cellZ, false, out GalleryPiece west) ? west.A.Z : double.NaN;
			hall.GalleryPZ = Gallery(cellX, cellZ, true, out GalleryPiece north) ? north.A.X : double.NaN;
			hall.GalleryNZ = Gallery(cellX, cellZ - 1, true, out GalleryPiece south) ? south.A.X : double.NaN;
			EntranceFor(ref hall);
			halls[key] = hall;
		}
		else
		{
			hall = default(Hall);
		}
		have[key] = found;
		return found;
	}

	// the room alone, no galleries or way in yet
	private bool Room(int cellX, int cellZ, out Hall hall)
	{
		hall = default(Hall);
		ulong seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamUnder, Gen2.Pack(cellX, cellZ));
		WorldRandom random = new WorldRandom(seed);
		double roll = Gen2.Unit(random.NextULong());
		hall.CellX = cellX;
		hall.CellZ = cellZ;
		hall.Seed = seed;
		hall.HalfX = MinHalf + Gen2.Unit(random.NextULong()) * (MaxHalf - MinHalf);
		hall.HalfZ = MinHalf + Gen2.Unit(random.NextULong()) * (MaxHalf - MinHalf);
		double roomX = Cell - 2.0 * (CellMargin + Wall + hall.HalfX);
		double roomZ = Cell - 2.0 * (CellMargin + Wall + hall.HalfZ);
		hall.Center = new XZ(cellX * Cell + CellMargin + Wall + hall.HalfX + Gen2.Unit(random.NextULong()) * roomX, cellZ * Cell + CellMargin + Wall + hall.HalfZ + Gen2.Unit(random.NextULong()) * roomZ);
		if (roll >= plan.Def.Districts[plan.DistrictAt(hall.Center.X, hall.Center.Z)].Underground)
		{
			return false;
		}
		return Clear(hall.Inner, VoidGap + Wall);
	}

	// the area and a margin round it clear of voids, pillars and the start
	private bool Clear(Area area, double margin)
	{
		XZ middle = new XZ((area.X0 + area.X1) * 0.5, (area.Z0 + area.Z1) * 0.5);
		XZ pillar = plan.PillarNear(middle.X, middle.Z).Position;
		if (area.Distance(pillar.X, pillar.Z) < PillarGap + margin || area.Distance(plan.Origin.X, plan.Origin.Z) < RoadPlan.StartClearance + margin)
		{
			return false;
		}
		for (double x = area.X0 - margin; x <= area.X1 + margin + 0.01; x += Math.Min(16.0, (area.X1 - area.X0 + 2.0 * margin) * 0.5))
		{
			for (double z = area.Z0 - margin; z <= area.Z1 + margin + 0.01; z += Math.Min(16.0, (area.Z1 - area.Z0 + 2.0 * margin) * 0.5))
			{
				if (plan.Chasms.Inside(x, z) > -margin * 0.5)
				{
					return false;
				}
			}
		}
		return true;
	}

	// the gallery from the hall in this cell to the one east of it (north if asked): along the middle of where the two
	// face each other, from one's wall to the other's. a share of the pairs, clear of everything
	public bool Gallery(int cellX, int cellZ, bool north, out GalleryPiece gallery)
	{
		(int, int, bool) key = (cellX, cellZ, north);
		if (galleries.TryGetValue(key, out (bool, GalleryPiece) known))
		{
			gallery = known.Item2;
			return known.Item1;
		}
		if (galleries.Count > 8192)
		{
			galleries.Clear();
		}
		bool found = FindGallery(cellX, cellZ, north, out gallery);
		galleries[key] = (found, gallery);
		return found;
	}

	private readonly Dictionary<(int, int, bool), (bool, GalleryPiece)> galleries = new Dictionary<(int, int, bool), (bool, GalleryPiece)>();

	private bool FindGallery(int cellX, int cellZ, bool north, out GalleryPiece gallery)
	{
		gallery = default(GalleryPiece);
		if (!Room(cellX, cellZ, out Hall a) || !Room(north ? cellX : cellX + 1, north ? cellZ + 1 : cellZ, out Hall b))
		{
			return false;
		}
		ulong roll = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamUnder + (north ? 2uL : 1uL), Gen2.Pack(cellX, cellZ));
		if (Gen2.Unit(roll) >= GalleryChance)
		{
			return false;
		}
		double half = GalleryWidth * 0.5 + Wall + 2.0;
		if (north)
		{
			double x0 = Math.Max(a.Center.X - a.HalfX, b.Center.X - b.HalfX) + half;
			double x1 = Math.Min(a.Center.X + a.HalfX, b.Center.X + b.HalfX) - half;
			if (x1 < x0)
			{
				return false;
			}
			double x = (x0 + x1) * 0.5;
			gallery.A = new XZ(x, a.Center.Z + a.HalfZ + Wall);
			gallery.B = new XZ(x, b.Center.Z - b.HalfZ - Wall);
		}
		else
		{
			double z0 = Math.Max(a.Center.Z - a.HalfZ, b.Center.Z - b.HalfZ) + half;
			double z1 = Math.Min(a.Center.Z + a.HalfZ, b.Center.Z + b.HalfZ) - half;
			if (z1 < z0)
			{
				return false;
			}
			double z = (z0 + z1) * 0.5;
			gallery.A = new XZ(a.Center.X + a.HalfX + Wall, z);
			gallery.B = new XZ(b.Center.X - b.HalfX - Wall, z);
		}
		return Clear(GalleryArea(gallery), VoidGap + Wall);
	}

	// ---- ways in

	// a passage on a side of the hall with no gallery, up to a ramp house on the deck whose doorway opens onto a road a
	// few metres off, where nothing on the deck is in the way. under the deck the passage keeps to its own cell and
	// to neighbouring cells with no hall (so it never meets another's galleries)
	private void EntranceFor(ref Hall hall)
	{
		WorldRandom random = new WorldRandom(WorldRandom.Derive(hall.Seed, Gen2.StreamUnder, 1uL));
		if (Gen2.Unit(random.NextULong()) >= EntranceChance)
		{
			return;
		}
		int first = random.Next(4);
		double slide = Gen2.Unit(random.NextULong()) * 2.0 - 1.0;
		for (int n = 0; n < 4; n++)
		{
			int side = (first + n) % 4;
			double gallery = (side == 0) ? hall.GalleryPX : (side == 1) ? hall.GalleryNX : (side == 2) ? hall.GalleryPZ : hall.GalleryNZ;
			if (!double.IsNaN(gallery))
			{
				continue;
			}
			for (int k = 0; k < 3; k++)
			{
				Hall at = hall;
				at.Out = (side == 0) ? new XZ(1.0, 0.0) : (side == 1) ? new XZ(-1.0, 0.0) : (side == 2) ? new XZ(0.0, 1.0) : new XZ(0.0, -1.0);
				at.EntranceAt = ((k == 0) ? slide : (k == 1) ? -0.6 : 0.6) * (at.HalfAlong - GalleryWidth * 0.5 - Wall);
				Area passage = at.PassageArea(0.0, PassageLength, GalleryWidth * 0.5 + Wall);
				if (!Free(at, passage) || !Clear(passage, VoidGap) || !Opens(ref at))
				{
					continue;
				}
				at.Entrance = true;
				hall = at;
				return;
			}
		}
	}

	// the passage's cells: its hall's own, or ones with no hall in them
	private bool Free(Hall hall, Area passage)
	{
		int cx0 = Gen2.Cell(passage.X0 - CellMargin, Cell);
		int cx1 = Gen2.Cell(passage.X1 + CellMargin, Cell);
		int cz0 = Gen2.Cell(passage.Z0 - CellMargin, Cell);
		int cz1 = Gen2.Cell(passage.Z1 + CellMargin, Cell);
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				if ((cx != hall.CellX || cz != hall.CellZ) && Room(cx, cz, out _))
				{
					return false;
				}
			}
		}
		return true;
	}

	public static Area HouseArea(Hall hall)
	{
		return hall.PassageArea(HouseFrom, PassageLength, GalleryWidth * 0.5 + Wall);
	}

	public static Area HoleArea(Hall hall)
	{
		return hall.PassageArea(HoleFrom, PassageLength, GalleryWidth * 0.5);
	}

	public static Area ApronArea(Hall hall)
	{
		return hall.PassageArea(PassageLength, PassageLength + hall.Apron, GalleryWidth * 0.5 + 1.0);
	}

	// the ramp house and its apron clear of everything on the deck, the apron ending on a road (sets Apron): a ground
	// street of the block the house stands in, or a buried road out in the open
	private bool Opens(ref Hall hall)
	{
		Area house = HouseArea(hall);
		XZ door = hall.PassagePoint(PassageLength, 0.0);
		XZ middle = new XZ((house.X0 + house.X1) * 0.5, (house.Z0 + house.Z1) * 0.5);
		if (plan.Chasms.Inside(middle.X, middle.Z) > -(Gen2.TileSize * 1.6 + 30.0) || house.Distance(plan.Origin.X, plan.Origin.Z) < RoadPlan.StartClearance + RoadGap)
		{
			return false;
		}
		XZ pillar = plan.PillarNear(middle.X, middle.Z).Position;
		double reach = (house.X1 - house.X0 + house.Z1 - house.Z0) * 0.5 + MaxApron;
		if (house.Distance(pillar.X, pillar.Z) < PillarGap || plan.Roads.HighwayNear(middle, reach))
		{
			return false;
		}
		plan.Viaducts.Near(middle.X, middle.Z, viaducts);
		if (plan.Viaducts.Blocks(middle, reach, viaducts))
		{
			return false;
		}
		int district = plan.DistrictAt(door.X, door.Z);
		for (int c = 0; c < 4; c++)
		{
			if (plan.DistrictAt(((c & 1) == 0) ? house.X0 : house.X1, ((c & 2) == 0) ? house.Z0 : house.Z1) != district)
			{
				return false;
			}
		}
		return (plan.Def.Districts[district].Roads == DistrictRoads.Streets) ? OntoStreet(ref hall, district, house, door) : OntoTrack(ref hall, house, door);
	}

	// the house inside one block, clear of its edges' strips; the apron straight out of the doorway to the middle of
	// the first street line it meets, a ground street there
	private bool OntoStreet(ref Hall hall, int district, Area house, XZ door)
	{
		plan.Roads.GridFrame(district, out XZ origin, out XZ u, out XZ v, out _);
		DistrictDef def = plan.Def.Districts[district];
		double half = def.StreetWidth * 0.5;
		double inset = Math.Max(half + def.Sidewalk, half + LotPlan.StairWidth + LotPlan.StairGap);
		double du = (door.X - origin.X) * u.X + (door.Z - origin.Z) * u.Z;
		double dv = (door.X - origin.X) * v.X + (door.Z - origin.Z) * v.Z;
		int k = Line(district, 0, du);
		int j = Line(district, 1, dv);
		double u0 = plan.Roads.GridLine(district, 0, k);
		double u1 = plan.Roads.GridLine(district, 0, k + 1);
		double v0 = plan.Roads.GridLine(district, 1, j);
		double v1 = plan.Roads.GridLine(district, 1, j + 1);
		// all of the house well inside the block
		for (int c = 0; c < 4; c++)
		{
			double x = ((c & 1) == 0) ? house.X0 : house.X1;
			double z = ((c & 2) == 0) ? house.Z0 : house.Z1;
			double gu = (x - origin.X) * u.X + (z - origin.Z) * u.Z;
			double gv = (x - origin.X) * v.X + (z - origin.Z) * v.Z;
			if (gu < u0 + inset || gu > u1 - inset || gv < v0 + inset || gv > v1 - inset)
			{
				return false;
			}
		}
		// the first line the apron meets
		double ou = hall.Out.X * u.X + hall.Out.Z * u.Z;
		double ov = hall.Out.X * v.X + hall.Out.Z * v.Z;
		double tu = (ou > 1e-9) ? (u1 - du) / ou : (ou < -1e-9) ? (u0 - du) / ou : double.MaxValue;
		double tv = (ov > 1e-9) ? (v1 - dv) / ov : (ov < -1e-9) ? (v0 - dv) / ov : double.MaxValue;
		bool acrossU = tu < tv;
		double t = Math.Min(tu, tv);
		if (t > MaxApron)
		{
			return false;
		}
		XZ end = new XZ(door.X + hall.Out.X * t, door.Z + hall.Out.Z * t);
		double eu = (end.X - origin.X) * u.X + (end.Z - origin.Z) * u.Z;
		double ev = (end.X - origin.X) * v.X + (end.Z - origin.Z) * v.Z;
		if (acrossU)
		{
			// a line across U is a cross street (line k or k + 1, along V), block j of it
			int line = (ou > 0.0) ? k + 1 : k;
			if (!plan.Roads.Street(district, line, j, 1) || plan.Roads.CrossHeight(district, line, j, ev) > 0.3 || ev < v0 + half + 2.0 || ev > v1 - half - 2.0)
			{
				return false;
			}
		}
		else
		{
			int line = (ov > 0.0) ? j + 1 : j;
			if (!plan.Roads.Street(district, k, line, 0) || plan.Roads.RaisedHeight(district, line, k + (eu - u0) / (u1 - u0)) > 0.3 || eu < u0 + half + 2.0 || eu > u1 - half - 2.0)
			{
				return false;
			}
		}
		hall.Apron = t;
		return true;
	}

	// the grid line at or below g on that axis
	private int Line(int district, int axis, double g)
	{
		int k = (int)Math.Floor(g / RoadPlan.StreetSpacing);
		while (plan.Roads.GridLine(district, axis, k) > g)
		{
			k--;
		}
		while (plan.Roads.GridLine(district, axis, k + 1) <= g)
		{
			k++;
		}
		return k;
	}

	// out in the open: a buried road crossing the apron's line a few metres in front of the doorway, none near the
	// house itself, no site's lot near either
	private bool OntoTrack(ref Hall hall, Area house, XZ door)
	{
		double best = double.MaxValue;
		XZ middle = new XZ((house.X0 + house.X1) * 0.5, (house.Z0 + house.Z1) * 0.5);
		int sx = Gen2.Cell(middle.X, RoadPlan.SiteCell);
		int sz = Gen2.Cell(middle.Z, RoadPlan.SiteCell);
		for (int cx = sx - 2; cx <= sx + 1; cx++)
		{
			for (int cz = sz - 2; cz <= sz + 1; cz++)
			{
				if (plan.Roads.Site(cx, cz, out XZ site) && house.Distance(site.X, site.Z) < LotPlan.SiteLot * 1.42 + MaxApron)
				{
					return false;
				}
				for (int d = 0; d < 2; d++)
				{
					if (!plan.Roads.Track(cx, cz, d == 1, out XZ p0, out XZ c1, out XZ c2, out XZ p1))
					{
						continue;
					}
					int steps = Math.Max(8, (int)(p0.DistanceTo(p1) / 2.0));
					for (int i = 0; i <= steps; i++)
					{
						double t = (double)i / steps;
						double s = 1.0 - t;
						double x = s * s * s * p0.X + 3.0 * s * s * t * c1.X + 3.0 * s * t * t * c2.X + t * t * t * p1.X;
						double z = s * s * s * p0.Z + 3.0 * s * s * t * c1.Z + 3.0 * s * t * t * c2.Z + t * t * t * p1.Z;
						if (house.Distance(x, z) < RoadPlan.TrackWidth * 0.5 + RoadGap)
						{
							return false;
						}
						double ahead = (x - door.X) * hall.Out.X + (z - door.Z) * hall.Out.Z;
						double aside = Math.Abs(-(x - door.X) * hall.Out.Z + (z - door.Z) * hall.Out.X);
						if (ahead > 6.0 && ahead < MaxApron && aside < 4.0)
						{
							best = Math.Min(best, ahead);
						}
					}
				}
			}
		}
		if (best == double.MaxValue)
		{
			return false;
		}
		hall.Apron = best;
		return true;
	}

	private readonly List<Viaduct> viaducts = new List<Viaduct>();

	// ---- for tiles

	// the halls whose middle is in the tile (with their ways in), and the gallery pieces (at most PieceLength long)
	// whose middle is
	public void Collect(int tileX, int tileZ, List<Hall> hallsOut, List<GalleryPiece> pieces)
	{
		hallsOut.Clear();
		pieces.Clear();
		double x0 = tileX * Gen2.TileSize;
		double z0 = tileZ * Gen2.TileSize;
		Area tile = new Area { X0 = x0, Z0 = z0, X1 = x0 + Gen2.TileSize, Z1 = z0 + Gen2.TileSize };
		int cx0 = Gen2.Cell(x0, Cell) - 1;
		int cx1 = Gen2.Cell(tile.X1, Cell) + 1;
		int cz0 = Gen2.Cell(z0, Cell) - 1;
		int cz1 = Gen2.Cell(tile.Z1, Cell) + 1;
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				if (HallAt(cx, cz, out Hall hall) && tile.Contains(hall.Center.X, hall.Center.Z))
				{
					hallsOut.Add(hall);
				}
				for (int d = 0; d < 2; d++)
				{
					if (!Gallery(cx, cz, d == 1, out GalleryPiece gallery))
					{
						continue;
					}
					double length = gallery.A.DistanceTo(gallery.B);
					int n = Math.Max(1, (int)Math.Ceiling(length / RoadPlan.PieceLength));
					for (int i = 0; i < n; i++)
					{
						XZ a = Lerp(gallery.A, gallery.B, (double)i / n);
						XZ b = Lerp(gallery.A, gallery.B, (double)(i + 1) / n);
						if (tile.Contains((a.X + b.X) * 0.5, (a.Z + b.Z) * 0.5))
						{
							pieces.Add(new GalleryPiece { A = a, B = b });
						}
					}
				}
			}
		}
	}

	// the holes in the deck over the passages' top ends that reach into the tile, under the ramp houses' roofs
	public void Holes(int tileX, int tileZ, List<Area> holes)
	{
		holes.Clear();
		double x0 = tileX * Gen2.TileSize;
		double z0 = tileZ * Gen2.TileSize;
		Area tile = new Area { X0 = x0, Z0 = z0, X1 = x0 + Gen2.TileSize, Z1 = z0 + Gen2.TileSize };
		int cx0 = Gen2.Cell(x0, Cell) - 1;
		int cx1 = Gen2.Cell(tile.X1, Cell) + 1;
		int cz0 = Gen2.Cell(z0, Cell) - 1;
		int cz1 = Gen2.Cell(tile.Z1, Cell) + 1;
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				if (!HallAt(cx, cz, out Hall hall) || !hall.Entrance)
				{
					continue;
				}
				Area hole = HoleArea(hall);
				Area part = new Area { X0 = Math.Max(hole.X0, tile.X0), Z0 = Math.Max(hole.Z0, tile.Z0), X1 = Math.Min(hole.X1, tile.X1), Z1 = Math.Min(hole.Z1, tile.Z1) };
				if (part.X1 > part.X0 && part.Z1 > part.Z0)
				{
					holes.Add(part);
				}
			}
		}
	}

	// whether something round p with this radius would stand in a ramp house or on its apron
	public bool Blocks(XZ p, double radius)
	{
		int cx = Gen2.Cell(p.X, Cell);
		int cz = Gen2.Cell(p.Z, Cell);
		for (int x = cx - 1; x <= cx + 1; x++)
		{
			for (int z = cz - 1; z <= cz + 1; z++)
			{
				if (HallAt(x, z, out Hall hall) && hall.Entrance && (HouseArea(hall).Distance(p.X, p.Z) < radius + 2.0 || ApronArea(hall).Distance(p.X, p.Z) < radius))
				{
					return true;
				}
			}
		}
		return false;
	}

	// whether (x, z) is over the underground: a hall (its walls too), a gallery or a passage
	public bool Under(double x, double z)
	{
		int cx = Gen2.Cell(x, Cell);
		int cz = Gen2.Cell(z, Cell);
		if (HallAt(cx, cz, out Hall hall) && hall.Inner.Grown(Wall).Contains(x, z))
		{
			return true;
		}
		// passages reach into the next cells
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				if (HallAt(cx + dx, cz + dz, out Hall way) && way.Entrance && way.PassageArea(0.0, PassageLength, GalleryWidth * 0.5 + Wall).Contains(x, z))
				{
					return true;
				}
			}
		}
		for (int dx = -1; dx <= 0; dx++)
		{
			for (int dz = -1; dz <= 0; dz++)
			{
				for (int d = 0; d < 2; d++)
				{
					if (Gallery(cx + dx, cz + dz, d == 1, out GalleryPiece g) && GalleryArea(g).Contains(x, z))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	// over a hall's pit, off the ledge and the bridge
	public bool Pit(double x, double z)
	{
		if (!HallAt(Gen2.Cell(x, Cell), Gen2.Cell(z, Cell), out Hall hall) || !PitArea(hall, out Area pit) || !pit.Contains(x, z))
		{
			return false;
		}
		return !BridgeArea(hall).Contains(x, z);
	}

	// the pit inside the ledge, if the hall's big enough for one
	public static bool PitArea(Hall hall, out Area pit)
	{
		pit = hall.Inner.Grown(-Ledge);
		return pit.X1 - pit.X0 >= 2.0 * MinPit && pit.Z1 - pit.Z0 >= 2.0 * MinPit;
	}

	// the bridge across the pit's short way, through the hall's middle
	public static Area BridgeArea(Hall hall)
	{
		double w = BridgeWidth * 0.5;
		return (hall.HalfX >= hall.HalfZ) ? new Area { X0 = hall.Center.X - w, Z0 = hall.Center.Z - hall.HalfZ, X1 = hall.Center.X + w, Z1 = hall.Center.Z + hall.HalfZ } : new Area { X0 = hall.Center.X - hall.HalfX, Z0 = hall.Center.Z - w, X1 = hall.Center.X + hall.HalfX, Z1 = hall.Center.Z + w };
	}

	public static Area GalleryArea(GalleryPiece g)
	{
		double w = GalleryWidth * 0.5;
		return (Math.Abs(g.A.X - g.B.X) < 1e-9) ? new Area { X0 = g.A.X - w, Z0 = Math.Min(g.A.Z, g.B.Z), X1 = g.A.X + w, Z1 = Math.Max(g.A.Z, g.B.Z) } : new Area { X0 = Math.Min(g.A.X, g.B.X), Z0 = g.A.Z - w, X1 = Math.Max(g.A.X, g.B.X), Z1 = g.A.Z + w };
	}

	// halls and ways in round centre, for the co-op check
	public ulong Hash(XZ center, int radius)
	{
		ulong hash = 14695981039346656037uL;
		int cx = Gen2.Cell(center.X, Cell);
		int cz = Gen2.Cell(center.Z, Cell);
		for (int x = cx - radius; x <= cx + radius; x++)
		{
			for (int z = cz - radius; z <= cz + radius; z++)
			{
				if (HallAt(x, z, out Hall hall))
				{
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(hall.Center.X * 1000.0 + hall.Center.Z));
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(hall.HalfX * 1000.0 + hall.HalfZ));
					hash = LayerPlan.Mix(hash, hall.Entrance ? LayerPlan.Centimetres(hall.Out.X * 1000.0 + hall.Out.Z * 100.0 + hall.EntranceAt + hall.Apron * 1e-3) : 0);
				}
			}
		}
		return hash;
	}

	private static XZ Lerp(XZ a, XZ b, double t)
	{
		return new XZ(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
	}
}
