using System;
using System.Collections.Generic;
using UnityEngine;

// where a section goes: the save file, the snapshot a joining friend gets, or both
[Flags]
public enum StateUse
{
	Save = 1,
	Join = 2,
	Both = Save | Join
}

public class StateSection
{
	public string Key;

	public int Version;

	public byte[] Data;
}

// systems register their IStateful when they're ready to be read (some at boot, some once the
// world is up). loaded sections wait here until their system registers, then it reads them.
// so when something registers is when its saved state comes back
public static class StateRegistry
{
	private static readonly Dictionary<string, IStateful> registered = new Dictionary<string, IStateful>();

	private static readonly Dictionary<string, StateUse> uses = new Dictionary<string, StateUse>();

	private static readonly Dictionary<string, StateSection> pending = new Dictionary<string, StateSection>();

	// the last good bytes of each section, for a save when its system can't write this time
	private static readonly Dictionary<string, StateSection> lastGood = new Dictionary<string, StateSection>();

	// loaded but couldn't be read (broken, or from a newer version): saves write these back as they were
	private static readonly Dictionary<string, StateSection> unread = new Dictionary<string, StateSection>();

	public static void Register(IStateful stateful, StateUse use = StateUse.Save)
	{
		registered[stateful.Key] = stateful;
		uses[stateful.Key] = use;
		if (pending.TryGetValue(stateful.Key, out StateSection section))
		{
			pending.Remove(stateful.Key);
			Apply(stateful, section);
		}
	}

	public static void Unregister(IStateful stateful)
	{
		if (registered.TryGetValue(stateful.Key, out IStateful current) && current == stateful)
		{
			registered.Remove(stateful.Key);
		}
	}

	public static bool IsRegistered(string key)
	{
		return registered.ContainsKey(key);
	}

	public static bool HasPending(string key)
	{
		return pending.ContainsKey(key);
	}

	// registered systems read theirs now, the rest wait
	public static void SetPending(IEnumerable<StateSection> sections)
	{
		foreach (StateSection section in sections)
		{
			if (registered.TryGetValue(section.Key, out IStateful stateful))
			{
				Apply(stateful, section);
			}
			else
			{
				pending[section.Key] = section;
			}
		}
	}

	public static void ClearPending()
	{
		pending.Clear();
		lastGood.Clear();
		unread.Clear();
	}

	// every registered section for that use. a save also keeps the unread ones,
	// so a system that didn't come up this time doesn't lose its state
	public static List<StateSection> Capture(StateUse use)
	{
		List<StateSection> sections = new List<StateSection>();
		foreach (KeyValuePair<string, IStateful> stateful in registered)
		{
			if ((uses[stateful.Key] & use) == 0)
			{
				continue;
			}
			if ((use & StateUse.Save) != 0 && unread.TryGetValue(stateful.Key, out StateSection kept))
			{
				sections.Add(kept);
				continue;
			}
			Add(sections, stateful.Value, (use & StateUse.Save) != 0);
		}
		if ((use & StateUse.Save) != 0)
		{
			sections.AddRange(pending.Values);
		}
		return sections;
	}

	public static List<StateSection> Capture(params string[] keys)
	{
		List<StateSection> sections = new List<StateSection>();
		foreach (string key in keys)
		{
			if (registered.TryGetValue(key, out IStateful stateful))
			{
				Add(sections, stateful, false);
			}
		}
		return sections;
	}

	// one broken section doesn't stop the whole save or snapshot. a snapshot goes without it, a save keeps its old
	// data: the file's about to replace the only good copy
	private static void Add(List<StateSection> sections, IStateful stateful, bool keepOld)
	{
		try
		{
			StateWriter writer = new StateWriter();
			stateful.Write(writer);
			StateSection section = new StateSection { Key = stateful.Key, Version = stateful.Version, Data = writer.ToArray() };
			sections.Add(section);
			lastGood[stateful.Key] = section;
		}
		catch (Exception e)
		{
			Debug.LogWarning("State: couldn't write " + stateful.Key + ": " + e);
			if (keepOld && lastGood.TryGetValue(stateful.Key, out StateSection old))
			{
				sections.Add(old);
			}
		}
	}

	private static void Apply(IStateful stateful, StateSection section)
	{
		if (section.Version > stateful.Version)
		{
			Debug.LogWarning("State: " + section.Key + " is from a newer version (" + section.Version + " > " + stateful.Version + "), skipped");
			unread[section.Key] = section;
			return;
		}
		try
		{
			stateful.Read(new StateReader(section.Data), section.Version);
			lastGood[section.Key] = section;
			unread.Remove(section.Key);
		}
		catch (Exception e)
		{
			Debug.LogWarning("State: couldn't read " + section.Key + ": " + e);
			unread[section.Key] = section;
		}
	}

	// count, then key, version and bytes per section. for sending them in one message
	public static byte[] Pack(List<StateSection> sections)
	{
		StateWriter writer = new StateWriter();
		writer.Put(sections.Count);
		foreach (StateSection section in sections)
		{
			writer.Put(section.Key);
			writer.Put(section.Version);
			writer.Put(section.Data);
		}
		return writer.ToArray();
	}

	public static List<StateSection> Unpack(byte[] data)
	{
		StateReader reader = new StateReader(data);
		List<StateSection> sections = new List<StateSection>();
		int count = reader.GetInt();
		for (int i = 0; i < count; i++)
		{
			sections.Add(new StateSection { Key = reader.GetString(), Version = reader.GetInt(), Data = reader.GetBytes() });
		}
		return sections;
	}
}
