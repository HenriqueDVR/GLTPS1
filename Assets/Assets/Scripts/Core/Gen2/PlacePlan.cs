using System;
using System.Collections.Generic;

public enum PlaceKind
{
	FuelDepot,
	PumpHouse,
	TankHull,
	DarkRoom
}

// a stop with a verb (17 section 3). Axis is its first side's direction, the second runs along (-Axis.Z, Axis.X).
// Room* is what's built inside the lot's rectangle
public struct Place
{
	public PlaceKind Kind;

	public int CellX;

	public int CellZ;

	public int District;

	public XZ Center;

	public XZ Axis;

	public double RoomU;

	public double RoomV;

	public double Height;

	// which side the door's in, toward the nearest road: 0 +u, 1 -u, 2 +v, 3 -v
	public int Door;

	// over a lot of the plan (hollowed out), else on open ground
	public bool OnLot;

	public ulong Seed;

	public bool Shelter => Kind == PlaceKind.TankHull || Kind == PlaceKind.DarkRoom;

	// from the centre to the door's middle
	public XZ DoorAt
	{
		get
		{
			XZ v = new XZ(-Axis.Z, Axis.X);
			switch (Door)
			{
			case 0:
				return new XZ(Center.X + Axis.X * RoomU, Center.Z + Axis.Z * RoomU);
			case 1:
				return new XZ(Center.X - Axis.X * RoomU, Center.Z - Axis.Z * RoomU);
			case 2:
				return new XZ(Center.X + v.X * RoomV, Center.Z + v.Z * RoomV);
			default:
				return new XZ(Center.X - v.X * RoomV, Center.Z - v.Z * RoomV);
			}
		}
	}
}

// what one of a place's containers holds
public struct PlaceLoot
{
	public int Food;

	public bool Ammo;

	public bool Empty => Food == 0 && !Ammo;
}

// places of one layer (17 section 9): one per 512 m cell. each 3x3 cells deal out fuel, water and shelter first, then
// by the district, so coverage holds by construction with nobody choosing anything at dusk. an overlay on the lots: the
// lot nearest the cell's spot gets a hollow shell instead of its building, roads and lots don't change. pure, cached
public sealed class PlacePlan
{
	public const double Cell = RoadPlan.SiteCell;

	public const int RegionCells = 3;

	private const double margin = 64.0;

	private const double clearOfVoid = 26.0;

	private const double clearOfUnder = 18.0;

	// a lot this much of a door's distance from a road counts as on a street
	private const double streetGap = 12.0;

	private const double smallestLot = 6.0;

	private const double reachLot = 90.0;

	private const int tries = 5;

	private readonly LayerPlan plan;

	private readonly Dictionary<long, PlaceKind?> kinds = new Dictionary<long, PlaceKind?>();

	private readonly HashSet<long> dealt = new HashSet<long>();

	private readonly Dictionary<long, Place?> places = new Dictionary<long, Place?>();

	private readonly List<Lot> lots = new List<Lot>();

	private readonly List<RoadPiece> pieces = new List<RoadPiece>();

	private readonly List<RoadPiece> onePiece = new List<RoadPiece>();

	private readonly List<RoadPatch> patches = new List<RoadPatch>();

	public PlacePlan(LayerPlan plan)
	{
		this.plan = plan;
	}

	private static long Key(int x, int z)
	{
		return ((long)x << 32) ^ (uint)z;
	}

	private static int RegionOf(int cell)
	{
		return (int)Math.Floor((double)cell / RegionCells);
	}

	// the place of a cell, if it has one
	public bool At(int cellX, int cellZ, out Place place)
	{
		long key = Key(cellX, cellZ);
		if (!places.TryGetValue(key, out Place? known))
		{
			known = Build(cellX, cellZ);
			places[key] = known;
		}
		place = known ?? default(Place);
		return known.HasValue;
	}

	// the place standing in a tile
	public bool InTile(int tileX, int tileZ, out Place place)
	{
		double x = (tileX + 0.5) * Gen2.TileSize;
		double z = (tileZ + 0.5) * Gen2.TileSize;
		return At(Gen2.Cell(x, Cell), Gen2.Cell(z, Cell), out place) && Gen2.Cell(place.Center.X, Gen2.TileSize) == tileX && Gen2.Cell(place.Center.Z, Gen2.TileSize) == tileZ;
	}

