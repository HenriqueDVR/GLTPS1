using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// 3 save slots. autosave every few mins, on quit and from the pause menu.
// in coop the host saves both girls. game over keeps the save
public static class GameSave
{
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
	}

	[Serializable]
	public class SaveData
	{
		public int version = 1;

		public string savedAt;

		public float playSeconds;

		public bool coop;

		public string seed;

		public string difficulty;

		public SavedGirl chito;

		public SavedGirl yuuri;

		public int food;

		public float water;

		public float fuel;

		public bool hasMap;

		public bool hasCamera;

		public float[] vehicle;

		public bool barrelMounted;

		public bool hasLooseBarrel;

		public float[] barrel;

		public List<string> takenItems = new List<string>();

		public List<string> pipeKeys = new List<string>();

		public List<float> pipeLevels = new List<float>();
	}

	public const int SlotCount = 3;

	private const string lastSlotKey = "lastSaveSlot";

	private const string difficultyKey = "difficultySetting";

	// -1 = no slot yet
	public static int CurrentSlot = -1;

	public static HashSet<ulong> RestoreTaken;

	private static SaveData pending;

	private static float playSecondsBefore;

	private static string Folder => Path.Combine(Application.persistentDataPath, "Saves");

	private static string SlotPath(int slot)
	{
		return Path.Combine(Folder, "slot" + (slot + 1) + ".json");
	}

	public static int LastSlot => PlayerPrefs.GetInt(lastSlotKey, -1);

	public static SaveData Read(int slot)
	{
		try
		{
			string path = SlotPath(slot);
			return File.Exists(path) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(path)) : null;
		}
		catch (Exception e)
		{
			Debug.LogWarning("Save: couldn't read slot " + (slot + 1) + ": " + e.Message);
			return null;
		}
	}

	public static string Describe(int slot)
	{
		SaveData data = Read(slot);
		if (data == null)
		{
			return "slot " + (slot + 1) + ": empty";
		}
		int minutes = Mathf.FloorToInt(data.playSeconds / 60f);
		string played = (minutes >= 60) ? (minutes / 60 + "h " + minutes % 60 + "m") : (minutes + " min");
		return "slot " + (slot + 1) + ": " + (data.coop ? "co-op" : "solo") + ", " + played + ", " + data.savedAt;
	}

	public static void StartNewGame(int slot)
	{
		Runner.EnsureExists();
		CurrentSlot = slot;
		pending = null;
		RestoreTaken = null;
		CoopPipes.SetRestore(null);
		playSecondsBefore = 0f;
		if (!CoopSession.Active || CoopSession.Instance.IsHost)
		{
			WorldGenerator.RequestedSeed = 0uL;
		}
		CoopGirlState.HostKept = null;
	}

	public static void Load(int slot)
	{
		SaveData data = Read(slot);
		if (data == null)
		{
			return;
		}
		Runner.EnsureExists();
		CurrentSlot = slot;
		pending = data;
		playSecondsBefore = data.playSeconds;
		PlayerPrefs.SetString(difficultyKey, data.difficulty);
		WorldGenerator.RequestedSeed = ulong.Parse(data.seed);
		RestoreTaken = new HashSet<ulong>();
		foreach (string item in data.takenItems)
		{
			RestoreTaken.Add(ulong.Parse(item));
		}
		Dictionary<ulong, float> pipes = new Dictionary<ulong, float>();
		for (int i = 0; data.pipeKeys != null && data.pipeLevels != null && i < data.pipeKeys.Count && i < data.pipeLevels.Count; i++)
		{
			pipes[ulong.Parse(data.pipeKeys[i])] = data.pipeLevels[i];
		}
		CoopPipes.SetRestore(pipes);
		CoopGirlState.HostKept = (CoopSession.Active && CoopSession.Instance.IsHost) ? ToState(data.yuuri) : null;
		Debug.Log("Save: loading slot " + (slot + 1));
		SceneManager.LoadScene(Scenes.Game);
	}

	// null if we can save
	public static string WhyNotSave()
	{
		if (!Scenes.InGame || CurrentSlot < 0)
		{
			return "Nothing to save.";
		}
		if (CoopSession.Active && !CoopSession.Instance.IsHost)
		{
			return "Only the host saves in co-op.";
		}
		GameObject manager = GameObject.FindWithTag("CharacterManager");
		Transform chito = Girl("ChitoParent");
		Transform yuuri = Girl("YuuriParent");
		if (manager == null || chito == null || yuuri == null)
		{
			return "Can't save during the ending.";
		}
		foreach (GameObject elevator in GameObject.FindGameObjectsWithTag("Elevator"))
		{
			Animator animator = elevator.GetComponent<Animator>();
			if (animator != null && animator.GetBool("CloseGate"))
			{
				return "Can't save in the elevator.";
			}
		}
		if (OutOfSomething(chito) && OutOfSomething(yuuri))
		{
			return "Can't save now.";
		}
		return null;
	}

	public static bool SaveIfPossible()
	{
		return WhyNotSave() == null && SaveNow();
	}

	public static bool SaveNow()
	{
		try
		{
			SaveData data = Capture();
			Directory.CreateDirectory(Folder);
			string path = SlotPath(CurrentSlot);
			File.WriteAllText(path + ".tmp", JsonUtility.ToJson(data, prettyPrint: true));
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(path + ".tmp", path);
			PlayerPrefs.SetInt(lastSlotKey, CurrentSlot);
			PlayerPrefs.Save();
			Debug.Log("Save: saved slot " + (CurrentSlot + 1));
			return true;
		}
		catch (Exception e)
		{
			Debug.LogWarning("Save: couldn't save: " + e);
			return false;
		}
	}

	private static Transform Girl(string name)
	{
		GameObject girl = GameObject.Find(name);
		return (girl != null) ? girl.transform : null;
	}

	private static bool OutOfSomething(Transform girl)
	{
		CharacterItemInteraction items = girl.GetComponentInChildren<CharacterItemInteraction>();
		return items.hungerLevel <= 0f || items.thirstLevel <= 0f || items.temperatureLevel <= 0f;
	}

	private static Vector3 Offset()
	{
		FloatingOrigin origin = UnityEngine.Object.FindObjectOfType<FloatingOrigin>();
		return (origin != null) ? origin.TotalOffset : Vector3.zero;
	}

	private static SavedGirl FromState(CoopGirlState state)
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

	private static CoopGirlState ToState(SavedGirl girl)
	{
		return new CoopGirlState
		{
			Position = new Vector3(girl.x, girl.y, girl.z),
			Yaw = girl.yaw,
			Hunger = girl.hunger,
			Thirst = girl.thirst,
			Temperature = girl.temperature,
			HasGun = girl.hasGun,
			Magazine = girl.magazine,
			Spare = girl.spare
		};
	}

	private static SaveData Capture()
	{
		Vector3 offset = Offset();
		Transform chito = Girl("ChitoParent");
		Transform yuuri = Girl("YuuriParent");
		bool coop = CoopSession.InGame;
		SaveData data = new SaveData
		{
			savedAt = DateTime.Now.ToString("d MMM HH:mm"),
			playSeconds = playSecondsBefore + Time.timeSinceLevelLoad,
			coop = coop,
			seed = GameSession.Get<WorldGenerator>().Seed.ToString(),
			difficulty = PlayerPrefs.GetString(difficultyKey, ""),
			chito = FromState(CoopGirlState.Capture(chito, chito.position + offset))
		};
		// friend away: use the yuuri the host kept, not the frozen one
		if (coop && !CoopSession.Instance.Connected && CoopGirlState.HostKept != null)
		{
			data.yuuri = FromState(CoopGirlState.HostKept);
		}
		else
		{
			data.yuuri = FromState(CoopGirlState.Capture(yuuri, yuuri.position + offset));
		}
		CharacterStatus status = UnityEngine.Object.FindObjectOfType<CharacterStatus>();
		data.food = status.index;
		data.water = status.waterLevel;
		data.fuel = UnityEngine.Object.FindObjectOfType<VehicleFuelManager>().currentFuel;
		data.hasMap = chito.GetComponent<PlayerMapController>().enabled || yuuri.GetComponent<PlayerMapController>().enabled;
		data.hasCamera = chito.GetComponent<PlayerCameraController>().enabled || yuuri.GetComponent<PlayerCameraController>().enabled;
		Transform vehicle = UnityEngine.Object.FindObjectOfType<VehicleMovement>().transform;
		// on the ket: load her standing next to it
		StandBeside(data.chito, vehicle, offset);
		StandBeside(data.yuuri, vehicle, offset);
		Vector3 vehiclePosition = vehicle.position + offset;
		Quaternion vehicleRotation = vehicle.rotation;
		data.vehicle = new float[7] { vehiclePosition.x, vehiclePosition.y, vehiclePosition.z, vehicleRotation.x, vehicleRotation.y, vehicleRotation.z, vehicleRotation.w };
		GameObject dropArea = GameObject.FindWithTag("BarrelDropArea");
		data.barrelMounted = dropArea != null && dropArea.GetComponent<SkinnedMeshRenderer>().enabled;
		GameObject barrel = GameObject.FindWithTag("Barrel");
		if (barrel != null && !data.barrelMounted)
		{
			Vector3 barrelPosition = barrel.transform.position + offset;
			data.hasLooseBarrel = true;
			data.barrel = new float[3] { barrelPosition.x, barrelPosition.y, barrelPosition.z };
		}
		foreach (ulong item in CoopItem.TakenIds)
		{
			data.takenItems.Add(item.ToString());
		}
		foreach (KeyValuePair<ulong, float> pipe in CoopPipes.Levels())
		{
			data.pipeKeys.Add(pipe.Key.ToString());
			data.pipeLevels.Add(pipe.Value);
		}
		return data;
	}

	private static void StandBeside(SavedGirl girl, Transform vehicle, Vector3 offset)
	{
		Vector3 position = new Vector3(girl.x, girl.y, girl.z) - offset;
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

	private static IEnumerator Apply(SaveData data)
	{
		while (Girl("ChitoParent") == null || UnityEngine.Object.FindObjectOfType<CharacterStatus>() == null || UnityEngine.Object.FindObjectOfType<VehicleMovement>() == null)
		{
			yield return null;
		}
		// let the Start()s run first
		yield return null;
		yield return null;
		Transform chito = Girl("ChitoParent");
		Transform yuuri = Girl("YuuriParent");
		CoopPlayers players = CoopPlayers.Instance;
		ToState(data.chito).ApplyStats(chito);
		ToState(data.yuuri).ApplyStats(yuuri);
		CharacterStatus status = UnityEngine.Object.FindObjectOfType<CharacterStatus>();
		chito.GetComponentInChildren<CharacterItemInteraction>().SetFood(data.food);
		status.waterLevel = data.water;
		UnityEngine.Object.FindObjectOfType<VehicleFuelManager>().currentFuel = data.fuel;
		if (data.hasMap)
		{
			chito.GetComponent<PlayerMapController>().enabled = true;
		}
		if (data.hasCamera)
		{
			chito.GetComponent<PlayerCameraController>().enabled = true;
		}
		PlaceVehicle(data);
		PlaceBarrel(data, chito);
		if (players != null && CoopSession.Instance.IsHost)
		{
			CoopGirlState.HostKept = ToState(data.yuuri);
		}
		// chito first, city builds around the player
		yield return PlaceOnGround(chito, new Vector3(data.chito.x, data.chito.y, data.chito.z), data.chito.yaw);
		if (players == null)
		{
			yield return PlaceOnGround(yuuri, new Vector3(data.yuuri.x, data.yuuri.y, data.yuuri.z), data.yuuri.yaw);
		}
		else if (players.Remote != null)
		{
			players.Remote.SetPositionAndRotation(FromLayout(new Vector3(data.yuuri.x, data.yuuri.y, data.yuuri.z)), Quaternion.Euler(0f, data.yuuri.yaw, 0f));
		}
		if (players != null && CoopSession.Instance.Connected)
		{
			// friend got the world before ket/map were back, resend
			CoopSession.Instance.ResendRejoin();
		}
		Debug.Log("Save: loaded");
#if DUMP_WORLDGEN
		yield return new WaitForSeconds(3f);
		Debug.Log("Save: after loading, Chito at " + (chito.position + Offset()).ToString("F1") + " (saved " + new Vector3(data.chito.x, data.chito.y, data.chito.z).ToString("F1") + "), Yuuri at " + (yuuri.position + Offset()).ToString("F1") + " (saved " + new Vector3(data.yuuri.x, data.yuuri.y, data.yuuri.z).ToString("F1") + "), food " + status.index + " water " + status.waterLevel.ToString("F1") + ", taken " + RestoreTaken.Count + ", Kettenkrad at " + (UnityEngine.Object.FindObjectOfType<VehicleMovement>().transform.position + Offset()).ToString("F1") + " (saved " + ((data.vehicle != null) ? new Vector3(data.vehicle[0], data.vehicle[1], data.vehicle[2]).ToString("F1") : "none") + ")");
#endif
	}

	private static Vector3 FromLayout(Vector3 layout)
	{
		return layout - Offset();
	}

	private static void PlaceVehicle(SaveData data)
	{
		if (data.vehicle == null || data.vehicle.Length < 7)
		{
			return;
		}
		Vector3 position = FromLayout(new Vector3(data.vehicle[0], data.vehicle[1], data.vehicle[2]));
		Quaternion rotation = new Quaternion(data.vehicle[3], data.vehicle[4], data.vehicle[5], data.vehicle[6]);
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

	private static void PlaceBarrel(SaveData data, Transform chito)
	{
		GameObject dropArea = GameObject.FindWithTag("BarrelDropArea");
		if (dropArea != null)
		{
			dropArea.GetComponent<SkinnedMeshRenderer>().enabled = data.barrelMounted;
		}
		GameObject barrel = GameObject.FindWithTag("Barrel");
		if (!data.hasLooseBarrel || data.barrel == null)
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
		barrel.transform.position = FromLayout(new Vector3(data.barrel[0], data.barrel[1], data.barrel[2]));
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

	public class Runner : MonoBehaviour
	{
		private const float autosaveInterval = 180f;

		private float nextAutosave;

		private static Runner instance;

		public static void EnsureExists()
		{
			if (instance == null)
			{
				GameObject runner = new GameObject("GameSave");
				UnityEngine.Object.DontDestroyOnLoad(runner);
				instance = runner.AddComponent<Runner>();
			}
		}

		private void Awake()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.buildIndex != Scenes.Game)
			{
				return;
			}
			nextAutosave = Time.unscaledTime + autosaveInterval;
			GameSaveMenu.BuildPauseButton();
			if (pending != null)
			{
				SaveData data = pending;
				pending = null;
				StartCoroutine(Apply(data));
			}
		}

#if DUMP_WORLDGEN
		private bool debugSaved;

		private bool debugPaused;
#endif

		private void Update()
		{
#if DUMP_WORLDGEN
			// -savein S
			string[] args = Environment.GetCommandLineArgs();
			int at = Array.IndexOf(args, "-savein");
			if (!debugSaved && at >= 0 && at < args.Length - 1 && float.TryParse(args[at + 1], out float seconds) && Scenes.InGame && Time.timeSinceLevelLoad > seconds)
			{
				debugSaved = true;
				Debug.Log("Save: debug save, " + (WhyNotSave() ?? "ok") + ", saved " + SaveNow());
				Transform c = Girl("ChitoParent");
				Transform y = Girl("YuuriParent");
				Debug.Log("Save: saved with Chito at " + (c.position + Offset()).ToString("F1") + ", Yuuri at " + (y.position + Offset()).ToString("F1"));
			}
			// -openpause
			if (!debugPaused && Array.IndexOf(args, "-openpause") >= 0 && Scenes.InGame && Time.timeSinceLevelLoad > 12f)
			{
				debugPaused = true;
				UnityEngine.Object.FindObjectOfType<UIManager>().OpenMainMenu();
			}
#endif
			if (Time.unscaledTime >= nextAutosave && Scenes.InGame)
			{
				nextAutosave = Time.unscaledTime + autosaveInterval;
				// no popup for autosave, too often
				SaveIfPossible();
			}
		}

		private void OnApplicationQuit()
		{
			SaveIfPossible();
		}
	}
}
