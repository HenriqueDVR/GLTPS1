using System;
using System.Collections.Generic;

// a void in the deck, layout coords. a rift: a bent run A-M-B, Half wide either side. a shaft: a turned rectangle
public struct Chasm
{
	public bool Shaft;

	public XZ A;

	public XZ M;

	public XZ B;

	public double Half;

	public XZ Center;

	// unit, the shaft's first side
	public XZ Axis;

	public double HalfU;

	public double HalfV;

	public int CellX;

	public int CellZ;

	public ulong Seed;
}

// world gen v2: where the deck is broken open (the source's gaps and voids, the chasm of episode 3). per region cell
// maybe a rift (a long crack, 24-60 m across) and up to two shafts (square holes), a district's share of each
// (DistrictDef.Rifts / Shafts). edges are ragged. kept clear of the pillars, the start and the expressway junctions;
// lots keep out, roads cross on bridges. below is the long fall towards the layer under this one.
// a pure function of (layer seed, cell); cells are cached since every ground cell asks
public sealed class ChasmPlan
{
	public const double MinRift = 600.0;

	public const double MaxRift = 1500.0;

	public const double MinHalf = 12.0;

	public const double MaxHalf = 30.0;

	public const double MinShaft = 18.0;

	public const double MaxShaft = 45.0;

	// the edge wanders this far in and out
	public const double Ragged = 4.0;

	public const double PillarGap = 150.0;

	public const double JunctionGap = 70.0;

	// from a void's edge to a lot
	public const double LotGap = 4.0;

	private const int shaftsPerCell = 2;

	private readonly LayerPlan plan;

	private readonly Dictionary<long, Chasm[]> cache = new Dictionary<long, Chasm[]>();

	private readonly List<Chasm> found = new List<Chasm>();

	public ChasmPlan(LayerPlan plan)
	{
		this.plan = plan;
	}

