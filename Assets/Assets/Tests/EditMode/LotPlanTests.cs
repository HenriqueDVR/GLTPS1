using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

// world gen v2 lots: blocks of the street grid split into lots, sparse lots on the open districts' sites
public class LotPlanTests
{
	private const ulong seed = 12345uL;

	private static LayerPlan Layer0()
	{
		return new LayerPlan(seed, 0, new XZ(0.0, 0.0), null);
	}

	private static LayerPlan Layer1()
	{
		return new LayerPlan(seed, 1, new XZ(7300.0, -2100.0), Layer0());
	}

	private static string F(double v)
	{
		return v.ToString("R", CultureInfo.InvariantCulture);
	}

	private static string Describe(List<Lot> lots)
	{
		StringBuilder text = new StringBuilder();
		foreach (Lot lot in lots)
		{
			text.Append(lot.Kind).Append(' ').Append(lot.District).Append(' ').Append(F(lot.Center.X)).Append(',').Append(F(lot.Center.Z)).Append(' ').Append(F(lot.Axis.X)).Append(',').Append(F(lot.Axis.Z)).Append(' ').Append(F(lot.HalfU)).Append(' ').Append(F(lot.HalfV)).Append(' ').Append(lot.Floors).Append(' ').Append(F(lot.Base)).Append(' ').Append(lot.Seed).Append('\n');
		}
		return text.ToString();
	}

	private static List<Lot> Area(LayerPlan plan, int x0, int z0, int size)
	{
		List<Lot> all = new List<Lot>();
		List<Lot> lots = new List<Lot>();
		for (int x = x0; x < x0 + size; x++)
		{
			for (int z = z0; z < z0 + size; z++)
			{
				plan.Lots.Collect(x, z, lots);
				all.AddRange(lots);
			}
		}
		return all;
	}

	// p in the lot grown by margin
	private static bool Inside(Lot lot, XZ p, double margin)
	{
		double x = p.X - lot.Center.X;
		double z = p.Z - lot.Center.Z;
		double u = x * lot.Axis.X + z * lot.Axis.Z;
		double v = -x * lot.Axis.Z + z * lot.Axis.X;
		return Math.Abs(u) < lot.HalfU + margin && Math.Abs(v) < lot.HalfV + margin;
	}

	// separating axes of two rectangles
	private static bool Overlap(Lot a, Lot b)
	{
		XZ[] axes = { a.Axis, new XZ(-a.Axis.Z, a.Axis.X), b.Axis, new XZ(-b.Axis.Z, b.Axis.X) };
		foreach (XZ axis in axes)
		{
			double ra = Reach(a, axis);
			double rb = Reach(b, axis);
			double d = Math.Abs((b.Center.X - a.Center.X) * axis.X + (b.Center.Z - a.Center.Z) * axis.Z);
			if (d >= ra + rb - 1e-6)
			{
				return false;
			}
		}
		return true;
	}

	private static double Reach(Lot lot, XZ axis)
	{
		double u = Math.Abs(lot.Axis.X * axis.X + lot.Axis.Z * axis.Z);
		double v = Math.Abs(-lot.Axis.Z * axis.X + lot.Axis.X * axis.Z);
		return lot.HalfU * u + lot.HalfV * v;
	}

	[Test]
	public void SameTileSameLotsInAnyOrder()
	{
		LayerPlan a = Layer1();
		List<Lot> lots = new List<Lot>();
		Dictionary<string, string> forward = new Dictionary<string, string>();
		for (int x = 50; x < 60; x++)
		{
			for (int z = -20; z < -10; z++)
			{
				a.Lots.Collect(x, z, lots);
				forward[x + "," + z] = Describe(lots);
			}
		}
		LayerPlan b = Layer1();
		for (int x = 59; x >= 50; x--)
		{
			for (int z = -11; z >= -20; z--)
			{
				b.Lots.Collect(x, z, lots);
				Assert.AreEqual(forward[x + "," + z], Describe(lots), "tile " + x + "," + z);
			}
		}
	}

