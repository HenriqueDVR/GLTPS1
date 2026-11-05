using System;

public enum DayPhase
{
	Dawn,
	Day,
	Dusk,
	Night
}

// the time of day as plain math. total = day count + time of day, 0 = midnight, 17 section 1 has the phases
public static class DayClock
{
	public const double DayLength = 1200.0;

	public const double DawnStart = 0.20;

	public const double DayStart = 0.30;

	public const double DuskStart = 0.85;

	public const double NightStart = 0.95;

	// a new game starts on day 1, mid-morning, with most of the day ahead
	public const double NewGame = 1.0 + 0.35;

	// a save from before the clock
	public const double OldSave = 1.0 + DayStart;

	// a friend further off than this is put right, nearer it blends
	public const double SnapSeconds = 30.0;

	public const double BlendSeconds = 10.0;

	public static int Day(double total)
	{
		return (int)Math.Floor(total);
	}

	public static double TimeOfDay(double total)
	{
		return total - Math.Floor(total);
	}

	public static DayPhase PhaseOf(double timeOfDay)
	{
		if (timeOfDay < DawnStart || timeOfDay >= NightStart)
		{
			return DayPhase.Night;
		}
		if (timeOfDay < DayStart)
		{
			return DayPhase.Dawn;
		}
		return (timeOfDay < DuskStart) ? DayPhase.Day : DayPhase.Dusk;
	}

	public static double Tick(double total, double seconds, double rate)
	{
		return total + seconds * rate / DayLength;
	}

	// a friend's clock, one frame on, heading for the host's. never runs backwards on a small difference, it waits
	public static double Follow(double total, double host, double seconds, double rate)
	{
		double off = host - total;
		if (Math.Abs(off) * DayLength > SnapSeconds)
		{
			return host;
		}
		double step = seconds * rate / DayLength + off * Math.Min(1.0, seconds / BlendSeconds);
		return total + Math.Max(0.0, step);
	}
}
