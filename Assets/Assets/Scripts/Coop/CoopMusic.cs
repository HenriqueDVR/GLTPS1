using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// host's radio is the real one, friend follows its track + position
public static class CoopMusic
{
	private static readonly NetDataWriter writer = new NetDataWriter();

	private static PlaylistPlayer player;

	private static bool hasState;

	private static int track;

	private static float position;

	private static float receivedAt;

	public static bool FollowsHost
	{
		get
		{
			if (CoopSession.Active)
			{
				return !CoopSession.Instance.IsHost;
			}
			return false;
		}
	}

	public static void ResetLevel()
	{
		hasState = false;
	}

	public static void Register(PlaylistPlayer playlistPlayer)
	{
		player = playlistPlayer;
		if (FollowsHost)
		{
			if (hasState)
			{
				Apply();
			}
			return;
		}
		SendCurrent();
	}

	public static void SendCurrent()
	{
		if (!CoopSession.Active || !CoopSession.Instance.IsHost || player == null)
		{
			return;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Music);
		writer.Put((byte)player.CurrentTrack);
		writer.Put(player.Position);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	// friend asks the host to skip
	public static void Next()
	{
		if (FollowsHost)
		{
			writer.Reset();
			writer.Put((byte)CoopSession.MessageType.MusicNext);
			CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
			return;
		}
		if (player != null)
		{
			player.NextTrack();
		}
	}

	public static void OnNextRequest()
	{
		if (player != null)
		{
			player.NextTrack();
		}
	}

	public static void OnMessage(NetDataReader reader)
	{
		track = reader.GetByte();
		// + half ping, host is already that far ahead
		position = reader.GetFloat() + CoopSession.Instance.Ping / 2000f;
		receivedAt = Time.unscaledTime;
		hasState = true;
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug music from host " + track + " at " + position.ToString("0.00"));
#endif
		if (player != null)
		{
			Apply();
		}
	}

	private static void Apply()
	{
		player.FollowHost(track, position + Time.unscaledTime - receivedAt);
	}
}
