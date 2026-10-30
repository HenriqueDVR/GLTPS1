using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

// world gen v2 viaducts: at most one per region cell, clear of the pillars and the start, lots keep out of them
public class ViaductPlanTests
{
	private const ulong seed = 12345uL;

	private static LayerPlan Layer0()
	{
		return new LayerPlan(seed, 0, new XZ(0.0, 0.0), null);
	}

	private static List<Viaduct> All(LayerPlan plan, int range)
	{
		List<Viaduct> all = new List<Viaduct>();
		for (int x = -range; x <= range; x++)
		{
			for (int z = -range; z <= range; z++)
			{
				if (plan.Viaducts.At(x, z, out Viaduct viaduct))
				{
					all.Add(viaduct);
				}
			}
		}
		return all;
	}

	[Test]
	public void SameEveryTime()
	{
		LayerPlan a = Layer0();
		LayerPlan b = Layer0();
		for (int x = -6; x <= 6; x++)
		{
			for (int z = -6; z <= 6; z++)
			{
				bool hasA = a.Viaducts.At(x, z, out Viaduct va);
				// asked in another order first
				b.Viaducts.At(z, x, out _);
				bool hasB = b.Viaducts.At(x, z, out Viaduct vb);
				Assert.AreEqual(hasA, hasB);
				if (hasA)
				{
					Assert.AreEqual(va.A, vb.A);
					Assert.AreEqual(va.B, vb.B);
					Assert.AreEqual(va.Height, vb.Height);
				}
			}
		}
	}

	[Test]
	public void SomeAndNotEverywhere()
	{
		int count = All(Layer0(), 8).Count;
		Assert.Greater(count, 10);
		Assert.Less(count, 17 * 17);
	}

	[Test]
	public void ClearOfPillarsAndStart()
	{
		LayerPlan plan = Layer0();
		foreach (Viaduct viaduct in All(plan, 8))
		{
			Assert.GreaterOrEqual(RoadPlan.SegmentDistance(plan.Origin, viaduct.A, viaduct.B), RoadPlan.StartClearance);
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					XZ pillar = LayerPlan.PillarPosition(seed, viaduct.CellX + dx, viaduct.CellZ + dz);
					Assert.GreaterOrEqual(RoadPlan.SegmentDistance(pillar, viaduct.A, viaduct.B), ViaductPlan.PillarGap);
				}
			}
			Assert.GreaterOrEqual(viaduct.Height, ViaductPlan.MinHeight);
			Assert.LessOrEqual(viaduct.Height, ViaductPlan.MaxHeight);
			// whole bays
			double bays = viaduct.Length / ViaductPlan.Bay;
			Assert.AreEqual(System.Math.Round(bays), bays, 1e-6);
		}
	}

	[Test]
	public void LotsKeepOut()
	{
		LayerPlan plan = Layer0();
		List<Viaduct> viaducts = All(plan, 4);
		Assert.Greater(viaducts.Count, 0);
		List<Lot> lots = new List<Lot>();
		int checkedLots = 0;
		foreach (Viaduct viaduct in viaducts)
		{
			XZ middle = viaduct.At(viaduct.Length * 0.5);
			int tx = Gen2.Cell(middle.X, Gen2.TileSize);
			int tz = Gen2.Cell(middle.Z, Gen2.TileSize);
			for (int x = tx - 2; x <= tx + 2; x++)
			{
				for (int z = tz - 2; z <= tz + 2; z++)
				{
					plan.Lots.Collect(x, z, lots);
					foreach (Lot lot in lots)
					{
						checkedLots++;
						double reach = System.Math.Sqrt(lot.HalfU * lot.HalfU + lot.HalfV * lot.HalfV);
						Assert.GreaterOrEqual(RoadPlan.SegmentDistance(lot.Center, viaduct.A, viaduct.B), reach + ViaductPlan.Width * 0.5);
					}
				}
			}
		}
		Assert.Greater(checkedLots, 0);
	}

	// frozen genVersion 2 output. a failure here means v2 saves or mixed-version co-op would break: bump genVersion instead
	[Test]
	public void GoldenValues()
	{
		StringBuilder text = new StringBuilder();
		foreach (Viaduct viaduct in All(Layer0(), 6))
		{
			text.Append(viaduct.CellX).Append(',').Append(viaduct.CellZ).Append(' ').Append(viaduct.A.X.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(viaduct.A.Z.ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(viaduct.B.X.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(viaduct.B.Z.ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(viaduct.Height.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
		}
		ulong hash = 14695981039346656037uL;
		foreach (char c in text.ToString())
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		Assert.AreEqual(0x8e90dcf141939a6buL, hash, "layer 0 viaducts");
	}
}
