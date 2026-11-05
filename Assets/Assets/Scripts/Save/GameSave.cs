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
	// a header plus a section per system (StateRegistry)
	[Serializable]
	public class SaveData
	{
		public int version;

		public string savedAt;

		public float playSeconds;

		public bool coop;

		public string seed;

		public string difficulty;

		// the open world's is 2. older files are the removed legacy world: not read
		public int genVersion;

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

	public const int CurrentVersion = 3;

	public enum SlotState
	{
		Empty,
		Ok,
		// the file's gone or broken, this is the one before it
		Backup,
		Corrupt,
		// a newer game wrote it
		Newer,
		// the removed legacy world's, the file's left alone
		Old
	}

	// sections that live for the whole run. the world's own register with the scene (SaveScene)
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void RegisterSections()
	{
		// read as soon as a save loads, the world's built from it
		StateRegistry.Register(new LayersSection(), StateUse.Save);
		StateRegistry.Register(new WorldFlags.State(), StateUse.Both);
		StateRegistry.Register(new CoopPipes.State(), StateUse.Both);
		StateRegistry.Register(new LastShelter.State(), StateUse.Save);
		StateRegistry.Register(new MapSheet.State(), StateUse.Both);
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
		Inspect(SlotPath(slot), out SaveData data);
		return data;
	}

	public static SlotState State(int slot)
	{
		return Inspect(SlotPath(slot), out _);
	}

	// the file, or what's left of the last write: a backup, or a temp file a crash left behind by the old way of saving
	public static SlotState Inspect(string path, out SaveData data)
	{
		data = null;
		bool any = File.Exists(path);
		if (any)
		{
			SlotState state = ReadFile(path, out data);
			if (state != SlotState.Corrupt)
			{
				return state;
			}
		}
		foreach (string other in new[] { path + ".bak", path + ".tmp" })
		{
			if (File.Exists(other))
			{
				any = true;
				if (ReadFile(other, out data) == SlotState.Ok)
				{
					return SlotState.Backup;
				}
			}
		}
		data = null;
		return any ? SlotState.Corrupt : SlotState.Empty;
	}

	private static SlotState ReadFile(string path, out SaveData data)
	{
		data = null;
		try
		{
			string json = File.ReadAllText(path);
			data = Parse(json);
			if (data == null)
			{
				SaveData header = JsonUtility.FromJson<SaveData>(json);
				return (header == null) ? SlotState.Corrupt : (header.version > CurrentVersion) ? SlotState.Newer : ulong.TryParse(header.seed, out _) ? SlotState.Old : SlotState.Corrupt;
			}
			if (!ulong.TryParse(data.seed, out _))
			{
				data = null;
				return SlotState.Corrupt;
			}
			ToStateSections(data.sections);
			return SlotState.Ok;
		}
		catch (Exception e)
		{
			Debug.LogWarning("Save: couldn't read " + Path.GetFileName(path) + ": " + e.Message);
			data = null;
			return SlotState.Corrupt;
		}
	}

	// the new file in full first, then swapped in, the old one kept as .bak. killed at any point the slot still reads.
	// a broken old file isn't worth a backup: it would push out the good one
	public static void WriteAtomic(string path, string json)
	{
		string temp = path + ".tmp";
		using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
		using (StreamWriter text = new StreamWriter(stream))
		{
			text.Write(json);
			text.Flush();
			stream.Flush(true);
		}
		if (!File.Exists(path))
		{
			File.Move(temp, path);
		}
		else if (ReadFile(path, out _) == SlotState.Corrupt)
		{
			File.Copy(path, path + ".corrupt", true);
			File.Delete(path);
			File.Move(temp, path);
		}
		else
		{
			File.Replace(temp, path, path + ".bak");
		}
	}

	// null: nothing this game can load, ReadFile tells a newer one from an old one
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
		// every file before version 3 was the legacy world, and some after
		if (data.version < CurrentVersion || data.genVersion != WorldGenerator.GenVersion)
		{
			return null;
		}
		return data;
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
		SlotState state = Inspect(SlotPath(slot), out SaveData data);
		if (state == SlotState.Corrupt)
		{
			return "slot " + (slot + 1) + ": damaged, can't be read";
		}
		if (state == SlotState.Newer)
		{
			return "slot " + (slot + 1) + ": from a newer version";
		}
		if (state == SlotState.Old)
		{
			return "slot " + (slot + 1) + ": from an old version, can't be loaded";
		}
		if (data == null)
		{
			return "slot " + (slot + 1) + ": empty";
		}
		int minutes = Mathf.FloorToInt(data.playSeconds / 60f);
		string played = (minutes >= 60) ? (minutes / 60 + "h " + minutes % 60 + "m") : (minutes + " min");
		return "slot " + (slot + 1) + ": " + (data.coop ? "co-op" : "solo") + ", " + played + ", " + data.savedAt + ((state == SlotState.Backup) ? " (backup)" : "");
	}

	public static void StartNewGame(int slot)
	{
		Runner.EnsureExists();
		CurrentSlot = slot;
		StateRegistry.ClearPending();
		RestoreTaken = null;
		CoopPipes.SetRestore(null);
		playSecondsBefore = 0f;
		WorldClock.ResetLevel();
		LastShelter.ResetLevel();
		MapSheet.ResetLevel();
		if (!CoopSession.Active || CoopSession.Instance.IsHost)
		{
			WorldGenerator.ClearRequested();
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
		Save.DifficultyChanged();
		WorldGenerator.ClearRequested();
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
		// layers, items, pipes and the clock are read now, the rest once the world is up
		WorldClock.ResetForLoad();
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
		// mid-ride the world is half one layer, half the other
		if (LayerTransition.Busy)
		{
			return "Can't save in the elevator.";
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
		if (!StateRegistry.IsRegistered(GirlsSection.SectionKey) || SaveScene.Restoring)
		{
			return "Can't save yet.";
		}
		foreach (GameObject elevator in GameObject.FindGameObjectsWithTag("Elevator"))
		{
			Animator animator = elevator.GetComponent<Animator>();
			// or standing in it: the car takes whoever's in it as a child, loaded in there they'd go wherever the
			// tower went
			if (animator != null && (animator.GetBool("CloseGate") || CoopElevator.Aboard(animator, chito) || CoopElevator.Aboard(animator, yuuri)))
			{
				return "Can't save in the elevator.";
			}
		}
		if (GirlNeeds.Out(Girls.Chito.Items) && GirlNeeds.Out(Girls.Yuuri.Items))
		{
			return "Can't save now.";
		}
		return null;
	}

	public static bool SaveIfPossible()
	{
		return WhyNotSave() == null && SaveNow();
	}

	private static bool savedForQuit;

	// CoopSession leaves on quit too. whichever hears first saves, while it still knows who's who: after that the host
	// would save the puppet, and a friend its own solo slot
	public static void SaveForQuit()
	{
		if (!savedForQuit)
		{
			savedForQuit = true;
			SaveIfPossible();
		}
	}

	public static bool SaveNow()
	{
		try
		{
			SaveData data = Capture();
			Directory.CreateDirectory(Folder);
			WriteAtomic(SlotPath(CurrentSlot), JsonUtility.ToJson(data, prettyPrint: true));
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

	private static SaveData Capture()
	{
		SaveData data = new SaveData
		{
			version = CurrentVersion,
			savedAt = DateTime.Now.ToString("d MMM HH:mm"),
			playSeconds = playSecondsBefore + Time.timeSinceLevelLoad,
			coop = CoopSession.InGame,
			seed = GameSession.Get<WorldGenerator>().Seed.ToString(),
			difficulty = PlayerPrefs.GetString(difficultyKey, ""),
			genVersion = WorldGenerator.GenVersion
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
			SaveForQuit();
		}
	}
}
