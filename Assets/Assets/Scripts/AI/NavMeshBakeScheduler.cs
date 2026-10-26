using System.Collections.Generic;
using UnityEngine;

// one navmesh build in the background at a time, whoever asked first goes first.
// two bakers building in the same frame was double the hitch
public static class NavMeshBakeScheduler
{
	private static readonly List<NavMeshAreaBaker> waiting = new List<NavMeshAreaBaker>();

	private static NavMeshAreaBaker running;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void Reset()
	{
		waiting.Clear();
		running = null;
	}

	public static void Request(NavMeshAreaBaker baker)
	{
		if (!waiting.Contains(baker))
		{
			waiting.Add(baker);
		}
	}

	// true once it's this baker's turn, it then owns the slot until Finished
	public static bool TryStart(NavMeshAreaBaker baker)
	{
		// a baker destroyed mid build without Forget
		if (running == null && !ReferenceEquals(running, null))
		{
			running = null;
		}
		for (int i = waiting.Count - 1; i >= 0; i--)
		{
			if (waiting[i] == null)
			{
				waiting.RemoveAt(i);
			}
		}
		if (running != null || waiting.Count == 0 || waiting[0] != baker)
		{
			return false;
		}
		waiting.RemoveAt(0);
		running = baker;
		return true;
	}

	public static void Finished(NavMeshAreaBaker baker)
	{
		if (running == baker)
		{
			running = null;
		}
	}

	public static void Forget(NavMeshAreaBaker baker)
	{
		waiting.Remove(baker);
		Finished(baker);
	}
}
