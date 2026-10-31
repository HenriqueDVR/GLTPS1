using NUnit.Framework;

// layers reached by riding the elevators (WP-WORLD-3)
public class LayerStackTests
{
	private const ulong seed = 12345uL;

	// nearest pillar to the origin with the state, by ring
	private static PillarInfo Find(LayerPlan plan, PillarState want, PillarInfo skip = default(PillarInfo))
	{
		for (int ring = 0; ring < 20; ring++)
		{
			for (int x = -ring; x <= ring; x++)
			{
				for (int z = -ring; z <= ring; z++)
				{
					if (System.Math.Max(System.Math.Abs(x), System.Math.Abs(z)) != ring || (x == skip.CellX && z == skip.CellZ && skip.State != PillarState.Solid))
					{
						continue;
					}
					PillarInfo p = plan.Pillar(x, z);
					if ((p.State & want) == want && (want != PillarState.Solid || p.State == PillarState.Solid))
					{
						return p;
					}
				}
			}
		}
		Assert.Fail("no " + want + " pillar");
		return default(PillarInfo);
	}

	[Test]
	public void UpGoesToANewLayerFromThatPillar()
	{
		LayerStack stack = new LayerStack(new LayerPlan(seed, 0, new XZ(0.0, 0.0), null));
		PillarInfo up = Find(stack.Get(0), PillarState.Up);
		LayerPlan one = stack.Ride(0, up.CellX, up.CellZ, 1);
		Assert.NotNull(one);
		Assert.AreEqual(1, one.Layer);
		Assert.AreEqual(2, stack.Count);
		Assert.AreEqual(up.Position, one.Origin);
		Assert.AreSame(stack.Get(0), one.Below);
		// arrived through it, so it goes back down from there
		PillarInfo there = one.Pillar(up.CellX, up.CellZ);
		Assert.AreNotEqual(0, (int)(there.State & PillarState.Down));
		Assert.AreEqual(-1, LayerStack.Way(PillarState.Down));
		Assert.AreSame(stack.Get(0), stack.Ride(1, up.CellX, up.CellZ, -1));
	}

	[Test]
	public void LayerKeepsTheOriginItWasFirstReachedBy()
	{
		LayerStack stack = new LayerStack(new LayerPlan(seed, 0, new XZ(0.0, 0.0), null));
		PillarInfo first = Find(stack.Get(0), PillarState.Up);
		PillarInfo second = Find(stack.Get(0), PillarState.Up, first);
		LayerPlan one = stack.Ride(0, first.CellX, first.CellZ, 1);
		Assert.AreSame(one, stack.Ride(0, second.CellX, second.CellZ, 1));
		Assert.AreEqual(first.Position, one.Origin);
		Assert.AreEqual(2, stack.Count);
	}

	[Test]
	public void OnlyWorkingElevatorsGoAnywhere()
	{
		LayerStack stack = new LayerStack(new LayerPlan(seed, 0, new XZ(0.0, 0.0), null));
		PillarInfo solid = Find(stack.Get(0), PillarState.Solid);
		Assert.IsNull(stack.Ride(0, solid.CellX, solid.CellZ, 1));
		Assert.IsNull(stack.Ride(0, solid.CellX, solid.CellZ, -1));
		Assert.AreEqual(0, LayerStack.Way(solid.State));
		Assert.AreEqual(1, LayerStack.Way(PillarState.Up | PillarState.Down));
		// nothing under layer 0
		PillarInfo up = Find(stack.Get(0), PillarState.Up);
		Assert.IsNull(stack.Ride(0, up.CellX, up.CellZ, -1));
		Assert.AreEqual(1, stack.Count);
	}

	[Test]
	public void LastLayerHasNoWayUp()
	{
		LayerStack stack = new LayerStack(new LayerPlan(seed, 0, new XZ(0.0, 0.0), null));
		PillarInfo up0 = Find(stack.Get(0), PillarState.Up);
		LayerPlan one = stack.Ride(0, up0.CellX, up0.CellZ, 1);
		PillarInfo up1 = Find(one, PillarState.Up);
		LayerPlan two = stack.Ride(1, up1.CellX, up1.CellZ, 1);
		Assert.True(two.Def.IsLast);
		for (int x = -8; x <= 8; x++)
		{
			for (int z = -8; z <= 8; z++)
			{
				Assert.AreEqual(0, (int)(two.Pillar(x, z).State & PillarState.Up));
			}
		}
		Assert.AreEqual(2.0 * Gen2.LayerHeight, LayerStack.Rise(2));
	}
}
