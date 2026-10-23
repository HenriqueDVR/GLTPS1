using UnityEngine;

// can't stop time in coop, just disable this player's controls
public static class CoopPause
{
	private static PlayerInputInitialize paused;

	private static bool playerWasOn;

	private static bool vehicleWasOn;

	public static void Pause()
	{
		// settings/photo pages reopen the menu on the way back
		if (paused != null || CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
		{
			return;
		}
		paused = CoopPlayers.Instance.Local.GetComponent<PlayerInputInitialize>();
		playerWasOn = paused.actions.Player.enabled;
		vehicleWasOn = paused.actions.Vehicle.enabled;
		paused.actions.Player.Disable();
		paused.actions.Vehicle.Disable();
	}

	public static void Resume()
	{
		if (paused == null)
		{
			return;
		}
		if (playerWasOn)
		{
			paused.actions.Player.Enable();
		}
		if (vehicleWasOn)
		{
			paused.actions.Vehicle.Enable();
		}
		paused = null;
	}
}
