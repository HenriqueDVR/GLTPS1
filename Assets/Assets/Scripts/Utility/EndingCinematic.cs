using System.Collections.Generic;
using Cinemachine;
using UnityEngine;

// last elevator ride as a shot over the city. the camera lets go of the mouse and pulls back to the side of the
// tower with the girls on the golden spiral's eye, they can still walk about. hud fades, fog thins, map pieces stay out further
[DefaultExecutionOrder(-10)]
public class EndingCinematic : MonoBehaviour
{
	// where the camera ends up from the girls, and how long it takes to get there
	private const float pullBack = 60f;

	private const float rise = 28f;

	private const float pullTime = 24f;

	// side on to the tower, so it stands at the left edge of the picture and the city fills the rest. 0 is straight out
	private const float sideways = 0.8f;

	// degrees a second it drifts round
	private const float drift = 0.6f;

	// girls go where the golden spiral winds in, lower left, 1/phi^2 in from the left and the bottom
	private const float spiralEye = 0.381966f;

	// a little wider at the end
	private const float widen = 8f;

	// fog comes down to this share of what the weather has, over this long
	private const float fogShare = 0.3f;

	private const float fogTime = 10f;

	// map pieces spawn and stay shown out to this far
	private const float reach = 3000f;

	private static EndingCinematic running;

	private Transform elevator;

	private CinemachineVirtualCamera shot;

	private Vector3 outward;

	private Vector3 startFlat;

	private float startUp;

	private float startFov;

	private float started;

	private float angle;

	private float baseFog;

	private float writtenFog = -1f;

	private float oldHide;

	private float oldSpawn;

	private NodeData nodeData;

	// hud that was up when the ride started, not the ending screen
	private readonly List<CanvasGroup> hud = new List<CanvasGroup>();

	private const float hudFadeTime = 2f;

	public static void Begin(Transform elevator)
	{
		Camera camera = Camera.main;
		if (running != null || elevator == null || camera == null)
		{
			return;
		}
		running = new GameObject("EndingCinematic").AddComponent<EndingCinematic>();
		running.Setup(elevator, camera);
	}

	private void Setup(Transform rideOn, Camera camera)
	{
		elevator = rideOn;
		started = Time.time;
		Vector3 anchor = Anchor();
		Vector3 startOffset = camera.transform.position - anchor;
		startFlat = new Vector3(startOffset.x, 0f, startOffset.z);
		startUp = startOffset.y;
		// the elevator runs up the outside of the column, out from its middle is open air
		Vector3 radial = anchor - TowerCentre();
		radial.y = 0f;
		radial = radial.sqrMagnitude > 1f ? radial.normalized : elevator.root.right;
		Vector3 tangent = Vector3.Cross(Vector3.up, radial);
		outward = (radial * (1f - sideways) + tangent * sideways).normalized;
		// whichever side puts the tower on the left, the girls are left of middle
		Vector3 right = Vector3.Cross(Vector3.up, -outward);
		if (Vector3.Dot(-radial, right) > 0f)
		{
			outward = (radial * (1f - sideways) - tangent * sideways).normalized;
		}
		// no body or aim on it, this places it every frame
		shot = new GameObject("EndingCamera").AddComponent<CinemachineVirtualCamera>();
		shot.transform.SetParent(transform);
		shot.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
		LensSettings lens = LensSettings.FromCamera(camera);
		startFov = lens.FieldOfView;
		shot.m_Lens = lens;
		shot.m_Priority = 1000;
		// blocks in the gaps out to the haze
		EndingCity.Build(transform, elevator.root);
		FindHud();
		// the listener goes on the live camera's look at target, with none it sat out here and everything went quiet
		CinemachineBrain brain = camera.GetComponent<CinemachineBrain>();
		ICinemachineCamera player = brain != null ? brain.ActiveVirtualCamera : null;
		if (player != null)
		{
			shot.LookAt = player.LookAt != null ? player.LookAt : player.Follow;
		}
		oldHide = WorldGenerator.HideDistance;
		WorldGenerator.HideDistance = Mathf.Max(oldHide, reach);
		GameObject data = GameObject.FindWithTag("NodeData");
		nodeData = data != null ? data.GetComponent<NodeData>() : null;
		if (nodeData != null)
		{
			oldSpawn = nodeData.spawnThreshold;
			nodeData.spawnThreshold = Mathf.Max(oldSpawn, reach);
		}
	}

