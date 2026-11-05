using System;

// what running out of something costs, as plain math (17 section 7, 09 failure flow). a setback, never a loss
public static class NeedsRules
{
	// out of something this long, slow and blurred, and she's down
	public const float ExhaustedFor = 30f;

	// up again after a revive
	public const float RevivedLevel = 25f;

	// up again after a night in the snow
	public const float WokenLevel = 60f;

	// a fall into a void, off each of the three
	public const float FallCost = 8f;

	// a fall never finishes her off by itself
	public const float FallFloor = 1f;

	// a night's worth, about a day's eating for two
	public const int NightFood = 2;

	// a tank's about 2 days
	public const float NightFuelShare = 0.5f;

	// never left unable to drive on
	public const float FuelFloorShare = 0.1f;

	public static bool Out(float hunger, float thirst, float warmth)
	{
		return hunger <= 0f || thirst <= 0f || warmth <= 0f;
	}

	public static bool DueDown(float outFor)
	{
		return outFor >= ExhaustedFor;
	}

	public static int FoodAfterNight(int food)
	{
		return Math.Max(0, food - NightFood);
	}

	public static float FuelAfterNight(float fuel, float max)
	{
		float left = fuel - max * NightFuelShare;
		return Math.Max(left, Math.Min(fuel, max * FuelFloorShare));
	}

	public static float AfterFall(float level)
	{
		return (level <= FallFloor) ? level : Math.Max(FallFloor, level - FallCost);
	}

	// the coming morning: past dawn it's tomorrow's, in the small hours it's today's
	public static double NextMorning(double total)
	{
		double day = Math.Floor(total);
		return (total - day < DayClock.DayStart) ? day + DayClock.DayStart : day + 1.0 + DayClock.DayStart;
	}
}
