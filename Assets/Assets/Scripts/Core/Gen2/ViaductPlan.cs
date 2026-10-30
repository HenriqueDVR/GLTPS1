using System;
using System.Collections.Generic;

// one stretch of arched viaduct, layout coords. bays of Bay metres from A towards B, a pier at the start of each
public struct Viaduct
{
	public XZ A;

	public XZ B;

	// top of the deck over the ground
	public double Height;

	public int CellX;

	public int CellZ;

	public double Length => A.DistanceTo(B);

	// the ramps at the ends rise this much a metre at most, about what the ket climbs
	public const double Grade = 0.065;

	// the deck's height s metres from A: ramps down to the deck at both ends, eased like the roads'
	public double HeightAt(double s)
	{
		double ramp = Height / Grade;
		double u = Math.Min(s, Length - s) / ramp;
		if (u <= 0.0)
		{
			return 0.0;
		}
		if (u >= 1.0)
		{
			return Height;
		}
		double m = 1.0 / (1.0 - RoadPlan.RampEase);
		if (u < RoadPlan.RampEase)
		{
			return Height * m * u * u / (2.0 * RoadPlan.RampEase);
		}
		if (u <= 1.0 - RoadPlan.RampEase)
		{
			return Height * m * (u - RoadPlan.RampEase * 0.5);
		}
		double rest = 1.0 - u;
		return Height * (1.0 - m * rest * rest / (2.0 * RoadPlan.RampEase));
	}

	public XZ At(double s)
	{
		double t = s / Length;
		return new XZ(A.X + (B.X - A.X) * t, A.Z + (B.Z - A.Z) * t);
	}
}

// world gen v2: arched viaducts, aqueduct-like, running high over and between the blocks (13, look of the source),
// ramps at both ends down to the deck so the ket can drive them.
// at most one per region cell, a district's share of cells (DistrictDef.Viaducts), kept clear of the pillars and
// the start. lots leave their corridor free; roads pass under the arches. a pure function of (layer seed, cell)
public sealed class ViaductPlan
{
	public const double Width = 8.0;

	// narrow bays on slim piers, so the openings stand tall like the source's, not squat like a roman aqueduct
	public const double Bay = 13.0;

	public const double PierWidth = 2.4;

	// longer than a region cell, but under two so the 3x3 round a point still finds every one
	public const double MinLength = 1000.0;

	public const double MaxLength = 2600.0;

	public const double MinHeight = 16.0;

	public const double MaxHeight = 30.0;

	// from the corridor's edge to a lot
	public const double LotGap = 3.0;

	// a viaduct passing nearer a pillar than this is left out
	public const double PillarGap = 140.0;

	private readonly LayerPlan plan;

	public ViaductPlan(LayerPlan plan)
	{
		this.plan = plan;
	}

	public bool At(int cellX, int cellZ, out Viaduct viaduct)
	{
		viaduct = default(Viaduct);
		ulong key = Gen2.Pack(cellX, cellZ);
		ulong seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamViaduct, key);
		WorldRandom random = new WorldRandom(seed);
		double span = Gen2.RegionSize - 2.0 * Gen2.PillarMargin;
		XZ centre = new XZ(cellX * Gen2.RegionSize + Gen2.PillarMargin + Gen2.Unit(random.NextULong()) * span, cellZ * Gen2.RegionSize + Gen2.PillarMargin + Gen2.Unit(random.NextULong()) * span);
		double roll = Gen2.Unit(random.NextULong());
		if (roll >= plan.Def.Districts[plan.DistrictAt(centre.X, centre.Z)].Viaducts)
		{
			return false;
		}
		double angle = Gen2.Unit(random.NextULong()) * Math.PI;
		double half = (MinLength + Gen2.Unit(random.NextULong()) * (MaxLength - MinLength)) * 0.5;
		// whole bays, so every peer cuts the same piers
		half = Math.Floor(half / Bay) * Bay;
		XZ dir = new XZ(Math.Cos(angle), Math.Sin(angle));
		viaduct.A = new XZ(centre.X - dir.X * half, centre.Z - dir.Z * half);
		viaduct.B = new XZ(centre.X + dir.X * half, centre.Z + dir.Z * half);
		viaduct.Height = MinHeight + Gen2.Unit(random.NextULong()) * (MaxHeight - MinHeight);
		viaduct.CellX = cellX;
		viaduct.CellZ = cellZ;
		if (RoadPlan.SegmentDistance(plan.Origin, viaduct.A, viaduct.B) < RoadPlan.StartClearance + Width)
		{
			return false;
		}
		// its ramps come down on solid deck
		if (plan.Chasms.Inside(viaduct.A.X, viaduct.A.Z) > -20.0 || plan.Chasms.Inside(viaduct.B.X, viaduct.B.Z) > -20.0)
		{
			return false;
		}
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				XZ pillar = LayerPlan.PillarPosition(plan.WorldSeed, cellX + dx, cellZ + dz);
				if (RoadPlan.SegmentDistance(pillar, viaduct.A, viaduct.B) < PillarGap)
				{
					return false;
				}
			}
		}
		return true;
	}

	// every viaduct that could reach within reach of p (they're shorter than a cell, so the 3x3 round it)
	public void Near(double x, double z, List<Viaduct> into)
	{
		into.Clear();
		int cx = Gen2.Cell(x, Gen2.RegionSize);
		int cz = Gen2.Cell(z, Gen2.RegionSize);
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dz = -1; dz <= 1; dz++)
			{
				if (At(cx + dx, cz + dz, out Viaduct viaduct))
				{
					into.Add(viaduct);
				}
			}
		}
	}

	// whether something round p with this radius is in a viaduct's corridor
	public bool Blocks(XZ p, double radius, List<Viaduct> near)
	{
		foreach (Viaduct viaduct in near)
		{
			if (RoadPlan.SegmentDistance(p, viaduct.A, viaduct.B) < radius + Width * 0.5 + LotGap)
			{
				return true;
			}
		}
		return false;
	}
}
