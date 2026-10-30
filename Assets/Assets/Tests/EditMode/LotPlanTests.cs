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

	// frozen genVersion 2 output. a failure here means v2 saves or mixed-version co-op would break: bump genVersion instead
	[Test]
	public void GoldenValues()
	{
		Assert.AreEqual(0xddf1741b305e763fuL, Fold(Layer0(), -12, -12, 24), "layer 0 lots");
		Assert.AreEqual(0x9b31d30bc8c0b13fuL, Fold(Layer1(), 50, -20, 12), "layer 1 lots");
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
