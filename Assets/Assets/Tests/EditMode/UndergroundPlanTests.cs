using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

// world gen v2 underground: halls in their cells clear of voids, pillars and the start, galleries between them,
// shafts with their ramp down where nothing on the deck is in the way
public class UndergroundPlanTests
{
	private const ulong seed = 12345uL;

	private static LayerPlan Layer0()
	{
		return new LayerPlan(seed, 0, new XZ(0.0, 0.0), null);
	}

	private static List<Hall> All(LayerPlan plan, int range)
	{
		List<Hall> all = new List<Hall>();
		for (int x = -range; x <= range; x++)
		{
			for (int z = -range; z <= range; z++)
			{
				if (plan.Underground.HallAt(x, z, out Hall hall))
				{
					all.Add(hall);
				}
			}
		}
		return all;
	}

	[Test]
	public void SameInAnyOrder()
	{
		LayerPlan a = Layer0();
		LayerPlan b = Layer0();
		for (int x = 8; x >= -8; x--)
		{
			for (int z = -8; z <= 8; z++)
			{
				b.Underground.HallAt(z, x, out _);
			}
		}
		for (int x = -8; x <= 8; x++)
		{
			for (int z = -8; z <= 8; z++)
			{
				bool ha = a.Underground.HallAt(x, z, out Hall one);
				bool hb = b.Underground.HallAt(x, z, out Hall two);
				Assert.AreEqual(ha, hb);
				Assert.AreEqual(one.Center.X, two.Center.X);
				Assert.AreEqual(one.Shaft, two.Shaft);
				Assert.AreEqual(one.ShaftCenter.Z, two.ShaftCenter.Z);
			}
		}
	}

	[Test]
	public void HallsInTheirCellsAndClear()
	{
		LayerPlan plan = Layer0();
		List<Hall> all = All(plan, 12);
		Assert.Greater(all.Count, 40);
		foreach (Hall hall in all)
		{
			Area inner = hall.Inner;
			Assert.GreaterOrEqual(inner.X0, hall.CellX * UndergroundPlan.Cell + UndergroundPlan.CellMargin);
			Assert.LessOrEqual(inner.X1, (hall.CellX + 1) * UndergroundPlan.Cell - UndergroundPlan.CellMargin);
			Assert.GreaterOrEqual(inner.Z0, hall.CellZ * UndergroundPlan.Cell + UndergroundPlan.CellMargin);
			Assert.LessOrEqual(inner.Z1, (hall.CellZ + 1) * UndergroundPlan.Cell - UndergroundPlan.CellMargin);
			Assert.GreaterOrEqual(inner.Distance(plan.Origin.X, plan.Origin.Z), RoadPlan.StartClearance);
			XZ pillar = plan.PillarNear(hall.Center.X, hall.Center.Z).Position;
			Assert.GreaterOrEqual(inner.Distance(pillar.X, pillar.Z), UndergroundPlan.PillarGap);
			for (double x = inner.X0; x <= inner.X1; x += 4.0)
			{
				for (double z = inner.Z0; z <= inner.Z1; z += 4.0)
				{
					Assert.IsFalse(plan.Chasms.Void(x, z), "hall under a void at " + x + "," + z);
					Assert.IsTrue(plan.Underground.Under(x, z));
				}
			}
		}
	}

	[Test]
	public void GalleriesJoinWalls()
	{
		LayerPlan plan = Layer0();
		int count = 0;
		for (int x = -10; x <= 10; x++)
		{
			for (int z = -10; z <= 10; z++)
			{
				for (int d = 0; d < 2; d++)
				{
					bool north = d == 1;
					if (!plan.Underground.Gallery(x, z, north, out GalleryPiece g))
					{
						continue;
					}
					count++;
					Assert.IsTrue(plan.Underground.HallAt(x, z, out Hall a));
					Assert.IsTrue(plan.Underground.HallAt(north ? x : x + 1, north ? z + 1 : z, out Hall b));
					double half = UndergroundPlan.GalleryWidth * 0.5;
					if (north)
					{
						Assert.AreEqual(a.Center.Z + a.HalfZ + UndergroundPlan.Wall, g.A.Z, 1e-9);
						Assert.AreEqual(b.Center.Z - b.HalfZ - UndergroundPlan.Wall, g.B.Z, 1e-9);
						Assert.LessOrEqual(g.A.X + half, System.Math.Min(a.Center.X + a.HalfX, b.Center.X + b.HalfX));
						Assert.GreaterOrEqual(g.A.X - half, System.Math.Max(a.Center.X - a.HalfX, b.Center.X - b.HalfX));
						Assert.AreEqual(a.GalleryPZ, g.A.X);
						Assert.AreEqual(b.GalleryNZ, g.A.X);
						Assert.IsFalse(a.Shaft && a.Out.Z > 0.5);
						Assert.IsFalse(b.Shaft && b.Out.Z < -0.5);
					}
					else
					{
						Assert.AreEqual(a.Center.X + a.HalfX + UndergroundPlan.Wall, g.A.X, 1e-9);
						Assert.AreEqual(b.Center.X - b.HalfX - UndergroundPlan.Wall, g.B.X, 1e-9);
						Assert.AreEqual(a.GalleryPX, g.A.Z);
						Assert.AreEqual(b.GalleryNX, g.A.Z);
						Assert.IsFalse(a.Shaft && a.Out.X > 0.5);
						Assert.IsFalse(b.Shaft && b.Out.X < -0.5);
					}
					XZ middle = new XZ((g.A.X + g.B.X) * 0.5, (g.A.Z + g.B.Z) * 0.5);
					Assert.IsTrue(plan.Underground.Under(middle.X, middle.Z));
				}
			}
		}
		Assert.Greater(count, 20);
	}

