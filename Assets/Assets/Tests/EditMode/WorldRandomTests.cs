using NUnit.Framework;

public class WorldRandomTests
{
	[Test]
	public void SameSeedSameSequence()
	{
		WorldRandom a = new WorldRandom(12345uL);
		WorldRandom b = new WorldRandom(12345uL);
		for (int i = 0; i < 100; i++)
		{
			Assert.AreEqual(a.NextULong(), b.NextULong());
		}
	}

	// splitmix64 reference value, catches any change to the mixing constants
	[Test]
	public void KnownFirstValue()
	{
		Assert.AreEqual(0xE220A8397B1DCDAFuL, new WorldRandom(0uL).NextULong());
	}

	// item spawners and layout nodes get their streams from this, saves depend on it
	[Test]
	public void KnownDerivedValues()
	{
		Assert.AreEqual(0x0A52A04FC9A316A0uL, WorldRandom.Derive(123uL, 5uL));
		Assert.AreEqual(0x7CCF8825D4A59560uL, WorldRandom.Derive(123uL, 5uL, 7uL));
		Assert.AreEqual(0x21931300C88F9AEBuL, WorldRandom.Derive(ulong.MaxValue, 1uL, 2uL));
	}

	[Test]
	public void NextStaysInRange()
	{
		WorldRandom random = new WorldRandom(99uL);
		for (int i = 0; i < 10000; i++)
		{
			int n = random.Next(7);
			Assert.IsTrue(n >= 0 && n < 7);
		}
	}

	// rolls 0..total inclusive so the first entry gets one extra share, [0,1,1] gives each a third
	[Test]
	public void WeightedIndexKeepsFirstEntryShare()
	{
		WorldRandom random = new WorldRandom(7uL);
		int[] weights = { 0, 1, 1 };
		int[] counts = new int[3];
		for (int i = 0; i < 30000; i++)
		{
			counts[random.NextWeightedIndex(weights)]++;
		}
		foreach (int count in counts)
		{
			Assert.AreEqual(10000, count, 500);
		}
	}
}
