using System;

[Flags]
public enum PillarState
{
	Solid = 0,
	// working elevator up, once the layer's story gate is open (the gate is runtime state, not here)
	Up = 1,
	// arrival point from below, every Up pillar of the layer below
	Down = 2
}

public struct PillarInfo
{
	public int CellX;

	public int CellZ;

	public XZ Position;

	public PillarState State;
}

public struct DistrictSample
{
	public int Primary;

	// the neighbouring band near a boundary, else same as Primary
	public int Secondary;

	// 0 = all Primary, up to 0.5 right on the boundary
	public float Blend;
}

// the coarse plan of one layer: district field and pillar grid.
// a pure function of (world seed, layer, origin, the layer below's plan), so co-op peers agree without sending anything
public sealed class LayerPlan
{
	public readonly ulong WorldSeed;

	public readonly int Layer;

	public readonly LayerDef Def;

	public readonly XZ Origin;

	public readonly ulong LayerSeed;

	// null on layer 0. its Up pillars are this layer's Down pillars
	public readonly LayerPlan Below;

	public readonly RoadPlan Roads;

	public readonly LotPlan Lots;

	public LayerPlan(ulong worldSeed, int layer, XZ origin, LayerPlan below)
	{
		if (layer > 0 && (below == null || below.Layer != layer - 1 || below.WorldSeed != worldSeed))
		{
			throw new ArgumentException("layer " + layer + " needs the plan of layer " + (layer - 1));
		}
		WorldSeed = worldSeed;
		Layer = layer;
		Def = Layers.Get(layer);
		Origin = origin;
		LayerSeed = Gen2.LayerSeed(worldSeed, layer);
		Below = (layer > 0) ? below : null;
		Roads = new RoadPlan(this);
		Lots = new LotPlan(this);
	}

	// at most WarpAmplitude either way, so district edges wobble instead of being rings
	public double Warp(double x, double z)
	{
		double n = 0.7 * Gen2.Noise(LayerSeed, Gen2.StreamWarp, x, z, Gen2.WarpWavelength) + 0.3 * Gen2.Noise(LayerSeed, Gen2.StreamWarp + 1, x, z, Gen2.WarpWavelength * 0.4);
		return n * Gen2.WarpAmplitude;
	}

	public double Progress(double x, double z)
	{
		return Math.Max(0.0, Origin.DistanceTo(new XZ(x, z)) + Warp(x, z));
	}

	public int DistrictAt(double x, double z)
	{
		return DistrictForProgress(Progress(x, z));
	}

	public int DistrictForProgress(double progress)
	{
		DistrictDef[] districts = Def.Districts;
		int result = 0;
		for (int i = 1; i < districts.Length; i++)
		{
			if (progress >= districts[i].From)
			{
				result = i;
			}
		}
		return result;
	}

	public DistrictSample SampleDistrict(double x, double z)
	{
		double progress = Progress(x, z);
		int primary = DistrictForProgress(progress);
		DistrictSample sample = new DistrictSample { Primary = primary, Secondary = primary };
		DistrictDef[] districts = Def.Districts;
		double half = Gen2.BlendWidth * 0.5;
		if (primary + 1 < districts.Length && districts[primary + 1].From - progress < half)
		{
			sample.Secondary = primary + 1;
			sample.Blend = (float)(0.5 - (districts[primary + 1].From - progress) / Gen2.BlendWidth);
		}
		else if (primary > 0 && progress - districts[primary].From < half)
		{
			sample.Secondary = primary - 1;
			sample.Blend = (float)(0.5 - (progress - districts[primary].From) / Gen2.BlendWidth);
		}
		return sample;
	}

