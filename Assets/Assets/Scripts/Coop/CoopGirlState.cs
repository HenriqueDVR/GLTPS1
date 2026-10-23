using System.Collections;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// a girl's position, stats and ammo. host keeps yuuri's so a rejoin carries on
public class CoopGirlState
{
	// layout space (before floating origin)
	public Vector3 Position;

	public float Yaw;

	public float Hunger;

	public float Thirst;

	public float Temperature;

	public bool HasGun;

	public int Magazine;

	public int Spare;

	// survives the partner dropping, cleared on level restart
	public static CoopGirlState HostKept;

	private static CoopGirlState pending;

	private static bool pendingHasVehicle;

	private static Vector3 pendingVehiclePosition;

	private static Quaternion pendingVehicleRotation;

	// don't send position until she's put back or the host takes the fresh spawn
	public static bool Pending => pending != null;

	public static CoopGirlState Capture(Transform girl, Vector3 layoutPosition)
	{
		CharacterItemInteraction items = girl.GetComponentInChildren<CharacterItemInteraction>();
		PlayerGunController gun = girl.GetComponentInChildren<PlayerGunController>(true);
		return new CoopGirlState
		{
			Position = layoutPosition,
			Yaw = girl.eulerAngles.y,
			Hunger = items.hungerLevel,
			Thirst = items.thirstLevel,
			Temperature = items.temperatureLevel,
			HasGun = gun != null,
			Magazine = (gun != null) ? gun.ammoCount : 0,
			Spare = (gun != null) ? gun.maxAmmoCount : 0
		};
	}

	public void ApplyStats(Transform girl)
	{
		CharacterItemInteraction items = girl.GetComponentInChildren<CharacterItemInteraction>();
		items.hungerLevel = Hunger;
		items.thirstLevel = Thirst;
		items.temperatureLevel = Temperature;
		PlayerGunController gun = girl.GetComponentInChildren<PlayerGunController>(true);
		if (gun != null && HasGun)
		{
			gun.ShowAmmo(Magazine, Spare);
		}
	}

	public void Write(NetDataWriter writer)
	{
		writer.Put(Position.x);
		writer.Put(Position.y);
		writer.Put(Position.z);
		writer.Put(Yaw);
		writer.Put(Hunger);
		writer.Put(Thirst);
		writer.Put(Temperature);
		writer.Put(HasGun);
		writer.Put(Magazine);
		writer.Put(Spare);
	}

	public static CoopGirlState Read(NetDataReader reader)
	{
		return new CoopGirlState
		{
			Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat()),
			Yaw = reader.GetFloat(),
			Hunger = reader.GetFloat(),
			Thirst = reader.GetFloat(),
			Temperature = reader.GetFloat(),
			HasGun = reader.GetBool(),
			Magazine = reader.GetInt(),
			Spare = reader.GetInt()
		};
	}

	public static void SendRejoin(NetDataWriter writer, bool hasVehicle, Vector3 vehicleLayoutPosition, Quaternion vehicleRotation)
	{
		if (HostKept == null)
		{
			return;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Rejoin);
		HostKept.Write(writer);
		writer.Put(hasVehicle);
		if (hasVehicle)
		{
			writer.Put(vehicleLayoutPosition.x);
			writer.Put(vehicleLayoutPosition.y);
			writer.Put(vehicleLayoutPosition.z);
			writer.Put(vehicleRotation.x);
			writer.Put(vehicleRotation.y);
			writer.Put(vehicleRotation.z);
			writer.Put(vehicleRotation.w);
		}
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	public static void OnRejoin(NetDataReader reader)
	{
		pending = Read(reader);
		pendingHasVehicle = reader.GetBool();
		if (pendingHasVehicle)
		{
			pendingVehiclePosition = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
			pendingVehicleRotation = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
		}
	}

	public static void ClearPending()
	{
		pending = null;
	}

	// city around her might not be built yet, hold her in the air until there's ground
	public static IEnumerator ApplyPending(CoopPlayers players)
	{
		if (pending == null)
		{
			yield break;
		}
		CoopGirlState state = pending;
		Transform local = players.Local;
		state.ApplyStats(local);
		if (pendingHasVehicle && CoopKettenkrad.Instance != null)
		{
			CoopKettenkrad.Instance.Place(players.FromLayout(pendingVehiclePosition), pendingVehicleRotation);
		}
		CharacterController controller = local.GetComponent<CharacterController>();
		controller.enabled = false;
		local.rotation = Quaternion.Euler(0f, state.Yaw, 0f);
		float giveUpAt = Time.unscaledTime + 15f;
		while (true)
		{
			Vector3 target = players.FromLayout(state.Position);
			local.position = target + Vector3.up * 0.5f;
			if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out var hit, 6f, -1, QueryTriggerInteraction.Ignore))
			{
				local.position = hit.point + Vector3.up * 0.1f;
				break;
			}
			if (Time.unscaledTime > giveUpAt)
			{
				break;
			}
			yield return null;
		}
		controller.enabled = true;
		pending = null;
		Debug.Log("Coop: back where " + local.name.Replace("Parent", "") + " was");
	}
}
