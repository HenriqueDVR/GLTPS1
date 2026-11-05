using System;

// layout-space position on a layer (x, z), doubles so far tiles stay exact
public struct XZ : IEquatable<XZ>
{
	public double X;

	public double Z;

	public XZ(double x, double z)
	{
		X = x;
		Z = z;
	}

	public double DistanceTo(XZ other)
	{
		double dx = X - other.X;
		double dz = Z - other.Z;
		return Math.Sqrt(dx * dx + dz * dz);
	}

	public bool Equals(XZ other)
	{
		return X == other.X && Z == other.Z;
	}

	public override bool Equals(object obj)
	{
		return obj is XZ other && Equals(other);
	}

	public override int GetHashCode()
	{
		return X.GetHashCode() * 31 + Z.GetHashCode();
	}

	public override string ToString()
	{
		return "(" + X + ", " + Z + ")";
	}
}

// world gen v2 constants and the shared hashing. everything here is part of tile output: until 1.0 it may change between
// releases, co-op peers must only agree
public static class Gen2
{
	public const int GenVersion = 2;

	public const double RegionSize = 1600.0;

	public const double TileSize = 128.0;

	// pillars stay this far inside their region cell
	public const double PillarMargin = 300.0;

	public const double WarpAmplitude = 300.0;

	public const double WarpWavelength = 2000.0;

	public const double BlendWidth = 100.0;

	// deck to deck, layer k's deck is k of these over layer 0's. the top of the level above as DistantPillar draws it
	// (the column's slab 546 m, +2, +80 lift, +54 to the deck's top). tuned in WP-WORLD-7
	public const double LayerHeight = 682.0;

	// rng streams, never renumber
	public const ulong StreamLayer = 0x4C41594552uL;

	public const ulong StreamPillarX = 0x50494C4C58uL;

	public const ulong StreamPillarZ = 0x50494C4C5AuL;

	public const ulong StreamUp = 0x5550uL;

	// which way the tower stands, same on every layer like its position
	public const ulong StreamPillarYaw = 0x50494C4C59uL;

	public const ulong StreamWarp = 0x57415250uL;

	public const ulong StreamTile = 0x54494C45uL;

	// roads (RoadPlan). the +1/+2/+3 next to each are taken too
	public const ulong StreamHighway = 0x48574159uL;

	public const ulong StreamElevated = 0x454C4556uL;

	public const ulong StreamStreets = 0x53545245uL;

	public const ulong StreamStreetLine = 0x4C494E45uL;

	public const ulong StreamStreetKeep = 0x4B454550uL;

	public const ulong StreamSite = 0x53495445uL;

	public const ulong StreamTrack = 0x5452434BuL;

	// lots (LotPlan)
	public const ulong StreamLot = 0x4C4F54uL;

	// arched viaducts (ViaductPlan)
	public const ulong StreamViaduct = 0x56494144uL;

	// rifts and shafts in the deck (ChasmPlan). +1 is the ragged edge, +2 the girders on the cut faces
	public const ulong StreamChasm = 0x4348534DuL;

	// raised streets (RoadPlan)
	public const ulong StreamRaised = 0x52414953uL;

	// doors from raised streets (LotPlan.Door). +1 is the other side, +2 the door's place
	public const ulong StreamDoor = 0x444F4F52uL;

	// stairs up to raised streets (LotPlan.Stair). +1 is the other side, +2 and +3 the place and the way up
	public const ulong StreamStair = 0x53544149uL;

	// the same two on raised cross streets, numbered alike
	public const ulong StreamCrossDoor = 0x58444F52uL;

	public const ulong StreamCrossStair = 0x58535441uL;

	// the underground (UndergroundPlan). +1 and +2 the galleries east and north
	public const ulong StreamUnder = 0x554E4452uL;

	// places (17): dealing, kinds, shapes
	public const ulong StreamPlace = 0x504C4143uL;