	// a shaft's hole: nothing on the deck reaches into it, no road, lot or void, and the ramp's grade suits the ket
	[Test]
	public void ShaftsWhereTheDeckIsClear()
	{
		LayerPlan plan = Layer0();
		Assert.Less(UndergroundPlan.Depth / (UndergroundPlan.Flights * UndergroundPlan.FlightLength), 0.094);
		Assert.Greater(UndergroundPlan.Drop * 4.0 - 0.6, 3.0, "room under the lap above");
		List<Lot> lots = new List<Lot>();
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		List<Area> holes = new List<Area>();
		int shafts = 0;
		foreach (Hall hall in All(plan, 10))
		{
			if (!hall.Shaft)
			{
				continue;
			}
			shafts++;
			Area area = UndergroundPlan.ShaftArea(hall, UndergroundPlan.Wall);
			Assert.Less(plan.Chasms.Inside(hall.ShaftCenter.X, hall.ShaftCenter.Z), -Gen2.TileSize);
			int tx0 = Gen2.Cell(area.X0, Gen2.TileSize) - 1;
			int tx1 = Gen2.Cell(area.X1, Gen2.TileSize) + 1;
			int tz0 = Gen2.Cell(area.Z0, Gen2.TileSize) - 1;
			int tz1 = Gen2.Cell(area.Z1, Gen2.TileSize) + 1;
			double hole = 0.0;
			for (int tx = tx0; tx <= tx1; tx++)
			{
				for (int tz = tz0; tz <= tz1; tz++)
				{
					plan.Lots.Collect(tx, tz, lots);
					foreach (Lot lot in lots)
					{
						Assert.Greater(area.Distance(lot.Center.X, lot.Center.Z), System.Math.Sqrt(lot.HalfU * lot.HalfU + lot.HalfV * lot.HalfV), "lot in a shaft");
					}
					plan.Roads.Collect(tx, tz, pieces, patches);
					foreach (RoadPiece piece in pieces)
					{
						for (double t = 0.0; t <= 1.0; t += 0.25)
						{
							double x = piece.A.X + (piece.B.X - piece.A.X) * t;
							double z = piece.A.Z + (piece.B.Z - piece.A.Z) * t;
							Assert.Greater(area.Distance(x, z), piece.Width * 0.5, "road in a shaft");
						}
					}
					plan.Underground.Holes(tx, tz, holes);
					foreach (Area h in holes)
					{
						hole += (h.X1 - h.X0) * (h.Z1 - h.Z0);
					}
				}
			}
			// every tile cuts its part of the hole once: the shaft but its start corner (other shafts may share tiles)
			double expected = UndergroundPlan.Shaft * UndergroundPlan.Shaft - UndergroundPlan.Ramp * UndergroundPlan.Ramp;
			Assert.GreaterOrEqual(hole, expected - 1e-6);
			// the start corner is deck, the far corner is open
			double h2 = UndergroundPlan.Shaft * 0.5;
			Assert.IsTrue(plan.Underground.Under(hall.ShaftPoint(h2 - 1.0, h2 - 1.0).X, hall.ShaftPoint(h2 - 1.0, h2 - 1.0).Z));
		}
		Assert.Greater(shafts, 2);
	}

	[Test]
	public void NothingUnderTheStart()
	{
		LayerPlan plan = Layer0();
		Assert.IsFalse(plan.Underground.Under(plan.Origin.X, plan.Origin.Z));
		Assert.IsFalse(plan.Underground.Blocks(plan.Origin, RoadPlan.StartClearance * 0.5));
	}

	// frozen genVersion 2 output. a failure here means v2 saves or mixed-version co-op would break: bump genVersion instead
	[Test]
	public void GoldenValues()
	{
		StringBuilder text = new StringBuilder();
		foreach (Hall hall in All(Layer0(), 8))
		{
			text.Append(hall.CellX).Append(',').Append(hall.CellZ).Append(' ').Append(F(hall.Center.X)).Append(',').Append(F(hall.Center.Z)).Append(' ').Append(F(hall.HalfX)).Append(' ').Append(F(hall.HalfZ)).Append(' ').Append(F(hall.GalleryPX)).Append(' ').Append(F(hall.GalleryPZ)).Append(' ').Append(hall.Shaft).Append(' ').Append(F(hall.ShaftCenter.X)).Append(',').Append(F(hall.ShaftCenter.Z)).Append('\n');
		}
		ulong hash = 14695981039346656037uL;
		foreach (char c in text.ToString())
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		Assert.AreEqual(0x4f683314edfbb06auL, hash, "layer 0 underground");
	}

	private static string F(double v)
	{
		return v.ToString("R", CultureInfo.InvariantCulture);
	}
}
