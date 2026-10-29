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
	// v1 saves were one flat object, v2 is this header plus a section per system (StateRegistry)
	[Serializable]
	public class SaveData
	{
		// 0 or 1 = the old flat format
		public int version;

		public string savedAt;

		public float playSeconds;

		public bool coop;

		public string seed;

		public string difficulty;

		public List<SavedSection> sections = new List<SavedSection>();
	}

	[Serializable]
	public class SavedSection
	{
		public string key;

		public int version;

		// base64
		public string data;
	}

	// what version 1 wrote, only read to import
	[Serializable]
	public class SaveDataV1
	{
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

	public const int CurrentVersion = 2;

	// sections that live for the whole run. the world's own register with the scene (SaveScene)
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void RegisterSections()
	{
		StateRegistry.Register(new CoopItem.State(), StateUse.Both);
		StateRegistry.Register(new CoopPipes.State(), StateUse.Both);
		StateRegistry.Register(new CoopGirlState.RejoinSection(), StateUse.Join);
	}

	public const int SlotCount = 3;

	private const string lastSlotKey = "lastSaveSlot";

	private const string difficultyKey = "difficultySetting";

	// -1 = no slot yet
	public static int CurrentSlot = -1;

	public static HashSet<ulong> RestoreTaken;

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
			return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
		}
		catch (Exception e)
		{
			Debug.LogWarning("Save: couldn't read slot " + (slot + 1) + ": " + e.Message);
			return null;
		}
	}

	// v1 files are turned into sections here, so nothing past this point knows about them
	public static SaveData Parse(string json)
	{
		SaveData data = JsonUtility.FromJson<SaveData>(json);
		if (data == null)
		{
			return null;
		}
		if (data.version > CurrentVersion)
		{
			Debug.LogWarning("Save: from a newer version (" + data.version + "), not read");
			return null;
		}
		if (data.version < 2)
		{
			data.sections = ImportV1(JsonUtility.FromJson<SaveDataV1>(json));
			data.version = CurrentVersion;
		}
		return data;
	}

	public static List<SavedSection> ImportV1(SaveDataV1 old)
	{
		List<SavedSection> sections = new List<SavedSection>();
		StateWriter writer = new StateWriter();
		GirlsSection.WriteData(writer, old.chito ?? new SavedGirl(), old.yuuri ?? new SavedGirl());
		sections.Add(Section(GirlsSection.SectionKey, 1, writer));
		writer = new StateWriter();
		new SuppliesSection { Food = old.food, Water = old.water, Fuel = old.fuel, HasMap = old.hasMap, HasCamera = old.hasCamera }.WriteData(writer);
		sections.Add(Section(SuppliesSection.SectionKey, 1, writer));
		writer = new StateWriter();
		KetSection ket = new KetSection { BarrelMounted = old.barrelMounted, HasLooseBarrel = old.hasLooseBarrel && old.barrel != null && old.barrel.Length >= 3 };
		if (old.vehicle != null && old.vehicle.Length >= 7)
		{
			ket.HasVehicle = true;
			ket.VehiclePosition = new Vector3(old.vehicle[0], old.vehicle[1], old.vehicle[2]);
			ket.VehicleRotation = new Quaternion(old.vehicle[3], old.vehicle[4], old.vehicle[5], old.vehicle[6]);
		}
		if (ket.HasLooseBarrel)
		{
			ket.Barrel = new Vector3(old.barrel[0], old.barrel[1], old.barrel[2]);
		}
		ket.WriteData(writer);
		sections.Add(Section(KetSection.SectionKey, 1, writer));
		List<ulong> taken = new List<ulong>();
		foreach (string item in old.takenItems ?? new List<string>())
		{
			taken.Add(ulong.Parse(item));
		}
		writer = new StateWriter();
		CoopItem.State.WriteData(writer, taken);
		sections.Add(Section(CoopItem.State.SectionKey, 1, writer));
		Dictionary<ulong, float> pipes = new Dictionary<ulong, float>();
		for (int i = 0; old.pipeKeys != null && old.pipeLevels != null && i < old.pipeKeys.Count && i < old.pipeLevels.Count; i++)
		{
			pipes[ulong.Parse(old.pipeKeys[i])] = old.pipeLevels[i];
		}
		writer = new StateWriter();
		CoopPipes.State.WriteData(writer, pipes);
		sections.Add(Section(CoopPipes.State.SectionKey, 1, writer));
		return sections;
	}

	private static SavedSection Section(string key, int version, StateWriter writer)
	{
		return new SavedSection { key = key, version = version, data = Convert.ToBase64String(writer.ToArray()) };
	}

	public static List<StateSection> ToStateSections(List<SavedSection> saved)
	{
		List<StateSection> sections = new List<StateSection>();
		foreach (SavedSection section in saved ?? new List<SavedSection>())
		{
			sections.Add(new StateSection { Key = section.key, Version = section.version, Data = Convert.FromBase64String(section.data ?? "") });
		}
		return sections;
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
		StateRegistry.ClearPending();
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
		playSecondsBefore = data.playSeconds;
		PlayerPrefs.SetString(difficultyKey, data.difficulty);
		WorldGenerator.RequestedSeed = ulong.Parse(data.seed);
		RestoreTaken = new HashSet<ulong>();
		CoopPipes.SetRestore(null);
		List<StateSection> sections = ToStateSections(data.sections);
		// the host sends yuuri to the friend as soon as the world is generated, before the girls section is read
		CoopGirlState.HostKept = null;
		StateSection girls = sections.Find(s => s.Key == GirlsSection.SectionKey);
		if (girls != null && CoopSession.Active && CoopSession.Instance.IsHost)
		{
			GirlsSection.ReadData(new StateReader(girls.Data), out SavedGirl _, out SavedGirl yuuri);
			CoopGirlState.HostKept = yuuri.ToState();
		}
		// items and pipes are read now, the rest once the world is up
		StateRegistry.ClearPending();
		StateRegistry.SetPending(sections);
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
		// saves don't store the generator yet, it would load as the old world (WP-WORLD-5)
		if (WorldGenerator.GenVersion == 2)
		{
			return "Saving isn't ready in the new world yet.";
		}
		if (CoopSession.Active && !CoopSession.Instance.IsHost)
		{
			return "Only the host saves in co-op.";
		}
		GameObject manager = GameObject.FindWithTag("CharacterManager");
		Transform chito = SaveScene.Chito;
		Transform yuuri = SaveScene.Yuuri;
		if (manager == null || chito == null || yuuri == null)
		{
			return "Can't save during the ending.";
		}
		// the world's sections register a few frames in, and a loaded save is still going back in till then
		if (!StateRegistry.IsRegistered(GirlsSection.SectionKey))
		{
			return "Can't save yet.";
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

	private static bool OutOfSomething(Transform girl)
	{
		CharacterItemInteraction items = girl.GetComponentInChildren<CharacterItemInteraction>();
		return items.hungerLevel <= 0f || items.thirstLevel <= 0f || items.temperatureLevel <= 0f;
	}

	private static SaveData Capture()
	{
		SaveData data = new SaveData
		{
			version = CurrentVersion,
			savedAt = DateTime.Now.ToString("d MMM HH:mm"),
			playSeconds = playSecondsBefore + Time.timeSinceLevelLoad,
			coop = CoopSession.InGame,
			seed = GameSession.Get<WorldGenerator>().Seed.ToString(),
			difficulty = PlayerPrefs.GetString(difficultyKey, "")
		};
		foreach (StateSection section in StateRegistry.Capture(StateUse.Save))
		{
			data.sections.Add(new SavedSection { key = section.Key, version = section.Version, data = Convert.ToBase64String(section.Data) });
		}
		return data;
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
				Transform c = SaveScene.Chito;
				Transform y = SaveScene.Yuuri;
				Debug.Log("Save: saved with Chito at " + (c.position + SaveScene.Offset()).ToString("F1") + ", Yuuri at " + (y.position + SaveScene.Offset()).ToString("F1"));
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
