using System;
using System.Collections.Generic;

// an axis-aligned rectangle, layout coords
public struct Area
{
	public double X0;

	public double Z0;

	public double X1;

	public double Z1;

	public bool Contains(double x, double z)
	{
		return x >= X0 && x < X1 && z >= Z0 && z < Z1;
	}

	// from p to the rectangle, 0 inside
	public double Distance(double x, double z)
	{
		double dx = Math.Max(0.0, Math.Max(X0 - x, x - X1));
		double dz = Math.Max(0.0, Math.Max(Z0 - z, z - Z1));
		return Math.Sqrt(dx * dx + dz * dz);
	}
}

// a hall of the underground, layout coords, its sides along x and z, the floor UndergroundPlan.Depth under the deck.
// where a gallery comes in on a side, the gallery's middle line (z on an x side, x on a z side), NaN where none.
// maybe a shaft beside it with a ramp from the deck spiralling down its walls
public struct Hall
{
	public int CellX;

	public int CellZ;

	public XZ Center;

	public double HalfX;

	public double HalfZ;

	public double GalleryPX;

	public double GalleryNX;

	public double GalleryPZ;

	public double GalleryNZ;

	public bool Shaft;

	public XZ ShaftCenter;

	// unit, from the hall to the shaft (along x or z). the shaft's own frame: q along Out, p along Left(Out)
	public XZ Out;

	public ulong Seed;

	public XZ Along => new XZ(-Out.Z, Out.X);

	// a point in the shaft's frame, (-Shaft/2, -Shaft/2) the corner by the hall where the ramp starts at the deck
	// and ends on the floor
	public XZ ShaftPoint(double p, double q)
	{
		return new XZ(ShaftCenter.X - Out.Z * p + Out.X * q, ShaftCenter.Z + Out.X * p + Out.Z * q);
	}

	public Area Inner => new Area { X0 = Center.X - HalfX, Z0 = Center.Z - HalfZ, X1 = Center.X + HalfX, Z1 = Center.Z + HalfZ };
}

// a stretch of gallery (a ket-wide tunnel between two halls), along x or z, layout coords
public struct GalleryPiece
{
	public XZ A;

	public XZ B;
}

// world gen v2: the underground (01, the raised city and the underground; episode 1's dark industrial interior).
// one level UndergroundPlan.Depth under the deck: halls on a coarse grid of cells, a district's share of cells
// (DistrictDef.Underground), joined to the next cell's hall by straight galleries. the way in for now: shafts beside
// some halls in open districts, a ramp spiralling down their walls from the deck to the floor, the ket fits.
// halls keep clear of voids, pillars and the start; shafts of roads, lots and viaducts too.
// a pure function of (layer seed, cell), cached
public sealed class UndergroundPlan
{
	public const double Cell = 256.0;

	// the floor this far under the deck, the halls' ceiling under a slab this thick
	public const double Depth = 25.0;

	public const double Slab = 4.0;

	public const double Wall = 1.0;

	public const double MinHalf = 16.0;

	public const double MaxHalf = 60.0;

	// from a hall to its cell's edge
	public const double CellMargin = 10.0;

	public const double GalleryWidth = 10.0;

	public const double GalleryHeight = 7.0;

	public const double GalleryChance = 0.8;

	// the shaft: its inside, the ramp's width along the walls, laps of the walls the ramp goes down in (four flights
	// a lap, a flat landing in each corner). 2 laps of 56 m: 320 m of ramp, 7.8%
	public const double Shaft = 56.0;

	public const double Ramp = 8.0;

	public const int Laps = 2;

	public const double ShaftChance = 0.5;

	// kept from voids, pillars, the start; a shaft from roads and lots
	public const double VoidGap = 16.0;

	public const double PillarGap = 120.0;

	public const double RoadGap = 12.0;

	private readonly LayerPlan plan;

	private readonly Dictionary<long, Hall> halls = new Dictionary<long, Hall>();

