using System;
using System.Collections.Generic;

public enum LotKind
{
	// a building from the district's kit
	Building,
	// rubble, a wreck, drifts
	Open,
	// one of the old node prefabs placed whole, if one fits (else the world side makes it a building or open lot)
	Landmark
}

// a buildable rectangle. Axis is its first side's direction, the second runs along (-Axis.Z, Axis.X)
public struct Lot
{
	public LotKind Kind;

	public int District;

	public XZ Center;

	public XZ Axis;

	public double HalfU;

	public double HalfV;

	public int Floors;

	// a building lot standing on a raised plate this high (podium), 0 on the deck
	public double Base;

	// a building entered from a raised street (LotPlan.Door): which of its v sides faces the street (+1, -1, 0 for
	// none), how far along Axis from the centre the door is, and the street's deck height there. it stands on a
	// plinth up to the deck instead of on any podium. one entered from a raised cross street is turned a quarter, so
	// its door's on a v side too
	public int Door;

	public double DoorAt;

	public double Deck;

	// the world side's own randomness for this lot (building shape, which landmark, props)
	public ulong Seed;
}

// a flight of stairs from the ground up to a raised street, against the deck's side, layout coords
public struct Stair
{
	public int District;

	// the foot on the stair's middle line, which way it climbs, which way is away from the street
	public XZ Foot;

	public XZ Dir;

	public XZ Out;

	public double Deck;

	// grid coordinate along its street of the top landing's middle, where the parapet's open (u on a raised street,
	// v on a raised cross street)
	public double LandingAt;

	public double Length => LotPlan.StairLength(Deck);
}

// lots of one layer (01-world-layers, lots). street districts: each block of the street grid, inset from the streets,
// split into lots. open districts: at most one lot per road site, most of the snow stays empty.
// pure like RoadPlan: a lot belongs to the tile holding its centre
public sealed class LotPlan
{
	// kept clear between a street's edge and the block

	public const double MinLot = 14.0;

	// blocks are split until both sides are this or less
	public const double MaxLot = 40.0;

	// half the side of a site's lot
	public const double SiteLot = 32.0;

	// lots keep this much more than their own size from an expressway
	public const double HighwayGap = 6.0;

	// doors from raised streets: share of block sides along a flat stretch of deck that get one, the lowest deck,
	// the entry's size on the lot's edge, the gap left in the street's parapet
	public const double DoorChance = 0.33;

	public const double DoorMinDeck = 8.0;

	public const double PorchWidth = 3.6;

	public const double PorchDepth = 3.2;

	public const double DoorWidth = 1.6;

	// stairs up to raised streets: share of block sides along a flat stretch of deck that get a flight, its width
	// (a wall on the outer side included), what the lots keep clear behind it, the steps, the landings
	public const double StairChance = 0.15;

	public const double StairWidth = 2.2;

	public const double StairGap = 1.0;

	// under the girls' step height (0.23 m)
	public const double StairRise = 0.2;

	public const double StairRun = 0.3;

	public const double MidLanding = 2.0;

	public const double TopLanding = 2.4;