	private Vector3 Anchor()
	{
		return elevator.position + Vector3.up * 1.5f;
	}

	// middle of the column, from its mesh so it works while the piece's renderers are off
	private Vector3 TowerCentre()
	{
		foreach (MeshFilter part in elevator.root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
		{
			if (part.name == "Cylinder" && part.sharedMesh != null)
			{
				return part.transform.TransformPoint(part.sharedMesh.bounds.center);
			}
		}
		return elevator.root.position;
	}

	private void LateUpdate()
	{
		if (elevator == null)
		{
			Destroy(gameObject);
			return;
		}
		float t = Mathf.SmoothStep(0f, 1f, (Time.time - started) / pullTime);
		angle += drift * Time.deltaTime;
		// round the girls from where the player had it, then out and up
		Vector3 goal = Quaternion.AngleAxis(angle, Vector3.up) * outward;
		Vector3 from = startFlat.sqrMagnitude > 0.01f ? startFlat.normalized : goal;
		Vector3 flat = Vector3.Slerp(from, goal, t) * Mathf.Lerp(startFlat.magnitude, pullBack, t);
		Vector3 anchor = Anchor();
		Vector3 position = anchor + flat + Vector3.up * Mathf.Lerp(startUp, rise, t);
		float fov = startFov + widen * t;
		shot.m_Lens.FieldOfView = fov;
		shot.transform.SetPositionAndRotation(position, Framed(anchor - position, fov, t));
		// weather keeps writing its own fog while it changes, go off whatever it last set
		if (!Mathf.Approximately(RenderSettings.fogDensity, writtenFog))
		{
			baseFog = RenderSettings.fogDensity;
		}
		writtenFog = baseFog * Mathf.Lerp(1f, fogShare, Mathf.SmoothStep(0f, 1f, (Time.time - started) / fogTime));
		RenderSettings.fogDensity = writtenFog;
		float shown = 1f - Mathf.Clamp01((Time.time - started) / hudFadeTime);
		foreach (CanvasGroup group in hud)
		{
			if (group != null)
			{
				group.alpha = Mathf.Min(group.alpha, shown);
			}
		}
	}

	// girls on the spiral's eye, from dead centre at the start
	private static Quaternion Framed(Vector3 toGirls, float fov, float t)
	{
		Camera camera = Camera.main;
		float aspect = camera != null ? camera.aspect : 16f / 9f;
		float up = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
		// where they should be in the view, as a direction from the camera
		float across = (spiralEye - 0.5f) * 2f * up * aspect * t;
		float down = (spiralEye - 0.5f) * 2f * up * t;
		Vector3 seen = new Vector3(across, down, 1f);
		return Quaternion.LookRotation(toGirls, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(seen, Vector3.up));
	}

	// what's on the screen space canvases right now minus the ending screen. the game picture is a quad not ui,
	// menus opened later aren't in it
	private void FindHud()
	{
		// the black and the credits, PillarNodeAnimationEvents shows the tagged object's first child
		GameObject tagged = GameObject.FindWithTag("EndingScreen");
		Transform ending = tagged == null ? null : tagged.transform.childCount > 0 ? tagged.transform.GetChild(0) : tagged.transform;
		foreach (Canvas canvas in FindObjectsOfType<Canvas>())
		{
			if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace)
			{
				continue;
			}
			foreach (Transform child in canvas.transform)
			{
				if (!child.gameObject.activeInHierarchy || (ending != null && (child == ending || ending.IsChildOf(child))))
				{
					continue;
				}
				CanvasGroup group = child.GetComponent<CanvasGroup>();
				hud.Add(group != null ? group : child.gameObject.AddComponent<CanvasGroup>());
			}
		}
	}

	private void OnDestroy()
	{
		if (running == this)
		{
			running = null;
		}
		// static, it would carry into the next game
		WorldGenerator.HideDistance = oldHide;
		if (nodeData != null)
		{
			nodeData.spawnThreshold = oldSpawn;
		}
	}
}