	// same on every layer: the layer isn't in the hash, so pillars line up through the stack
	public static XZ PillarPosition(ulong worldSeed, int cellX, int cellZ)
	{
		ulong key = Gen2.Pack(cellX, cellZ);
		double span = Gen2.RegionSize - 2.0 * Gen2.PillarMargin;
		double x = cellX * Gen2.RegionSize + Gen2.PillarMargin + Gen2.Unit(WorldRandom.Derive(worldSeed, Gen2.StreamPillarX, key)) * span;
		double z = cellZ * Gen2.RegionSize + Gen2.PillarMargin + Gen2.Unit(WorldRandom.Derive(worldSeed, Gen2.StreamPillarZ, key)) * span;
		return new XZ(x, z);
	}

	// degrees, a quarter turn steps so the elevator faces along the grid
	public static double PillarYaw(ulong worldSeed, int cellX, int cellZ)
	{
		return (WorldRandom.Derive(worldSeed, Gen2.StreamPillarYaw, Gen2.Pack(cellX, cellZ)) % 4uL) * 90.0;
	}

	public bool IsUp(int cellX, int cellZ)
	{
		if (Def.IsLast)
		{
			return false;
		}
		XZ position = PillarPosition(WorldSeed, cellX, cellZ);
		if (Progress(position.X, position.Z) < Def.ExitMinDistance)
		{
			return false;
		}
		return Gen2.Unit(WorldRandom.Derive(LayerSeed, Gen2.StreamUp, Gen2.Pack(cellX, cellZ))) < Def.UpChance;
	}

	public PillarInfo Pillar(int cellX, int cellZ)
	{
		PillarState state = PillarState.Solid;
		if (IsUp(cellX, cellZ))
		{
			state |= PillarState.Up;
		}
		if (Below != null && Below.IsUp(cellX, cellZ))
		{
			state |= PillarState.Down;
		}
		return new PillarInfo
		{
			CellX = cellX,
			CellZ = cellZ,
			Position = PillarPosition(WorldSeed, cellX, cellZ),
			State = state
		};
	}

	public PillarInfo PillarNear(double x, double z)
	{
		return Pillar(Gen2.Cell(x, Gen2.RegionSize), Gen2.Cell(z, Gen2.RegionSize));
	}

	// co-op check: both peers hash the plan around a point and compare.
	// region cells within radius: pillars and their states, plus districts on a 400 m grid
	public ulong PlanHash(XZ center, int radius)
	{
		ulong hash = 14695981039346656037uL;
		hash = Mix(hash, Gen2.GenVersion);
		hash = Mix(hash, Layer);
		hash = Mix(hash, Centimetres(Origin.X));
		hash = Mix(hash, Centimetres(Origin.Z));
		int cx = Gen2.Cell(center.X, Gen2.RegionSize);
		int cz = Gen2.Cell(center.Z, Gen2.RegionSize);
		const int samples = 4;
		double step = Gen2.RegionSize / samples;
		for (int x = cx - radius; x <= cx + radius; x++)
		{
			for (int z = cz - radius; z <= cz + radius; z++)
			{
				PillarInfo pillar = Pillar(x, z);
				hash = Mix(hash, x);
				hash = Mix(hash, z);
				hash = Mix(hash, Centimetres(pillar.Position.X));
				hash = Mix(hash, Centimetres(pillar.Position.Z));
				hash = Mix(hash, (long)pillar.State);
				for (int i = 0; i < samples; i++)
				{
					for (int j = 0; j < samples; j++)
					{
						hash = Mix(hash, DistrictAt(x * Gen2.RegionSize + (i + 0.5) * step, z * Gen2.RegionSize + (j + 0.5) * step));
					}
				}
			}
		}
		return hash;
	}

	internal static long Centimetres(double v)
	{
		return (long)Math.Round(v * 100.0);
	}

	// fnv-1a over the 8 bytes
	internal static ulong Mix(ulong hash, long value)
	{
		ulong v = (ulong)value;
		for (int i = 0; i < 8; i++)
		{
			hash = (hash ^ ((v >> (i * 8)) & 0xFF)) * 1099511628211uL;
		}
		return hash;
	}
}