	[Test]
	public void LotsInTheirTileAndApart()
	{
		LayerPlan plan = Layer1();
		List<Lot> lots = new List<Lot>();
		List<Lot> all = new List<Lot>();
		for (int x = 50; x < 66; x++)
		{
			for (int z = -24; z < -8; z++)
			{
				plan.Lots.Collect(x, z, lots);
				foreach (Lot lot in lots)
				{
					// a whole block belongs to the tile holding the block's centre, its lots reach up to half a block out
					double slack = (RoadPlan.StreetSpacing + RoadPlan.StreetJitter) * 0.5;
					Assert.That(lot.Center.X, Is.InRange(x * Gen2.TileSize - slack, (x + 1) * Gen2.TileSize + slack));
					Assert.That(lot.Center.Z, Is.InRange(z * Gen2.TileSize - slack, (z + 1) * Gen2.TileSize + slack));
					Assert.GreaterOrEqual(lot.HalfU * 2.0, LotPlan.MinLot * 0.35 - 1e-9);
					Assert.LessOrEqual(Math.Max(lot.HalfU, lot.HalfV) * 2.0, Math.Max(LotPlan.MaxLot, LotPlan.SiteLot * 2.0) + 1e-9);
				}
				all.AddRange(lots);
			}
		}
		Assert.Greater(all.Count, 200);
		for (int i = 0; i < all.Count; i++)
		{
			for (int j = i + 1; j < all.Count; j++)
			{
				if (all[i].Center.DistanceTo(all[j].Center) < 120.0)
				{
					Assert.IsFalse(Overlap(all[i], all[j]), "lots at " + all[i].Center + " and " + all[j].Center);
				}
			}
		}
	}

	// no street, expressway or buried road runs through a lot
	[Test]
	public void ClearOfTheRoads()
	{
		foreach (LayerPlan plan in new[] { Layer0(), Layer1() })
		{
			int x0 = (int)(plan.Origin.X / Gen2.TileSize) - 30;
			int z0 = (int)(plan.Origin.Z / Gen2.TileSize) - 30;
			Dictionary<long, List<Lot>> buckets = new Dictionary<long, List<Lot>>();
			foreach (Lot lot in Area(plan, x0, z0, 60))
			{
				long key = ((long)Gen2.Cell(lot.Center.X, Gen2.TileSize) << 32) ^ (uint)Gen2.Cell(lot.Center.Z, Gen2.TileSize);
				if (!buckets.TryGetValue(key, out List<Lot> bucket))
				{
					buckets[key] = bucket = new List<Lot>();
				}
				bucket.Add(lot);
			}
			List<RoadPiece> pieces = new List<RoadPiece>();
			List<RoadPatch> patches = new List<RoadPatch>();
			int checkedPieces = 0;
			for (int x = x0; x < x0 + 60; x++)
			{
				for (int z = z0; z < z0 + 60; z++)
				{
					plan.Roads.Collect(x, z, pieces, patches);
					foreach (RoadPiece p in pieces)
					{
						if (p.Kind == RoadKind.Track)
						{
							continue;
						}
						checkedPieces++;
						XZ mid = new XZ((p.A.X + p.B.X) * 0.5, (p.A.Z + p.B.Z) * 0.5);
						int bx = Gen2.Cell(mid.X, Gen2.TileSize);
						int bz = Gen2.Cell(mid.Z, Gen2.TileSize);
						for (int ix = bx - 1; ix <= bx + 1; ix++)
						{
							for (int iz = bz - 1; iz <= bz + 1; iz++)
							{
								if (!buckets.TryGetValue(((long)ix << 32) ^ (uint)iz, out List<Lot> near))
								{
									continue;
								}
								foreach (Lot lot in near)
								{
									double margin = p.Width * 0.5 - 0.01;
									Assert.IsFalse(Inside(lot, p.A, margin) || Inside(lot, p.B, margin) || Inside(lot, mid, margin), p.Kind + " through the lot at " + lot.Center);
								}
							}
						}
					}
				}
			}
			Assert.Greater(checkedPieces, 100);
		}
	}

