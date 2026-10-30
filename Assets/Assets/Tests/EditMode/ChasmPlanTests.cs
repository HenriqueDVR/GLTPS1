using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

// world gen v2 voids: rifts and shafts in the deck, clear of the pillars, the start and the junctions; lots keep out
public class ChasmPlanTests
{
	private const ulong seed = 12345uL;

	private static LayerPlan Layer0()
	{
		return new LayerPlan(seed, 0, new XZ(0.0, 0.0), null);
	}

	private static List<Chasm> All(LayerPlan plan, int range)
	{
		List<Chasm> all = new List<Chasm>();
		for (int x = -range; x <= range; x++)
		{
			for (int z = -range; z <= range; z++)
			{
				all.AddRange(plan.Chasms.In(x, z));
			}
		}
		return all;
	}

	[Test]
	public void SameInAnyOrder()
	{
		LayerPlan a = Layer0();
		LayerPlan b = Layer0();
		for (int i = 0; i < 400; i++)
		{
			double x = (i * 7919 % 6000) - 3000.0;
			double z = (i * 104729 % 6000) - 3000.0;
			b.Chasms.Inside(-x, z);
			Assert.AreEqual(a.Chasms.Inside(x, z), b.Chasms.Inside(x, z));
		}
	}

	[Test]
	public void RiftsAndShaftsBoth()
	{
		List<Chasm> all = All(Layer0(), 6);
		Assert.Greater(all.FindAll(c => c.Shaft).Count, 5);
		Assert.Greater(all.FindAll(c => !c.Shaft).Count, 5);
	}

	[Test]
	public void ClearOfStartAndPillars()
	{
		LayerPlan plan = Layer0();
		Assert.IsFalse(plan.Chasms.Void(plan.Origin.X, plan.Origin.Z));
		for (double a = 0.0; a < 6.28; a += 0.2)
		{
			Assert.IsFalse(plan.Chasms.Void(plan.Origin.X + System.Math.Cos(a) * RoadPlan.StartClearance, plan.Origin.Z + System.Math.Sin(a) * RoadPlan.StartClearance));
		}
		for (int x = -4; x <= 4; x++)
		{
			for (int z = -4; z <= 4; z++)
			{
				XZ pillar = LayerPlan.PillarPosition(seed, x, z);
				for (double a = 0.0; a < 6.28; a += 0.3)
				{
					Assert.IsFalse(plan.Chasms.Void(pillar.X + System.Math.Cos(a) * 100.0, pillar.Z + System.Math.Sin(a) * 100.0), "near the pillar of " + x + "," + z);
				}
			}
		}
	}

	[Test]
	public void LotsKeepOut()
	{
		LayerPlan plan = Layer0();
		List<Lot> lots = new List<Lot>();
		int checkedLots = 0;
		foreach (Chasm chasm in All(plan, 3))
		{
			int tx = Gen2.Cell(chasm.Center.X, Gen2.TileSize);
			int tz = Gen2.Cell(chasm.Center.Z, Gen2.TileSize);
			for (int x = tx - 2; x <= tx + 2; x++)
			{
				for (int z = tz - 2; z <= tz + 2; z++)
				{
					plan.Lots.Collect(x, z, lots);
					foreach (Lot lot in lots)
					{
						checkedLots++;
						for (int c = 0; c < 4; c++)
						{
							double u = ((c & 1) == 0) ? -lot.HalfU : lot.HalfU;
							double v = ((c & 2) == 0) ? -lot.HalfV : lot.HalfV;
							double px = lot.Center.X + lot.Axis.X * u - lot.Axis.Z * v;
							double pz = lot.Center.Z + lot.Axis.Z * u + lot.Axis.X * v;
							Assert.IsFalse(plan.Chasms.Void(px, pz));
						}
						Assert.IsFalse(plan.Chasms.Void(lot.Center.X, lot.Center.Z));
					}
				}
			}
		}
		Assert.Greater(checkedLots, 0);
	}

	[Test]
	public void PodiumsFitTheirStairs()
	{
		LayerPlan plan = Layer0();
		List<Lot> lots = new List<Lot>();
		int podiums = 0;
		for (int x = 10; x < 40; x++)
		{
			for (int z = 10; z < 40; z++)
			{
				plan.Lots.Collect(x, z, lots);
				foreach (Lot lot in lots)
				{
					if (lot.Base > 0.0)
					{
						podiums++;
						Assert.AreEqual(LotKind.Building, lot.Kind);
						Assert.GreaterOrEqual(lot.Base, 1.0);
						Assert.LessOrEqual(lot.Base, 20.0);
						Assert.LessOrEqual(lot.Base * 1.5, System.Math.Max(lot.HalfU, lot.HalfV) * 2.0 + 1e-9);
					}
				}
			}
		}
		Assert.Greater(podiums, 0);
	}

	// frozen genVersion 2 output. a failure here means v2 saves or mixed-version co-op would break: bump genVersion instead
	[Test]
	public void GoldenValues()
	{
		StringBuilder text = new StringBuilder();
		foreach (Chasm chasm in All(Layer0(), 5))
		{
			text.Append(chasm.Shaft).Append(' ').Append(chasm.CellX).Append(',').Append(chasm.CellZ).Append(' ').Append(F(chasm.A.X)).Append(',').Append(F(chasm.A.Z)).Append(' ').Append(F(chasm.M.X)).Append(',').Append(F(chasm.M.Z)).Append(' ').Append(F(chasm.B.X)).Append(',').Append(F(chasm.B.Z)).Append(' ').Append(F(chasm.Half)).Append(' ').Append(F(chasm.HalfU)).Append(' ').Append(F(chasm.HalfV)).Append('\n');
		}
		LayerPlan plan = Layer0();
		for (int i = 0; i < 200; i++)
		{
			text.Append(F(plan.Chasms.Inside(i * 37.0 - 3000.0, i * 23.0 - 2000.0))).Append('\n');
		}
		ulong hash = 14695981039346656037uL;
		foreach (char c in text.ToString())
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		Assert.AreEqual(0xf93c353eab551c6auL, hash, "layer 0 voids");
	}

	private static string F(double v)
	{
		return v.ToString("R", CultureInfo.InvariantCulture);
	}
}
