using NUnit.Framework;
using UnityEngine;

public class DayCurveTests
{
	[Test]
	public void TheDayLooksAsItAlwaysDid()
	{
		foreach (double time in new[] { 0.30, 0.45, 0.575, 0.7, 0.85 })
		{
			DayLook look = DayCurve.At(time);
			Assert.AreEqual(1f, look.Light, 1e-4f);
			Assert.AreEqual(1f, look.Ambient, 1e-4f);
			Assert.AreEqual(Color.white, look.Fog);
			Assert.AreEqual(0f, look.Stars, 1e-4f);
		}
		DayLook noon = DayCurve.Noon;
		Assert.AreEqual(0f, noon.Round, 1e-3f);
		Assert.AreEqual(1f, noon.Height, 1e-3f);
	}

	[Test]
	public void NightIsDarkWithStars()
	{
		foreach (double time in new[] { 0.97, 0.0, 0.1, 0.17 })
		{
			DayLook look = DayCurve.At(time);
			Assert.IsTrue(look.IsMoon);
			Assert.Less(look.Light, 0.1f);
			Assert.Less(look.Ambient, 0.2f);
			Assert.Less(look.Fog.grayscale, 0.15f);
			Assert.AreEqual(1f, look.Stars, 1e-3f);
		}
	}

	[Test]
	public void NothingJumpsAndTheLightSwapsInTheDark()
	{
		const int steps = 12000;
		DayLook last = DayCurve.At(0.0);
		for (int i = 1; i <= steps; i++)
		{
			DayLook look = DayCurve.At((double)i / steps % 1.0);
			Assert.Less(Mathf.Abs(look.Ambient - last.Ambient), 0.01f, "ambient at " + i);
			Assert.Less(Mathf.Abs(look.Fog.r - last.Fog.r), 0.01f, "fog at " + i);
			Assert.Less(Mathf.Abs(look.Stars - last.Stars), 0.01f, "stars at " + i);
			Assert.Less(Mathf.Abs(look.Light - last.Light), 0.01f, "light at " + i);
			if (look.IsMoon != last.IsMoon)
			{
				Assert.Less(look.Light, 0.01f, "swap at " + i);
			}
			last = look;
		}
	}

	[Test]
	public void ColdIsThreeTimesAtNightAndEases()
	{
		Assert.AreEqual(1f, DayCurve.Cold(0.5), 1e-4f);
		Assert.AreEqual(1f, DayCurve.Cold(0.30), 1e-4f);
		Assert.AreEqual(3f, DayCurve.Cold(0.0), 1e-4f);
		Assert.AreEqual(3f, DayCurve.Cold(0.97), 1e-4f);
		Assert.AreEqual(2f, DayCurve.Cold(0.90), 1e-3f);
		Assert.AreEqual(2f, DayCurve.Cold(0.25), 1e-3f);
	}
}