	// is this lot the one a place hollowed out
	public bool OnLot(Lot lot, out Place place)
	{
		return At(Gen2.Cell(lot.Center.X, Cell), Gen2.Cell(lot.Center.Z, Cell), out place) && place.OnLot && Math.Abs(place.Center.X - lot.Center.X) < 0.01 && Math.Abs(place.Center.Z - lot.Center.Z) < 0.01;
	}

	// nothing else is put down here
	public bool Blocks(XZ p, double clear)
	{
		if (!At(Gen2.Cell(p.X, Cell), Gen2.Cell(p.Z, Cell), out Place place))
		{
			return false;
		}
		double dx = p.X - place.Center.X;
		double dz = p.Z - place.Center.Z;
		double u = dx * place.Axis.X + dz * place.Axis.Z;
		double v = -dx * place.Axis.Z + dz * place.Axis.X;
		return Math.Abs(u) < place.RoomU + clear && Math.Abs(v) < place.RoomV + clear;
	}

	// the nearest of a kind, for a hint at dusk. cells out to reach
	public bool Nearest(Func<Place, bool> wanted, XZ from, double reach, out Place found)
	{
		found = default(Place);
		double best = double.MaxValue;
		int cx0 = Gen2.Cell(from.X - reach, Cell);
		int cx1 = Gen2.Cell(from.X + reach, Cell);
		int cz0 = Gen2.Cell(from.Z - reach, Cell);
		int cz1 = Gen2.Cell(from.Z + reach, Cell);
		for (int x = cx0; x <= cx1; x++)
		{
			for (int z = cz0; z <= cz1; z++)
			{
				if (At(x, z, out Place place) && wanted(place))
				{
					double distance = place.Center.DistanceTo(from);
					if (distance < best)
					{
						best = distance;
						found = place;
					}
				}
			}
		}
		return best < double.MaxValue;
	}

	public static ulong Id(int layer, int cellX, int cellZ, int index)
	{
		return Gen2.PlaceTag | ((ulong)(layer & 0xFF) << 52) | ((ulong)((uint)cellX & 0xFFFFFu) << 32) | ((ulong)((uint)cellZ & 0xFFFFFu) << 12) | ((uint)index & 0xFFFu);
	}

	public static PlaceLoot Loot(Place place, int index)
	{
		ulong roll = WorldRandom.Derive(place.Seed, Gen2.StreamPlace, 100uL + (ulong)index);
		double pick = Gen2.Unit(roll);
		PlaceLoot loot = default(PlaceLoot);
		if (pick < TileOdds.ContainerFood)
		{
			loot.Food = 1 + (int)((roll >> 16) % 3uL);
		}
		else if (pick < TileOdds.ContainerFood + TileOdds.ContainerAmmo)
		{
			loot.Ammo = true;
		}
		return loot;
	}

	// ---- dealing

	private static double Between(ulong roll, double from, double to)
	{
		return from + Gen2.Unit(roll) * (to - from);
	}

	private bool Hostable(XZ p)
	{
		if (plan.Origin.DistanceTo(p) < RoadPlan.StartClearance + LotPlan.SiteLot * 1.42)
		{
			return false;
		}
		if (plan.Chasms.Inside(p.X, p.Z) > -clearOfVoid || plan.Underground.Blocks(p, clearOfUnder))
		{
			return false;
		}
		return !(plan.TowerNear(p.X, p.Z, out XZ tower) && tower.DistanceTo(p) < RoadPlan.PillarClearance + clearOfVoid);
	}

