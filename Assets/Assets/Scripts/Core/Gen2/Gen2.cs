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

// world gen v2 constants and the shared hashing. everything here is part of the genVersion 2 output:
// once a v2 release ships, changing any of it needs a genVersion bump
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

	// rng streams, never renumber
	public const ulong StreamLayer = 0x4C41594552uL;

	public const ulong StreamPillarX = 0x50494C4C58uL;

	public const ulong StreamPillarZ = 0x50494C4C5AuL;

	public const ulong StreamUp = 0x5550uL;

	public const ulong StreamWarp = 0x57415250uL;

	public const ulong StreamTile = 0x54494C45uL;

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
