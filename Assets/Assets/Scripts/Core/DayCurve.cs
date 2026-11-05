using UnityEngine;

// what the time of day does to the look. every value's a factor on the rest of the stack, 1 and white in the day, so the
// day looks as it always did. 04 has the order, 17 section 1 the phases
public struct DayLook
{
	public float Light;

	public Color LightColour;

	public float Ambient;

	public Color AmbientColour;

	public Color Fog;

	public float Stars;

	// the light's direction: degrees round from the midday one, and how high it stands against that
	public float Round;

	public float Height;

	public bool IsMoon;
}

public static class DayCurve
{
	// where the sun's where the scene put it
	private const double midday = (DayClock.DayStart + DayClock.DuskStart) / 2.0;

	private const float sunSwing = 70f;

	// how much of its height it loses at each end of the day
	private const float sunDrop = 0.6f;

	private const float moonRound = 150f;

	private const float moonHeight = 2f;

	private const float moonLight = 0.05f;

	private const float nightCold = 3f;


	private struct Key
	{
		public double At;

		public float Sun;

		public float Moon;

		public Color SunColour;

		public float Ambient;

		public Color AmbientColour;

		public Color Fog;

		public float Stars;

		public Key(double at, float sun, float moon, Color sunColour, float ambient, Color ambientColour, Color fog, float stars)
		{
			At = at;
			Sun = sun;
			Moon = moon;
			SunColour = sunColour;
			Ambient = ambient;
			AmbientColour = ambientColour;
			Fog = fog;
			Stars = stars;
		}
	}

	private static readonly Color white = Color.white;

	private static readonly Color lowSun = new Color(1f, 0.88f, 0.8f);

	private static readonly Color nightSky = new Color(0.55f, 0.65f, 1f);

	private static readonly Color nightFog = new Color(0.05f, 0.07f, 0.13f);

	// 0 and 1 are the same, midnight
	private static readonly Key[] keys =
	{
		new Key(0.0, 0f, moonLight, lowSun, 0.14f, nightSky, nightFog, 1f),
		new Key(0.18, 0f, moonLight, lowSun, 0.14f, nightSky, nightFog, 1f),
		new Key(DayClock.DawnStart, 0f, 0f, lowSun, 0.16f, nightSky, new Color(0.09f, 0.1f, 0.16f), 0.7f),
		new Key(0.25, 0.45f, 0f, lowSun, 0.5f, new Color(0.9f, 0.88f, 0.95f), new Color(0.62f, 0.6f, 0.62f), 0f),
		new Key(DayClock.DayStart, 1f, 0f, white, 1f, white, white, 0f),
		new Key(DayClock.DuskStart, 1f, 0f, white, 1f, white, white, 0f),
		new Key(0.90, 0.5f, 0f, lowSun, 0.5f, new Color(0.88f, 0.86f, 0.95f), new Color(0.5f, 0.46f, 0.5f), 0f),
		new Key(DayClock.NightStart, 0f, 0f, lowSun, 0.18f, nightSky, new Color(0.08f, 0.1f, 0.17f), 0.8f),
		new Key(0.97, 0f, moonLight, lowSun, 0.14f, nightSky, nightFog, 1f),
		new Key(1.0, 0f, moonLight, lowSun, 0.14f, nightSky, nightFog, 1f)
	};

	public static DayLook Noon => At(midday);

	public static DayLook At(double timeOfDay)
	{
		int next = 1;
		while (next < keys.Length - 1 && timeOfDay >= keys[next].At)
		{
			next++;
		}
		Key a = keys[next - 1];
		Key b = keys[next];
		float t = Mathf.SmoothStep(0f, 1f, (float)((timeOfDay - a.At) / (b.At - a.At)));
		float sun = Mathf.Lerp(a.Sun, b.Sun, t);
		float moon = Mathf.Lerp(a.Moon, b.Moon, t);
		bool isMoon = timeOfDay < DayClock.DawnStart || timeOfDay >= DayClock.NightStart;
		// the sun's whole arc, dawn to the end of dusk, centred on midday
		float along = (float)((SunTime(timeOfDay) - midday) / ((DayClock.NightStart - DayClock.DawnStart) / 2.0));
		return new DayLook
		{
			Light = isMoon ? moon : sun,
			LightColour = isMoon ? nightSky : Color.Lerp(a.SunColour, b.SunColour, t),
			Ambient = Mathf.Lerp(a.Ambient, b.Ambient, t),
			AmbientColour = Color.Lerp(a.AmbientColour, b.AmbientColour, t),
			Fog = Color.Lerp(a.Fog, b.Fog, t),
			Stars = Mathf.Lerp(a.Stars, b.Stars, t),
			Round = isMoon ? moonRound : along * sunSwing,
			Height = isMoon ? moonHeight : 1f - along * along * sunDrop,
			IsMoon = isMoon
		};
	}

	private static double SunTime(double timeOfDay)
	{
		return timeOfDay;
	}

	// how much faster the cold bites out in the open. rises over dusk, eases over dawn
	public static float Cold(double timeOfDay)
	{
		float day = 0f;
		if (timeOfDay >= DayClock.DuskStart)
		{
			day = Mathf.InverseLerp((float)DayClock.DuskStart, (float)DayClock.NightStart, (float)timeOfDay);
		}
		else if (timeOfDay < DayClock.DayStart)
		{
			day = 1f - Mathf.InverseLerp((float)DayClock.DawnStart, (float)DayClock.DayStart, (float)timeOfDay);
		}
		return Mathf.Lerp(1f, nightCold, day);
	}
}
