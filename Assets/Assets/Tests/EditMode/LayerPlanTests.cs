using System;
using System.Collections.Generic;
using NUnit.Framework;

// world gen v2 plan: districts and the pillar grid. golden values freeze the genVersion 2 output
public class LayerPlanTests
{
	private const ulong seed = 12345uL;

	private static LayerPlan[] Stack(ulong worldSeed)
	{
		LayerPlan l0 = new LayerPlan(worldSeed, 0, new XZ(0.0, 0.0), null);
		LayerPlan l1 = new LayerPlan(worldSeed, 1, new XZ(7300.0, -2100.0), l0);
		LayerPlan l2 = new LayerPlan(worldSeed, 2, new XZ(-4000.0, 9100.0), l1);
		return new[] { l0, l1, l2 };
	}

	[Test]
	public void SameInputsSameAnswersInAnyOrder()
	{
		LayerPlan a = Stack(seed)[1];
		List<string> forward = new List<string>();
		for (int x = -6; x <= 6; x++)
		{
			for (int z = -6; z <= 6; z++)
			{
				PillarInfo p = a.Pillar(x, z);
				forward.Add(x + "," + z + " " + p.Position + " " + p.State + " " + a.DistrictAt(x * 900.0, z * 700.0));
			}
		}
		// a fresh plan, walked backwards
		LayerPlan b = Stack(seed)[1];
		List<string> backward = new List<string>();
		for (int x = 6; x >= -6; x--)
		{
			for (int z = 6; z >= -6; z--)
			{
				PillarInfo p = b.Pillar(x, z);
				backward.Add(x + "," + z + " " + p.Position + " " + p.State + " " + b.DistrictAt(x * 900.0, z * 700.0));
			}
		}
		backward.Reverse();
		CollectionAssert.AreEqual(forward, backward);
	}

	[Test]
	public void PillarsLineUpThroughLayers()
	{
		LayerPlan[] stack = Stack(seed);
		for (int x = -5; x <= 5; x++)
		{
			for (int z = -5; z <= 5; z++)
			{
				XZ p0 = stack[0].Pillar(x, z).Position;
				Assert.AreEqual(p0, stack[1].Pillar(x, z).Position);
				Assert.AreEqual(p0, stack[2].Pillar(x, z).Position);
				Assert.GreaterOrEqual(p0.X, x * Gen2.RegionSize + Gen2.PillarMargin);
				Assert.LessOrEqual(p0.X, (x + 1) * Gen2.RegionSize - Gen2.PillarMargin);
				Assert.GreaterOrEqual(p0.Z, z * Gen2.RegionSize + Gen2.PillarMargin);
				Assert.LessOrEqual(p0.Z, (z + 1) * Gen2.RegionSize - Gen2.PillarMargin);
			}
		}
	}

	[Test]
	public void PillarStates()
	{
		LayerPlan[] stack = Stack(seed);
		int up = 0;
		int eligible = 0;
		for (int x = -15; x <= 15; x++)
		{
			for (int z = -15; z <= 15; z++)
			{
				PillarInfo p0 = stack[0].Pillar(x, z);
				PillarInfo p1 = stack[1].Pillar(x, z);
				PillarInfo p2 = stack[2].Pillar(x, z);
				Assert.AreEqual(PillarState.Solid, p0.State & PillarState.Down, "nothing below layer 0");
				Assert.AreEqual(PillarState.Solid, p2.State & PillarState.Up, "last layer has no Up");
				Assert.AreEqual((p0.State & PillarState.Up) != 0, (p1.State & PillarState.Down) != 0, "Down = Up of the layer below");
				Assert.AreEqual((p1.State & PillarState.Up) != 0, (p2.State & PillarState.Down) != 0, "Down = Up of the layer below");
				if (stack[0].Progress(p0.Position.X, p0.Position.Z) >= stack[0].Def.ExitMinDistance)
				{
					eligible++;
					if ((p0.State & PillarState.Up) != 0)
					{
						up++;
					}
				}
				else
				{
					Assert.AreEqual(PillarState.Solid, p0.State & PillarState.Up, "no Up before ExitMinDistance");
				}
			}
		}
		Assert.Greater(eligible, 500);
		Assert.AreEqual(stack[0].Def.UpChance, (double)up / eligible, 0.06);
	}

	[Test]
	public void WarpStaysInBounds()
	{
		LayerPlan plan = Stack(seed)[0];
		double min = 0.0;
		double max = 0.0;
		for (int i = 0; i < 4000; i++)
		{
			double w = plan.Warp(i * 37.3 - 70000.0, i * -91.7 + 20000.0);
			min = Math.Min(min, w);
			max = Math.Max(max, w);
		}
		Assert.GreaterOrEqual(min, -Gen2.WarpAmplitude);
		Assert.LessOrEqual(max, Gen2.WarpAmplitude);
		// it actually wobbles
		Assert.Less(min, -100.0);
		Assert.Greater(max, 100.0);
	}