	// Math.Sin and Cos can differ in the last bits between runtimes. plain doubles, adding and multiplying, can't
	public static double Sin(double x)
	{
		x -= 2.0 * Math.PI * Math.Floor(x / (2.0 * Math.PI) + 0.5);
		if (x > Math.PI * 0.5)
		{
			x = Math.PI - x;
		}
		else if (x < -Math.PI * 0.5)
		{
			x = -Math.PI - x;
		}
		double y = x * x;
		double sum = 0.0;
		for (int n = 19; n >= 3; n -= 2)
		{
			sum = (1.0 - sum) * y / (n * (n - 1));
		}
		return x * (1.0 - sum);
	}

	public static double Cos(double x)
	{
		return Sin(x + Math.PI * 0.5);
	}

	// world items spawned by v2 tiles: bit 61, then (layer:8)(tileX:20)(tileZ:20)(index:12) as in 01-world-layers.
	// legacy spawner ids are small, scene pickups use bit 62, items made at runtime will use bit 62 with a peer (03).
	// index is where the lot is, not a count, so taking a lot away (a place, a set piece) moves no other lot's item
	public const ulong TileItemTag = 0x2000000000000000uL;

	// a place's containers and the like: bit 60, then the same layout with the 512 m cell for the tile (PlacePlan.Id)
	public const ulong PlaceTag = 0x1000000000000000uL;

	// (u:5)(v:5)(child:2): the lot's centre on an 8 m grid from 64 m before the tile's corner. lots are 14 m+ apart and
	// reach about half a block (56 m) out of the tile that owns them
	public static int ItemIndex(XZ lot, int tileX, int tileZ, int child)
	{
		int u = (int)Math.Floor((lot.X - tileX * TileSize + 64.0) / 8.0);
		int v = (int)Math.Floor((lot.Z - tileZ * TileSize + 64.0) / 8.0);
		return ((u & 31) << 7) | ((v & 31) << 2) | (child & 3);
	}

	public static ulong ItemId(int layer, int tileX, int tileZ, int index)
	{
		return TileItemTag | ((ulong)(layer & 0xFF) << 52) | ((ulong)((uint)tileX & 0xFFFFFu) << 32) | ((ulong)((uint)tileZ & 0xFFFFFu) << 12) | ((uint)index & 0xFFFu);
	}

	public static ulong Pack(int x, int z)
	{
		return ((ulong)(uint)x << 32) | (uint)z;
	}

	public static ulong LayerSeed(ulong worldSeed, int layer)
	{
		return WorldRandom.Derive(worldSeed, StreamLayer, (ulong)layer);
	}

	public static ulong TileSeed(ulong layerSeed, int tileX, int tileZ)
	{
		return WorldRandom.Derive(layerSeed, StreamTile, Pack(tileX, tileZ));
	}

	public static int Cell(double v, double size)
	{
		return (int)Math.Floor(v / size);
	}

	// [0, 1) from the top 53 bits
	public static double Unit(ulong v)
	{
		return (v >> 11) * (1.0 / 9007199254740992.0);
	}

	// value noise in [-1, 1], smooth, one lattice point per wavelength
	public static double Noise(ulong seed, ulong stream, double x, double z, double wavelength)
	{
		double fx = x / wavelength;
		double fz = z / wavelength;
		double flx = Math.Floor(fx);
		double flz = Math.Floor(fz);
		int ix = (int)flx;
		int iz = (int)flz;
		double tx = Smooth(fx - flx);
		double tz = Smooth(fz - flz);
		double a = Lattice(seed, stream, ix, iz);
		double b = Lattice(seed, stream, ix + 1, iz);
		double c = Lattice(seed, stream, ix, iz + 1);
		double d = Lattice(seed, stream, ix + 1, iz + 1);
		double top = a + (b - a) * tx;
		double bottom = c + (d - c) * tx;
		return top + (bottom - top) * tz;
	}

	private static double Lattice(ulong seed, ulong stream, int x, int z)
	{
		return Unit(WorldRandom.Derive(seed, stream, Pack(x, z))) * 2.0 - 1.0;
	}

	private static double Smooth(double t)
	{
		return t * t * (3.0 - 2.0 * t);
	}
}
