using System;
using System.Collections;
using UnityEngine;

// the save sections that need the world up: both girls, supplies, the ket and barrel.
// GameSession registers them once the scene's objects exist, which is also when a loaded save is put back
[Serializable]
public class SavedGirl
{
	public float x;

	public float y;

	public float z;

	public float yaw;

	public float hunger;

	public float thirst;

	public float temperature;

	public bool hasGun;

	public int magazine;

	public int spare;

	public Vector3 Position => new Vector3(x, y, z);

	public void Write(StateWriter writer)
	{
		writer.Put(new Vector3(x, y, z));
		writer.Put(yaw);
		writer.Put(hunger);
		writer.Put(thirst);
		writer.Put(temperature);
		writer.Put(hasGun);
		writer.Put(magazine);
		writer.Put(spare);
	}

	public static SavedGirl Read(StateReader reader)
	{
		Vector3 position = reader.GetVector3();
		return new SavedGirl
		{
			x = position.x,
			y = position.y,
			z = position.z,
			yaw = reader.GetFloat(),
			hunger = reader.GetFloat(),
			thirst = reader.GetFloat(),
			temperature = reader.GetFloat(),
			hasGun = reader.GetBool(),
			magazine = reader.GetInt(),
			spare = reader.GetInt()
		};
	}

	public static SavedGirl From(CoopGirlState state)
	{
		return new SavedGirl
		{
			x = state.Position.x,
			y = state.Position.y,
			z = state.Position.z,
			yaw = state.Yaw,
			hunger = state.Hunger,
			thirst = state.Thirst,
			temperature = state.Temperature,
			hasGun = state.HasGun,
			magazine = state.Magazine,
			spare = state.Spare
		};
	}

	public CoopGirlState ToState()
	{
		return new CoopGirlState
		{
			Position = Position,
			Yaw = yaw,
			Hunger = hunger,
			Thirst = thirst,
			Temperature = temperature,
			HasGun = hasGun,
			Magazine = magazine,
			Spare = spare
		};
	}
}

public class GirlsSection : IStateful
{
	public const string SectionKey = "girls";

	public string Key => SectionKey;

	public int Version => 1;

	public static void WriteData(StateWriter writer, SavedGirl chito, SavedGirl yuuri)
	{
		chito.Write(writer);
		yuuri.Write(writer);
	}

	public static void ReadData(StateReader reader, out SavedGirl chito, out SavedGirl yuuri)
	{
		chito = SavedGirl.Read(reader);
		yuuri = SavedGirl.Read(reader);
	}

	public void Write(StateWriter writer)
	{
		Vector3 offset = SaveScene.Offset();
		Transform chito = SaveScene.Chito;
		Transform yuuri = SaveScene.Yuuri;
		SavedGirl savedChito = SavedGirl.From(CoopGirlState.Capture(chito, chito.position + offset));
		SavedGirl savedYuuri;
		// friend away: use the yuuri the host kept, not the frozen one
		if (CoopSession.InGame && !CoopSession.Instance.Connected && CoopGirlState.HostKept != null)
		{
			savedYuuri = SavedGirl.From(CoopGirlState.HostKept);
		}
		else
		{
			savedYuuri = SavedGirl.From(CoopGirlState.Capture(yuuri, yuuri.position + offset));
		}
		// on the ket: load her standing next to it
		Transform vehicle = UnityEngine.Object.FindObjectOfType<VehicleMovement>().transform;
		SaveScene.StandBeside(savedChito, vehicle, offset);
		SaveScene.StandBeside(savedYuuri, vehicle, offset);
		WriteData(writer, savedChito, savedYuuri);
	}

	public void Read(StateReader reader, int version)
	{
		ReadData(reader, out SavedGirl chito, out SavedGirl yuuri);
		GameSession.Current.StartCoroutine(SaveScene.PutGirlsBack(chito, yuuri));
	}
}

public class SuppliesSection : IStateful
{
	public const string SectionKey = "supplies";

	public int Food;

	public float Water;

	public float Fuel;

	public bool HasMap;

	public bool HasCamera;

	public string Key => SectionKey;

	public int Version => 1;

	public void WriteData(StateWriter writer)
	{
		writer.Put(Food);
		writer.Put(Water);
		writer.Put(Fuel);
		writer.Put(HasMap);
		writer.Put(HasCamera);
	}

	public void ReadData(StateReader reader)
	{
		Food = reader.GetInt();
		Water = reader.GetFloat();
		Fuel = reader.GetFloat();
		HasMap = reader.GetBool();
		HasCamera = reader.GetBool();
	}