	// the fields stay mostly empty snow, the city is packed
	[Test]
	public void FieldsSparseCityDense()
	{
		LayerPlan l0 = Layer0();
		int fields = 0;
		foreach (Lot lot in Area(l0, -8, -8, 16))
		{
			fields += l0.Def.Districts[lot.District].Id == "fields" ? 1 : 0;
		}
		LayerPlan l1 = Layer1();
		int city = Area(l1, (int)(l1.Origin.X / Gen2.TileSize) - 8, (int)(l1.Origin.Z / Gen2.TileSize) - 8, 16).Count;
		// 2 km by 2 km each
		Assert.Less(fields, 20, "fields");
		Assert.Greater(city, 400, "city");
		Assert.Greater(fields, 0, "the fields still have the odd landmark");
	}

	[Test]
	public void LotKindsFollowTheDistrict()
	{
		LayerPlan l0 = Layer0();
		int buildings = 0, open = 0, landmarks = 0;
		foreach (Lot lot in Area(l0, -40, -40, 80))
		{
			DistrictDef def = l0.Def.Districts[lot.District];
			if (def.Roads == DistrictRoads.Open)
			{
				Assert.AreNotEqual(LotKind.Building, lot.Kind, "open districts have no blocks");
			}
			Assert.That(lot.Floors, Is.InRange(def.FloorsMin, Math.Max(def.FloorsMin, def.FloorsMax)));
			buildings += lot.Kind == LotKind.Building ? 1 : 0;
			open += lot.Kind == LotKind.Open ? 1 : 0;
			landmarks += lot.Kind == LotKind.Landmark ? 1 : 0;
		}
		Assert.Greater(buildings, 0);
		Assert.Greater(open, 0);
		Assert.Greater(landmarks, 0);
	}

	// tile item ids: unique per (layer, tile, index), and never a legacy, scene pickup or runtime id
	[Test]
	public void ItemIdsDontClash()
	{
		HashSet<ulong> seen = new HashSet<ulong>();
		foreach (int layer in new[] { 0, 1, 2 })
		{
			foreach (int x in new[] { -524288, -3, 0, 7, 524287 })
			{
				foreach (int z in new[] { -524288, -1, 0, 5, 524287 })
				{
					foreach (int i in new[] { 0, 1, 4095 })
					{
						ulong id = Gen2.ItemId(layer, x, z, i);
						Assert.IsTrue(seen.Add(id), layer + " " + x + "," + z + " " + i);
						Assert.AreEqual(Gen2.TileItemTag, id & 0x6000000000000000uL, "bit 61 only, never bit 62");
					}
				}
			}
		}
		// the legacy generator's biggest ids: edges at stream 1000000 + id, shifted by 16
		Assert.Less((1000000uL + 100000uL) << 16 | 0xFFFFuL, Gen2.TileItemTag);
	}

