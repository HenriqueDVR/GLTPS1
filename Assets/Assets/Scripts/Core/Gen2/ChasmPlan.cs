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
// lots keep out, roads cross on bridges. below is the long fall towards the layer under this one. over the first
// layer there are collapsed sections too (Collapse), open right through to the layer below.
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

	// collapsed sections, layers over the first: where the deck has fallen away right through, a few hundred metres
	// across, open to the layer below. about this share of the deck (the noise's level, measured)
	public const double CollapseLevel = 0.3;

	// metres per unit of the noise over its level: no more than one a metre anywhere (the octaves' steepest, smoothed),
	// so Inside never says further from an edge than it is, which the deck's and the lots' margins count on
	private const double collapseScale = 130.0;

	// and never this near a pillar: its tower stands in deck, a ride lands on it
	public const double CollapsePillarGap = 320.0;

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
				if (plan.HasTower(chasm.CellX + dx, chasm.CellZ + dz) && Distance(chasm, pillar) < PillarGap + reach)
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
		return Inside(x, z, Collapse(x, z));
	}

	// with the collapse there already worked out
	public double Inside(double x, double z, double collapse)
	{
		double best = Math.Max(Rifts(x, z), collapse);
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

	// how far p is inside a collapsed section, positive in it. a pure function of (world seed, layer, place), known
	// before the layer is planned (its districts come from where the ride lands), so the layer under it can draw the
	// same holes overhead (DistantPillar). the first layer has none, nothing's under it
	public static double Collapse(ulong worldSeed, int layer, double x, double z)
	{
		if (layer < 1)
		{
			return double.MinValue;
		}
		ulong seed = Gen2.LayerSeed(worldSeed, layer);
		double n = 0.6 * Gen2.Noise(seed, Gen2.StreamChasm + 3, x, z, 900.0) + 0.3 * Gen2.Noise(seed, Gen2.StreamChasm + 4, x, z, 330.0) + 0.1 * Gen2.Noise(seed, Gen2.StreamChasm + 5, x, z, 110.0);
		double inside = (n - CollapseLevel) * collapseScale;
		int cx = Gen2.Cell(x, Gen2.RegionSize);
		int cz = Gen2.Cell(z, Gen2.RegionSize);
		XZ p = new XZ(x, z);
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				inside = Math.Min(inside, Pillar(worldSeed, cx + dx, cz + dz).DistanceTo(p) - CollapsePillarGap);
			}
		}
		return inside;
	}

	// pillar positions as Collapse asks for them, nine a call. main thread only, like the plans
	private static readonly Dictionary<long, XZ> pillars = new Dictionary<long, XZ>();

	private static ulong pillarsSeed;

	private static XZ Pillar(ulong worldSeed, int cellX, int cellZ)
	{
		if (worldSeed != pillarsSeed || pillars.Count > 4096)
		{
			pillars.Clear();
			pillarsSeed = worldSeed;
		}
		long key = ((long)cellX << 32) ^ (uint)cellZ;
		if (!pillars.TryGetValue(key, out XZ at))
		{
			at = LayerPlan.PillarPosition(worldSeed, cellX, cellZ);
			pillars.Add(key, at);
		}
		return at;
	}

	// whether a collapse reaches within margin of the square x0..x0+size, z0..z0+size: a 5 x 5 look, no misses since
	// Collapse never changes faster than a metre a metre
	public bool CollapseNear(double x0, double z0, double size, double margin)
	{
		if (plan.Layer < 1)
		{
			return false;
		}
		double step = size / 4.0;
		double reach = step * 0.7072 + margin;
		for (int i = 0; i <= 4; i++)
		{
			for (int j = 0; j <= 4; j++)
			{
				if (Collapse(x0 + i * step, z0 + j * step) > -reach)
				{
					return true;
				}
			}
		}
		return false;
	}

	// Inside without the collapses: the rifts and shafts alone
	public double Rifts(double x, double z)
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
		return best;
	}

	public double Collapse(double x, double z)
	{
		return Collapse(plan.WorldSeed, plan.Layer, x, z);
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
		if (Collapse(p.X, p.Z) > -(radius + Ragged + LotGap))
		{
			return true;
		}
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
