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
		Weather = 4
	}

	public const ulong SessionId = 0uL;

	private static readonly Dictionary<byte, Action<ulong, NetDataReader>> handlers = new Dictionary<byte, Action<ulong, NetDataReader>>();

	private static readonly NetDataWriter writer = new NetDataWriter();

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
}
