using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// shooter sends muzzle pos + dir, other side fires the same ray
public static class CoopShots
{
	private static readonly NetDataWriter writer = new NetDataWriter();

	private static int pendingRounds;

	// resent now and then in case one got lost in a level load
	private const float ammoStateInterval = 1f;

	private static float nextAmmoState;

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

	public static void Tick(Transform local)
	{
		PlayerGunController gun = local.GetComponentInChildren<PlayerGunController>();
		if (gun == null)
		{
			return;
		}
		if (pendingRounds != 0)
		{
			gun.AddSpareAmmo(pendingRounds);
			pendingRounds = 0;
		}
		if (Time.unscaledTime >= nextAmmoState)
		{
			nextAmmoState = Time.unscaledTime + ammoStateInterval;
			SendAmmoState(gun, DeliveryMethod.Sequenced);
		}
	}

	public static void AmmoChanged(PlayerGunController gun)
	{
		if (CoopSession.InGame && !CoopPlayers.IsPuppet(gun))
		{
			SendAmmoState(gun, DeliveryMethod.ReliableOrdered);
		}
	}

	private static void SendAmmoState(PlayerGunController gun, DeliveryMethod method)
	{
		if (!CoopSession.InGame)
		{
			return;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.AmmoState);
		writer.Put(gun.ammoCount);
		writer.Put(gun.maxAmmoCount);
		CoopSession.Instance.Send(writer, method);
	}

	public static void OnAmmoState(NetDataReader reader)
	{
		int magazine = reader.GetInt();
		int spare = reader.GetInt();
		CoopPlayers instance = CoopPlayers.Instance;
		PlayerGunController gun = (instance != null && instance.Remote != null) ? instance.Remote.GetComponentInChildren<PlayerGunController>() : null;
		if (gun != null)
		{
			gun.ShowAmmo(magazine, spare);
#if DUMP_WORLDGEN
			Debug.Log("CoopDebug other player's ammo " + magazine + " + " + spare + ", shown " + gun.ammoCount + " + " + gun.maxAmmoCount + " with " + gun.ammoCountUI.Count + " icons");
#endif
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
