using NUnit.Framework;

public class DayClockTests
{
	private const double second = 1.0 / DayClock.DayLength;

	[Test]
	public void PhasesMatchTheDesign()
	{
		Assert.AreEqual(DayPhase.Night, DayClock.PhaseOf(0.0));
		Assert.AreEqual(DayPhase.Night, DayClock.PhaseOf(0.19));
		Assert.AreEqual(DayPhase.Dawn, DayClock.PhaseOf(0.20));
		Assert.AreEqual(DayPhase.Day, DayClock.PhaseOf(0.30));
		Assert.AreEqual(DayPhase.Dusk, DayClock.PhaseOf(0.85));
		Assert.AreEqual(DayPhase.Night, DayClock.PhaseOf(0.95));
	}

	[Test]
	public void NewGameIsDuskOfDayOne()
	{
		Assert.AreEqual(1, DayClock.Day(DayClock.NewGame));
		Assert.AreEqual(DayPhase.Dusk, DayClock.PhaseOf(DayClock.TimeOfDay(DayClock.NewGame)));
		Assert.AreEqual(DayPhase.Day, DayClock.PhaseOf(DayClock.TimeOfDay(DayClock.OldSave)));
	}

	[Test]
	public void ADayTakesTwentyMinutesAndCountsAtMidnight()
	{
		double total = DayClock.Tick(1.5, 1200.0, 1.0);
		Assert.AreEqual(2, DayClock.Day(total));
		Assert.AreEqual(0.5, DayClock.TimeOfDay(total), 1e-9);
		Assert.AreEqual(2, DayClock.Day(DayClock.Tick(1.99, 60.0, 1.0)));
		Assert.AreEqual(1.5, DayClock.Tick(1.5, 100.0, 0.0), 1e-12);
	}

	[Test]
	public void FriendBehindCatchesUp()
	{
		double friend = 1.5;
		double host = 1.5 + 5.0 * second;
		for (int i = 0; i < 3600; i++)
		{
			host = DayClock.Tick(host, 1.0 / 60.0, 1.0);
			friend = DayClock.Follow(friend, host, 1.0 / 60.0, 1.0);
		}
		Assert.AreEqual(host, friend, 0.1 * second);
	}

	[Test]
	public void FriendAheadWaitsAndNeverRunsBack()
	{
		double friend = 1.5 + 5.0 * second;
		double host = 1.5;
		for (int i = 0; i < 3600; i++)
		{
			host = DayClock.Tick(host, 1.0 / 60.0, 1.0);
			double next = DayClock.Follow(friend, host, 1.0 / 60.0, 1.0);
			Assert.GreaterOrEqual(next, friend);
			friend = next;
		}
		Assert.AreEqual(host, friend, 0.1 * second);
	}

	[Test]
	public void FarOffSnaps()
	{
		Assert.AreEqual(1.6, DayClock.Follow(1.5, 1.6, 1.0 / 60.0, 1.0), 1e-12);
		Assert.AreEqual(1.4, DayClock.Follow(1.5, 1.4, 1.0 / 60.0, 1.0), 1e-12);
	}

	[Test]
	public void SectionRoundTripsAndOldSavesGetDaylight()
	{
		WorldClock.ResetForLoad();
		Assert.AreEqual(DayPhase.Day, WorldClock.Phase);
		StateWriter writer = new StateWriter();
		writer.Put(4.9);
		new WorldClock.State().Read(new StateReader(writer.ToArray()), 1);
		Assert.AreEqual(4, WorldClock.Day);
		Assert.AreEqual(DayPhase.Dusk, WorldClock.Phase);
		StateWriter written = new StateWriter();
		new WorldClock.State().Write(written);
		Assert.AreEqual(writer.ToArray(), written.ToArray());
		WorldClock.ResetLevel();
		Assert.AreEqual(1, WorldClock.Day);
		Assert.AreEqual(DayPhase.Dusk, WorldClock.Phase);
	}

	[Test]
	public void BadSectionIsRefused()
	{
		StateWriter writer = new StateWriter();
		writer.Put(double.NaN);
		Assert.Throws<System.IO.InvalidDataException>(() => new WorldClock.State().Read(new StateReader(writer.ToArray()), 1));
	}

	[Test]
	public void AdvanceSkipsAheadOnly()
	{
		WorldClock.ResetLevel();
		Assert.IsFalse(WorldClock.Advance(-5.0));
		Assert.IsTrue(WorldClock.Advance(120.0));
		Assert.AreEqual(0.95, WorldClock.TimeOfDay, 1e-9);
		Assert.AreEqual(DayPhase.Night, WorldClock.Phase);
		WorldClock.ResetLevel();
	}
}