	// doors from raised streets: each on a building lot by a flat stretch of high deck, and the street's parapet
	// left open right in front of it
	[Test]
	public void DoorsFromRaisedStreets()
	{
		LayerPlan plan = Layer0();
		List<Lot> lots = new List<Lot>();
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		int doors = 0;
		for (int x = 20; x < 36; x++)
		{
			for (int z = 12; z < 28; z++)
			{
				plan.Lots.Collect(x, z, lots);
				foreach (Lot lot in lots)
				{
					if (lot.Door == 0)
					{
						continue;
					}
					doors++;
					Assert.AreEqual(LotKind.Building, lot.Kind);
					Assert.GreaterOrEqual(lot.Deck, LotPlan.DoorMinDeck);
					Assert.LessOrEqual(System.Math.Abs(lot.DoorAt), lot.HalfU - LotPlan.PorchWidth * 0.5);
					XZ v = new XZ(-lot.Axis.Z * lot.Door, lot.Axis.X * lot.Door);
					DistrictDef def = plan.Def.Districts[lot.District];
					// where the walkway meets the deck's edge
					double out_ = lot.HalfV + def.Sidewalk + 0.1;
					XZ meet = new XZ(lot.Center.X + v.X * out_ + lot.Axis.X * lot.DoorAt, lot.Center.Z + v.Z * out_ + lot.Axis.Z * lot.DoorAt);
					bool open = false;
					for (int dx = -1; dx <= 1 && !open; dx++)
					{
						for (int dz = -1; dz <= 1 && !open; dz++)
						{
							plan.Roads.Collect(Gen2.Cell(meet.X, Gen2.TileSize) + dx, Gen2.Cell(meet.Z, Gen2.TileSize) + dz, pieces, patches);
							foreach (RoadPiece piece in pieces)
							{
								double length = piece.A.DistanceTo(piece.B);
								double s = ((meet.X - piece.A.X) * piece.TangentA.X + (meet.Z - piece.A.Z) * piece.TangentA.Z) / length;
								double across = -(meet.X - piece.A.X) * piece.TangentA.Z + (meet.Z - piece.A.Z) * piece.TangentA.X;
								if (s < 0.0 || s > 1.0 || System.Math.Abs(System.Math.Abs(across) - piece.Width * 0.5) > 0.2)
								{
									continue;
								}
								// a cross street's deck may slope a little
								Assert.AreEqual(lot.Deck, piece.HeightA + (piece.HeightB - piece.HeightA) * s, 0.05);
								// the lot's on the side away from the deck's middle, so that's the side that's open
								double from = (across > 0.0) ? piece.LeftFrom : piece.RightFrom;
								double to = (across > 0.0) ? piece.LeftTo : piece.RightTo;
								open = s >= from - 1e-6 && s <= to + 1e-6;
							}
						}
					}
					Assert.IsTrue(open, "door at " + meet + " has no opening in the parapet");
				}
			}
		}
		Assert.Greater(doors, 5);
	}

	// stairs up to raised streets: against the deck's side, no lot in their way, the parapet open by the top landing
	[Test]
	public void StairsUpToRaisedStreets()
	{
		LayerPlan plan = Layer0();
		List<Lot> lots = new List<Lot>();
		List<Lot> near = new List<Lot>();
		List<Stair> stairs = new List<Stair>();
		List<Stair> none = new List<Stair>();
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		int count = 0;
		for (int x = 20; x < 36; x++)
		{
			for (int z = 12; z < 28; z++)
			{
				plan.Lots.Collect(x, z, lots, stairs);
				foreach (Stair stair in stairs)
				{
					count++;
					Assert.GreaterOrEqual(stair.Deck, LotPlan.DoorMinDeck);
					XZ top = new XZ(stair.Foot.X + stair.Dir.X * stair.Length, stair.Foot.Z + stair.Dir.Z * stair.Length);
					// no lot reaches into it
					for (int dx = -1; dx <= 1; dx++)
					{
						for (int dz = -1; dz <= 1; dz++)
						{
							plan.Lots.Collect(x + dx, z + dz, near, none);
							foreach (Lot lot in near)
							{
								for (double s = 0.0; s <= stair.Length; s += 1.0)
								{
									XZ p = new XZ(stair.Foot.X + stair.Dir.X * s, stair.Foot.Z + stair.Dir.Z * s);
									Assert.IsFalse(Inside(lot, p, LotPlan.StairWidth * 0.5), "lot in a stair at " + p);
								}
							}
						}
					}
					// the deck's edge beside the top landing, open
					double inward = LotPlan.StairWidth * 0.5 + 0.1;
					XZ landing = new XZ(stair.Foot.X + stair.Dir.X * (stair.Length - LotPlan.TopLanding * 0.5) - stair.Out.X * inward, stair.Foot.Z + stair.Dir.Z * (stair.Length - LotPlan.TopLanding * 0.5) - stair.Out.Z * inward);
					bool open = false;
					for (int dx = -1; dx <= 1 && !open; dx++)
					{
						for (int dz = -1; dz <= 1 && !open; dz++)
						{
							plan.Roads.Collect(Gen2.Cell(landing.X, Gen2.TileSize) + dx, Gen2.Cell(landing.Z, Gen2.TileSize) + dz, pieces, patches);
							foreach (RoadPiece piece in pieces)
							{
								double length = piece.A.DistanceTo(piece.B);
								double s = ((landing.X - piece.A.X) * piece.TangentA.X + (landing.Z - piece.A.Z) * piece.TangentA.Z) / length;
								double across = -(landing.X - piece.A.X) * piece.TangentA.Z + (landing.Z - piece.A.Z) * piece.TangentA.X;
								if (s < 0.0 || s > 1.0 || System.Math.Abs(System.Math.Abs(across) - piece.Width * 0.5) > 0.2)
								{
									continue;
								}
								Assert.AreEqual(stair.Deck, piece.HeightA + (piece.HeightB - piece.HeightA) * s, 0.05);
								double from = (across > 0.0) ? piece.LeftFrom : piece.RightFrom;
								double to = (across > 0.0) ? piece.LeftTo : piece.RightTo;
								open = s >= from - 1e-6 && s <= to + 1e-6;
							}
						}
					}
					Assert.IsTrue(open, "stair at " + stair.Foot + " has no opening at the top");
				}
			}
		}
		Assert.Greater(count, 3);
	}