	private readonly Dictionary<long, bool> have = new Dictionary<long, bool>();

	public UndergroundPlan(LayerPlan plan)
	{
		this.plan = plan;
	}

	public static int Flights => Laps * 4;

	public static double FlightLength => Shaft - 2.0 * Ramp;

	// how far each flight goes down
	public static double Drop => Depth / Flights;

	// the hall in this cell, if there is one, with its galleries and shaft
	public bool HallAt(int cellX, int cellZ, out Hall hall)
	{
		long key = ((long)cellX << 32) ^ (uint)cellZ;
		if (have.TryGetValue(key, out bool known))
		{
			hall = known ? halls[key] : default(Hall);
			return known;
		}
		if (have.Count > 4096)
		{
			have.Clear();
			halls.Clear();
		}
		bool found = Room(cellX, cellZ, out hall);
		if (found)
		{
			hall.GalleryPX = Gallery(cellX, cellZ, false, out GalleryPiece east) ? east.A.Z : double.NaN;
			hall.GalleryNX = Gallery(cellX - 1, cellZ, false, out GalleryPiece west) ? west.A.Z : double.NaN;
			hall.GalleryPZ = Gallery(cellX, cellZ, true, out GalleryPiece north) ? north.A.X : double.NaN;
			hall.GalleryNZ = Gallery(cellX, cellZ - 1, true, out GalleryPiece south) ? south.A.X : double.NaN;
			ShaftFor(ref hall);
			halls[key] = hall;
		}
		else
		{
			hall = default(Hall);
		}
		have[key] = found;
		return found;
	}

