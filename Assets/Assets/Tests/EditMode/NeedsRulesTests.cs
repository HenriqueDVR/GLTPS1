using NUnit.Framework;

public class NeedsRulesTests
{
	[Test]
	public void OutOfAnyOneOfThem()
	{
		Assert.IsFalse(NeedsRules.Out(1f, 1f, 1f));
		Assert.IsTrue(NeedsRules.Out(0f, 1f, 1f));
		Assert.IsTrue(NeedsRules.Out(1f, -0.1f, 1f));
		Assert.IsTrue(NeedsRules.Out(1f, 1f, 0f));
	}

	[Test]
	public void DownOnlyAfterBeingExhausted()
	{
		Assert.IsFalse(NeedsRules.DueDown(0f));
		Assert.IsFalse(NeedsRules.DueDown(NeedsRules.ExhaustedFor - 0.01f));
		Assert.IsTrue(NeedsRules.DueDown(NeedsRules.ExhaustedFor));
	}

	[Test]
	public void ANightCostsFoodAndFuelButNotTheWayOn()
	{
		Assert.AreEqual(3, NeedsRules.FoodAfterNight(5));
		Assert.AreEqual(0, NeedsRules.FoodAfterNight(1));
		Assert.AreEqual(50f, NeedsRules.FuelAfterNight(100f, 100f));
		// never under the floor because of the night
		Assert.AreEqual(10f, NeedsRules.FuelAfterNight(40f, 100f));
		// but a tank already under it isn't filled
		Assert.AreEqual(4f, NeedsRules.FuelAfterNight(4f, 100f));
	}

	[Test]
	public void AFallNeverFinishesHerOff()
	{
		Assert.AreEqual(42f, NeedsRules.AfterFall(50f));
		Assert.AreEqual(NeedsRules.FallFloor, NeedsRules.AfterFall(3f));
		Assert.AreEqual(0.5f, NeedsRules.AfterFall(0.5f));
	}

	[Test]
	public void MorningIsTheNextOne()
	{
		Assert.AreEqual(4.3, NeedsRules.NextMorning(3.9), 1e-9);
		Assert.AreEqual(4.3, NeedsRules.NextMorning(3.31), 1e-9);
		// the small hours: this morning, not tomorrow's
		Assert.AreEqual(4.3, NeedsRules.NextMorning(4.1), 1e-9);
	}
}