	// where in the cell, the road's site if it has one. false: all of it's void or start
	private bool Spot(int cellX, int cellZ, out XZ spot)
	{
		if (plan.Roads.Site(cellX, cellZ, out spot) && Hostable(spot))
		{
			return true;
		}
		ulong seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamPlace, Gen2.Pack(cellX, cellZ));
		for (int i = 0; i < tries * 2; i++)
		{
			spot = new XZ(Between(WorldRandom.Derive(seed, 1uL, (ulong)i), cellX * Cell + margin, (cellX + 1) * Cell - margin), Between(WorldRandom.Derive(seed, 2uL, (ulong)i), cellZ * Cell + margin, (cellZ + 1) * Cell - margin));
			if (Hostable(spot))
			{
				return true;
			}
		}
		return false;
	}

	private void Deal(int regionX, int regionZ)
	{
		long region = Key(regionX, regionZ);
		if (!dealt.Add(region))
		{
			return;
		}
		ulong seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamPlace + 1, Gen2.Pack(regionX, regionZ));
		List<KeyValuePair<ulong, long>> order = new List<KeyValuePair<ulong, long>>();
		for (int i = 0; i < RegionCells; i++)
		{
			for (int j = 0; j < RegionCells; j++)
			{
				int x = regionX * RegionCells + i;
				int z = regionZ * RegionCells + j;
				kinds[Key(x, z)] = null;
				if (Spot(x, z, out _))
				{
					order.Add(new KeyValuePair<ulong, long>(WorldRandom.Derive(seed, 1uL, (ulong)(i * RegionCells + j)), Key(x, z)));
				}
			}
		}
		order.Sort((a, b) => a.Key.CompareTo(b.Key));
		for (int n = 0; n < order.Count; n++)
		{
			kinds[order[n].Value] = Kind(n, order[n].Value);
		}
	}

	private PlaceKind Kind(int dealtBefore, long cell)
	{
		int cellX = (int)(cell >> 32);
		int cellZ = (int)(uint)cell;
		DistrictDef district = plan.Def.Districts[plan.DistrictAt((cellX + 0.5) * Cell, (cellZ + 0.5) * Cell)];
		double pick = Gen2.Unit(WorldRandom.Derive(plan.LayerSeed, Gen2.StreamPlace + 2, Gen2.Pack(cellX, cellZ)));
		switch (dealtBefore)
		{
		case 0:
			return PlaceKind.FuelDepot;
		case 1:
			return PlaceKind.PumpHouse;
		case 2:
			return (pick < district.Tanks) ? PlaceKind.TankHull : PlaceKind.DarkRoom;
		default:
			if (pick < TileOdds.ExtraSupply)
			{
				return (pick < TileOdds.ExtraSupply * 0.5) ? PlaceKind.FuelDepot : PlaceKind.PumpHouse;
			}
			pick = (pick - TileOdds.ExtraSupply) / (1.0 - TileOdds.ExtraSupply);
			return (pick < district.Tanks) ? PlaceKind.TankHull : PlaceKind.DarkRoom;
		}
	}

	// ---- shape

	private Place? Build(int cellX, int cellZ)
	{
		Deal(RegionOf(cellX), RegionOf(cellZ));
		PlaceKind? kind = kinds[Key(cellX, cellZ)];
		if (!kind.HasValue || !Spot(cellX, cellZ, out XZ spot))
		{
			return null;
		}
		Place place = default(Place);
		place.Kind = kind.Value;
		place.CellX = cellX;
		place.CellZ = cellZ;
		place.Seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamPlace + 3, Gen2.Pack(cellX, cellZ));
		int tileX = Gen2.Cell(spot.X, Gen2.TileSize);
		int tileZ = Gen2.Cell(spot.Z, Gen2.TileSize);
		RoadsAround(tileX, tileZ);
		bool found = NearestLot(spot, tileX, tileZ, cellX, cellZ, out Lot lot);
		double halfU;
		double halfV;
		if (found)
		{
			place.OnLot = true;
			place.Center = lot.Center;
			place.Axis = lot.Axis;
			place.District = lot.District;
			halfU = lot.HalfU;
			halfV = lot.HalfV;
		}
		else
		{
			if (!OpenGround(place.Seed, ref spot))
			{
				return null;
			}
			place.Center = spot;
			double tilt = Gen2.Unit(WorldRandom.Derive(place.Seed, Gen2.StreamPlace, 1uL)) * 2.0 - 1.0;
			double norm = Math.Sqrt(1.0 + tilt * tilt);
			place.Axis = new XZ(1.0 / norm, tilt / norm);
			place.District = plan.DistrictAt(spot.X, spot.Z);
			halfU = 12.0;
			halfV = 10.0;
		}
		Size(ref place, halfU, halfV);
		place.Door = DoorSide(place);
		return place;
	}

	private void RoadsAround(int tileX, int tileZ)
	{
		// its own tile's are enough in a town, and looking further was most of the cost of a place
		plan.Roads.Collect(tileX, tileZ, onePiece, patches);
		pieces.Clear();
		pieces.AddRange(onePiece);
		if (pieces.Count > 0)
		{
			return;
		}
		for (int x = tileX - 1; x <= tileX + 1; x++)
		{
			for (int z = tileZ - 1; z <= tileZ + 1; z++)
			{
				plan.Roads.Collect(x, z, onePiece, patches);
				pieces.AddRange(onePiece);
			}
		}
	}

	private double RoadDistance(XZ p)
	{
		double best = double.MaxValue;
		foreach (RoadPiece piece in pieces)
		{
			best = Math.Min(best, RoadPlan.SegmentDistance(p, piece.A, piece.B) - piece.Width * 0.5);
		}
		return best;
	}

	// the lot nearest the spot that's big enough, on the deck, its door to a street if any is
	private bool NearestLot(XZ spot, int tileX, int tileZ, int cellX, int cellZ, out Lot found)
	{
		plan.Lots.Collect(tileX, tileZ, lots);
		found = default(Lot);
		double best = double.MaxValue;
		foreach (Lot lot in lots)
		{
			if (Math.Min(lot.HalfU, lot.HalfV) < smallestLot || lot.Base > 0.0 || lot.Door != 0 || Gen2.Cell(lot.Center.X, Cell) != cellX || Gen2.Cell(lot.Center.Z, Cell) != cellZ)
			{
				continue;
			}
			double away = lot.Center.DistanceTo(spot);
			if (away > reachLot || !Hostable(lot.Center))
			{
				continue;
			}
			// on the block's edge beats nearer
			double gap = RoadDistance(lot.Center) - Math.Max(lot.HalfU, lot.HalfV);
			double score = away + ((gap > streetGap) ? 1000.0 : 0.0);
			if (score < best)
			{
				best = score;
				found = lot;
			}
		}
		return best < double.MaxValue;
	}

	// no lot to use: on the snow, off the road
	private bool OpenGround(ulong seed, ref XZ spot)
	{
		const double clear = 18.0;
		for (int i = 0; i < tries; i++)
		{
			if (RoadDistance(spot) > clear && Hostable(spot))
			{
				return true;
			}
			double angle = Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamPlace, 10uL + (ulong)i)) * Math.PI * 2.0;
			spot = new XZ(spot.X + Gen2.Cos(angle) * 30.0, spot.Z + Gen2.Sin(angle) * 30.0);
		}
		return RoadDistance(spot) > clear && Hostable(spot);
	}

	private static void Size(ref Place place, double halfU, double halfV)
	{
		switch (place.Kind)
		{
		case PlaceKind.TankHull:
			place.RoomU = 4.6;
			place.RoomV = 2.2;
			place.Height = 2.4;
			break;
		case PlaceKind.PumpHouse:
			place.RoomU = Math.Min(6.5, halfU - 1.0);
			place.RoomV = Math.Min(5.0, halfV - 1.0);
			place.Height = 4.0;
			break;
		case PlaceKind.FuelDepot:
			place.RoomU = Math.Min(7.0, halfU - 1.0);
			place.RoomV = Math.Min(5.0, halfV - 1.0);
			place.Height = 3.0;
			break;
		default:
			place.RoomU = Math.Min(10.0, halfU - 1.0);
			place.RoomV = Math.Min(7.0, halfV - 1.0);
			place.Height = 3.2;
			break;
		}
	}

	private int DoorSide(Place place)
	{
		XZ v = new XZ(-place.Axis.Z, place.Axis.X);
		XZ to = default(XZ);
		double best = double.MaxValue;
		foreach (RoadPiece piece in pieces)
		{
			double distance = RoadPlan.SegmentDistance(place.Center, piece.A, piece.B);
			if (distance < best)
			{
				best = distance;
				XZ mid = new XZ((piece.A.X + piece.B.X) * 0.5, (piece.A.Z + piece.B.Z) * 0.5);
				to = new XZ(mid.X - place.Center.X, mid.Z - place.Center.Z);
			}
		}
		// the hull's way in is at its ends
		bool endsOnly = place.Kind == PlaceKind.TankHull;
		if (best == double.MaxValue)
		{
			int side = (int)(WorldRandom.Derive(place.Seed, Gen2.StreamPlace, 2uL) % 4uL);
			return endsOnly ? (side & 1) : side;
		}
		double du = to.X * place.Axis.X + to.Z * place.Axis.Z;
		double dv = to.X * v.X + to.Z * v.Z;
		if (endsOnly || Math.Abs(du) >= Math.Abs(dv))
		{
			return (du >= 0.0) ? 0 : 1;
		}
		return (dv >= 0.0) ? 2 : 3;
	}
}
