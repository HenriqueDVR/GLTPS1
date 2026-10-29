using System.Collections.Generic;
using UnityEngine;

// water left in shot pipes. a rejoin or a loaded save rebuilds them full
public static class CoopPipes
{
	private static Dictionary<ulong, float> restore = new Dictionary<ulong, float>();

	private static ulong Key(Transform pipe)
	{
		string path = pipe.name;
		for (Transform t = pipe.parent; t != null; t = t.parent)
		{
			path = t.name + "/" + path;
		}
		return WorldLayout.Hash(path);
	}

	public static void Restore(PipeWaterLevel pipe)
	{
		if (restore.TryGetValue(Key(pipe.transform), out float level))
		{
			pipe.waterlevel = level;
		}
	}

	public static Dictionary<ulong, float> Levels()
	{
		Dictionary<ulong, float> levels = new Dictionary<ulong, float>(restore);
		foreach (PipeWaterLevel pipe in Object.FindObjectsOfType<PipeWaterLevel>(includeInactive: true))
		{
			if (pipe.waterlevel < pipe.StartLevel)
			{
				levels[Key(pipe.transform)] = Mathf.Max(pipe.waterlevel, 0f);
			}
		}
		return levels;
	}

	public static void SetRestore(Dictionary<ulong, float> levels)
	{
		restore = levels ?? new Dictionary<ulong, float>();
	}

	// in the save and in the join snapshot
	public class State : IStateful
	{
		public const string SectionKey = "pipes";

		public string Key => SectionKey;

		public int Version => 1;

		public static void WriteData(StateWriter writer, Dictionary<ulong, float> levels)
		{
			writer.Put(levels.Count);
			foreach (KeyValuePair<ulong, float> pipe in levels)
			{
				writer.Put(pipe.Key);
				writer.Put(pipe.Value);
			}
		}

		public static Dictionary<ulong, float> ReadData(StateReader reader)
		{
			Dictionary<ulong, float> levels = new Dictionary<ulong, float>();
			int count = reader.GetInt();
			for (int i = 0; i < count; i++)
			{
				ulong key = reader.GetULong();
				levels[key] = reader.GetFloat();
			}
			return levels;
		}

		public void Write(StateWriter writer)
		{
			WriteData(writer, Levels());
		}

		public void Read(StateReader reader, int version)
		{
			restore = ReadData(reader);
			foreach (PipeWaterLevel pipe in Object.FindObjectsOfType<PipeWaterLevel>(includeInactive: true))
			{
				Restore(pipe);
			}
#if DUMP_WORLDGEN
			Debug.Log("CoopDebug pipes restored: " + restore.Count);
#endif
		}
	}

#if DUMP_WORLDGEN
	private static bool debugDrained;

	// -cooppipes
	public static void DebugTick(bool host, Transform remote)
	{
		if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cooppipes") < 0)
		{
			return;
		}
		PipeWaterLevel[] pipes = Object.FindObjectsOfType<PipeWaterLevel>(includeInactive: true);
		if (host && !debugDrained && pipes.Length > 0)
		{
			debugDrained = true;
			System.Array.Sort(pipes, (PipeWaterLevel a, PipeWaterLevel b) => Key(a.transform).CompareTo(Key(b.transform)));
			for (int i = 0; i < 3 && i < pipes.Length; i++)
			{
				pipes[i].waterlevel = pipes[i].StartLevel * 0.37f;
			}
		}
		int low = 0;
		foreach (PipeWaterLevel pipe in pipes)
		{
			if (pipe.waterlevel < pipe.StartLevel)
			{
				low++;
			}
		}
		Debug.Log("CoopDebug pipes " + pipes.Length + ", not full " + low + ", water catcher on the other girl " + (remote != null && remote.Find("CoopWaterCatcher") != null));
	}
#endif
}
