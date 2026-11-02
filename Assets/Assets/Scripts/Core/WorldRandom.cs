using System.Collections.Generic;

// splitmix64, same on every machine (UnityEngine.Random / System.Random aren't)
public sealed class WorldRandom
{
	private ulong state;

	public WorldRandom(ulong seed)
	{
		state = seed;
	}

	public static ulong Derive(ulong seed, ulong stream, ulong index = 0)
	{
		// no object: a cold tile comes here thousands of times
		return Mix(seed ^ (stream * 0x9E3779B97F4A7C15uL) ^ (index * 0xD1B54A32D192ED03uL));
	}

	public ulong NextULong()
	{
		ulong num = Mix(state);
		state += 0x9E3779B97F4A7C15uL;
		return num;
	}

	private static ulong Mix(ulong state)
	{
		ulong num = state + 0x9E3779B97F4A7C15uL;
		num = (num ^ (num >> 30)) * 0xBF58476D1CE4E5B9uL;
		num = (num ^ (num >> 27)) * 0x94D049BB133111EBuL;
		return num ^ (num >> 31);
	}

	public int Next(int max)
	{
		return (int)(NextULong() % (ulong)max);
	}

	// same as RandomWeightedGenerator.GenerateRandomIndex, which rolls 0..total inclusive
	// so the first entry gets one extra share. balance depends on it ([0,1,1] still gives food 1/3)
	public int NextWeightedIndex(IList<int> weights)
	{
		int num = 0;
		foreach (int weight in weights)
		{
			num += weight;
		}
		int num2 = Next(num + 1);
		int num3 = 0;
		for (int i = 0; i < weights.Count; i++)
		{
			num3 += weights[i];
			if (num2 <= num3)
			{
				return i;
			}
		}
		return 0;
	}
}