	public static double StairLength(double deck)
	{
		return Math.Ceiling(deck / StairRise) * StairRun + MidLanding + TopLanding;
	}

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
	}

	private readonly LayerPlan plan;

	private readonly HashSet<int> districts = new HashSet<int>();

	// expressway segments around the tile being collected
	private readonly List<XZ> highway = new List<XZ>();

	// and the viaducts, whose corridors stay free
	private readonly List<Viaduct> viaducts = new List<Viaduct>();

	// the same for one block asked about on its own (doors), so a query never clobbers a tile being collected
	private readonly List<XZ> doorHighway = new List<XZ>();

	private readonly List<Viaduct> doorViaducts = new List<Viaduct>();

	private readonly List<Lot> doorLots = new List<Lot>();

	private readonly Dictionary<(int, int, int, int), (bool, double, double)> doors = new Dictionary<(int, int, int, int), (bool, double, double)>();

	private readonly Dictionary<(int, int, int, int), (bool, Stair)> stairs = new Dictionary<(int, int, int, int), (bool, Stair)>();

	private readonly List<Viaduct> stairViaducts = new List<Viaduct>();

	public LotPlan(LayerPlan plan)
	{
		this.plan = plan;
	}

	// and the stairs of the blocks centred in it, if asked
	public void Collect(int tileX, int tileZ, List<Lot> lots, List<Stair> stairsOut = null)
	{
		lots.Clear();
		stairsOut?.Clear();
		Rect tile = new Rect
		{
			X0 = tileX * Gen2.TileSize,
			Z0 = tileZ * Gen2.TileSize,
			X1 = (tileX + 1) * Gen2.TileSize,
			Z1 = (tileZ + 1) * Gen2.TileSize
		};
		// a block's lots reach about half a block out of the tile, a site's lot SiteLot
		double reach = RoadPlan.StreetSpacing + RoadPlan.StreetJitter + SiteLot * 1.42 + HighwayGap + RoadPlan.HighwayWidth;
		plan.Roads.HighwaySegments(tile.X0 - reach, tile.Z0 - reach, tile.X1 + reach, tile.Z1 + reach, highway);
		plan.Viaducts.Near((tile.X0 + tile.X1) * 0.5, (tile.Z0 + tile.Z1) * 0.5, viaducts);
		CollectBlocks(tile, lots, stairsOut);
		CollectSites(tile, lots);
	}

	// an expressway's edge within distance of p
	private static bool HighwayNear(XZ p, double distance, List<XZ> highway)
	{
		double reach = distance + RoadPlan.HighwayWidth * 0.5;
		for (int i = 0; i + 1 < highway.Count; i += 2)
		{
			if (RoadPlan.SegmentDistance(p, highway[i], highway[i + 1]) < reach)
			{
				return true;
			}
		}
		return false;
	}

	// ---- street districts

	private void CollectBlocks(Rect tile, List<Lot> lots, List<Stair> stairsOut)
	{
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
			plan.Roads.GridFrame(district, out XZ origin, out XZ u, out XZ v, out ulong gridSeed);
			// the tile in grid space, grown by a block so every block centred inside is in range
			double reach = RoadPlan.StreetSpacing + RoadPlan.StreetJitter;
			double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
			for (int c = 0; c < 4; c++)
			{
				double x = ((c & 1) == 0 ? tile.X0 : tile.X1) - origin.X;
				double z = ((c & 2) == 0 ? tile.Z0 : tile.Z1) - origin.Z;
				double gu = x * u.X + z * u.Z;
				double gv = x * v.X + z * v.Z;
				u0 = Math.Min(u0, gu);
				u1 = Math.Max(u1, gu);
				v0 = Math.Min(v0, gv);
				v1 = Math.Max(v1, gv);
			}
			int k0 = (int)Math.Floor((u0 - reach) / RoadPlan.StreetSpacing);
			int k1 = (int)Math.Ceiling((u1 + reach) / RoadPlan.StreetSpacing);
			int j0 = (int)Math.Floor((v0 - reach) / RoadPlan.StreetSpacing);
			int j1 = (int)Math.Ceiling((v1 + reach) / RoadPlan.StreetSpacing);
			for (int k = k0; k <= k1; k++)
			{
				for (int j = j0; j <= j1; j++)
				{
					Block(tile, district, origin, u, v, gridSeed, k, j, lots, stairsOut);
				}
			}
		}
	}

	// the block between grid lines k, k+1 and j, j+1, if its centre is in this tile, with its doors marked
	private void Block(Rect tile, int district, XZ origin, XZ u, XZ v, ulong gridSeed, int k, int j, List<Lot> lots, List<Stair> stairsOut)
	{
		double cu = (plan.Roads.GridLine(district, 0, k) + plan.Roads.GridLine(district, 0, k + 1)) * 0.5;
		double cv = (plan.Roads.GridLine(district, 1, j) + plan.Roads.GridLine(district, 1, j + 1)) * 0.5;
		if (!tile.Contains(At(origin, u, v, cu, cv)))
		{
			return;
		}
		int first = lots.Count;
		BlockLots(district, origin, u, v, gridSeed, k, j, highway, viaducts, lots);
		foreach (int side in Sides)
		{
			if (Door(district, k, j, side, out double at, out double deck))
			{
				MarkDoor(district, k, j, side, at, deck, origin, u, v, lots, first);
			}
		}
		foreach (int side in Sides)
		{
			if (stairsOut != null && Stair(district, k, j, side, out Stair stair))
			{
				stairsOut.Add(stair);
			}
		}
	}

	// a block's sides, in the order their doors take lots: its -v and +v edges (raised streets), its -u and +u edges
	// (raised cross streets)
	private static readonly int[] Sides = { -1, 1, -2, 2 };

	// the door's lot on that side, if one's left that no earlier side took. a cross street's is turned a quarter
	private void MarkDoor(int district, int k, int j, int side, double at, double deck, XZ origin, XZ u, XZ v, List<Lot> lots, int first)
	{
		int i = DoorLot(district, k, j, side, at, origin, u, v, lots, first);
		if (i < 0)
		{
			return;
		}
		Lot lot = lots[i];
		if (Math.Abs(side) == 2)
		{
			// Left(v) is -u
			double hu = lot.HalfU;
			lot.Axis = v;
			lot.HalfU = lot.HalfV;
			lot.HalfV = hu;
			lot.Door = (side > 0) ? -1 : 1;
			lot.DoorAt = at - Grid(origin, v, lot.Center);
		}
		else
		{
			lot.Door = side;
			lot.DoorAt = at - Grid(origin, u, lot.Center);
		}
		lot.Deck = deck;
		lots[i] = lot;
	}

	// the lots of block (k, j) wherever it is, if it all lies in the district
	private void BlockLots(int district, XZ origin, XZ u, XZ v, ulong gridSeed, int k, int j, List<XZ> highway, List<Viaduct> viaducts, List<Lot> lots)
	{
		double bu0 = plan.Roads.GridLine(district, 0, k);
		double bu1 = plan.Roads.GridLine(district, 0, k + 1);
		double bv0 = plan.Roads.GridLine(district, 1, j);
		double bv1 = plan.Roads.GridLine(district, 1, j + 1);
		double cu = (bu0 + bu1) * 0.5;
		double cv = (bv0 + bv1) * 0.5;
		XZ centre = At(origin, u, v, cu, cv);
		double inset = plan.Def.Districts[district].StreetWidth * 0.5 + plan.Def.Districts[district].Sidewalk;
		double hu = (bu1 - bu0) * 0.5 - inset;
		double hv = (bv1 - bv0) * 0.5 - inset;
		if (hu * 2.0 < MinLot || hv * 2.0 < MinLot)
		{
			return;
		}
		for (int c = 0; c < 4; c++)
		{
			XZ corner = At(origin, u, v, cu + ((c & 1) == 0 ? -hu : hu), cv + ((c & 2) == 0 ? -hv : hv));
			if (plan.DistrictAt(corner.X, corner.Z) != district)
			{
				return;
			}
		}
		XZ pillar = plan.PillarNear(centre.X, centre.Z).Position;
		if (RectDistance(origin, u, v, cu, cv, hu, hv, pillar) < RoadPlan.PillarClearance || RectDistance(origin, u, v, cu, cv, hu, hv, plan.Origin) < RoadPlan.StartClearance)
		{
			return;
		}
		// a side with stairs keeps them clear
		double half = plan.Def.Districts[district].StreetWidth * 0.5;
		double clear = Math.Max(inset, half + StairWidth + StairGap);
		double v0 = bv0 + (Stair(district, k, j, -1, out _) ? clear : inset);
		double v1 = bv1 - (Stair(district, k, j, 1, out _) ? clear : inset);
		double u0 = bu0 + (Stair(district, k, j, -2, out _) ? clear : inset);
		double u1 = bu1 - (Stair(district, k, j, 2, out _) ? clear : inset);
		cv = (v0 + v1) * 0.5;
		hv = (v1 - v0) * 0.5;
		cu = (u0 + u1) * 0.5;
		hu = (u1 - u0) * 0.5;
		if (hv * 2.0 < MinLot || hu * 2.0 < MinLot)
		{
			return;
		}
		Split(district, origin, u, v, cu, cv, hu, hv, WorldRandom.Derive(gridSeed, Gen2.StreamLot, Gen2.Pack(k, j)), highway, viaducts, lots);
	}

	// ---- doors from raised streets (01, the raised city)

	// whether the street along side (+1 the +v edge, -1 the -v edge, +2 the +u edge, -2 the -u edge) of block (k, j)
	// is a raised one with a door into the block from it: the deck high all along the block (and flat, on a raised
	// street), a building lot on that edge with room for the entry that no earlier side took, a share of those. grid
	// coordinate along the street of the door's middle, the deck height there. cached, pure
	public bool Door(int district, int k, int j, int side, out double at, out double deck)
	{
		(int, int, int, int) key = (district, k, j, side);
		if (doors.TryGetValue(key, out (bool, double, double) known))
		{
			at = known.Item2;
			deck = known.Item3;
			return known.Item1;
		}
		if (doors.Count > 8192)
		{
			doors.Clear();
		}
		bool found = FindDoor(district, k, j, side, out at, out deck);
		doors[key] = (found, at, deck);
		return found;
	}

	private bool FindDoor(int district, int k, int j, int side, out double at, out double deck)
	{
		at = deck = 0.0;
		DistrictDef def = plan.Def.Districts[district];
		bool cross = Math.Abs(side) == 2;
		if (def.Roads != DistrictRoads.Streets || def.Style == BuildingStyle.Industrial || (cross ? def.RaisedCross : def.Raised) <= 0.0 || Stair(district, k, j, side, out _))
		{
			return false;
		}
		plan.Roads.GridFrame(district, out XZ origin, out XZ u, out XZ v, out ulong gridSeed);
		ulong stream = cross ? Gen2.StreamCrossDoor : Gen2.StreamDoor;
		ulong roll = WorldRandom.Derive(gridSeed, stream + (side > 0 ? 1uL : 0uL), Gen2.Pack(k, j));
		if (Gen2.Unit(roll) >= DoorChance)
		{
			return false;
		}
		double inset = def.StreetWidth * 0.5 + def.Sidewalk;
		double from, to;
		if (cross)
		{
			from = plan.Roads.GridLine(district, 1, j) + inset + PorchWidth;
			to = plan.Roads.GridLine(district, 1, j + 1) - inset - PorchWidth;
		}
		else
		{
			int line = (side > 0) ? j + 1 : j;
			deck = plan.Roads.RaisedHeight(district, line, k);
			if (deck < DoorMinDeck || Math.Abs(plan.Roads.RaisedHeight(district, line, k + 0.5) - deck) > 1e-9 || Math.Abs(plan.Roads.RaisedHeight(district, line, k + 0.999) - deck) > 1e-9)
			{
				return false;
			}
			from = plan.Roads.GridLine(district, 0, k) + inset + PorchWidth;
			to = plan.Roads.GridLine(district, 0, k + 1) - inset - PorchWidth;
		}
		if (to <= from)
		{
			return false;
		}
		at = from + (to - from) * Gen2.Unit(WorldRandom.Derive(roll, stream, 2uL));
		if (cross)
		{
			// a cross street's deck may slope a little, the entry meets it where it is
			deck = plan.Roads.CrossHeight(district, (side > 0) ? k + 1 : k, j, at);
			if (deck < DoorMinDeck)
			{
				return false;
			}
		}
		// the sides before this one first, before doorLots is taken
		int earlier = Array.IndexOf(Sides, side);
		bool[] had = new bool[earlier];
		double[] hadAt = new double[earlier];
		double[] hadDeck = new double[earlier];
		for (int n = 0; n < earlier; n++)
		{
			had[n] = Door(district, k, j, Sides[n], out hadAt[n], out hadDeck[n]);
		}
		XZ centre = At(origin, u, v, (plan.Roads.GridLine(district, 0, k) + plan.Roads.GridLine(district, 0, k + 1)) * 0.5, (plan.Roads.GridLine(district, 1, j) + plan.Roads.GridLine(district, 1, j + 1)) * 0.5);
		double reach = RoadPlan.StreetSpacing + RoadPlan.StreetJitter + HighwayGap + RoadPlan.HighwayWidth;
		plan.Roads.HighwaySegments(centre.X - reach, centre.Z - reach, centre.X + reach, centre.Z + reach, doorHighway);
		plan.Viaducts.Near(centre.X, centre.Z, doorViaducts);
		doorLots.Clear();
		BlockLots(district, origin, u, v, gridSeed, k, j, doorHighway, doorViaducts, doorLots);
		for (int n = 0; n < earlier; n++)
		{
			if (had[n])
			{
				MarkDoor(district, k, j, Sides[n], hadAt[n], hadDeck[n], origin, u, v, doorLots, 0);
			}
		}
		return DoorLot(district, k, j, side, at, origin, u, v, doorLots, 0) >= 0;
	}

	// ---- stairs up to raised streets (01, the raised city)

	// whether a flight of stairs climbs from the ground to the raised street along side (+1 the +v edge, -1 the -v
	// edge, +2 and -2 the u edges, on raised cross streets) of block (k, j): the deck high all along the block (and
	// flat, on a raised street), the flight against its side over the sidewalk, clear of pillars, voids and
	// expressways, a share of those. cached, pure
	public bool Stair(int district, int k, int j, int side, out Stair stair)
	{
		(int, int, int, int) key = (district, k, j, side);
		if (stairs.TryGetValue(key, out (bool, Stair) known))
		{
			stair = known.Item2;
			return known.Item1;
		}
		if (stairs.Count > 8192)
		{
			stairs.Clear();
		}
		bool found = (Math.Abs(side) == 2) ? FindCrossStair(district, k, j, side, out stair) : FindStair(district, k, j, side, out stair);
		stairs[key] = (found, stair);
		return found;
	}

	private bool FindStair(int district, int k, int j, int side, out Stair stair)
	{
		stair = default(Stair);
		DistrictDef def = plan.Def.Districts[district];
		if (def.Roads != DistrictRoads.Streets || def.Raised <= 0.0)
		{
			return false;
		}
		plan.Roads.GridFrame(district, out XZ origin, out XZ u, out XZ v, out ulong gridSeed);
		ulong roll = WorldRandom.Derive(gridSeed, Gen2.StreamStair + (side > 0 ? 1uL : 0uL), Gen2.Pack(k, j));
		if (Gen2.Unit(roll) >= StairChance)
		{
			return false;
		}
		int line = (side > 0) ? j + 1 : j;
		double deck = plan.Roads.RaisedHeight(district, line, k);
		if (deck < DoorMinDeck || Math.Abs(plan.Roads.RaisedHeight(district, line, k + 0.5) - deck) > 1e-9 || Math.Abs(plan.Roads.RaisedHeight(district, line, k + 0.999) - deck) > 1e-9)
		{
			return false;
		}
		double inset = def.StreetWidth * 0.5 + def.Sidewalk;
		double length = StairLength(deck);
		double from = plan.Roads.GridLine(district, 0, k) + inset;
		double to = plan.Roads.GridLine(district, 0, k + 1) - inset - length;
		if (to <= from)
		{
			return false;
		}
		double u0 = from + (to - from) * Gen2.Unit(WorldRandom.Derive(roll, Gen2.StreamStair, 2uL));
		double climb = (Gen2.Unit(WorldRandom.Derive(roll, Gen2.StreamStair, 3uL)) < 0.5) ? 1.0 : -1.0;
		// the block's on the other side of the street from the line's far side: +v of line j, -v of line j + 1
		double away = (side > 0) ? -1.0 : 1.0;
		double mid = plan.Roads.GridLine(district, 1, line) + away * (def.StreetWidth * 0.5 + StairWidth * 0.5);
		stair.District = district;
		stair.Deck = deck;
		stair.Foot = At(origin, u, v, (climb > 0.0) ? u0 : u0 + length, mid);
		stair.Dir = new XZ(u.X * climb, u.Z * climb);
		stair.Out = new XZ(v.X * away, v.Z * away);
		stair.LandingAt = (climb > 0.0) ? u0 + length - TopLanding * 0.5 : u0 + TopLanding * 0.5;
		return StairClear(district, origin, u, v, u0, length, mid);
	}

	// a flight against a raised cross street: like a raised street's, but its deck may slope a little, so the flight
	// is as tall as the deck where the top landing meets it. none in a block with a raised street's flight, they'd
	// meet at the corner
	private bool FindCrossStair(int district, int k, int j, int side, out Stair stair)
	{
		stair = default(Stair);
		DistrictDef def = plan.Def.Districts[district];
		if (def.Roads != DistrictRoads.Streets || def.RaisedCross <= 0.0 || Stair(district, k, j, -1, out _) || Stair(district, k, j, 1, out _))
		{
			return false;
		}
		plan.Roads.GridFrame(district, out XZ origin, out XZ u, out XZ v, out ulong gridSeed);
		ulong roll = WorldRandom.Derive(gridSeed, Gen2.StreamCrossStair + (side > 0 ? 1uL : 0uL), Gen2.Pack(k, j));
		if (Gen2.Unit(roll) >= StairChance)
		{
			return false;
		}
		int line = (side > 0) ? k + 1 : k;
		double inset = def.StreetWidth * 0.5 + def.Sidewalk;
		double from = plan.Roads.GridLine(district, 1, j) + inset;
		double end = plan.Roads.GridLine(district, 1, j + 1) - inset;
		double low = Math.Min(plan.Roads.CrossHeight(district, line, j, from), plan.Roads.CrossHeight(district, line, j, end));
		double high = Math.Max(plan.Roads.CrossHeight(district, line, j, from), plan.Roads.CrossHeight(district, line, j, end));
		double to = end - StairLength(high);
		if (low < DoorMinDeck || to <= from)
		{
			return false;
		}
		double v0 = from + (to - from) * Gen2.Unit(WorldRandom.Derive(roll, Gen2.StreamCrossStair, 2uL));
		double climb = (Gen2.Unit(WorldRandom.Derive(roll, Gen2.StreamCrossStair, 3uL)) < 0.5) ? 1.0 : -1.0;
		// climbing towards +v the landing moves with the length, which moves with the deck there: a few rounds settle it
		double deck = high;
		double landing = 0.0;
		for (int n = 0; n < 3; n++)
		{
			landing = (climb > 0.0) ? v0 + StairLength(deck) - TopLanding * 0.5 : v0 + TopLanding * 0.5;
			deck = plan.Roads.CrossHeight(district, line, j, landing);
		}
		double length = StairLength(deck);
		// the block's on the other side of the street from the line's far side: +u of line k, -u of line k + 1
		double away = (side > 0) ? -1.0 : 1.0;
		double mid = plan.Roads.GridLine(district, 0, line) + away * (def.StreetWidth * 0.5 + StairWidth * 0.5);
		stair.District = district;
		stair.Deck = deck;
		stair.Foot = At(origin, u, v, mid, (climb > 0.0) ? v0 : v0 + length);
		stair.Dir = new XZ(v.X * climb, v.Z * climb);
		stair.Out = new XZ(u.X * away, u.Z * away);
		stair.LandingAt = (climb > 0.0) ? v0 + length - TopLanding * 0.5 : v0 + TopLanding * 0.5;
		return StairClear(district, origin, v, u, v0, length, mid);
	}

	// a flight from along to along + length (grid coordinates on axis), across from the street's line on the other
	// axis, clear of everything it can't stand in
	private bool StairClear(int district, XZ origin, XZ axis, XZ other, double along, double length, double across)
	{
		XZ middle = At(origin, axis, other, along + length * 0.5, across);
		plan.Viaducts.Near(middle.X, middle.Z, stairViaducts);
		for (double s = 0.0; s <= length + 0.01; s += 4.0)
		{
			XZ p = At(origin, axis, other, along + Math.Min(s, length), across);
			if (plan.DistrictAt(p.X, p.Z) != district || plan.Chasms.Blocks(p, StairWidth) || plan.Viaducts.Blocks(p, StairWidth, stairViaducts) || plan.Roads.HighwayNear(p, StairWidth + 4.0) || plan.PillarNear(p.X, p.Z).Position.DistanceTo(p) < RoadPlan.PillarClearance + StairWidth || plan.Origin.DistanceTo(p) < RoadPlan.StartClearance)
			{
				return false;
			}
		}
		return true;
	}

	// the building lot from first on that reaches the block's edge on that side with room for the entry at grid
	// coordinate at along it, and that no earlier side took. -1 if none
	private int DoorLot(int district, int k, int j, int side, double at, XZ origin, XZ u, XZ v, List<Lot> lots, int first)
	{
		DistrictDef def = plan.Def.Districts[district];
		double inset = def.StreetWidth * 0.5 + def.Sidewalk;
		bool cross = Math.Abs(side) == 2;
		int sign = Math.Sign(side);
		XZ along = cross ? v : u;
		XZ across = cross ? u : v;
		double edge = cross ? ((side > 0) ? plan.Roads.GridLine(district, 0, k + 1) - inset : plan.Roads.GridLine(district, 0, k) + inset) : ((side > 0) ? plan.Roads.GridLine(district, 1, j + 1) - inset : plan.Roads.GridLine(district, 1, j) + inset);
		for (int i = first; i < lots.Count; i++)
		{
			Lot lot = lots[i];
			if (lot.Kind != LotKind.Building || lot.Door != 0)
			{
				continue;
			}
			double halfAlong = cross ? lot.HalfV : lot.HalfU;
			double halfAcross = cross ? lot.HalfU : lot.HalfV;
			if (Math.Abs(Grid(origin, across, lot.Center) + sign * halfAcross - edge) < 0.01 && Math.Abs(at - Grid(origin, along, lot.Center)) <= halfAlong - PorchWidth * 0.5 - 0.5 && halfAcross * 2.0 >= PorchDepth + 8.0)
			{
				return i;
			}
		}
		return -1;
	}

	// p's grid coordinate along axis
	private static double Grid(XZ origin, XZ axis, XZ p)
	{
		return (p.X - origin.X) * axis.X + (p.Z - origin.Z) * axis.Z;
	}

	// halves the longer side at 35-65% until both fit MaxLot
	private void Split(int district, XZ origin, XZ u, XZ v, double cu, double cv, double hu, double hv, ulong seed, List<XZ> highway, List<Viaduct> viaducts, List<Lot> lots)
	{
		if (hu * 2.0 <= MaxLot && hv * 2.0 <= MaxLot)
		{
			AddLot(district, At(origin, u, v, cu, cv), u, hu, hv, seed, highway, viaducts, lots);
			return;
		}
		double at = 0.35 + 0.3 * Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 1uL));
		ulong first = WorldRandom.Derive(seed, Gen2.StreamLot, 2uL);
		ulong second = WorldRandom.Derive(seed, Gen2.StreamLot, 3uL);
		if (hu >= hv)
		{
			double a = hu * 2.0 * at * 0.5;
			double b = hu - a;
			Split(district, origin, u, v, cu - hu + a, cv, a, hv, first, highway, viaducts, lots);
			Split(district, origin, u, v, cu + hu - b, cv, b, hv, second, highway, viaducts, lots);
		}
		else
		{
			double a = hv * 2.0 * at * 0.5;
			double b = hv - a;
			Split(district, origin, u, v, cu, cv - hv + a, hu, a, first, highway, viaducts, lots);
			Split(district, origin, u, v, cu, cv + hv - b, hu, b, second, highway, viaducts, lots);
		}
	}

	private void AddLot(int district, XZ centre, XZ axis, double hu, double hv, ulong seed, List<XZ> highway, List<Viaduct> viaducts, List<Lot> lots)
	{
		double reach = Math.Sqrt(hu * hu + hv * hv);
		if (HighwayNear(centre, reach + HighwayGap, highway) || plan.Viaducts.Blocks(centre, reach, viaducts) || plan.Chasms.Blocks(centre, reach) || plan.Underground.Blocks(centre, reach))
		{
			return;
		}
		DistrictDef def = plan.Def.Districts[district];
		double total = def.LotBuilding + def.LotOpen + def.LotLandmark;
		double pick = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 4uL)) * total;
		LotKind kind = (pick < def.LotBuilding || total <= 0.0) ? LotKind.Building : (pick < def.LotBuilding + def.LotOpen) ? LotKind.Open : LotKind.Landmark;
		lots.Add(new Lot
		{
			Kind = kind,
			District = district,
			Center = centre,
			Axis = axis,
			HalfU = hu,
			HalfV = hv,
			Floors = Floors(def, seed),
			Base = (kind == LotKind.Building) ? Podium(def, seed, hu, hv) : 0.0,
			Seed = WorldRandom.Derive(seed, Gen2.StreamLot, 5uL)
		});
	}

	// 6-20 m, low enough that its stairs (1.5 m of run a metre up) fit along the lot's long side
	private static double Podium(DistrictDef def, ulong seed, double hu, double hv)
	{
		if (Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 12uL)) >= def.Podiums)
		{
			return 0.0;
		}
		double height = 6.0 + Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 13uL)) * 14.0;
		return Math.Floor(Math.Min(height, Math.Max(hu, hv) * 2.0 / 1.5));
	}

	private static int Floors(DistrictDef def, ulong seed)
	{
		int range = Math.Max(0, def.FloorsMax - def.FloorsMin) + 1;
		return def.FloorsMin + (int)(Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 6uL)) * range);
	}

	// ---- open districts

	private void CollectSites(Rect tile, List<Lot> lots)
	{
		int cx0 = Gen2.Cell(tile.X0, RoadPlan.SiteCell);
		int cx1 = Gen2.Cell(tile.X1, RoadPlan.SiteCell);
		int cz0 = Gen2.Cell(tile.Z0, RoadPlan.SiteCell);
		int cz1 = Gen2.Cell(tile.Z1, RoadPlan.SiteCell);
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				if (!plan.Roads.Site(cx, cz, out XZ site) || !tile.Contains(site))
				{
					continue;
				}
				int district = plan.DistrictAt(site.X, site.Z);
				DistrictDef def = plan.Def.Districts[district];
				ulong seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamLot + 1, Gen2.Pack(cx, cz));
				double pick = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 4uL));
				// the rest of the sites stay empty snow
				if (pick >= def.LotLandmark + def.LotOpen || HighwayNear(site, SiteLot * 1.42 + HighwayGap, highway) || plan.Viaducts.Blocks(site, SiteLot * 1.42, viaducts) || plan.Chasms.Blocks(site, SiteLot * 1.42))
				{
					continue;
				}
				double tilt = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 7uL)) * 2.0 - 1.0;
				double norm = Math.Sqrt(1.0 + tilt * tilt);
				lots.Add(new Lot
				{
					Kind = (pick < def.LotLandmark) ? LotKind.Landmark : LotKind.Open,
					District = district,
					Center = site,
					Axis = new XZ(1.0 / norm, tilt / norm),
					HalfU = SiteLot,
					HalfV = SiteLot,
					Floors = Floors(def, seed),
					Seed = WorldRandom.Derive(seed, Gen2.StreamLot, 5uL)
				});
			}
		}
	}

	// ---- shared

	private static XZ At(XZ origin, XZ u, XZ v, double gu, double gv)
	{
		return new XZ(origin.X + u.X * gu + v.X * gv, origin.Z + u.Z * gu + v.Z * gv);
	}

	// from p to the nearest point of the grid-space rectangle (centre cu, cv, half sizes hu, hv)
	private static double RectDistance(XZ origin, XZ u, XZ v, double cu, double cv, double hu, double hv, XZ p)
	{
		double x = p.X - origin.X;
		double z = p.Z - origin.Z;
		double du = Math.Max(0.0, Math.Abs(x * u.X + z * u.Z - cu) - hu);
		double dv = Math.Max(0.0, Math.Abs(x * v.X + z * v.Z - cv) - hv);
		return Math.Sqrt(du * du + dv * dv);
	}
}