	public void Write(StateWriter writer)
	{
		Transform chito = SaveScene.Chito;
		Transform yuuri = SaveScene.Yuuri;
		CharacterStatus status = UnityEngine.Object.FindObjectOfType<CharacterStatus>();
		Food = status.index;
		Water = status.waterLevel;
		Fuel = UnityEngine.Object.FindObjectOfType<VehicleFuelManager>().currentFuel;
		HasMap = chito.GetComponent<PlayerMapController>().enabled || yuuri.GetComponent<PlayerMapController>().enabled;
		HasCamera = chito.GetComponent<PlayerCameraController>().enabled || yuuri.GetComponent<PlayerCameraController>().enabled;
		WriteData(writer);
	}

	public void Read(StateReader reader, int version)
	{
		ReadData(reader);
		Transform chito = SaveScene.Chito;
		chito.GetComponentInChildren<CharacterItemInteraction>().SetFood(Food);
		UnityEngine.Object.FindObjectOfType<CharacterStatus>().waterLevel = Water;
		UnityEngine.Object.FindObjectOfType<VehicleFuelManager>().currentFuel = Fuel;
		if (HasMap)
		{
			chito.GetComponent<PlayerMapController>().enabled = true;
		}
		if (HasCamera)
		{
			chito.GetComponent<PlayerCameraController>().enabled = true;
		}
	}
}

public class KetSection : IStateful
{
	public const string SectionKey = "ket";

	public bool HasVehicle;

	public Vector3 VehiclePosition;

	public Quaternion VehicleRotation = Quaternion.identity;

	public bool BarrelMounted;

	public bool HasLooseBarrel;

	public Vector3 Barrel;

	public string Key => SectionKey;

	public int Version => 1;

	public void WriteData(StateWriter writer)
	{
		writer.Put(HasVehicle);
		writer.Put(VehiclePosition);
		writer.Put(VehicleRotation);
		writer.Put(BarrelMounted);
		writer.Put(HasLooseBarrel);
		writer.Put(Barrel);
	}

	public void ReadData(StateReader reader)
	{
		HasVehicle = reader.GetBool();
		VehiclePosition = reader.GetVector3();
		VehicleRotation = reader.GetQuaternion();
		BarrelMounted = reader.GetBool();
		HasLooseBarrel = reader.GetBool();
		Barrel = reader.GetVector3();
	}

	public void Write(StateWriter writer)
	{
		Vector3 offset = SaveScene.Offset();
		Transform vehicle = UnityEngine.Object.FindObjectOfType<VehicleMovement>().transform;
		HasVehicle = true;
		VehiclePosition = vehicle.position + offset;
		VehicleRotation = vehicle.rotation;
		GameObject dropArea = GameObject.FindWithTag("BarrelDropArea");
		BarrelMounted = dropArea != null && dropArea.GetComponent<SkinnedMeshRenderer>().enabled;
		GameObject barrel = GameObject.FindWithTag("Barrel");
		HasLooseBarrel = barrel != null && !BarrelMounted;
		Barrel = HasLooseBarrel ? barrel.transform.position + offset : Vector3.zero;
		WriteData(writer);
	}

	public void Read(StateReader reader, int version)
	{
		ReadData(reader);
		if (HasVehicle)
		{
			SaveScene.PlaceVehicle(SaveScene.FromLayout(VehiclePosition), VehicleRotation);
		}
		SaveScene.PlaceBarrel(BarrelMounted, HasLooseBarrel, SaveScene.FromLayout(Barrel), SaveScene.Chito);
	}
}

// scene helpers for the sections above
public static class SaveScene
{
	public static Transform Chito => (Girls.Chito != null) ? Girls.Chito.transform : null;

	public static Transform Yuuri => (Girls.Yuuri != null) ? Girls.Yuuri.transform : null;

	public static Vector3 Offset()
	{
		FloatingOrigin origin = GameSession.Get<FloatingOrigin>();
		return (origin != null) ? origin.TotalOffset : Vector3.zero;
	}

	public static Vector3 FromLayout(Vector3 layout)
	{
		return layout - Offset();
	}

	public static bool Ready()
	{
		return Chito != null && UnityEngine.Object.FindObjectOfType<CharacterStatus>() != null && UnityEngine.Object.FindObjectOfType<VehicleMovement>() != null;
	}

	public static void StandBeside(SavedGirl girl, Transform vehicle, Vector3 offset)
	{
		Vector3 position = girl.Position - offset;
		Vector3 flat = position - vehicle.position;
		flat.y = 0f;
		if (flat.magnitude > 2f)
		{
			return;
		}
		Vector3 beside = vehicle.position + vehicle.right * 2.5f + offset;
		girl.x = beside.x;
		girl.y = beside.y;
		girl.z = beside.z;
	}

	public static void PlaceVehicle(Vector3 position, Quaternion rotation)
	{
		if (CoopKettenkrad.Instance != null)
		{
			CoopKettenkrad.Instance.Place(position, rotation);
			return;
		}
		VehicleMovement vehicle = UnityEngine.Object.FindObjectOfType<VehicleMovement>();
		Rigidbody body = vehicle.GetComponent<Rigidbody>();
		body.position = position;
		body.rotation = rotation;
		vehicle.transform.SetPositionAndRotation(position, rotation);
		body.velocity = Vector3.zero;
	}

