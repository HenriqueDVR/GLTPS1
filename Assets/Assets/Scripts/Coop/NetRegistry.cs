using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// live updates for systems that register a kind, so a new one never touches CoopSession.
// MessageType.Entity: [kind][netId][payload]. netIds are seed-derived for world things,
// 0x4000.. | peer | counter for things spawned in play, 0 for a system that's one per session
public static class NetRegistry
{
	// one per system, never reuse a number
	public enum Kind : byte
	{
		Nuko = 1,
		Item = 2,
		Clock = 3,
		Weather = 4,
		Flag = 5,
		Line = 6,
		Wake = 7
	}

	public const ulong SessionId = 0uL;

	private static readonly Dictionary<byte, Action<ulong, NetDataReader>> handlers = new Dictionary<byte, Action<ulong, NetDataReader>>();

	private static readonly Dictionary<byte, Func<ulong, NetDataReader, bool>> deciders = new Dictionary<byte, Func<ulong, NetDataReader, bool>>();

	private static readonly Dictionary<ushort, Action<bool>> asked = new Dictionary<ushort, Action<bool>>();

	private static ushort nextToken;

	private static readonly NetDataWriter writer = new NetDataWriter();

	// host: a friend said Ready. arrived: in the world for the first time, or back in it after a drop, not just on
	// another layer. send here what events told them while they were loading or away
	public static event Action<bool> CatchUp;

	// the host decides what's shared: true alone, or hosting
	public static bool HostOrSolo => !CoopSession.Active || CoopSession.Instance.IsHost;

	public static void Register(Kind kind, Action<ulong, NetDataReader> handler)
	{
		handlers[(byte)kind] = handler;
	}

	public static void Write(NetDataWriter message, Kind kind, ulong netId, Action<NetDataWriter> body)
	{
		message.Reset();
		message.Put((byte)CoopSession.MessageType.Entity);
		message.Put((byte)kind);
		message.Put(netId);
		body?.Invoke(message);
	}

	public static void Send(Kind kind, ulong netId, Action<NetDataWriter> body, DeliveryMethod method)
	{
		if (!CoopSession.Active)
		{
			return;
		}
		Write(writer, kind, netId, body);
		CoopSession.Instance.Send(writer, method);
	}

	// reader is just past the MessageType byte
	public static void Dispatch(NetDataReader reader)
	{
		byte kind = reader.GetByte();
		ulong netId = reader.GetULong();
		if (handlers.TryGetValue(kind, out Action<ulong, NetDataReader> handler))
		{
			handler(netId, reader);
		}
		else
		{
			Debug.LogWarning("NetRegistry: nobody handles kind " + kind);
		}
	}

	public static void RunCatchUp(bool arrived)
	{
		CatchUp?.Invoke(arrived);
	}

	// what the host says to a friend's Ask. runs on the host, or right here alone
	public static void RegisterDecider(Kind kind, Func<ulong, NetDataReader, bool> decider)
	{
		deciders[(byte)kind] = decider;
	}

	// ask the host, the same call on every side: alone or hosting it's decided on the spot, a friend hears later.
	// false: nobody to ask (the link's down), answer won't come
	public static bool Ask(Kind kind, ulong netId, Action<NetDataWriter> body, Action<bool> answer)
	{
		if (HostOrSolo)
		{
			writer.Reset();
			body?.Invoke(writer);
			bool granted = Decide((byte)kind, netId, new NetDataReader(writer.CopyData()));
			answer?.Invoke(granted);
			return true;
		}
		if (!CoopSession.Instance.Connected)
		{
			return false;
		}
		WriteRequest(writer, kind, netId, answer, body);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
		return true;
	}

	public static void WriteRequest(NetDataWriter message, Kind kind, ulong netId, Action<bool> answer, Action<NetDataWriter> body)
	{
		ushort token = nextToken++;
		asked[token] = answer;
		message.Reset();
		message.Put((byte)CoopSession.MessageType.Request);
		message.Put((byte)kind);
		message.Put(netId);
		message.Put(token);
		body?.Invoke(message);
	}

	// host, reader just past the MessageType byte: decides, and writes what goes back
	public static void AnswerRequest(NetDataReader reader, NetDataWriter reply)
	{
		byte kind = reader.GetByte();
		ulong netId = reader.GetULong();
		ushort token = reader.GetUShort();
		bool granted = Decide(kind, netId, reader);
		reply.Reset();
		reply.Put((byte)CoopSession.MessageType.Answer);
		reply.Put(token);
		reply.Put(granted);
	}

	private static bool Decide(byte kind, ulong netId, NetDataReader reader)
	{
		if (deciders.TryGetValue(kind, out Func<ulong, NetDataReader, bool> decider))
		{
			return decider(netId, reader);
		}
		Debug.LogWarning("NetRegistry: nobody decides kind " + kind);
		return false;
	}

	// friend, reader just past the MessageType byte
	public static void OnAnswer(NetDataReader reader)
	{
		ushort token = reader.GetUShort();
		bool granted = reader.GetBool();
		if (asked.TryGetValue(token, out Action<bool> answer))
		{
			asked.Remove(token);
			answer?.Invoke(granted);
		}
	}

	// no answer's coming: it's a no, so whatever waits on one can carry on
	public static void LinkLost()
	{
		List<Action<bool>> answers = new List<Action<bool>>(asked.Values);
		asked.Clear();
		foreach (Action<bool> answer in answers)
		{
			answer?.Invoke(false);
		}
	}

	// a new world: whoever was waiting is gone with the old one
	public static void ForgetAsked()
	{
		asked.Clear();
	}
}

// numbers for the stale-dropping wrapper, one count per message type and per NetRegistry kind:
// a kind that's been silent a while mustn't look stale next to a busy one
public class NetSequence
{
	private readonly Dictionary<ushort, ushort> next = new Dictionary<ushort, ushort>();

	private readonly Dictionary<ushort, ushort> last = new Dictionary<ushort, ushort>();

	public static ushort KeyOf(byte[] message, int at)
	{
		byte type = message[at];
		if (type == (byte)CoopSession.MessageType.Entity && at + 1 < message.Length)
		{
			return (ushort)(type | (message[at + 1] << 8));
		}
		return type;
	}

	public ushort Next(ushort key)
	{
		next.TryGetValue(key, out ushort sequence);
		next[key] = (ushort)(sequence + 1);
		return sequence;
	}

	public bool Fresh(ushort key, ushort sequence)
	{
		if (last.TryGetValue(key, out ushort seen) && (short)(sequence - seen) <= 0)
		{
			return false;
		}
		last[key] = sequence;
		return true;
	}

	// the other side starts counting again on a new link
	public void ClearSeen()
	{
		last.Clear();
	}
}
