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
}