	// raised cross streets get doors and stairs too (their place is checked above with the rest)
	[Test]
	public void DoorsAndStairsOnRaisedCrossStreets()
	{
		LayerPlan plan = Layer0();
		int doors = 0;
		int stairs = 0;
		for (int district = 0; district < plan.Def.Districts.Length; district++)
		{
			if (plan.Def.Districts[district].RaisedCross <= 0.0)
			{
				continue;
			}
			for (int k = -50; k <= 50; k++)
			{
				for (int j = -50; j <= 50; j++)
				{
					for (int side = -2; side <= 2; side += 4)
					{
						if (plan.Lots.Door(district, k, j, side, out _, out double deck))
						{
							doors++;
							Assert.GreaterOrEqual(deck, LotPlan.DoorMinDeck);
							Assert.IsFalse(plan.Lots.Stair(district, k, j, side, out _));
						}
						if (plan.Lots.Stair(district, k, j, side, out Stair stair))
						{
							stairs++;
							Assert.GreaterOrEqual(stair.Deck, LotPlan.DoorMinDeck);
							Assert.IsFalse(plan.Lots.Stair(district, k, j, -1, out _) || plan.Lots.Stair(district, k, j, 1, out _));
						}
					}
				}
			}
		}
		Assert.Greater(doors, 5);
		Assert.Greater(stairs, 3);
		// and some in the area the tests above check: a cross street's door lot is turned a quarter from its block's
		// others, its stairs climb across the raised streets' ones
		List<Lot> lots = new List<Lot>();
		List<Stair> flights = new List<Stair>();
		int turned = 0;
		int across = 0;
		for (int x = 20; x < 36; x++)
		{
			for (int z = 12; z < 28; z++)
			{
				plan.Lots.Collect(x, z, lots, flights);
				foreach (Lot lot in lots)
				{
					if (lot.Door != 0 && lots.Exists(o => o.Door == 0 && o.District == lot.District && o.Kind == LotKind.Building && System.Math.Abs(o.Axis.X * lot.Axis.X + o.Axis.Z * lot.Axis.Z) < 1e-9))
					{
						turned++;
					}
				}
				foreach (Stair stair in flights)
				{
					if (lots.Exists(o => o.Door == 0 && o.District == stair.District && o.Kind == LotKind.Building && System.Math.Abs(o.Axis.X * stair.Dir.X + o.Axis.Z * stair.Dir.Z) < 1e-9))
					{
						across++;
					}
				}
			}
		}
		Assert.Greater(turned, 0);
		Assert.Greater(across, 0);
	}

	// frozen genVersion 2 output. a failure here means v2 saves or mixed-version co-op would break: bump genVersion instead
	[Test]
	public void GoldenValues()
	{
		Assert.AreEqual(0x4cc05ad33b67f01duL, Fold(Layer0(), -12, -12, 24), "layer 0 lots");
		Assert.AreEqual(0x39752b320365bbc0uL, Fold(Layer1(), 50, -20, 12), "layer 1 lots");
	}

	private static ulong Fold(LayerPlan plan, int x0, int z0, int size)
	{
		ulong hash = 14695981039346656037uL;
		foreach (char c in Describe(Area(plan, x0, z0, size)))
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		return hash;
	}
}