	// the voids whose middle is in this region cell
	public Chasm[] In(int cellX, int cellZ)
	{
		long key = ((long)cellX << 32) ^ (uint)cellZ;
		if (cache.TryGetValue(key, out Chasm[] chasms))
		{
			return chasms;
		}
		if (cache.Count > 4096)
		{
			cache.Clear();
		}
		found.Clear();
		ulong seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamChasm, Gen2.Pack(cellX, cellZ));
		WorldRandom random = new WorldRandom(seed);
		double span = Gen2.RegionSize - 2.0 * Gen2.PillarMargin;
		// a rift
		XZ centre = new XZ(cellX * Gen2.RegionSize + Gen2.PillarMargin + Unit(random) * span, cellZ * Gen2.RegionSize + Gen2.PillarMargin + Unit(random) * span);
		double roll = Unit(random);
		double angle = Unit(random) * Math.PI;
		double length = MinRift + Unit(random) * (MaxRift - MinRift);
		double bend = (Unit(random) * 2.0 - 1.0) * 0.5;
		double half = MinHalf + Unit(random) * (MaxHalf - MinHalf);
		if (roll < Def(centre).Rifts)
		{
			XZ dir = new XZ(Math.Cos(angle), Math.Sin(angle));
			XZ side = new XZ(-dir.Z, dir.X);
			Chasm rift = new Chasm
			{
				A = new XZ(centre.X - dir.X * length * 0.5, centre.Z - dir.Z * length * 0.5),
				M = new XZ(centre.X + side.X * bend * length * 0.25, centre.Z + side.Z * bend * length * 0.25),
				B = new XZ(centre.X + dir.X * length * 0.5, centre.Z + dir.Z * length * 0.5),
				Half = half,
				Center = centre,
				Axis = dir,
				HalfU = length * 0.5,
				HalfV = half,
				CellX = cellX,
				CellZ = cellZ,
				Seed = WorldRandom.Derive(seed, Gen2.StreamChasm, 1uL)
			};
			if (Clear(rift))
			{
				found.Add(rift);
			}
		}
		for (int i = 0; i < shaftsPerCell; i++)
		{
			XZ at = new XZ(cellX * Gen2.RegionSize + Gen2.PillarMargin + Unit(random) * span, cellZ * Gen2.RegionSize + Gen2.PillarMargin + Unit(random) * span);
			double shaftRoll = Unit(random);
			double turn = Unit(random) * Math.PI;
			double hu = MinShaft + Unit(random) * (MaxShaft - MinShaft);
			double hv = MinShaft + Unit(random) * (MaxShaft - MinShaft);
			if (shaftRoll >= Def(at).Shafts)
			{
				continue;
			}
			Chasm shaft = new Chasm
			{
				Shaft = true,
				A = at,
				M = at,
				B = at,
				Half = Math.Sqrt(hu * hu + hv * hv),
				Center = at,
				Axis = new XZ(Math.Cos(turn), Math.Sin(turn)),
				HalfU = hu,
				HalfV = hv,
				CellX = cellX,
				CellZ = cellZ,
				Seed = WorldRandom.Derive(seed, Gen2.StreamChasm, 2uL + (ulong)i)
			};
			if (Clear(shaft))
			{
				found.Add(shaft);
			}
		}
		chasms = found.ToArray();
		cache[key] = chasms;
		return chasms;
	}

	private DistrictDef Def(XZ p)
	{
		return plan.Def.Districts[plan.DistrictAt(p.X, p.Z)];
	}

	private static double Unit(WorldRandom random)
	{
		return Gen2.Unit(random.NextULong());
	}

	private bool Clear(Chasm chasm)
	{
		double reach = chasm.Shaft ? chasm.Half : chasm.Half + Ragged;
		if (Distance(chasm, plan.Origin) < RoadPlan.StartClearance + reach)
		{
			return false;
		}
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				XZ pillar = LayerPlan.PillarPosition(plan.WorldSeed, chasm.CellX + dx, chasm.CellZ + dz);
				if (Distance(chasm, pillar) < PillarGap + reach)
				{
					return false;
				}
			}
		}
		int hx = Gen2.Cell(chasm.Center.X, RoadPlan.HighwayCell);
		int hz = Gen2.Cell(chasm.Center.Z, RoadPlan.HighwayCell);
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				if (plan.Roads.HasAnyHighway(hx + dx, hz + dz) && Distance(chasm, plan.Roads.Junction(hx + dx, hz + dz)) < JunctionGap + reach)
				{
					return false;
				}
			}
		}
		return true;
	}

	// from p to the chasm's middle line (a rift) or middle (a shaft)
	private static double Distance(Chasm chasm, XZ p)
	{
		return chasm.Shaft ? chasm.Center.DistanceTo(p) : Math.Min(RoadPlan.SegmentDistance(p, chasm.A, chasm.M), RoadPlan.SegmentDistance(p, chasm.M, chasm.B));
	}

	// how far p is inside a void, ragged edges and all: positive in it, negative on solid deck
	public double Inside(double x, double z)
	{
		int cx = Gen2.Cell(x, Gen2.RegionSize);
		int cz = Gen2.Cell(z, Gen2.RegionSize);
		double best = double.MinValue;
		XZ p = new XZ(x, z);
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				foreach (Chasm chasm in In(cx + dx, cz + dz))
				{
					best = Math.Max(best, Inside(chasm, p));
				}
			}
		}
		if (best == double.MinValue)
		{
			return best;
		}
		return best + Gen2.Noise(plan.LayerSeed, Gen2.StreamChasm + 1, x, z, 20.0) * Ragged;
	}

	public bool Void(double x, double z)
	{
		return Inside(x, z) > 0.0;
	}

	private static double Inside(Chasm chasm, XZ p)
	{
		if (!chasm.Shaft)
		{
			return chasm.Half - Distance(chasm, p);
		}
		double dx = p.X - chasm.Center.X;
		double dz = p.Z - chasm.Center.Z;
		double u = dx * chasm.Axis.X + dz * chasm.Axis.Z;
		double v = -dx * chasm.Axis.Z + dz * chasm.Axis.X;
		return Math.Min(chasm.HalfU - Math.Abs(u), chasm.HalfV - Math.Abs(v));
	}

	// whether something round p with this radius reaches a void (ragged edge included)
	public bool Blocks(XZ p, double radius)
	{
		int cx = Gen2.Cell(p.X, Gen2.RegionSize);
		int cz = Gen2.Cell(p.Z, Gen2.RegionSize);
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				foreach (Chasm chasm in In(cx + dx, cz + dz))
				{
					if (Inside(chasm, p) > -(radius + Ragged + LotGap))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	// co-op check, folded into the world hash with the plan's and the roads': the voids of the region cells round
	// center, and the viaducts
	public ulong Hash(XZ center, int radius)
	{
		ulong hash = 14695981039346656037uL;
		int cx = Gen2.Cell(center.X, Gen2.RegionSize);
		int cz = Gen2.Cell(center.Z, Gen2.RegionSize);
		for (int x = cx - radius; x <= cx + radius; x++)
		{
			for (int z = cz - radius; z <= cz + radius; z++)
			{
				foreach (Chasm chasm in In(x, z))
				{
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(chasm.A.X + chasm.M.Z));
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(chasm.B.X * 1000.0 + chasm.Half));
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(chasm.HalfU * 1000.0 + chasm.HalfV));
				}
				hash = LayerPlan.Mix(hash, plan.Viaducts.At(x, z, out Viaduct viaduct) ? LayerPlan.Centimetres(viaduct.A.X + viaduct.B.Z + viaduct.Height) : 0);
				hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(Math.Max(-1e6, Inside(x * Gen2.RegionSize + 800.0, z * Gen2.RegionSize + 800.0))));
			}
		}
		return hash;
	}

	// every void near p, for building
	public void Near(double x, double z, List<Chasm> into)
	{
		into.Clear();
		int cx = Gen2.Cell(x, Gen2.RegionSize);
		int cz = Gen2.Cell(z, Gen2.RegionSize);
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				into.AddRange(In(cx + dx, cz + dz));
			}
		}
	}
}
