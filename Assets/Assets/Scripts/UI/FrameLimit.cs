using UnityEngine;

// fps limit from the settings menu. 0 = vsync, -1 = unlimited
public static class FrameLimit
{
	private static readonly int[] options = { 0, 30, 60, 120, 144, 240, -1 };

	private const string key = "fpsLimit";

	public static int Current
	{
		get
		{
			int saved = PlayerPrefs.GetInt(key, 0);
			return System.Array.IndexOf(options, saved) >= 0 ? saved : 0;
		}
	}

	public static string Label
	{
		get
		{
			switch (Current)
			{
			case 0:
				return "fps limit: vsync";
			case -1:
				return "fps limit: unlimited";
			default:
				return "fps limit: " + Current;
			}
		}
	}

	// before any menu opens, the settings page only wakes up when it's first shown
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	public static void Apply()
	{
		int limit = Current;
		QualitySettings.vSyncCount = (limit == 0) ? 1 : 0;
		Application.targetFrameRate = (limit > 0) ? limit : -1;
	}

	public static void Next()
	{
		int index = System.Array.IndexOf(options, Current);
		PlayerPrefs.SetInt(key, options[(index + 1) % options.Length]);
		PlayerPrefs.Save();
		Apply();
	}
}