	// the room alone, no galleries or shaft yet
	private bool Room(int cellX, int cellZ, out Hall hall)
	{
		hall = default(Hall);
		ulong seed = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamUnder, Gen2.Pack(cellX, cellZ));
		WorldRandom random = new WorldRandom(seed);
		double roll = Gen2.Unit(random.NextULong());
		hall.CellX = cellX;
		hall.CellZ = cellZ;
		hall.Seed = seed;
		hall.HalfX = MinHalf + Gen2.Unit(random.NextULong()) * (MaxHalf - MinHalf);
		hall.HalfZ = MinHalf + Gen2.Unit(random.NextULong()) * (MaxHalf - MinHalf);
		double roomX = Cell - 2.0 * (CellMargin + Wall + hall.HalfX);
		double roomZ = Cell - 2.0 * (CellMargin + Wall + hall.HalfZ);
		hall.Center = new XZ(cellX * Cell + CellMargin + Wall + hall.HalfX + Gen2.Unit(random.NextULong()) * roomX, cellZ * Cell + CellMargin + Wall + hall.HalfZ + Gen2.Unit(random.NextULong()) * roomZ);
		if (roll >= plan.Def.Districts[plan.DistrictAt(hall.Center.X, hall.Center.Z)].Underground)
		{
			return false;
		}
		Area inner = hall.Inner;
		return Clear(inner, VoidGap + Wall);
	}

	// the area and a margin round it clear of voids, pillars and the start
	private bool Clear(Area area, double margin)
	{
		XZ middle = new XZ((area.X0 + area.X1) * 0.5, (area.Z0 + area.Z1) * 0.5);
		XZ pillar = plan.PillarNear(middle.X, middle.Z).Position;
		if (area.Distance(pillar.X, pillar.Z) < PillarGap + margin || area.Distance(plan.Origin.X, plan.Origin.Z) < RoadPlan.StartClearance + margin)
		{
			return false;
		}
		for (double x = area.X0 - margin; x <= area.X1 + margin + 0.01; x += Math.Min(16.0, (area.X1 - area.X0 + 2.0 * margin) * 0.5))
		{
			for (double z = area.Z0 - margin; z <= area.Z1 + margin + 0.01; z += Math.Min(16.0, (area.Z1 - area.Z0 + 2.0 * margin) * 0.5))
			{
				if (plan.Chasms.Inside(x, z) > -margin * 0.5)
				{
					return false;
				}
			}
		}
		return true;
	}

	// the gallery from the hall in this cell to the one east of it (north if asked): along the middle of where the two
	// face each other, from one's wall to the other's. a share of the pairs, clear of everything
	public bool Gallery(int cellX, int cellZ, bool north, out GalleryPiece gallery)
	{
		(int, int, bool) key = (cellX, cellZ, north);
		if (galleries.TryGetValue(key, out (bool, GalleryPiece) known))
		{
			gallery = known.Item2;
			return known.Item1;
		}
		if (galleries.Count > 8192)
		{
			galleries.Clear();
		}
		bool found = FindGallery(cellX, cellZ, north, out gallery);
		galleries[key] = (found, gallery);
		return found;
	}

	private readonly Dictionary<(int, int, bool), (bool, GalleryPiece)> galleries = new Dictionary<(int, int, bool), (bool, GalleryPiece)>();

	private bool FindGallery(int cellX, int cellZ, bool north, out GalleryPiece gallery)
	{
		gallery = default(GalleryPiece);
		if (!Room(cellX, cellZ, out Hall a) || !Room(north ? cellX : cellX + 1, north ? cellZ + 1 : cellZ, out Hall b))
		{
			return false;
		}
		ulong roll = WorldRandom.Derive(plan.LayerSeed, Gen2.StreamUnder + (north ? 2uL : 1uL), Gen2.Pack(cellX, cellZ));
		if (Gen2.Unit(roll) >= GalleryChance)
		{
			return false;
		}
		double half = GalleryWidth * 0.5 + Wall + 2.0;
		Area area;
		if (north)
		{
			double x0 = Math.Max(a.Center.X - a.HalfX, b.Center.X - b.HalfX) + half;
			double x1 = Math.Min(a.Center.X + a.HalfX, b.Center.X + b.HalfX) - half;
			if (x1 < x0)
			{
				return false;
			}
			double x = (x0 + x1) * 0.5;
			gallery.A = new XZ(x, a.Center.Z + a.HalfZ + Wall);
			gallery.B = new XZ(x, b.Center.Z - b.HalfZ - Wall);
			area = new Area { X0 = x - GalleryWidth * 0.5, Z0 = gallery.A.Z, X1 = x + GalleryWidth * 0.5, Z1 = gallery.B.Z };
		}
		else
		{
			double z0 = Math.Max(a.Center.Z - a.HalfZ, b.Center.Z - b.HalfZ) + half;
			double z1 = Math.Min(a.Center.Z + a.HalfZ, b.Center.Z + b.HalfZ) - half;
			if (z1 < z0)
			{
				return false;
			}
			double z = (z0 + z1) * 0.5;
			gallery.A = new XZ(a.Center.X + a.HalfX + Wall, z);
			gallery.B = new XZ(b.Center.X - b.HalfX - Wall, z);
			area = new Area { X0 = gallery.A.X, Z0 = z - GalleryWidth * 0.5, X1 = gallery.B.X, Z1 = z + GalleryWidth * 0.5 };
		}
		return Clear(area, VoidGap + Wall);
	}

	// a shaft on a side of the hall with no gallery, long enough, in the hall's own cell, where nothing on the deck
	// is in the way of its hole: an open district, no road, site, viaduct or void near
	private void ShaftFor(ref Hall hall)
	{
		WorldRandom random = new WorldRandom(WorldRandom.Derive(hall.Seed, Gen2.StreamUnder, 1uL));
		if (Gen2.Unit(random.NextULong()) >= ShaftChance)
		{
			return;
		}
		int first = random.Next(4);
		for (int n = 0; n < 4; n++)
		{
			int side = (first + n) % 4;
			XZ out_ = (side == 0) ? new XZ(1.0, 0.0) : (side == 1) ? new XZ(-1.0, 0.0) : (side == 2) ? new XZ(0.0, 1.0) : new XZ(0.0, -1.0);
			double gallery = (side == 0) ? hall.GalleryPX : (side == 1) ? hall.GalleryNX : (side == 2) ? hall.GalleryPZ : hall.GalleryNZ;
			double halfAlong = (side < 2) ? hall.HalfZ : hall.HalfX;
			double halfOut = (side < 2) ? hall.HalfX : hall.HalfZ;
			if (!double.IsNaN(gallery) || halfAlong < Shaft * 0.5 + Wall)
			{
				continue;
			}
			// slid along the side so it stays within the hall's
			double room = halfAlong - Shaft * 0.5 - Wall;
			double slide = (Gen2.Unit(random.NextULong()) * 2.0 - 1.0) * room;
			XZ along = new XZ(-out_.Z, out_.X);
			double reach = halfOut + Wall + Shaft * 0.5;
			hall.ShaftCenter = new XZ(hall.Center.X + out_.X * reach + along.X * slide, hall.Center.Z + out_.Z * reach + along.Z * slide);
			hall.Out = out_;
			Area outer = ShaftArea(hall, Wall);
			Area cell = new Area { X0 = hall.CellX * Cell + CellMargin, Z0 = hall.CellZ * Cell + CellMargin, X1 = (hall.CellX + 1) * Cell - CellMargin, Z1 = (hall.CellZ + 1) * Cell - CellMargin };
			if (outer.X0 < cell.X0 || outer.Z0 < cell.Z0 || outer.X1 > cell.X1 || outer.Z1 > cell.Z1 || !Open(outer))
			{
				continue;
			}
			hall.Shaft = true;
			return;
		}
	}

	// the shaft's inside, grown by margin
	public static Area ShaftArea(Hall hall, double margin)
	{
		double half = Shaft * 0.5 + margin;
		return new Area { X0 = hall.ShaftCenter.X - half, Z0 = hall.ShaftCenter.Z - half, X1 = hall.ShaftCenter.X + half, Z1 = hall.ShaftCenter.Z + half };
	}

	// nothing on the deck round a shaft's hole: all of it in open districts, far from voids (the tiles round it are
	// plain deck), from expressways, buried roads, sites' lots, viaducts, pillars and the start
	private bool Open(Area area)
	{
		XZ middle = new XZ((area.X0 + area.X1) * 0.5, (area.Z0 + area.Z1) * 0.5);
		double radius = Shaft * 0.75 + Wall;
		for (int c = 0; c < 4; c++)
		{
			double x = ((c & 1) == 0) ? area.X0 : area.X1;
			double z = ((c & 2) == 0) ? area.Z0 : area.Z1;
			if (plan.Def.Districts[plan.DistrictAt(x, z)].Roads != DistrictRoads.Open)
			{
				return false;
			}
		}
		if (plan.Chasms.Inside(middle.X, middle.Z) > -(Gen2.TileSize * 2.0 + Shaft) || plan.Roads.HighwayNear(middle, radius + RoadGap) || area.Distance(plan.Origin.X, plan.Origin.Z) < RoadPlan.StartClearance + RoadGap)
		{
			return false;
		}
		XZ pillar = plan.PillarNear(middle.X, middle.Z).Position;
		if (area.Distance(pillar.X, pillar.Z) < PillarGap)
		{
			return false;
		}
		plan.Viaducts.Near(middle.X, middle.Z, viaducts);
		if (plan.Viaducts.Blocks(middle, radius, viaducts))
		{
			return false;
		}
		int sx = Gen2.Cell(middle.X, RoadPlan.SiteCell);
		int sz = Gen2.Cell(middle.Z, RoadPlan.SiteCell);
		for (int cx = sx - 2; cx <= sx + 1; cx++)
		{
			for (int cz = sz - 2; cz <= sz + 1; cz++)
			{
				if (plan.Roads.Site(cx, cz, out XZ site) && area.Distance(site.X, site.Z) < LotPlan.SiteLot * 1.42 + RoadGap)
				{
					return false;
				}
				for (int d = 0; d < 2; d++)
				{
					if (plan.Roads.Track(cx, cz, d == 1, out XZ p0, out XZ c1, out XZ c2, out XZ p1) && CurveNear(area, p0, c1, c2, p1, RoadPlan.TrackWidth * 0.5 + RoadGap))
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	private readonly List<Viaduct> viaducts = new List<Viaduct>();

	// a bezier passing within distance of the area, sampled every few metres (buried roads bend round pillars too,
	// which these samples don't see, but those bends are far from any shaft: shafts keep PillarGap)
	private static bool CurveNear(Area area, XZ p0, XZ c1, XZ c2, XZ p1, double distance)
	{
		int steps = Math.Max(8, (int)(p0.DistanceTo(p1) / 4.0));
		for (int i = 0; i <= steps; i++)
		{
			double t = (double)i / steps;
			double s = 1.0 - t;
			double x = s * s * s * p0.X + 3.0 * s * s * t * c1.X + 3.0 * s * t * t * c2.X + t * t * t * p1.X;
			double z = s * s * s * p0.Z + 3.0 * s * s * t * c1.Z + 3.0 * s * t * t * c2.Z + t * t * t * p1.Z;
			if (area.Distance(x, z) < distance)
			{
				return true;
			}
		}
		return false;
	}

	// ---- for tiles

	// the halls whose middle is in the tile (with their shafts), and the gallery pieces (at most PieceLength long)
	// whose middle is
	public void Collect(int tileX, int tileZ, List<Hall> hallsOut, List<GalleryPiece> pieces)
	{
		hallsOut.Clear();
		pieces.Clear();
		double x0 = tileX * Gen2.TileSize;
		double z0 = tileZ * Gen2.TileSize;
		Area tile = new Area { X0 = x0, Z0 = z0, X1 = x0 + Gen2.TileSize, Z1 = z0 + Gen2.TileSize };
		int cx0 = Gen2.Cell(x0, Cell) - 1;
		int cx1 = Gen2.Cell(tile.X1, Cell) + 1;
		int cz0 = Gen2.Cell(z0, Cell) - 1;
		int cz1 = Gen2.Cell(tile.Z1, Cell) + 1;
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				if (HallAt(cx, cz, out Hall hall) && tile.Contains(hall.Center.X, hall.Center.Z))
				{
					hallsOut.Add(hall);
				}
				for (int d = 0; d < 2; d++)
				{
					if (!Gallery(cx, cz, d == 1, out GalleryPiece gallery))
					{
						continue;
					}
					double length = gallery.A.DistanceTo(gallery.B);
					int n = Math.Max(1, (int)Math.Ceiling(length / RoadPlan.PieceLength));
					for (int i = 0; i < n; i++)
					{
						XZ a = Lerp(gallery.A, gallery.B, (double)i / n);
						XZ b = Lerp(gallery.A, gallery.B, (double)(i + 1) / n);
						if (tile.Contains((a.X + b.X) * 0.5, (a.Z + b.Z) * 0.5))
						{
							pieces.Add(new GalleryPiece { A = a, B = b });
						}
					}
				}
			}
		}
	}

	// the holes in the deck over shafts that reach into the tile: each shaft's inside but the corner the ramp starts
	// from, which stays deck
	public void Holes(int tileX, int tileZ, List<Area> holes)
	{
		holes.Clear();
		double x0 = tileX * Gen2.TileSize;
		double z0 = tileZ * Gen2.TileSize;
		int cx0 = Gen2.Cell(x0 - Cell, Cell);
		int cx1 = Gen2.Cell(x0 + Gen2.TileSize + Cell, Cell);
		int cz0 = Gen2.Cell(z0 - Cell, Cell);
		int cz1 = Gen2.Cell(z0 + Gen2.TileSize + Cell, Cell);
		for (int cx = cx0; cx <= cx1; cx++)
		{
			for (int cz = cz0; cz <= cz1; cz++)
			{
				if (!HallAt(cx, cz, out Hall hall) || !hall.Shaft)
				{
					continue;
				}
				double h = Shaft * 0.5;
				AddHole(holes, hall.ShaftPoint(-h + Ramp, -h), hall.ShaftPoint(h, h), x0, z0);
				AddHole(holes, hall.ShaftPoint(-h, -h + Ramp), hall.ShaftPoint(-h + Ramp, h), x0, z0);
			}
		}
	}

	private static void AddHole(List<Area> holes, XZ a, XZ b, double x0, double z0)
	{
		Area area = new Area { X0 = Math.Max(Math.Min(a.X, b.X), x0), Z0 = Math.Max(Math.Min(a.Z, b.Z), z0), X1 = Math.Min(Math.Max(a.X, b.X), x0 + Gen2.TileSize), Z1 = Math.Min(Math.Max(a.Z, b.Z), z0 + Gen2.TileSize) };
		if (area.X1 > area.X0 && area.Z1 > area.Z0)
		{
			holes.Add(area);
		}
	}

	// whether something round p with this radius would stand in a shaft's hole or on its walls
	public bool Blocks(XZ p, double radius)
	{
		int cx = Gen2.Cell(p.X, Cell);
		int cz = Gen2.Cell(p.Z, Cell);
		for (int x = cx - 1; x <= cx + 1; x++)
		{
			for (int z = cz - 1; z <= cz + 1; z++)
			{
				if (HallAt(x, z, out Hall hall) && hall.Shaft && ShaftArea(hall, Wall).Distance(p.X, p.Z) < radius + 2.0)
				{
					return true;
				}
			}
		}
		return false;
	}

	// whether (x, z) is over the underground's floor: in a hall, a gallery or a shaft
	public bool Under(double x, double z)
	{
		int cx = Gen2.Cell(x, Cell);
		int cz = Gen2.Cell(z, Cell);
		if (HallAt(cx, cz, out Hall hall) && (hall.Inner.Contains(x, z) || (hall.Shaft && ShaftArea(hall, 0.0).Contains(x, z))))
		{
			return true;
		}
		for (int dx = -1; dx <= 0; dx++)
		{
			for (int dz = -1; dz <= 0; dz++)
			{
				for (int d = 0; d < 2; d++)
				{
					if (Gallery(cx + dx, cz + dz, d == 1, out GalleryPiece g) && GalleryArea(g).Contains(x, z))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static Area GalleryArea(GalleryPiece g)
	{
		double w = GalleryWidth * 0.5;
		return (Math.Abs(g.A.X - g.B.X) < 1e-9) ? new Area { X0 = g.A.X - w, Z0 = Math.Min(g.A.Z, g.B.Z), X1 = g.A.X + w, Z1 = Math.Max(g.A.Z, g.B.Z) } : new Area { X0 = Math.Min(g.A.X, g.B.X), Z0 = g.A.Z - w, X1 = Math.Max(g.A.X, g.B.X), Z1 = g.A.Z + w };
	}

	// halls and shafts round centre, for the co-op check
	public ulong Hash(XZ center, int radius)
	{
		ulong hash = 14695981039346656037uL;
		int cx = Gen2.Cell(center.X, Cell);
		int cz = Gen2.Cell(center.Z, Cell);
		for (int x = cx - radius; x <= cx + radius; x++)
		{
			for (int z = cz - radius; z <= cz + radius; z++)
			{
				if (HallAt(x, z, out Hall hall))
				{
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(hall.Center.X * 1000.0 + hall.Center.Z));
					hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(hall.HalfX * 1000.0 + hall.HalfZ));
					hash = LayerPlan.Mix(hash, hall.Shaft ? LayerPlan.Centimetres(hall.ShaftCenter.X * 1000.0 + hall.ShaftCenter.Z) : 0);
				}
			}
		}
		return hash;
	}

	private static XZ Lerp(XZ a, XZ b, double t)
	{
		return new XZ(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
	}
}
