using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// things in the world that happened for good: an item taken, a container opened, a span dropped. a set of ids, saved
// and sent to a joining friend. ids are seed-derived and carry the layer, so there's nothing per layer in here.
// host decides, a friend asks with Claim and hears the result. never unset: a restart goes back to the last save
public static class WorldFlags
{
	private static readonly HashSet<ulong> flags = new HashSet<ulong>();

	// a flag newly set here, whatever brought it: the host, a friend's Claim, a snapshot
	public static event Action<ulong> Changed;

	public static IEnumerable<ulong> All => flags;

	public static int Count => flags.Count;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	public static void Init()
	{
		NetRegistry.Register(NetRegistry.Kind.Flag, OnFlag);
		NetRegistry.RegisterDecider(NetRegistry.Kind.Flag, (id, reader) => Set(id));
	}

	// keep what a loaded save already has
	public static void ResetLevel()
	{
		flags.Clear();
		NetRegistry.ForgetAsked();
		if (GameSave.RestoreTaken != null)
		{
			flags.UnionWith(GameSave.RestoreTaken);
		}
	}

	public static bool Has(ulong id)
	{
		return flags.Contains(id);
	}

	// host or alone. false: it was already set, somebody else got there first
	public static bool Set(ulong id)
	{
		if (!NetRegistry.HostOrSolo)
		{
			Debug.LogWarning("WorldFlags: a friend set " + id + ", it should Claim");
			return false;
		}
		if (!flags.Add(id))
		{
			return false;
		}
		NetRegistry.Send(NetRegistry.Kind.Flag, id, null, DeliveryMethod.ReliableOrdered);
		Announce(id);
		return true;
	}

	// from any side: the first to ask gets true. false back at once if the link's down, no answer comes then
	public static bool Claim(ulong id, Action<bool> answer)
	{
		return NetRegistry.Ask(NetRegistry.Kind.Flag, id, null, answer);
	}

	private static void OnFlag(ulong id, NetDataReader reader)
	{
		if (!NetRegistry.HostOrSolo && flags.Add(id))
		{
			Announce(id);
		}
	}

	private static void Announce(ulong id)
	{
		Interactable.OnFlagSet(id);
		Changed?.Invoke(id);
	}

	// in the save and in the join snapshot. the key's from when this was only taken items, old saves have it
	public class State : IStateful
	{
		public const string SectionKey = "items";

		public string Key => SectionKey;

		public int Version => 1;

		public static void WriteData(StateWriter writer, ICollection<ulong> ids)
		{
			writer.Put(ids.Count);
			foreach (ulong id in ids)
			{
				writer.Put(id);
			}
		}

		public static List<ulong> ReadData(StateReader reader)
		{
			List<ulong> ids = new List<ulong>();
			int count = reader.GetInt();
			for (int i = 0; i < count; i++)
			{
				ids.Add(reader.GetULong());
			}
			return ids;
		}

		public void Write(StateWriter writer)
		{
			WriteData(writer, flags);
		}

		// before the world is built (a save loading, the friend joining) or into a running one (the friend resuming)
		public void Read(StateReader reader, int version)
		{
			List<ulong> ids = ReadData(reader);
			GameSave.RestoreTaken = new HashSet<ulong>(ids);
			foreach (ulong id in ids)
			{
				if (flags.Add(id))
				{
					Announce(id);
				}
			}
		}
	}
}
