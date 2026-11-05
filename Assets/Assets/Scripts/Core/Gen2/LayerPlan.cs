using System;
using System.Collections.Generic;

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

	// layer 0: which way the start tunnel's portal faces (unit; zero for none), so its dead end can go on down into
	// the underground (UndergroundPlan.Start). from the scene, the same on every peer
	public readonly XZ StartFacing;

	public readonly ulong LayerSeed;

	// null on layer 0. its Up pillars are this layer's Down pillars
	public readonly LayerPlan Below;

	public readonly RoadPlan Roads;

	public readonly LotPlan Lots;

	public readonly ViaductPlan Viaducts;

	public readonly ChasmPlan Chasms;

	public readonly UndergroundPlan Underground;

	public readonly PlacePlan Places;

	public LayerPlan(ulong worldSeed, int layer, XZ origin, LayerPlan below, XZ startFacing = default(XZ))
	{
		if (layer > 0 && (below == null || below.Layer != layer - 1 || below.WorldSeed != worldSeed))
		{
			throw new ArgumentException("layer " + layer + " needs the plan of layer " + (layer - 1));
		}
		WorldSeed = worldSeed;
		Layer = layer;
		Def = Layers.Get(layer);
		Origin = origin;
		StartFacing = startFacing;
		LayerSeed = Gen2.LayerSeed(worldSeed, layer);
		Below = (layer > 0) ? below : null;
		Roads = new RoadPlan(this);
		Viaducts = new ViaductPlan(this);
		Chasms = new ChasmPlan(this);
		Underground = new UndergroundPlan(this);
		Lots = new LotPlan(this);
		Places = new PlacePlan(this);
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
			if (progress >= districts[i].From * Layers.DistanceScale)
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
		double next = (primary + 1 < districts.Length) ? districts[primary + 1].From * Layers.DistanceScale : 0.0;
		double from = districts[primary].From * Layers.DistanceScale;
		if (primary + 1 < districts.Length && next - progress < half)
		{
			sample.Secondary = primary + 1;
			sample.Blend = (float)(0.5 - (next - progress) / Gen2.BlendWidth);
		}
		else if (primary > 0 && progress - from < half)
		{
			sample.Secondary = primary - 1;
			sample.Blend = (float)(0.5 - (progress - from) / Gen2.BlendWidth);
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
		if (Progress(position.X, position.Z) < Def.ExitMinDistance * Layers.DistanceScale)
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

	private readonly System.Collections.Generic.Dictionary<ulong, bool> towers = new System.Collections.Generic.Dictionary<ulong, bool>();

	// a tower stands in this cell on this layer only if its elevator works from here (up, or back down to where it
	// came from): every tower is somewhere to go (decided 2026-11-02). the rest of the grid is open city
	public bool HasTower(int cellX, int cellZ)
	{
		ulong key = Gen2.Pack(cellX, cellZ);
		if (!towers.TryGetValue(key, out bool has))
		{
			if (towers.Count > 4096)
			{
				towers.Clear();
			}
			has = IsUp(cellX, cellZ) || (Below != null && Below.IsUp(cellX, cellZ));
			towers.Add(key, has);
		}
		return has;
	}

	// the tower of p's own cell, if it has one. pillars sit PillarMargin inside their cell, so no other is that close
	public bool TowerNear(double x, double z, out XZ at)
	{
		int cellX = Gen2.Cell(x, Gen2.RegionSize);
		int cellZ = Gen2.Cell(z, Gen2.RegionSize);
		at = PillarPosition(WorldSeed, cellX, cellZ);
		return HasTower(cellX, cellZ);
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

	private const int hashReach = 2;

	private const int hashStep = 16;

	private readonly ulong[] hashTiles = new ulong[(hashReach * 2 + 1) * (hashReach * 2 + 1)];

	private int hashTilesDone;

	// the next of TilesHash's tiles, false when they're all done: a ride does them ahead, a few a second
	public bool TilesHashStep()
	{
		if (hashTilesDone == hashTiles.Length)
		{
			return false;
		}
		int side = hashReach * 2 + 1;
		int x = hashTilesDone / side - hashReach;
		int z = hashTilesDone % side - hashReach;
		hashTiles[hashTilesDone++] = TileHash(Gen2.Cell(Origin.X, Gen2.TileSize) + x * hashStep, Gen2.Cell(Origin.Z, Gen2.TileSize) + z * hashStep);
		return true;
	}

	// streets, lots and their items on a few tiles spread round the origin, so the far districts count too. the tiles
	// are the same as a build would see, which costs: read it when a peer needs it
	public ulong TilesHash()
	{
		while (TilesHashStep())
		{
		}
		ulong hash = TileOdds.Hash();
		foreach (ulong tile in hashTiles)
		{
			hash = Mix(hash, (long)tile);
		}
		return hash;
	}

	private readonly List<RoadPiece> hashPieces = new List<RoadPiece>();

	private readonly List<RoadPatch> hashPatches = new List<RoadPatch>();

	private readonly List<Lot> hashLots = new List<Lot>();

	private readonly List<Stair> hashStairs = new List<Stair>();

	// what the plan says about one tile: its road pieces, its lots, their stairs, and the items they hold
	public ulong TileHash(int x, int z)
	{
		ulong hash = 14695981039346656037uL;
		Roads.Collect(x, z, hashPieces, hashPatches);
		foreach (RoadPiece piece in hashPieces)
		{
			hash = Mix(hash, (long)piece.Kind);
			hash = Mix(hash, Centimetres(piece.A.X));
			hash = Mix(hash, Centimetres(piece.A.Z));
			hash = Mix(hash, Centimetres(piece.B.X));
			hash = Mix(hash, Centimetres(piece.B.Z));
			hash = Mix(hash, Centimetres(piece.HeightA));
			hash = Mix(hash, Centimetres(piece.HeightB));
			hash = Mix(hash, Centimetres(piece.Width));
			hash = Mix(hash, piece.Index);
		}
		foreach (RoadPatch patch in hashPatches)
		{
			hash = Mix(hash, (long)patch.Kind);
			hash = Mix(hash, Centimetres(patch.Center.X));
			hash = Mix(hash, Centimetres(patch.Center.Z));
			hash = Mix(hash, Centimetres(patch.Size));
		}
		if (Places.InTile(x, z, out Place place))
		{
			hash = Mix(hash, (long)place.Kind);
			hash = Mix(hash, Centimetres(place.Center.X));
			hash = Mix(hash, Centimetres(place.Center.Z));
			hash = Mix(hash, place.Door);
		}
		Lots.Collect(x, z, hashLots, hashStairs);
		foreach (Lot lot in hashLots)
		{
			hash = Mix(hash, (long)lot.Kind);
			hash = Mix(hash, lot.District);
			hash = Mix(hash, Centimetres(lot.Center.X));
			hash = Mix(hash, Centimetres(lot.Center.Z));
			hash = Mix(hash, Centimetres(lot.Axis.X * 1000.0));
			hash = Mix(hash, Centimetres(lot.HalfU));
			hash = Mix(hash, Centimetres(lot.HalfV));
			hash = Mix(hash, lot.Floors);
			hash = Mix(hash, Centimetres(lot.Base));
			hash = Mix(hash, lot.Door);
			hash = Mix(hash, (long)lot.Seed);
			if (LotPlan.HasItem(lot, out _))
			{
				hash = Mix(hash, (long)Gen2.ItemId(Layer, x, z, Gen2.ItemIndex(lot.Center, x, z, 0)));
			}
		}
		foreach (Stair stair in hashStairs)
		{
			hash = Mix(hash, Centimetres(stair.Foot.X));
			hash = Mix(hash, Centimetres(stair.Foot.Z));
			hash = Mix(hash, Centimetres(stair.Deck));
		}
		return hash;
	}

	public static long Centimetres(double v)
	{
		return (long)Math.Round(v * 100.0);
	}

	public static ulong Mix(ulong hash, string text)
	{
		foreach (char c in text)
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		return hash;
	}

	// fnv-1a over the 8 bytes
	public static ulong Mix(ulong hash, long value)
	{
		ulong v = (ulong)value;
		for (int i = 0; i < 8; i++)
		{
			hash = (hash ^ ((v >> (i * 8)) & 0xFF)) * 1099511628211uL;
		}
		return hash;
	}
}
