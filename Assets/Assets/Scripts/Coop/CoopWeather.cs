using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// host rolls weather, friend just plays it (rain/cold matter so both need the same)
public static class CoopWeather
{
	private static WeatherManager follower;

	private static bool hasState;

	private static int index;

	private static bool on;

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

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		NetRegistry.Register(NetRegistry.Kind.Weather, OnMessage);
	}

	public static void ResetLevel()
	{
		hasState = false;
	}

	public static void Register(WeatherManager weatherManager)
	{
		follower = weatherManager;
		if (hasState)
		{
			weatherManager.ApplyHostWeather(index, on);
		}
	}

	public static void Changed(int newIndex, bool newOn)
	{
		if (!CoopSession.Active || !CoopSession.Instance.IsHost)
		{
			return;
		}
		index = newIndex;
		on = newOn;
		hasState = true;
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug weather rolled " + index + (on ? " on" : " off"));
#endif
		SendCurrent();
	}

	public static void SendCurrent()
	{
		if (!hasState)
		{
			return;
		}
		NetRegistry.Send(NetRegistry.Kind.Weather, NetRegistry.SessionId, delegate(NetDataWriter writer)
		{
			writer.Put((byte)index);
			writer.Put(on);
		}, DeliveryMethod.ReliableOrdered);
	}

	private static void OnMessage(ulong netId, NetDataReader reader)
	{
		if (!FollowsHost)
		{
			return;
		}
		index = reader.GetByte();
		on = reader.GetBool();
		hasState = true;
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug weather from host " + index + (on ? " on" : " off"));
#endif
		if (follower != null)
		{
			follower.ApplyHostWeather(index, on);
		}
	}
}
