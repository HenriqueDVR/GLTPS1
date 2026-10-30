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

	// the world side's own randomness for this lot (building shape, which landmark, props)
	public ulong Seed;
}

// lots of one layer (01-world-layers, lots). street districts: each block of the street grid, inset from the streets,
// split into lots. open districts: at most one lot per road site, most of the snow stays empty.
// pure like RoadPlan: a lot belongs to the tile holding its centre
public sealed class LotPlan
{
	// kept clear between a street's edge and the block
	public const double Sidewalk = 3.0;

	public const double MinLot = 14.0;

	// blocks are split until both sides are this or less
	public const double MaxLot = 40.0;

	// half the side of a site's lot
	public const double SiteLot = 32.0;

	// lots keep this much more than their own size from an expressway
	public const double HighwayGap = 6.0;

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

	public LotPlan(LayerPlan plan)
	{
		this.plan = plan;
	}

	public void Collect(int tileX, int tileZ, List<Lot> lots)
	{
		lots.Clear();
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
		CollectBlocks(tile, lots);
		CollectSites(tile, lots);
	}

	// an expressway's edge within distance of p
	private bool HighwayNear(XZ p, double distance)
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

	private void CollectBlocks(Rect tile, List<Lot> lots)
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
					Block(tile, district, origin, u, v, gridSeed, k, j, lots);
				}
			}
		}
	}

	// the block between grid lines k, k+1 and j, j+1, if its centre is in this tile and it all lies in the district
	private void Block(Rect tile, int district, XZ origin, XZ u, XZ v, ulong gridSeed, int k, int j, List<Lot> lots)
	{
		double bu0 = plan.Roads.GridLine(district, 0, k);
		double bu1 = plan.Roads.GridLine(district, 0, k + 1);
		double bv0 = plan.Roads.GridLine(district, 1, j);
		double bv1 = plan.Roads.GridLine(district, 1, j + 1);
		double cu = (bu0 + bu1) * 0.5;
		double cv = (bv0 + bv1) * 0.5;
		XZ centre = At(origin, u, v, cu, cv);
		if (!tile.Contains(centre))
		{
			return;
		}
		double inset = RoadPlan.StreetWidth * 0.5 + Sidewalk;
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
		Split(district, origin, u, v, cu, cv, hu, hv, WorldRandom.Derive(gridSeed, Gen2.StreamLot, Gen2.Pack(k, j)), lots);
	}

	// halves the longer side at 35-65% until both fit MaxLot
	private void Split(int district, XZ origin, XZ u, XZ v, double cu, double cv, double hu, double hv, ulong seed, List<Lot> lots)
	{
		if (hu * 2.0 <= MaxLot && hv * 2.0 <= MaxLot)
		{
			AddLot(district, At(origin, u, v, cu, cv), u, hu, hv, seed, lots);
			return;
		}
		double at = 0.35 + 0.3 * Gen2.Unit(WorldRandom.Derive(seed, Gen2.StreamLot, 1uL));
		ulong first = WorldRandom.Derive(seed, Gen2.StreamLot, 2uL);
		ulong second = WorldRandom.Derive(seed, Gen2.StreamLot, 3uL);
		if (hu >= hv)
		{
			double a = hu * 2.0 * at * 0.5;
			double b = hu - a;
			Split(district, origin, u, v, cu - hu + a, cv, a, hv, first, lots);
			Split(district, origin, u, v, cu + hu - b, cv, b, hv, second, lots);
		}
		else
		{
			double a = hv * 2.0 * at * 0.5;
			double b = hv - a;
			Split(district, origin, u, v, cu, cv - hv + a, hu, a, first, lots);
			Split(district, origin, u, v, cu, cv + hv - b, hu, b, second, lots);
		}
	}

	private void AddLot(int district, XZ centre, XZ axis, double hu, double hv, ulong seed, List<Lot> lots)
	{
		if (HighwayNear(centre, Math.Sqrt(hu * hu + hv * hv) + HighwayGap))
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
			Seed = WorldRandom.Derive(seed, Gen2.StreamLot, 5uL)
		});
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
				if (pick >= def.LotLandmark + def.LotOpen || HighwayNear(site, SiteLot * 1.42 + HighwayGap))
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
