using UnityEngine.SceneManagement;

// build indices in one place
public static class Scenes
{
	public const int Title = 0;

	public const int Game = 1;

	public static bool InGame => SceneManager.GetActiveScene().buildIndex == Game;
}
