using UnityEngine;
using UnityEngine.InputSystem;

// debug only, start with -noclip. F8 flies the girl round the map: wasd along the camera, space up, ctrl down, shift fast.
// F7 shows every camera's clip planes and the ceiling tiles. F4 clouds, F5 haze dome, F6 upper level on and off,
// F3 upper level flat red with no fades, F2 hides every map piece so only the far stuff draws
public class NoClip : MonoBehaviour
{
	private const float speed = 40f;

	private const float fastSpeed = 300f;

	private Transform girl;

	private CharacterController controller;

	private bool readout;

	private bool flat;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noclip") < 0)
		{
			return;
		}
		GameObject holder = new GameObject("NoClip");
		DontDestroyOnLoad(holder);
		holder.AddComponent<NoClip>();
	}

	private void Update()
	{
		Keyboard keyboard = Keyboard.current;
		if (keyboard == null)
		{
			return;
		}
		if (keyboard.f8Key.wasPressedThisFrame)
		{
			Toggle();
		}
		if (keyboard.f7Key.wasPressedThisFrame)
		{
			readout = !readout;
		}
		if (keyboard.f2Key.wasPressedThisFrame)
		{
			HideWorld();
		}
		if (keyboard.f3Key.wasPressedThisFrame)
		{
			flat = !flat;
			Shader.SetGlobalFloat("_DebugFlat", flat ? 1f : 0f);
		}
		if (keyboard.f4Key.wasPressedThisFrame)
		{
			Flip("DistantCloud");
		}
		if (keyboard.f5Key.wasPressedThisFrame)
		{
			Flip("DistantSky");
		}
		if (keyboard.f6Key.wasPressedThisFrame)
		{
			Flip("DistantCeiling");
		}
		if (girl == null || controller == null || controller.enabled || Camera.main == null)
		{
			return;
		}
		Transform view = Camera.main.transform;
		Vector3 move = Vector3.zero;
		move += view.forward * ((keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
		move += view.right * ((keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f));
		move += Vector3.up * ((keyboard.spaceKey.isPressed ? 1f : 0f) - (keyboard.leftCtrlKey.isPressed ? 1f : 0f));
		girl.position += Vector3.ClampMagnitude(move, 1f) * (keyboard.leftShiftKey.isPressed ? fastSpeed : speed) * Time.deltaTime;
	}

	private readonly System.Collections.Generic.List<string> off = new System.Collections.Generic.List<string>();

	private readonly System.Collections.Generic.List<MeshRenderer> hiddenWorld = new System.Collections.Generic.List<MeshRenderer>();

	private void HideWorld()
	{
		if (hiddenWorld.Count > 0)
		{
			foreach (MeshRenderer part in hiddenWorld)
			{
				if (part != null)
				{
					part.enabled = true;
				}
			}
			hiddenWorld.Clear();
			off.Remove("world");
			return;
		}
		foreach (MeshRenderer part in FindObjectsOfType<MeshRenderer>())
		{
			// not the far stuff, the ui, or the psx screen quad the picture is shown on
			string root = part.transform.root.name;
			if (part.enabled && !part.name.StartsWith("Distant") && root != "DistantPillar" && root != "PSXRenderer" && part.gameObject.layer != 5)
			{
				part.enabled = false;
				hiddenWorld.Add(part);
			}
		}
		off.Add("world");
	}

	private void Flip(string layer)
	{
		bool show = off.Contains(layer);
		if (show)
		{
			off.Remove(layer);
		}
		else
		{
			off.Add(layer);
		}
		foreach (MeshRenderer part in FindObjectsOfType<MeshRenderer>())
		{
			if (part.name == layer)
			{
				part.enabled = show;
			}
		}
	}

	private void OnGUI()
	{
		if (!readout)
		{
			return;
		}
		System.Text.StringBuilder text = new System.Text.StringBuilder();
		text.AppendLine("main: " + (Camera.main != null ? Camera.main.name : "none") + (off.Count > 0 ? "   off: " + string.Join(", ", off) : ""));
		foreach (Camera camera in Camera.allCameras)
		{
			text.AppendLine(camera.name + "  far " + camera.farClipPlane.ToString("F0") + "  near " + camera.nearClipPlane.ToString("F2") + "  fov " + camera.fieldOfView.ToString("F0") + (camera.orthographic ? "  ortho" : "") + "  rt " + (camera.targetTexture != null ? camera.targetTexture.name : "-") + "  depth " + camera.depth);
		}
		Cinemachine.CinemachineCore core = Cinemachine.CinemachineCore.Instance;
		for (int i = 0; i < core.VirtualCameraCount; i++)
		{
			Cinemachine.CinemachineVirtualCameraBase vcam = core.GetVirtualCamera(i);
			text.AppendLine("vcam " + vcam.name + "  far " + vcam.State.Lens.FarClipPlane.ToString("F0") + (core.IsLive(vcam) ? "  live" : ""));
		}
		Vector3 eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
		text.AppendLine("eye " + eye.ToString("F0") + (flat ? "   FLAT" : ""));
		foreach (MeshRenderer part in FindObjectsOfType<MeshRenderer>())
		{
			if (part.name == "DistantCeiling")
			{
				Vector3 apart = part.transform.position - eye;
				text.AppendLine("tile  x " + (apart.x / 1000f).ToString("F1") + "km  z " + (apart.z / 1000f).ToString("F1") + "km  up " + apart.y.ToString("F0") + "  " + (part.isVisible ? "drawn" : "NOT drawn") + (part.enabled ? "" : "  off") + "  fade " + part.sharedMaterial.GetFloat("_FarFade").ToString("F0") + "  bounds " + part.bounds.size.ToString("F0"));
			}
		}
		GUI.color = Color.black;
		GUI.Label(new Rect(11f, 11f, 900f, 600f), text.ToString());
		GUI.color = Color.yellow;
		GUI.Label(new Rect(10f, 10f, 900f, 600f), text.ToString());
	}

	private void Toggle()
	{
		if (girl != null && controller != null && !controller.enabled)
		{
			controller.enabled = true;
			girl = null;
			return;
		}
		Transform local = Local();
		controller = local != null ? local.GetComponentInChildren<CharacterController>() : null;
		// not while she's on the ket, that already turns the controller off
		if (controller == null || !controller.enabled)
		{
			return;
		}
		girl = controller.transform;
		controller.enabled = false;
	}

	private static Transform Local()
	{
		if (CoopPlayers.Instance != null && CoopPlayers.Instance.Local != null)
		{
			return CoopPlayers.Instance.Local;
		}
		GameObject data = GameObject.FindWithTag("NodeData");
		NodeData nodeData = data != null ? data.GetComponent<NodeData>() : null;
		return nodeData != null && nodeData.player != null ? nodeData.player.transform : null;
	}
}