	public static void PlaceBarrel(bool mounted, bool loose, Vector3 position, Transform chito)
	{
		GameObject dropArea = GameObject.FindWithTag("BarrelDropArea");
		if (dropArea != null)
		{
			dropArea.GetComponent<SkinnedMeshRenderer>().enabled = mounted;
		}
		GameObject barrel = GameObject.FindWithTag("Barrel");
		if (!loose)
		{
			if (barrel != null)
			{
				UnityEngine.Object.Destroy(barrel);
			}
			return;
		}
		CharacterBarrelInteraction carrier = chito.GetComponent<CharacterBarrelInteraction>();
		if (barrel == null)
		{
			barrel = carrier.SpawnBarrel();
		}
		barrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript = carrier;
		barrel.transform.position = position;
	}

	public static IEnumerator PutGirlsBack(SavedGirl savedChito, SavedGirl savedYuuri)
	{
		Transform chito = Chito;
		Transform yuuri = Yuuri;
		CoopPlayers players = CoopPlayers.Instance;
		savedChito.ToState().ApplyStats(chito);
		savedYuuri.ToState().ApplyStats(yuuri);
		if (players != null && CoopSession.Instance.IsHost)
		{
			CoopGirlState.HostKept = savedYuuri.ToState();
		}
		// chito first, city builds around the player
		yield return PlaceOnGround(chito, savedChito.Position, savedChito.yaw);
		if (players == null)
		{
			yield return PlaceOnGround(yuuri, savedYuuri.Position, savedYuuri.yaw);
		}
		else if (players.Remote != null)
		{
			players.Remote.SetPositionAndRotation(FromLayout(savedYuuri.Position), Quaternion.Euler(0f, savedYuuri.yaw, 0f));
		}
		if (players != null && CoopSession.Instance.Connected)
		{
			// friend got the world before ket/map were back, resend
			CoopSession.Instance.ResendRejoin();
		}
		Debug.Log("Save: loaded");
#if DUMP_WORLDGEN
		yield return new WaitForSeconds(3f);
		Debug.Log("Save: after loading, Chito at " + (chito.position + Offset()).ToString("F1") + " (saved " + savedChito.Position.ToString("F1") + "), Yuuri at " + (yuuri.position + Offset()).ToString("F1") + " (saved " + savedYuuri.Position.ToString("F1") + "), food " + UnityEngine.Object.FindObjectOfType<CharacterStatus>().index + ", taken " + (GameSave.RestoreTaken != null ? GameSave.RestoreTaken.Count : 0) + ", Kettenkrad at " + (UnityEngine.Object.FindObjectOfType<VehicleMovement>().transform.position + Offset()).ToString("F1"));
#endif
	}

	// city might not be built yet, hold her up until there's ground
	private static IEnumerator PlaceOnGround(Transform girl, Vector3 layout, float yaw)
	{
		CharacterController controller = girl.GetComponent<CharacterController>();
		UnityEngine.AI.NavMeshAgent agent = girl.GetComponent<UnityEngine.AI.NavMeshAgent>();
		bool controllerWasOn = controller != null && controller.enabled;
		bool agentWasOn = agent != null && agent.enabled;
		if (controller != null)
		{
			controller.enabled = false;
		}
		if (agent != null)
		{
			agent.enabled = false;
		}
		girl.rotation = Quaternion.Euler(0f, yaw, 0f);
		float giveUpAt = Time.unscaledTime + 15f;
		while (true)
		{
			Vector3 target = FromLayout(layout);
			girl.position = target + Vector3.up * 0.5f;
			if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out var hit, 6f, -1, QueryTriggerInteraction.Ignore))
			{
				girl.position = hit.point + Vector3.up * 0.1f;
				break;
			}
			if (Time.unscaledTime > giveUpAt)
			{
				break;
			}
			yield return null;
		}
		if (controller != null)
		{
			controller.enabled = controllerWasOn;
		}
		if (agent != null)
		{
			agent.enabled = agentWasOn;
		}
	}

	private static readonly IStateful[] sections = { new SuppliesSection(), new KetSection(), new GirlsSection() };

	// waits for the scene's objects and their Start()s, then registers (a loaded save goes back in now, girls last)
	public static IEnumerator Register()
	{
		while (!Ready())
		{
			yield return null;
		}
		yield return null;
		yield return null;
		foreach (IStateful section in sections)
		{
			StateRegistry.Register(section);
		}
	}

	public static void Unregister()
	{
		foreach (IStateful section in sections)
		{
			StateRegistry.Unregister(section);
		}
	}
}
