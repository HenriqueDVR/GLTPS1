using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// shooter sends muzzle pos + dir, other side fires the same ray
public static class CoopShots
{
	private static readonly NetDataWriter writer = new NetDataWriter();

	private static int pendingRounds;

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

	public static bool GiveAmmoToGunOwner(PlayerGunController gun, int rounds)
	{
		if (!CoopSession.InGame || !CoopPlayers.IsPuppet(gun))
		{
			return false;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Ammo);
		writer.Put(rounds);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug ammo sent to the other player: " + rounds);
#endif
		CoopNotice.Show("Gave " + CoopPlayers.Instance.Remote.name.Replace("Parent", "") + " " + rounds + ((rounds == 1) ? " round." : " rounds."));
		return true;
	}

	public static void OnAmmo(NetDataReader reader)
	{
		int rounds = reader.GetInt();
		CoopPlayers instance = CoopPlayers.Instance;
		PlayerGunController gun = (instance != null && instance.Local != null) ? instance.Local.GetComponentInChildren<PlayerGunController>() : null;
		if (gun == null)
		{
			pendingRounds += rounds;
			return;
		}
		gun.AddSpareAmmo(rounds);
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug ammo from the other player: " + rounds + ", spare now " + gun.maxAmmoCount);
#endif
		CoopNotice.Show(instance.Remote.name.Replace("Parent", "") + " found you " + rounds + ((rounds == 1) ? " round." : " rounds."));
	}

	public static void GivePendingAmmo(Transform local)
	{
		if (pendingRounds == 0)
		{
			return;
		}
		PlayerGunController gun = local.GetComponentInChildren<PlayerGunController>();
		if (gun != null)
		{
			gun.AddSpareAmmo(pendingRounds);
			pendingRounds = 0;
		}
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
