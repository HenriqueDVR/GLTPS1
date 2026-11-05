using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

// world gen v2 underground: halls in their cells clear of voids, pillars and the start, galleries between them,
// ways in with a ramp up to a ramp house where nothing on the deck is in the way
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
				Assert.AreEqual(one.Entrance, two.Entrance);
				Assert.AreEqual(one.EntranceAt, two.EntranceAt);
				Assert.AreEqual(one.Apron, two.Apron);
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
			if (plan.TowerNear(hall.Center.X, hall.Center.Z, out XZ pillar))
			{
				Assert.GreaterOrEqual(inner.Distance(pillar.X, pillar.Z), UndergroundPlan.PillarGap);
			}
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
						Assert.IsFalse(a.Entrance && a.Out.Z > 0.5);
						Assert.IsFalse(b.Entrance && b.Out.Z < -0.5);
					}
					else
					{
						Assert.AreEqual(a.Center.X + a.HalfX + UndergroundPlan.Wall, g.A.X, 1e-9);
						Assert.AreEqual(b.Center.X - b.HalfX - UndergroundPlan.Wall, g.B.X, 1e-9);
						Assert.AreEqual(a.GalleryPX, g.A.Z);
						Assert.AreEqual(b.GalleryNX, g.A.Z);
						Assert.IsFalse(a.Entrance && a.Out.X > 0.5);
						Assert.IsFalse(b.Entrance && b.Out.X < -0.5);
					}
					XZ middle = new XZ((g.A.X + g.B.X) * 0.5, (g.A.Z + g.B.Z) * 0.5);
					Assert.IsTrue(plan.Underground.Under(middle.X, middle.Z));
				}
			}
		}
		Assert.Greater(count, 10);
	}

	// the ramp's floor: flat out of the hall, up to the deck, never steeper than Grade, the deck open
	// over it where there's no more room under its roof
	[Test]
	public void PassageFloor()
	{
		Assert.AreEqual(-UndergroundPlan.Level, UndergroundPlan.PassageFloor(0.0), 1e-9);
		Assert.AreEqual(-UndergroundPlan.Level, UndergroundPlan.PassageFloor(UndergroundPlan.Flat), 1e-9);
		Assert.AreEqual(0.0, UndergroundPlan.PassageFloor(UndergroundPlan.PassageLength), 1e-9);
		for (double s = 0.0; s < UndergroundPlan.PassageLength; s += 0.5)
		{
			double grade = UndergroundPlan.PassageFloor(s + 0.5) - UndergroundPlan.PassageFloor(s);
			Assert.GreaterOrEqual(grade, -1e-9);
			Assert.LessOrEqual(grade / 0.5, UndergroundPlan.Grade + 0.002);
		}
		Assert.AreEqual(-(UndergroundPlan.Roof + UndergroundPlan.Headroom), UndergroundPlan.PassageFloor(UndergroundPlan.HoleFrom), 1e-6);
		Assert.Less(UndergroundPlan.PassageLength - UndergroundPlan.HouseFrom, 50.0);
		Assert.Less(UndergroundPlan.HouseFrom, UndergroundPlan.HoleFrom);
	}

	// a ramp house: nothing on the deck in it or on its apron, its apron ending on a road, the deck open over the top of
	// its ramp only, the passage under the deck all the way
	[Test]
	public void WaysInWhereTheDeckIsClear()
	{
		LayerPlan plan = Layer0();
		List<Lot> lots = new List<Lot>();
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		List<Area> holes = new List<Area>();
		int ways = 0;
		foreach (Hall hall in All(plan, 12))
		{
			if (!hall.Entrance)
			{
				continue;
			}
			ways++;
			Area house = UndergroundPlan.HouseArea(hall);
			Area apron = UndergroundPlan.ApronArea(hall);
			Area passage = hall.PassageArea(0.0, UndergroundPlan.PassageLength, UndergroundPlan.GalleryWidth * 0.5 + UndergroundPlan.Wall);
			// under the deck it meets no other hall
			foreach (Hall other in All(plan, 12))
			{
				if (other.CellX != hall.CellX || other.CellZ != hall.CellZ)
				{
					Assert.IsFalse(passage.Overlaps(other.Inner.Grown(UndergroundPlan.Wall)), "passage into another hall");
				}
			}
			for (double s = 0.0; s < UndergroundPlan.PassageLength; s += 4.0)
			{
				XZ p = hall.PassagePoint(s, 0.0);
				Assert.IsTrue(plan.Underground.Under(p.X, p.Z));
			}
			XZ end = hall.PassagePoint(UndergroundPlan.PassageLength + hall.Apron, 0.0);
			int tx0 = Gen2.Cell(System.Math.Min(house.X0, apron.X0), Gen2.TileSize) - 1;
			int tx1 = Gen2.Cell(System.Math.Max(house.X1, apron.X1), Gen2.TileSize) + 1;
			int tz0 = Gen2.Cell(System.Math.Min(house.Z0, apron.Z0), Gen2.TileSize) - 1;
			int tz1 = Gen2.Cell(System.Math.Max(house.Z1, apron.Z1), Gen2.TileSize) + 1;
			double hole = 0.0;
			bool onRoad = false;
			string nearest = "";
			double near = double.MaxValue;
			for (int tx = tx0; tx <= tx1; tx++)
			{
				for (int tz = tz0; tz <= tz1; tz++)
				{
					plan.Lots.Collect(tx, tz, lots);
					foreach (Lot lot in lots)
					{
						double reach = System.Math.Sqrt(lot.HalfU * lot.HalfU + lot.HalfV * lot.HalfV);
						Assert.Greater(house.Distance(lot.Center.X, lot.Center.Z), reach, "lot in a ramp house");
						Assert.Greater(apron.Distance(lot.Center.X, lot.Center.Z), reach - 2.0, "lot on an apron");
					}
					plan.Roads.Collect(tx, tz, pieces, patches);
					foreach (RoadPiece piece in pieces)
					{
						for (double t = 0.0; t <= 1.0; t += 0.125)
						{
							double x = piece.A.X + (piece.B.X - piece.A.X) * t;
							double z = piece.A.Z + (piece.B.Z - piece.A.Z) * t;
							Assert.Greater(house.Distance(x, z), piece.Width * 0.5, "road through a ramp house");
						}
						if (RoadPlan.SegmentDistance(end, piece.A, piece.B) < near)
						{
							near = RoadPlan.SegmentDistance(end, piece.A, piece.B);
							nearest = piece.Kind + " " + piece.A + "-" + piece.B + " w " + piece.Width + " h " + piece.HeightA + "/" + piece.HeightB + " at " + near;
						}
						if (RoadPlan.SegmentDistance(end, piece.A, piece.B) < piece.Width * 0.5 + 0.5 && piece.HeightA < 0.3 && piece.HeightB < 0.3)
						{
							onRoad = true;
						}
					}
					plan.Underground.Holes(tx, tz, holes);
					foreach (Area h in holes)
					{
						if (h.Overlaps(house))
						{
							hole += (h.X1 - h.X0) * (h.Z1 - h.Z0);
						}
					}
				}
			}
			Assert.IsTrue(onRoad, "apron from " + hall.PassagePoint(UndergroundPlan.PassageLength, 0.0) + " ends off any road at " + end + " (" + plan.Def.Districts[plan.DistrictAt(end.X, end.Z)].Id + ", nearest " + nearest + ")");
			Area expected = UndergroundPlan.HoleArea(hall);
			Assert.AreEqual((expected.X1 - expected.X0) * (expected.Z1 - expected.Z0), hole, 1e-6);
			Assert.Less(plan.Chasms.Inside(house.X0, house.Z0), -Gen2.TileSize);
		}
		Assert.Greater(ways, 3);
	}

	// nothing down there out of reach: from the halls with a way in, through galleries, every hall is reached
	[Test]
	public void EveryHallCanBeReached()
	{
		foreach (LayerPlan plan in new[] { Layer0(), new LayerPlan(seed, 0, new XZ(0.0, 0.0), null, new XZ(0.0, 1.0)) })
		{
			HashSet<(int, int)> reached = new HashSet<(int, int)>();
			Queue<(int, int)> open = new Queue<(int, int)>();
			List<Hall> all = All(plan, 12);
			plan.Underground.Start(out StartWay start);
			foreach (Hall hall in all)
			{
				bool isStart = plan.Underground.Start(out _) && hall.CellX == start.Hall.CellX && hall.CellZ == start.Hall.CellZ;
				if ((hall.Entrance || isStart) && reached.Add((hall.CellX, hall.CellZ)))
				{
					open.Enqueue((hall.CellX, hall.CellZ));
				}
			}
			while (open.Count > 0)
			{
				(int x, int z) = open.Dequeue();
				if (plan.Underground.Gallery(x, z, false, out _) && reached.Add((x + 1, z)))
				{
					open.Enqueue((x + 1, z));
				}
				if (plan.Underground.Gallery(x - 1, z, false, out _) && reached.Add((x - 1, z)))
				{
					open.Enqueue((x - 1, z));
				}
				if (plan.Underground.Gallery(x, z, true, out _) && reached.Add((x, z + 1)))
				{
					open.Enqueue((x, z + 1));
				}
				if (plan.Underground.Gallery(x, z - 1, true, out _) && reached.Add((x, z - 1)))
				{
					open.Enqueue((x, z - 1));
				}
			}
			Assert.Greater(all.Count, 15);
			foreach (Hall hall in all)
			{
				Assert.IsTrue(reached.Contains((hall.CellX, hall.CellZ)), "hall at " + hall.Center + " out of reach");
			}
		}
	}

	// the start tunnel's dead end goes on down: whichever way the tunnel faces, the ramp, the chamber and the gallery
	// reach a hall with its mouth on the side facing them, and no other hall or gallery gets in the way
	[Test]
	public void StartTunnelGoesDown()
	{
		Assert.IsFalse(Layer0().Underground.Start(out _), "no facing, no way down");
		foreach (XZ facing in new[] { new XZ(0.0, 1.0), new XZ(0.6, 0.8), new XZ(-1.0, 0.0), new XZ(-0.3, -0.954), new XZ(0.707, -0.707) })
		{
			LayerPlan plan = new LayerPlan(seed, 0, new XZ(0.0, 0.0), null, facing);
			Assert.IsTrue(plan.Underground.Start(out StartWay way), "facing " + facing);
			Assert.AreEqual(0.0, UndergroundPlan.StartFloor(0.0), 1e-9);
			Assert.AreEqual(-UndergroundPlan.Level, UndergroundPlan.StartFloor(UndergroundPlan.RampLength + 1.0), 1e-9);
			Assert.Greater(UndergroundPlan.StartRoof(UndergroundPlan.StartCover + 1.0), UndergroundPlan.StartFloor(UndergroundPlan.StartCover + 1.0) + UndergroundPlan.Headroom);
			Assert.Less(UndergroundPlan.StartRoof(UndergroundPlan.StartCover + 1.0), 0.0, "under the deck past the embankment");
			// the line runs down to the chamber, the gallery meets the hall's wall in its span
			for (double s = 0.0; s <= UndergroundPlan.RampLength + UndergroundPlan.StartFlat; s += 4.0)
			{
				XZ p = way.Point(s, 0.0);
				Assert.IsTrue(plan.Underground.Under(p.X, p.Z));
			}
			Assert.IsTrue(plan.Underground.Under(way.Chamber.X, way.Chamber.Z));
			Assert.IsTrue(plan.Underground.Under((way.GalleryA.X + way.GalleryB.X) * 0.5, (way.GalleryA.Z + way.GalleryB.Z) * 0.5));
			Assert.IsTrue(plan.Underground.HallAt(way.Hall.CellX, way.Hall.CellZ, out Hall hall));
			Assert.AreEqual(way.Hall.Center.X, hall.Center.X);
			Area inner = hall.Inner.Grown(UndergroundPlan.Wall);
			Assert.AreEqual(0.0, inner.Distance(way.GalleryB.X, way.GalleryB.Z), 1e-6);
			double mouth = (System.Math.Abs(way.Out.X) > 0.5) ? way.GalleryB.Z : way.GalleryB.X;
			double gap = (way.Out.X > 0.5) ? hall.GalleryNX : (way.Out.X < -0.5) ? hall.GalleryPX : (way.Out.Z > 0.5) ? hall.GalleryNZ : hall.GalleryPZ;
			Assert.AreEqual(mouth, gap, 1e-9);
			Assert.GreaterOrEqual(hall.Inner.Distance(plan.Origin.X, plan.Origin.Z), RoadPlan.StartClearance);
			// nothing else down there in its way
			for (int x = -6; x <= 6; x++)
			{
				for (int z = -6; z <= 6; z++)
				{
					if (plan.Underground.HallAt(x, z, out Hall other) && (other.CellX != hall.CellX || other.CellZ != hall.CellZ))
					{
						Assert.IsFalse(other.Inner.Grown(UndergroundPlan.Wall).Overlaps(way.Keep), "hall in the start's way");
						Assert.IsFalse(other.Inner.Overlaps(hall.Inner.Grown(UndergroundPlan.Wall)), "hall in the start's hall");
					}
					for (int d = 0; d < 2; d++)
					{
						if (plan.Underground.Gallery(x, z, d == 1, out GalleryPiece g))
						{
							Assert.IsFalse(UndergroundPlan.GalleryArea(g).Overlaps(way.Keep), "gallery in the start's way");
						}
					}
				}
			}
			// the deck's open over the top of the ramp
			List<Area> holes = new List<Area>();
			XZ top = way.Point(UndergroundPlan.StartHole * 0.5, 0.0);
			plan.Underground.Holes(Gen2.Cell(top.X, Gen2.TileSize), Gen2.Cell(top.Z, Gen2.TileSize), holes);
			Assert.IsTrue(holes.Exists(h => h.Contains(top.X, top.Z)));
		}
	}

	[Test]
	public void NothingUnderTheStart()
	{
		LayerPlan plan = Layer0();
		Assert.IsFalse(plan.Underground.Under(plan.Origin.X, plan.Origin.Z));
		Assert.IsFalse(plan.Underground.Blocks(plan.Origin, RoadPlan.StartClearance * 0.5));
	}

	// the world as it stands. a failure means tile output changed: fine before 1.0 if you meant it (update the numbers), a bug if you didn't
	[Test]
	public void GoldenValues()
	{
		StringBuilder text = new StringBuilder();
		foreach (Hall hall in All(Layer0(), 8))
		{
			text.Append(hall.CellX).Append(',').Append(hall.CellZ).Append(' ').Append(F(hall.Center.X)).Append(',').Append(F(hall.Center.Z)).Append(' ').Append(F(hall.HalfX)).Append(' ').Append(F(hall.HalfZ)).Append(' ').Append(F(hall.GalleryPX)).Append(' ').Append(F(hall.GalleryPZ)).Append(' ').Append(hall.Entrance).Append(' ').Append(F(hall.Out.X)).Append(',').Append(F(hall.Out.Z)).Append(' ').Append(F(hall.EntranceAt)).Append(' ').Append(F(hall.Apron)).Append('\n');
		}
		ulong hash = 14695981039346656037uL;
		foreach (char c in text.ToString())
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		Assert.AreEqual(0x9614ba2fa515e795uL, hash, "layer 0 underground");
	}

	private static string F(double v)
	{
		return v.ToString("R", CultureInfo.InvariantCulture);
	}
}
