using Cinemachine;
using UnityEngine;

// listen from the character not the camera, turned like the camera
[DefaultExecutionOrder(1000)]
public class CharacterListener : MonoBehaviour
{
	private Camera watched;

	private CinemachineBrain brain;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		GameObject obj = new GameObject("CharacterListener");
		DontDestroyOnLoad(obj);
		obj.AddComponent<CharacterListener>();
		obj.AddComponent<AudioListener>().enabled = false;
	}

	private void LateUpdate()
	{
		Camera camera = Camera.main;
		AudioListener mine = GetComponent<AudioListener>();
		if (camera != watched)
		{
			watched = camera;
			brain = camera != null ? camera.GetComponent<CinemachineBrain>() : null;
			AudioListener old = camera != null ? camera.GetComponent<AudioListener>() : null;
			if (old != null)
			{
				old.enabled = false;
			}
			mine.enabled = camera != null;
		}
		if (camera == null)
		{
			return;
		}
		Transform target = null;
		if (brain != null && brain.ActiveVirtualCamera != null)
		{
			target = brain.ActiveVirtualCamera.LookAt;
			if (target == null)
			{
				target = brain.ActiveVirtualCamera.Follow;
			}
		}
		transform.SetPositionAndRotation(target != null ? target.position : camera.transform.position, camera.transform.rotation);
	}
}
