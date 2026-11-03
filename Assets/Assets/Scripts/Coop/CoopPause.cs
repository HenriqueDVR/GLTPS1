using UnityEngine;

// can't stop time in coop, just turn this player's controls off
public static class CoopPause
{
	private static InputContextStack.Token paused;

	public static bool Paused => paused != null;

	public static void Pause()
	{
		// settings/photo pages reopen the menu on the way back
		if (paused != null || CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
		{
			return;
		}
		paused = CoopPlayers.Instance.Local.GetComponent<PlayerInputInitialize>().Contexts.Push(InputContext.Menu, "pause");
	}

	// the world's gone with its input stack, with the menu still open (quit to title)
	public static void Forget()
	{
		paused = null;
	}

	public static void Resume()
	{
		paused?.Release();
		paused = null;
	}
}