	[Test]
	public void DistrictsFollowDistance()
	{
		LayerPlan plan = Stack(seed)[0];
		for (int i = 0; i < 64; i++)
		{
			double angle = i * (Math.PI * 2.0 / 64.0);
			double dx = Math.Cos(angle);
			double dz = Math.Sin(angle);
			// bands are 1.2 km+ wide and the warp is +-300 m, so the band middles are always right
			Assert.AreEqual(0, plan.DistrictAt(dx * 500.0, dz * 500.0));
			Assert.AreEqual(1, plan.DistrictAt(dx * 1900.0, dz * 1900.0));
			Assert.AreEqual(2, plan.DistrictAt(dx * 3200.0, dz * 3200.0));
			Assert.AreEqual(3, plan.DistrictAt(dx * 4400.0, dz * 4400.0));
			Assert.AreEqual(4, plan.DistrictAt(dx * 5600.0, dz * 5600.0));
			// the far district repeats forever
			Assert.AreEqual(4, plan.DistrictAt(dx * 250000.0, dz * 250000.0));
		}
	}

	[Test]
	public void BlendNearBoundaries()
	{
		LayerPlan plan = Stack(seed)[0];
		DistrictSample deep = plan.SampleDistrict(300.0, 0.0);
		if (plan.Progress(300.0, 0.0) < 1200.0 - Gen2.BlendWidth)
		{
			Assert.AreEqual(deep.Primary, deep.Secondary);
			Assert.AreEqual(0f, deep.Blend);
		}
		for (int i = 0; i < 2000; i++)
		{
			DistrictSample s = plan.SampleDistrict(i * 5.0, 0.0);
			Assert.GreaterOrEqual(s.Blend, 0f);
			Assert.LessOrEqual(s.Blend, 0.5f);
			Assert.LessOrEqual(Math.Abs(s.Secondary - s.Primary), 1);
		}
	}

	[Test]
	public void LayerNeedsTheLayerBelow()
	{
		Assert.Throws<ArgumentException>(() => new LayerPlan(seed, 1, new XZ(0.0, 0.0), null));
		LayerPlan l0 = new LayerPlan(seed, 0, new XZ(0.0, 0.0), null);
		Assert.Throws<ArgumentException>(() => new LayerPlan(seed + 1, 1, new XZ(0.0, 0.0), l0));
	}

	[Test]
	public void PlanHashChangesWithInputs()
	{
		LayerPlan[] stack = Stack(seed);
		ulong h = stack[1].PlanHash(new XZ(100.0, 100.0), 1);
		Assert.AreEqual(h, Stack(seed)[1].PlanHash(new XZ(100.0, 100.0), 1));
		// same region cell, same hash
		Assert.AreEqual(h, stack[1].PlanHash(new XZ(1500.0, 1500.0), 1));
		Assert.AreNotEqual(h, stack[1].PlanHash(new XZ(1700.0, 100.0), 1));
		Assert.AreNotEqual(h, stack[0].PlanHash(new XZ(100.0, 100.0), 1));
		Assert.AreNotEqual(h, Stack(seed + 1)[1].PlanHash(new XZ(100.0, 100.0), 1));
		Assert.AreNotEqual(h, new LayerPlan(seed, 1, new XZ(7300.0, -2000.0), stack[0]).PlanHash(new XZ(100.0, 100.0), 1));
	}

	// frozen genVersion 2 output, cross-checked against a separate python port.
	// a failure here means v2 saves or mixed-version co-op would break: bump genVersion instead
	[Test]
	public void GoldenValues()
	{
		LayerPlan[] stack = Stack(seed);
		PillarInfo pillar = stack[0].Pillar(3, -2);
		Assert.AreEqual(5302.082255563856, pillar.Position.X);
		Assert.AreEqual(-2235.8773276292013, pillar.Position.Z);
		Assert.AreEqual(PillarState.Up, pillar.State);
		Assert.AreEqual(93.7593734312775, stack[1].Warp(1234.5, -678.9));
		Assert.AreEqual(0x68e79e103c60b619uL, stack[2].LayerSeed);
		Assert.AreEqual(0x44a4b0a5590e73a5uL, stack[0].PlanHash(new XZ(6000.0, 2500.0), 2));
		Assert.AreEqual(0x9592be5f3f908847uL, stack[1].PlanHash(new XZ(6000.0, 2500.0), 2));
		Assert.AreEqual(0xa9bfbc3612ad2695uL, stack[2].PlanHash(new XZ(6000.0, 2500.0), 2));
	}
}
