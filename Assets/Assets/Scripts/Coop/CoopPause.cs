using UnityEngine;

// can't stop time in coop, just turn this player's controls off
public static class CoopPause
{
	private static InputContextStack.Token paused;

	public static void Pause()
	{
		// settings/photo pages reopen the menu on the way back
		if (paused != null || CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
		{
			return;
		}
		paused = CoopPlayers.Instance.Local.GetComponent<PlayerInputInitialize>().Contexts.Push(InputContext.Menu, "pause");
	}

	public static void Resume()
	{
		paused?.Release();
		paused = null;
	}
}
