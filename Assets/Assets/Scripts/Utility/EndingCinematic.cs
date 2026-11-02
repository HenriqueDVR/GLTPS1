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

	// a ride between layers wants to see the layer it goes to, far out
	public const float RideFogShare = 0.08f;

	// going down the layer under fills in as the car drops, thicker fog keeps its edge in the haze
	public const float DownRideFogShare = 0.3f;

	private float fogTo = fogShare;

	private const float fogTime = 10f;

	// map pieces spawn and stay shown out to this far
	private const float reach = 3000f;

	private static EndingCinematic running;

	private Transform elevator;

	private CinemachineVirtualCamera shot;

	private Vector3 outward;

	// the column's middle (flat, relative to the elevator) and how far out its wall is, the camera keeps outside it
	private Vector3 columnFromCar;

	private float columnRadius;

	private const float columnMargin = 8f;

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

	// from the player's camera into the shot, and back to it at a ride's end, this long
	private const float easeIn = 2f;

	private const float easeOut = 2f;

	// the player's camera as the ride started, from the elevator (it moves)
	private Vector3 startFromCar;

	private Quaternion startRotation;

	// a ride's end: when the blend back started, nothing moves the shot after
	private float leaving = -1f;

	private CinemachineBrain brain;

	private CinemachineBlendDefinition oldBlend;

	// a ride between layers goes through a deck, and following it the camera saw inside the slab and what's under it.
	// it stays its side, watching the car go in, then cuts to the other side to watch it come out
	private bool cut;

	// off the deck's top by this much (the shaft head's frame is 5.5 m), and further under its underside so it sees the
	// hole from off to the side, not straight up into the dark
	private const float deckClearance = 7f;

	private const float undersideClearance = 30f;

	// floor to roof, the cut waits till it's all gone in, and holds on the hole this long
	private const float carHeight = 5f;

	private const float cutHold = 2f;

	private float hiddenSince = -1f;

	// steepest it looks up or down at the car, backing out up to this far to keep it
	private const float maxPitch = 32f;

	private const float maxBackOut = 65f;

	private bool smoothing;

	private Vector3 smoothed;

	private Vector3 smoothVelocity;

	private const float smoothTime = 0.45f;

	public static void Begin(Transform elevator, float fog = fogShare)
	{
		Camera camera = Camera.main;
		if (running != null || elevator == null || camera == null)
		{
			return;
		}
		running = new GameObject("EndingCinematic").AddComponent<EndingCinematic>();
		running.fogTo = fog;
		running.Setup(elevator, camera);
	}

	// a ride between layers ends on the next deck, not in the credits: the camera blends back to the player's, the fog
	// and the hud come back over the same time
	public static void End()
	{
		if (running == null)
		{
			return;
		}
		running.Leave();
		running = null;
	}

	private void Leave()
	{
		leaving = Time.time;
		if (brain != null)
		{
			brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.EaseInOut, easeOut);
		}
		shot.m_Priority = -1000;
	}

	// the blend back: the fog thickens again, the hud comes up, then it's gone
	private void Leaving()
	{
		float done = Mathf.Clamp01((Time.time - leaving) / easeOut);
		if (writtenFog >= 0f)
		{
			if (!Mathf.Approximately(RenderSettings.fogDensity, writtenFog))
			{
				baseFog = RenderSettings.fogDensity;
			}
			writtenFog = baseFog * Mathf.Lerp(fogTo, 1f, Mathf.SmoothStep(0f, 1f, done));
			RenderSettings.fogDensity = writtenFog;
		}
		foreach (CanvasGroup group in hud)
		{
			if (group != null)
			{
				group.alpha = Mathf.Max(group.alpha, done);
			}
		}
		if (done >= 1f)
		{
			Destroy(gameObject);
		}
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
		columnFromCar = new Vector3(-radial.x, 0f, -radial.z);
		columnRadius = ColumnRadius();
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
		startFromCar = camera.transform.position - Anchor();
		startRotation = camera.transform.rotation;
		// blocks in the gaps out to the haze
		EndingCity.Build(transform, elevator.root);
		FindHud();
		// the listener goes on the live camera's look at target, with none it sat out here and everything went quiet
		brain = camera.GetComponent<CinemachineBrain>();
		if (brain != null)
		{
			oldBlend = brain.m_DefaultBlend;
		}
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

	// half the column's width, from its mesh like TowerCentre
	private float ColumnRadius()
	{
		foreach (MeshFilter part in elevator.root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
		{
			if (part.name == "Cylinder" && part.sharedMesh != null)
			{
				Vector3 size = Vector3.Scale(part.sharedMesh.bounds.size, part.transform.lossyScale);
				// lying on its side in the model, the two smallest of its sizes are across
				float a = Mathf.Abs(size.x);
				float b = Mathf.Abs(size.y);
				float c = Mathf.Abs(size.z);
				return (a + b + c - Mathf.Max(a, Mathf.Max(b, c))) * 0.25f;
			}
		}
		return 0f;
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
		if (leaving >= 0f)
		{
			Leaving();
			return;
		}
		float t = Mathf.SmoothStep(0f, 1f, (Time.time - started) / pullTime);
		angle += drift * Time.deltaTime;
		// round the girls from where the player had it, then out and up. past a ride's cut it's a new shot, out from
		// the tower (riding down the cut comes early, the player's side was often the tower's)
		bool jumped = Cut();
		float round = cut ? Mathf.Max(t, 0.85f) : t;
		Vector3 goal = Quaternion.AngleAxis(angle, Vector3.up) * outward;
		Vector3 from = startFlat.sqrMagnitude > 0.01f ? startFlat.normalized : goal;
		Vector3 flat = Vector3.Slerp(from, goal, round) * Mathf.Lerp(startFlat.magnitude, pullBack, round);
		Vector3 anchor = Anchor();
		Vector3 position = anchor + flat + Vector3.up * Mathf.Lerp(startUp, rise, t);
		position.y = OffDeck(position.y);
		Vector3 aim = Aim(anchor);
		position = Pitched(aim, position, goal);
		// never inside the tower, the player's camera could have been in its wall when the ride started
		Vector3 column = anchor + columnFromCar;
		Vector3 off = new Vector3(position.x - column.x, 0f, position.z - column.z);
		if (columnRadius > 0f && off.magnitude < columnRadius + columnMargin)
		{
			off = (off.sqrMagnitude > 0.01f ? off.normalized : -columnFromCar.normalized) * (columnRadius + columnMargin);
			position = new Vector3(column.x + off.x, position.y, column.z + off.z);
		}
		float fov = startFov + widen * t;
		shot.m_Lens.FieldOfView = fov;
		// out of the player's camera gently, not a jump to the shot's first frame
		float into = Mathf.SmoothStep(0f, 1f, (Time.time - started) / easeIn);
		Vector3 placed = Smoothed(Vector3.Lerp(anchor + startFromCar, position, into), jumped);
		placed.y = OffDeck(placed.y);
		Quaternion framed = Framed(aim - placed, fov, t);
		shot.transform.SetPositionAndRotation(placed, Quaternion.Slerp(startRotation, framed, into));
		// weather keeps writing its own fog while it changes, go off whatever it last set
		if (!Mathf.Approximately(RenderSettings.fogDensity, writtenFog))
		{
			baseFog = RenderSettings.fogDensity;
		}
		writtenFog = baseFog * Mathf.Lerp(1f, fogTo, Mathf.SmoothStep(0f, 1f, (Time.time - started) / fogTime));
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

	// the car's been all inside the deck a moment: true the frame it cuts to the other side
	private bool Cut()
	{
		if (cut || !LayerTransition.Crossing(out float top, out float bottom, out int way))
		{
			return false;
		}
		float floor = elevator.position.y;
		bool inside = (way > 0) ? floor > bottom + 1f : floor + carHeight < top - 1f;
		if (!inside)
		{
			return false;
		}
		if (hiddenSince < 0f)
		{
			hiddenSince = Time.time;
		}
		cut = Time.time - hiddenSince >= cutHold;
		return cut;
	}

	// the side of the deck the camera's on: where the car comes from till the cut, then where it's going
	private float OffDeck(float y)
	{
		if (!LayerTransition.Crossing(out float top, out float bottom, out int way))
		{
			return y;
		}
		bool above = (way > 0) == cut;
		return above ? Mathf.Max(y, top + deckClearance) : Mathf.Min(y, bottom - undersideClearance);
	}

	// what it looks at: the car, or while it's inside the deck the mouth of its hole on the camera's side
	private Vector3 Aim(Vector3 anchor)
	{
		if (!LayerTransition.Crossing(out float top, out float bottom, out int way))
		{
			return anchor;
		}
		bool above = (way > 0) == cut;
		return new Vector3(anchor.x, above ? Mathf.Max(anchor.y, top) : Mathf.Min(anchor.y, bottom), anchor.z);
	}

	// held off a deck while the car goes on, it backs out rather than look straight up or down at it (under the deck
	// that was all its dark underside), swinging out from the tower as it goes
	private static Vector3 Pitched(Vector3 aim, Vector3 position, Vector3 outward)
	{
		Vector3 flat = new Vector3(position.x - aim.x, 0f, position.z - aim.z);
		float wanted = Mathf.Min(Mathf.Abs(aim.y - position.y) / Mathf.Tan(maxPitch * Mathf.Deg2Rad), maxBackOut);
		if (flat.magnitude >= wanted)
		{
			return position;
		}
		float swing = 1f - flat.magnitude / Mathf.Max(wanted, 0.01f);
		Vector3 way = Vector3.Slerp(flat.sqrMagnitude > 0.01f ? flat.normalized : outward, outward, swing);
		flat = way * wanted;
		return new Vector3(aim.x + flat.x, position.y, aim.z + flat.z);
	}

	// eased after where it should be, in layout coords so a floating origin shift isn't a move. the cut is a cut
	private Vector3 Smoothed(Vector3 wanted, bool jump)
	{
		FloatingOrigin origin = GameSession.Get<FloatingOrigin>();
		Vector3 offset = (origin != null) ? origin.TotalOffset : Vector3.zero;
		Vector3 layout = wanted + offset;
		if (jump || !smoothing)
		{
			smoothing = true;
			smoothed = layout;
			smoothVelocity = Vector3.zero;
		}
		else
		{
			smoothed = Vector3.SmoothDamp(smoothed, layout, ref smoothVelocity, smoothTime);
		}
		return smoothed - offset;
	}

	// girls on the spiral's eye, from dead centre at the start. from under them it's the top third instead, so the view
	// goes out over the city below, not up at the deck
	private static Quaternion Framed(Vector3 toGirls, float fov, float t)
	{
		Camera camera = Camera.main;
		float aspect = camera != null ? camera.aspect : 16f / 9f;
		float up = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
		float under = Mathf.Lerp(1f, -1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.4f, toGirls.normalized.y)));
		// where they should be in the view, as a direction from the camera
		float across = (spiralEye - 0.5f) * 2f * up * aspect * t;
		float down = (spiralEye - 0.5f) * 2f * up * Mathf.Max(t, (1f - under) * 0.5f) * under;
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
		if (brain != null)
		{
			brain.m_DefaultBlend = oldBlend;
		}
		// static, it would carry into the next game
		WorldGenerator.HideDistance = oldHide;
		if (nodeData != null)
		{
			nodeData.spawnThreshold = oldSpawn;
		}
	}
}
