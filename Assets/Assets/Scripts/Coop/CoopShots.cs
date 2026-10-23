using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// shooter sends muzzle pos + dir, other side fires the same ray
public static class CoopShots
{
	private static readonly NetDataWriter writer = new NetDataWriter();

	public static void Fired(Vector3 origin, Vector3 direction)
	{
		if (!CoopSession.InGame || CoopPlayers.Instance == null)
		{
			return;
		}
		Vector3 vector = CoopPlayers.Instance.ToLayout(origin);
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Shot);
		writer.Put(vector.x);
		writer.Put(vector.y);
		writer.Put(vector.z);
		writer.Put(direction.x);
		writer.Put(direction.y);
		writer.Put(direction.z);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	public static void OnShot(NetDataReader reader)
	{
		Vector3 layout = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
		Vector3 direction = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
		CoopPlayers instance = CoopPlayers.Instance;
		if (instance == null || instance.Remote == null)
		{
			return;
		}
		PlayerGunController componentInChildren = instance.Remote.GetComponentInChildren<PlayerGunController>();
		if (componentInChildren != null)
		{
			componentInChildren.Shoot(instance.FromLayout(layout), direction, fromOtherPlayer: true);
		}
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug shot from the other player at " + layout.ToString("F1") + " toward " + direction.ToString("F2"));
#endif
	}
}
