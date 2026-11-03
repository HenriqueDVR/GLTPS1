using UnityEngine;
using UnityEngine.InputSystem;
using LiteNetLib;
using LiteNetLib.Utils;

// standing still or on the back seat she's not a statue: looks about, at the other girl, the ket, the sky, her feet,
// shifts her weight, and now and then does something: warms her hands, catches a snowflake, points at things from the
// back seat. the number keys are emotes: she stands still and does it, and the other player sees it (owner sends it).
// all on top of the idle clip, on the bones after the animator's posed them: hips and knees too, feet staying put, so
// it's her whole body and not an arm on a statue. needs no clips and fades out as soon as anything else plays. in a
// blizzard she leans into it and shades her eyes, walking too. added at runtime (Girl.Init), any control.
// idle ones are looks only: each machine picks its own and nothing's sent
public class GirlIdle : MonoBehaviour
{
	private enum Look
	{
		Ahead,
		Side,
		Sky,
		Feet,
		Other,
		Ket
	}

	public enum Act
	{
		None,
		Stretch,
		WarmHands,
		Yawn,
		ShadeEyes,
		Scratch,
		Wave,
		Tilt,
		Point,
		ArmsUp,
		Bob,
		ReachSky,
		CatchSnow
	}

	// a bone's own axes: which way its length runs, and which way the palm faces when the arm's held out as modelled.
	// aiming only the length left the roll to chance, the arm came out twisted
	private struct Bone
	{
		public Transform Transform;

		public Quaternion Frame;
	}

	private struct Leg
	{
		public Transform Thigh;

		public Transform Shin;

		public Transform Foot;

		public Vector3 Planted;

		public Quaternion PlantedTurn;
	}

	// what the rest of her does during an act, degrees and metres. leans and shifts are to her right, forward
	private struct Motion
	{
		public float Drop;

		public float Shift;

		public float Lean;

		public float Side;

		public float Twist;

		public float Nod;

		public float Tilt;
	}

	// still this long before she starts
	private const float settle = 1.5f;

	private const float fade = 0.4f;

	// how long a look lasts, and how long she faces ahead between them
	private const float lookMin = 1.5f;

	private const float lookMax = 4f;

	private const float aheadMin = 2.5f;

	private const float aheadMax = 7f;

	private const float turnTime = 0.35f;

	// of a turn, what the torso takes, the head does the rest
	private const float torsoShare = 0.3f;

	private const float yawMost = 70f;

	private const float pitchMost = 30f;

	private const float sideMost = 55f;

	private const float swayMost = 2.5f;

	private const float swayRate = 0.15f;

	private const float otherWithin = 15f;

	private const float ketWithin = 8f;

	// doing something: how long between, standing and on the back with nothing else to do
	private const float actGapMin = 12f;

	private const float actGapMax = 35f;

	private const float rideGapMin = 6f;

	private const float rideGapMax = 18f;

	private const float actFade = 0.35f;

	private const float itemWithin = 20f;

	private const float fastRide = 4f;

	// lying back on the seat already: her neck hasn't much further to go, least of all back
	private const float rideYawMost = 35f;

	private const float ridePitchUp = 5f;

	private const float ridePitchDown = 25f;

	// the head's wider than the shoulders, straight from the shoulder the arm goes through the face
	private const float clearOfHead = 0.4f;

	private const float emoteTurnRate = 360f;

	private const float flakeFall = 2.8f;

	private const float flakeMelt = 1.2f;

	private const float flakeFrom = 1.2f;

	private const float flakeSize = 0.022f;

	private const float roofUpTo = 40f;

	// WeatherManager's blizzard preset
	private const int blizzard = 2;

	private const float stormFade = 0.8f;

	// a held elbow's never dead straight, and a pointing one only just
	private const float armMost = 0.93f;

	private const float pointMost = 0.985f;

	private static readonly Collider[] nearby = new Collider[16];

	private static readonly NetDataWriter writer = new NetDataWriter();

	private static readonly Act[] emotes = { Act.Wave, Act.Point, Act.Scratch, Act.ArmsUp, Act.Tilt, Act.Stretch };

	private static readonly Key[] emoteKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };

	private Girl girl;

	private Animator animator;

	private Transform head;

	private Transform torso;

	private Transform root;

	private Leg[] legs;

	// her own axes this frame. lying on the back seat with her head to the front, her right's the ket's left
	private Vector3 ownRight;

	private Vector3 ownFront;

	private Vector3 ownSpine;

	private Bone[] left;

	private Bone[] right;

	private CharacterVehicleInteraction vehicle;

	private WeatherManager weather;

	private int velocity;

	private int sit;

	private int groundMask;

	private int[] free;

	private Look look;

	private float nextLook;

	private float stillFor;

	private float weight;

	private float yaw;

	private float pitch;

	private float roll;

	private float yawTo;

	private float pitchTo;

	private float rollTo;

	private float yawSpeed;

	private float pitchSpeed;

	private float rollSpeed;

	private float seed;

	private VehicleMovement ket;

	private Act act;

	private float actWeight;

	private float actUntil;

	private float actStart;

	private float actLength;

	private float tiltSide;

	private float stormWeight;

	private bool storming;

	private float nextRoof;

	private float nextAct;

	private Vector3 pointAt;

	private bool pointRight;

	private bool emoting;

	private Vector3 emoteFacing;

	private InputContextStack.Token emoteHold;

	private bool flakeDue;

	private Transform flake;

	private float flakeElapsed;

	private Vector3 flakeStart;

	public void Init(Girl girl)
	{
		this.girl = girl;
		animator = girl.Animator;
		string name = girl.Id.ToString();
		torso = animator != null ? animator.transform.Find(name + "/Root/Torso") : null;
		head = torso != null ? torso.Find("Head") : null;
		if (head == null)
		{
			enabled = false;
			return;
		}
		left = Arm(".L");
		right = Arm(".R");
		root = torso.parent;
		legs = new[] { LegOf(".L"), LegOf(".R") };
		if (legs[0].Foot == null || legs[1].Foot == null)
		{
			legs = null;
		}
		vehicle = girl.GetComponentInChildren<CharacterVehicleInteraction>();
		velocity = Animator.StringToHash("velocityXZ");
		sit = Animator.StringToHash(name + "_Sit_Ket_2");
		groundMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Vehicle", "Player", "AI", "Items", "Barrel");
		free = new[] { Animator.StringToHash("None"), Animator.StringToHash("none"), Animator.StringToHash("locomotion"), sit };
		seed = Random.value * 100f;
		nextAct = Time.time + Random.Range(actGapMin * 0.5f, actGapMax);
	}

	private Bone[] Arm(string side)
	{
		Transform shoulder = torso.Find("Shoulder" + side);
		Transform forearm = shoulder != null ? shoulder.Find("Forearm" + side) : null;
		Transform hand = forearm != null ? forearm.Find("Hand" + side) : null;
		if (hand == null)
		{
			return null;
		}
		foreach (SkinnedMeshRenderer skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
		{
			int up = System.Array.IndexOf(skin.bones, shoulder);
			int mid = System.Array.IndexOf(skin.bones, forearm);
			int end = System.Array.IndexOf(skin.bones, hand);
			if (up < 0 || mid < 0 || end < 0 || skin.sharedMesh == null)
			{
				continue;
			}
			// as modelled, from the mesh's own record of it
			Matrix4x4[] bind = skin.sharedMesh.bindposes;
			Vector3 down = skin.transform.InverseTransformDirection(Vector3.down);
			Vector3 elbow = bind[mid].inverse.GetColumn(3);
			Vector3 wrist = bind[end].inverse.GetColumn(3);
			Vector3 start = bind[up].inverse.GetColumn(3);
			return new[] { BoneOf(shoulder, bind[up], elbow - start, down), BoneOf(forearm, bind[mid], wrist - elbow, down), BoneOf(hand, bind[end], wrist - elbow, down) };
		}
		return null;
	}

	private Leg LegOf(string side)
	{
		Transform thigh = root.Find("Pelvis/Thigh" + side);
		Transform shin = thigh != null ? thigh.Find("Shin" + side) : null;
		return new Leg { Thigh = thigh, Shin = shin, Foot = shin != null ? shin.Find("Foot" + side) : null };
	}

	private static Bone BoneOf(Transform bone, Matrix4x4 bind, Vector3 along, Vector3 down)
	{
		Matrix4x4 modelled = bind.inverse;
		Quaternion rest = Quaternion.LookRotation(modelled.GetColumn(2), modelled.GetColumn(1));
		return new Bone { Transform = bone, Frame = Quaternion.Inverse(rest) * Quaternion.LookRotation(along, Vector3.ProjectOnPlane(down, along)) };
	}

	private bool Riding => animator.GetCurrentAnimatorStateInfo(1).shortNameHash == sit && (vehicle == null || !vehicle.constraint);

	// nothing but standing there, or sitting on the back
	private bool Free()
	{
		if (!emoting && animator.GetFloat(velocity) > 0.05f && !Riding)
		{
			return false;
		}
		return Unoccupied();
	}

	private bool Unoccupied()
	{
		if (vehicle != null && vehicle.constraint)
		{
			return false;
		}
		// the base layer's just the idle: the ket, carrying, eating, the rifle, dying are all on the ones above
		for (int layer = 1; layer < animator.layerCount; layer++)
		{
			if (animator.GetLayerWeight(layer) <= 0f)
			{
				continue;
			}
			if (animator.IsInTransition(layer) || System.Array.IndexOf(free, animator.GetCurrentAnimatorStateInfo(layer).shortNameHash) < 0)
			{
				return false;
			}
		}
		return true;
	}

	private void ReadFrame()
	{
		Vector3 across = left != null && right != null ? right[0].Transform.position - left[0].Transform.position : animator.transform.right;
		ownRight = across.normalized;
		ownSpine = (head.position - torso.position).normalized;
		ownFront = Vector3.Cross(ownRight, ownSpine).normalized;
	}

	private void Update()
	{
		if (girl != Girls.Local || head == null || emoting || girl.Input == null)
		{
			return;
		}
		InputContext mode = girl.Input.Contexts.Mode;
		Keyboard keyboard = Keyboard.current;
		if (keyboard == null || (mode != InputContext.OnFoot && mode != InputContext.Riding))
		{
			return;
		}
		for (int i = 0; i < emoteKeys.Length; i++)
		{
			if (keyboard[emoteKeys[i]].wasPressedThisFrame && Free())
			{
				Camera view = Camera.main;
				Vector3 toward = view != null ? view.transform.forward : animator.transform.forward;
				Emote(emotes[i], toward);
				SendEmote(i, toward);
				return;
			}
		}
	}

	private static void SendEmote(int which, Vector3 toward)
	{
		if (!CoopSession.InGame)
		{
			return;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Emote);
		writer.Put((byte)which);
		writer.Put(toward.x);
		writer.Put(toward.y);
		writer.Put(toward.z);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	// the other player's done one
	public static void OnEmote(NetDataReader reader)
	{
		int which = reader.GetByte();
		Vector3 toward = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
		Transform remote = CoopPlayers.Instance != null ? CoopPlayers.Instance.Remote : null;
		GirlIdle idle = remote != null ? remote.GetComponent<GirlIdle>() : null;
		if (idle != null && which < emotes.Length)
		{
			idle.Emote(emotes[which], toward);
		}
	}

	private void Emote(Act what, Vector3 toward)
	{
		emoting = true;
		stillFor = settle;
		emoteFacing = Vector3.ProjectOnPlane(toward, Vector3.up).normalized;
		if (girl == Girls.Local && girl.Input != null && girl.Input.Contexts.Mode == InputContext.OnFoot)
		{
			emoteHold = girl.Input.Contexts.Push(InputContext.Emoting, "emote");
		}
		yawTo = pitchTo = rollTo = 0f;
		ReadFrame();
		if (what == Act.Point)
		{
			PointTowards(head.position + toward.normalized * 50f);
		}
		Begin(what);
	}

	private void EndEmote()
	{
		emoting = false;
		emoteHold?.Release();
		emoteHold = null;
	}

	private void LateUpdate()
	{
		ReadFrame();
		stillFor = Free() ? stillFor + Time.deltaTime : 0f;
		weight = Mathf.MoveTowards(weight, stillFor >= settle ? 1f : 0f, Time.deltaTime / fade);
		UpdateStorm();
		if (weight <= 0f && !emoting)
		{
			look = Look.Ahead;
			act = Act.None;
			actWeight = 0f;
			yaw = yawTo = pitch = pitchTo = roll = rollTo = 0f;
			nextLook = Time.time + Random.Range(0f, aheadMax - aheadMin);
			nextAct = Mathf.Max(nextAct, Time.time + actGapMin * 0.5f);
			PoseStorm();
			return;
		}
		// something else started (shot, downed): it goes
		if (emoting && stillFor <= 0f)
		{
			EndEmote();
		}
		UpdateAct();
		float actTime = Time.time - actStart;
		if (act == Act.Point && emoting)
		{
			FacePoint();
		}
		else if (act == Act.ReachSky && right != null)
		{
			Gaze(right[2].Transform.position);
		}
		else if (act == Act.CatchSnow && right != null)
		{
			Gaze(flake != null ? flake.position : right[2].Transform.position);
			pitchTo = Mathf.Min(pitchTo, 22f);
		}
		if (act == Act.None && Time.time >= nextLook)
		{
			PickLook();
		}
		if (act == Act.None)
		{
			Aim();
		}
		yaw = Mathf.SmoothDamp(yaw, yawTo, ref yawSpeed, turnTime);
		pitch = Mathf.SmoothDamp(pitch, pitchTo, ref pitchSpeed, turnTime);
		roll = Mathf.SmoothDamp(roll, rollTo, ref rollSpeed, turnTime);
		// the weight going from foot to foot, slow enough to not read as a wobble
		float swing = (Mathf.PerlinNoise(Time.time * swayRate, seed) - 0.5f) * 2f;
		Motion move = act != Act.None ? Moves(actTime) : default;
		float moved = weight * actWeight;
		bool riding = Riding;
		if (!riding)
		{
			// always a little down: a straight knee jumps a long way for the smallest move
			MoveHips(ownRight * (weight * swing * 0.015f + moved * move.Shift) - Vector3.up * (weight * 0.012f + moved * move.Drop));
		}
		float bend = riding ? 0.6f : 1f;
		float turnBy = riding ? Mathf.Clamp(yaw, -rideYawMost, rideYawMost) : yaw;
		float nodBy = riding ? Mathf.Clamp(pitch, -ridePitchUp, ridePitchDown) : pitch;
		// round her own spine and not the world's up: on the back seat she's lying back and that bent her neck sideways
		torso.rotation = Quaternion.AngleAxis(weight * turnBy * torsoShare + moved * move.Twist * bend, ownSpine) * Quaternion.AngleAxis(weight * swing * swayMost - moved * move.Side * bend, ownFront) * Quaternion.AngleAxis(moved * move.Lean * bend, ownRight) * torso.rotation;
		ReadFrame();
		head.rotation = Quaternion.AngleAxis(weight * turnBy * (1f - torsoShare), ownSpine) * Quaternion.AngleAxis(weight * nodBy + moved * move.Nod, ownRight) * Quaternion.AngleAxis(weight * roll + moved * move.Tilt, ownFront) * head.rotation;
		PoseArms(actTime);
		PoseStorm();
		UpdateFlake();
	}

	// the body moves, the feet stay where they were: the knees take it up
	private void MoveHips(Vector3 by)
	{
		if (legs == null)
		{
			return;
		}
		for (int i = 0; i < legs.Length; i++)
		{
			legs[i].Planted = legs[i].Foot.position;
			legs[i].PlantedTurn = legs[i].Foot.rotation;
		}
		root.position += by;
		for (int i = 0; i < legs.Length; i++)
		{
			Plant(legs[i]);
		}
	}

	private void Plant(Leg leg)
	{
		Vector3 hip = leg.Thigh.position;
		Vector3 knee = leg.Shin.position;
		float upper = (knee - hip).magnitude;
		float lower = (leg.Foot.position - knee).magnitude;
		Vector3 to = leg.Planted - hip;
		float far = Mathf.Clamp(to.magnitude, Mathf.Abs(upper - lower) + 0.001f, (upper + lower) * 0.999f);
		Vector3 dir = to.normalized;
		float along = (upper * upper - lower * lower + far * far) / (2f * far);
		float outward = Mathf.Sqrt(Mathf.Max(upper * upper - along * along, 0f));
		// a nearly straight leg says next to nothing about which way the knee goes, left to that it flicked sideways. a nudge
		// to the front settles it, and a properly bent knee, walking, still goes its own way
		Vector3 side = (Vector3.ProjectOnPlane(knee - hip, dir) + Vector3.ProjectOnPlane(animator.transform.forward, dir) * 0.05f).normalized;
		Vector3 wanted = hip + dir * along + side * outward;
		leg.Thigh.rotation = Quaternion.FromToRotation(knee - hip, wanted - hip) * leg.Thigh.rotation;
		leg.Shin.rotation = Quaternion.FromToRotation(leg.Foot.position - leg.Shin.position, leg.Planted - leg.Shin.position) * leg.Shin.rotation;
		leg.Foot.rotation = leg.PlantedTurn;
	}

	private static float Ease(float time, float from, float to)
	{
		return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - from) / (to - from)));
	}

	private Motion Moves(float time)
	{
		Motion move = default;
		float t = Time.time;
		switch (act)
		{
		case Act.Stretch:
		{
			float up = Ease(time, 0f, 0.8f) * (1f - Ease(time, actLength - 0.7f, actLength));
			float across = Mathf.Sin(time * 2.4f);
			float after = Ease(time, actLength - 0.7f, actLength);
			move.Lean = -7f * up + 4f * after;
			move.Side = across * 5f * up;
			move.Shift = across * 0.02f * up;
			move.Nod = -16f * up + 5f * after;
			move.Drop = 0.015f * after;
			break;
		}
		case Act.WarmHands:
			move.Drop = 0.02f;
			move.Lean = 7f;
			move.Nod = 6f + Mathf.Sin(time * 2.2f) * 3f;
			move.Side = Mathf.Sin(time * 1.1f) * 1.5f;
			move.Twist = Mathf.Sin(time * 1.7f) * 3f;
			move.Shift = Mathf.Sin(time * 1.1f) * 0.012f;
			move.Tilt = Mathf.Sin(time * 17f) * 0.6f;
			break;
		case Act.Yawn:
		{
			float open = YawnOpen(time);
			float after = Ease(time, 1.7f, 2.6f);
			move.Lean = -4f * open + 3f * after;
			move.Nod = -9f * open + 5f * after;
			move.Drop = 0.006f * open + 0.018f * after;
			move.Twist = 3f * open;
			move.Side = 2f * open;
			move.Shift = 0.015f * open - 0.01f * after;
			break;
		}
		case Act.ShadeEyes:
			move.Lean = 5f;
			move.Drop = 0.01f;
			move.Side = Mathf.Sin(time * 0.9f) * 2f;
			move.Twist = Mathf.Sin(time * 0.6f) * 5f;
			break;
		case Act.Scratch:
			move.Side = -2f;
			move.Shift = -0.012f;
			move.Drop = 0.008f;
			move.Twist = Mathf.Sin(time * 1.5f) * 3f;
			move.Tilt = Mathf.Sin(time * 3f) * 1.5f;
			break;
		case Act.Wave:
		{
			float wag = Mathf.Sin(t * 9f);
			move.Side = -3f;
			move.Shift = -0.02f;
			move.Twist = wag * 1.5f;
			move.Nod = -2f;
			move.Tilt = -5f;
			move.Drop = 0.004f;
			break;
		}
		case Act.Tilt:
			move.Side = tiltSide * 2f;
			move.Shift = tiltSide * 0.012f;
			move.Twist = tiltSide * 4f;
			move.Lean = 2f;
			move.Drop = 0.006f;
			break;
		case Act.Point:
		{
			float side = pointRight ? 1f : -1f;
			move.Twist = side * 6f;
			move.Side = side * 2f;
			move.Shift = side * 0.015f;
			move.Lean = 4f;
			break;
		}
		case Act.ArmsUp:
		{
			float across = Mathf.Sin(time * 3f);
			move.Side = across * 5f;
			move.Shift = across * 0.02f;
			move.Nod = -10f;
			move.Lean = -3f;
			move.Drop = across * across * 0.01f;
			break;
		}
		case Act.Bob:
		{
			float beat = Mathf.Sin(t * 4f);
			move.Tilt = beat * 7f;
			move.Nod = beat * beat * 5f;
			move.Side = -beat * 2.5f;
			move.Shift = -beat * 0.012f;
			move.Twist = beat * 2f;
			move.Drop = beat * beat * 0.01f;
			break;
		}
		case Act.ReachSky:
			move.Side = 3f * Ease(time, 0f, 1.1f) * (1f - Ease(time, 2.3f, 3f));
			move.Lean = 2f * Ease(time, 2.3f, 3f);
			break;
		case Act.CatchSnow:
		{
			float reach = Ease(time, flakeFall * 0.35f, flakeFall * 0.8f);
			move.Lean = 4f * reach;
			move.Drop = 0.012f * reach;
			move.Twist = 4f * reach;
			move.Side = 2f * reach;
			move.Shift = 0.012f * reach;
			break;
		}
		}
		return move;
	}

	// mouth covered at its widest, hand back down after
	private static float YawnOpen(float time)
	{
		return Ease(time, 0f, 0.8f) * (1f - Ease(time, 1.7f, 2.6f));
	}

	// a blizzard on her: not under cover, and each machine works it out from the same weather
	private void UpdateStorm()
	{
		if (Time.time >= nextRoof)
		{
			nextRoof = Time.time + 0.5f;
			if (weather == null)
			{
				weather = FindObjectOfType<WeatherManager>();
			}
			storming = weather != null && weather.start && weather.index == blizzard && !Physics.Raycast(head.position + Vector3.up, Vector3.up, roofUpTo, groundMask, QueryTriggerInteraction.Ignore);
		}
		stormWeight = Mathf.MoveTowards(stormWeight, storming && !Riding && Unoccupied() ? 1f : 0f, Time.deltaTime / stormFade);
	}

	// head down into it, a hand to her brow, pushed about by the gusts
	private void PoseStorm()
	{
		float w = stormWeight * (1f - actWeight);
		if (w <= 0f || right == null)
		{
			return;
		}
		float gust = (Mathf.PerlinNoise(Time.time * 0.7f, seed + 7f) - 0.5f) * 2f;
		float shove = (Mathf.PerlinNoise(Time.time * 1.9f, seed + 13f) - 0.5f) * 2f;
		torso.rotation = Quaternion.AngleAxis(w * gust * 5f, ownSpine) * Quaternion.AngleAxis(w * (gust * 4f + shove * 2f), ownFront) * Quaternion.AngleAxis(w * (11f + shove * 2f), ownRight) * torso.rotation;
		ReadFrame();
		head.rotation = Quaternion.AngleAxis(w * 7f, ownRight) * Quaternion.AngleAxis(w * -gust * 6f, ownSpine) * head.rotation;
		Vector3 up = Vector3.up;
		ReachTo(right, true, Brow(up) + ownRight * shove * 0.02f, ownRight - up * 0.2f, -ownRight, -up, w);
	}

	// out in front of her forehead, and not at arm's length
	private Vector3 Brow(Vector3 up)
	{
		return head.position + (ownFront * 0.6f + up * 0.46f + ownRight * 0.28f) * Reach(right);
	}

	// at a spot, in her own terms: lying down, her left and right and up aren't the world's
	private void Gaze(Vector3 where)
	{
		Vector3 to = where - head.position;
		Vector3 flat = Vector3.ProjectOnPlane(to, ownSpine);
		yawTo = Mathf.Clamp(Vector3.SignedAngle(ownFront, flat, ownSpine), -yawMost, yawMost);
		pitchTo = Mathf.Clamp(-Mathf.Atan2(Vector3.Dot(to, ownSpine), flat.magnitude) * Mathf.Rad2Deg, -pitchMost, pitchMost);
	}

	// where the camera's looking: the arm can't go behind her, so on foot she turns to it first
	private void FacePoint()
	{
		if (!Riding && girl == Girls.Local && girl.AgentVehicle != null && emoteFacing.sqrMagnitude > 0.5f)
		{
			Transform child = girl.AgentVehicle.Child;
			child.rotation = Quaternion.RotateTowards(child.rotation, Quaternion.LookRotation(emoteFacing), emoteTurnRate * Time.deltaTime);
		}
		Gaze(pointAt);
	}

	private void UpdateAct()
	{
		if (act == Act.None)
		{
			if (Time.time >= nextAct && weight >= 1f && stormWeight <= 0f)
			{
				PickAct();
			}
			return;
		}
		actWeight = Mathf.MoveTowards(actWeight, Time.time < actUntil ? 1f : 0f, Time.deltaTime / actFade);
		if (Time.time >= actUntil && actWeight <= 0f)
		{
			act = Act.None;
			look = Look.Ahead;
			yawTo = pitchTo = rollTo = 0f;
			nextLook = Time.time + Random.Range(aheadMin, aheadMax);
			nextAct = Time.time + (Riding ? Random.Range(rideGapMin, rideGapMax) : Random.Range(actGapMin, actGapMax));
			EndEmote();
		}
	}

	private void PickAct()
	{
		if (Riding)
		{
			PickRidingAct();
		}
		else
		{
			PickStandingAct();
		}
	}

	private void PickStandingAct()
	{
		float dice = Random.value;
		if (dice < 0.3f && Snowing())
		{
			Begin(Act.CatchSnow);
		}
		else if (dice < 0.55f)
		{
			Begin(Act.WarmHands);
		}
		else if (dice < 0.7f)
		{
			Begin(Act.Yawn);
		}
		else if (dice < 0.82f)
		{
			Begin(Act.Scratch);
		}
		else if (dice < 0.92f)
		{
			Begin(Act.Bob);
			yawTo = Random.Range(-20f, 20f);
		}
		else
		{
			Begin(Act.Tilt);
		}
	}

	private void PickRidingAct()
	{
		float dice = Random.value;
		float speed = ket != null || FindKet() ? ket.GetComponent<Rigidbody>().velocity.magnitude : 0f;
		if (speed > fastRide && dice < 0.2f)
		{
			Begin(Act.ArmsUp);
		}
		else if (dice < 0.35f && PointAtItem())
		{
			Begin(Act.Point);
		}
		else if (dice < 0.55f)
		{
			// the sky
			float side = Random.value < 0.5f ? -1f : 1f;
			Vector3 up = Quaternion.AngleAxis(side * Random.Range(10f, 60f), Vector3.up) * animator.transform.forward + Vector3.up * Random.Range(0.8f, 1.6f);
			PointTowards(head.position + up * 50f);
			Begin(Act.Point);
		}
		else if (dice < 0.7f)
		{
			// off to the side, at nothing you can see
			float side = Random.value < 0.5f ? -1f : 1f;
			Vector3 off = Quaternion.AngleAxis(side * Random.Range(40f, 100f), Vector3.up) * animator.transform.forward + Vector3.up * 0.1f;
			PointTowards(head.position + off * 50f);
			Begin(Act.Point);
		}
		else if (dice < 0.8f)
		{
			Begin(Act.Bob);
			yawTo = Random.Range(-20f, 20f);
		}
		else if (dice < 0.92f)
		{
			Begin(Act.ReachSky);
		}
		else
		{
			Begin(Act.Tilt);
			yawTo = Random.Range(-40f, 40f);
			pitchTo = -8f;
		}
	}

	private void Begin(Act what)
	{
		act = what;
		actStart = Time.time;
		actLength = LengthOf(what);
		actUntil = actStart + actLength;
		look = Look.Side;
		if (what == Act.Scratch)
		{
			pitchTo = 4f;
			rollTo = 8f;
		}
		else if (what == Act.Tilt)
		{
			tiltSide = Random.value < 0.5f ? -1f : 1f;
			rollTo = tiltSide * -14f;
		}
		else if (what == Act.CatchSnow)
		{
			flakeDue = true;
		}
	}

	private static float LengthOf(Act what)
	{
		switch (what)
		{
		case Act.Stretch:
			return 3.2f;
		case Act.WarmHands:
			return 3.5f;
		case Act.Yawn:
			return 3f;
		case Act.Scratch:
			return 2.4f;
		case Act.Wave:
			return 2.5f;
		case Act.Tilt:
			return 2f;
		case Act.Point:
			return 3f;
		case Act.ArmsUp:
			return 2.5f;
		case Act.Bob:
			return 4f;
		case Act.ReachSky:
			return 4.2f;
		case Act.CatchSnow:
			return flakeFall + flakeMelt + 0.4f;
		}
		return 3f;
	}

	private bool FindKet()
	{
		ket = FindObjectOfType<VehicleMovement>();
		return ket != null;
	}

	private Girl Partner()
	{
		foreach (Girl other in Girls.All)
		{
			if (other != girl && other != null)
			{
				return other;
			}
		}
		return null;
	}

	// snow falling, and on her: not in a tunnel, or underground where it's hidden
	private bool Snowing()
	{
		if (weather == null)
		{
			weather = FindObjectOfType<WeatherManager>();
		}
		if (weather == null || weather.SnowParticles == null || !weather.SnowParticles.gameObject.activeInHierarchy || !weather.SnowParticles.isPlaying)
		{
			return false;
		}
		return !Physics.Raycast(head.position + Vector3.up, Vector3.up, roofUpTo, groundMask, QueryTriggerInteraction.Ignore);
	}

	// where the hand's going to be when she catches one
	private Vector3 CatchPoint()
	{
		Vector3 flatFront = Vector3.Cross(ownRight, Vector3.up).normalized;
		return right[0].Transform.position + (flatFront * 0.75f - Vector3.up * 0.3f + ownRight * 0.2f) * Reach(right);
	}

	// one flake, from up where she can spot it down to her hand, and gone
	private void UpdateFlake()
	{
		if (flakeDue && right != null)
		{
			flakeDue = false;
			if (flake != null)
			{
				Destroy(flake.gameObject);
			}
			flake = MakeFlake();
			flakeElapsed = 0f;
			flakeStart = CatchPoint() + Vector3.up * flakeFrom + ownRight * 0.15f;
		}
		if (flake == null)
		{
			return;
		}
		flakeElapsed += Time.deltaTime;
		float t = flakeElapsed / flakeFall;
		Vector3 flatFront = Vector3.Cross(ownRight, Vector3.up).normalized;
		Vector3 palm = right[2].Transform.position + flatFront * 0.06f + Vector3.up * 0.03f;
		// falling to where the hand will be, the hand comes up under it on the way
		Vector3 end = Vector3.Lerp(CatchPoint(), palm, Ease(t, 0.6f, 1f));
		Vector3 drift = ownRight * Mathf.Sin(flakeElapsed * 3f) * 0.05f * (1f - Mathf.Clamp01(t));
		flake.position = Vector3.Lerp(flakeStart, end, Mathf.Clamp01(t)) + drift;
		flake.Rotate(40f * Time.deltaTime, 90f * Time.deltaTime, 0f, Space.Self);
		// melts once it's landed
		float melted = Mathf.Clamp01((flakeElapsed - flakeFall) / flakeMelt);
		flake.localScale = Vector3.one * flakeSize * (1f - melted);
		if (melted >= 1f || act != Act.CatchSnow)
		{
			Destroy(flake.gameObject);
			flake = null;
		}
	}

	private Transform MakeFlake()
	{
		GameObject made = GameObject.CreatePrimitive(PrimitiveType.Cube);
		Destroy(made.GetComponent<Collider>());
		made.name = "snowflake";
		made.layer = gameObject.layer;
		Renderer renderer = made.GetComponent<Renderer>();
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
		if (unlit != null)
		{
			renderer.material = new Material(unlit) { color = Color.white };
		}
		return made.transform;
	}

	private bool PointAtItem()
	{
		int found = Physics.OverlapSphereNonAlloc(transform.position, itemWithin, nearby, LayerMask.GetMask("Items"), QueryTriggerInteraction.Collide);
		for (int i = 0; i < found; i++)
		{
			Vector3 at = nearby[i].bounds.center;
			Vector3 flat = at - transform.position;
			flat.y = 0f;
			if (flat.magnitude > 3f && Mathf.Abs(Vector3.SignedAngle(animator.transform.forward, flat, Vector3.up)) < 100f)
			{
				PointTowards(at);
				return true;
			}
		}
		return false;
	}

	// picks the arm on that side and stays with it: a target straight ahead flipped from arm to arm every frame
	private void PointTowards(Vector3 where)
	{
		pointAt = where;
		pointRight = Vector3.Dot(where - head.position, ownRight) >= 0f;
		Gaze(where);
	}

	private void PoseArms(float time)
	{
		if (act == Act.None || actWeight <= 0f || left == null || right == null)
		{
			return;
		}
		float w = weight * actWeight;
		Vector3 u = Vector3.up;
		Vector3 r = ownRight;
		// where her chest faces, for what's done in front of her face, and flat in front of her, for what goes up in the air.
		// the same standing up, on the back seat one's the sky
		Vector3 f = ownFront;
		Vector3 flat = Vector3.Cross(r, u).normalized;
		float reach = Reach(right);
		Vector3 h = head.position;
		Vector3 sl = left[0].Transform.position;
		Vector3 sr = right[0].Transform.position;
		float t = Time.time;
		switch (act)
		{
		case Act.Stretch:
		{
			float arms = w * Ease(time, 0f, 0.7f) * (1f - Ease(time, actLength - 0.7f, actLength));
			ReachTo(left, false, h + (u * 1.15f - r * 0.4f + flat * 0.1f) * reach, -r - flat * 0.3f, u, flat, arms);
			ReachTo(right, true, h + (u * 1.15f + r * 0.4f + flat * 0.1f) * reach, r - flat * 0.3f, u, flat, arms);
			break;
		}
		case Act.WarmHands:
		{
			// side by side, palm on palm, sliding up and down along each other
			float rub = Mathf.Sin(t * 9f) * 0.05f;
			Vector3 fingers = (u + f * 0.35f).normalized;
			// tips leaning out a touch, straight up they crossed
			ReachTo(left, false, h + (f * 0.66f - u * (0.12f + rub) - r * 0.04f) * reach, -r - u * 0.6f, fingers - r * 0.2f, r, w);
			ReachTo(right, true, h + (f * 0.66f - u * (0.12f - rub) + r * 0.04f) * reach, r - u * 0.6f, fingers + r * 0.2f, -r, w);
			break;
		}
		case Act.Yawn:
			ReachTo(right, true, h + (f * 0.55f + u * 0.02f + r * 0.08f) * reach, r - u * 0.6f, u, -f, w * YawnOpen(time));
			break;
		case Act.ShadeEyes:
			ReachTo(right, true, Brow(u), r - u * 0.2f, -r, -u, w);
			break;
		case Act.Scratch:
			ReachTo(right, true, h + (r * 0.5f + u * 0.5f + f * Mathf.Sin(t * 12f) * 0.05f) * reach, r - u * 0.2f, u - r, -r, w);
			break;
		case Act.Wave:
		{
			// palm to the front, the forearm going side to side off a bent elbow
			float wag = Mathf.Sin(t * 9f);
			ReachTo(right, true, sr + (u * 0.52f + flat * 0.12f + r * (0.5f + wag * 0.16f)) * reach, r - u * 0.5f, u + r * wag * 0.4f, flat, w);
			break;
		}
		case Act.Point:
		{
			Bone[] arm = pointRight ? right : left;
			Vector3 from = pointRight ? sr : sl;
			// lying back her head's more in the way
			Vector3 dir = ((pointAt - from).normalized + (pointRight ? r : -r) * (Riding ? clearOfHead * 1.6f : clearOfHead)).normalized;
			ReachTo(arm, pointRight, from + dir * reach * 2f, -u, dir, Mathf.Abs(Vector3.Dot(dir, u)) > 0.8f ? flat : -u, w, pointMost);
			break;
		}
		case Act.ArmsUp:
		{
			// straight up from each shoulder, a little outward
			float sway = Mathf.Sin(time * 3f) * 0.08f;
			ReachTo(left, false, sl + (u * 0.9f - r * (0.25f - sway) + flat * 0.1f) * reach, -r, u, flat, w);
			ReachTo(right, true, sr + (u * 0.9f + r * (0.25f + sway) + flat * 0.1f) * reach, r, u, flat, w);
			break;
		}
		case Act.ReachSky:
		{
			// slowly after something up there, a quick snatch, then the hand back to her face to look what she got
			float back = Ease(time, 2.3f, 3f);
			float snatch = Ease(time, 1.8f, 2f) * (1f - Ease(time, 2f, 2.3f));
			Vector3 sky = sr + (u * (0.8f + snatch * 0.2f) + flat * 0.2f + r * (clearOfHead + Mathf.Sin(time * 1.6f) * 0.2f)) * reach;
			Vector3 near = h + (f * 0.62f + r * 0.25f) * reach;
			ReachTo(right, true, Vector3.Lerp(sky, near, back), r - flat, Vector3.Slerp(u, r + u, back), Vector3.Slerp(flat, -f, back), w * Ease(time, 0f, 1.1f));
			break;
		}
		case Act.CatchSnow:
			// palm up, only once the flake's close
			ReachTo(right, true, CatchPoint(), r - u * 0.5f, flat, u, w * Ease(time, flakeFall * 0.35f, flakeFall * 0.8f));
			break;
		}
	}

	private static float Reach(Bone[] arm)
	{
		return (arm[1].Transform.position - arm[0].Transform.position).magnitude + (arm[2].Transform.position - arm[1].Transform.position).magnitude;
	}

	// the elbow goes the bend's way, the hand to the target or as far as the arm goes, with its fingers along fingers and
	// its palm to palm. the upper arm and forearm roll so the elbow hinges in that plane, the wrist's half the twist to
	// the palm: a straight line between bones says nothing about roll, left alone it was any old way
	private static void ReachTo(Bone[] arm, bool isRight, Vector3 target, Vector3 bend, Vector3 fingers, Vector3 palm, float w, float most = armMost)
	{
		Vector3 start = arm[0].Transform.position;
		float upper = (arm[1].Transform.position - start).magnitude;
		float lower = (arm[2].Transform.position - arm[1].Transform.position).magnitude;
		Vector3 to = target - start;
		float far = Mathf.Clamp(to.magnitude, 0.01f, (upper + lower) * most);
		Vector3 dir = to.normalized;
		float along = (upper * upper - lower * lower + far * far) / (2f * far);
		float outward = Mathf.Sqrt(Mathf.Max(upper * upper - along * along, 0f));
		Vector3 perpendicular = Vector3.ProjectOnPlane(bend, dir).normalized;
		Vector3 elbow = start + dir * along + perpendicular * outward;
		Vector3 wrist = start + dir * far;
		// the hinge: which way the palm side of the arm faces when it bends like this
		Vector3 down = Vector3.Cross(perpendicular, dir) * (isRight ? 1f : -1f);
		Turn(arm[0], elbow - start, down, w);
		Vector3 fore = wrist - elbow;
		Vector3 forearmPalm = Vector3.Slerp(Vector3.ProjectOnPlane(down, fore).normalized, Vector3.ProjectOnPlane(palm, fore).normalized, 0.5f);
		Turn(arm[1], fore, forearmPalm, w);
		Turn(arm[2], fingers, palm, w);
	}

	private static void Turn(Bone bone, Vector3 along, Vector3 down, float w)
	{
		if (along.sqrMagnitude < 0.000001f)
		{
			return;
		}
		Vector3 flat = Vector3.ProjectOnPlane(down, along);
		if (flat.sqrMagnitude < 0.000001f)
		{
			flat = Vector3.ProjectOnPlane(Vector3.forward, along);
		}
		Quaternion wanted = Quaternion.LookRotation(along, flat) * Quaternion.Inverse(bone.Frame);
		bone.Transform.rotation = Quaternion.Slerp(bone.Transform.rotation, wanted, w);
	}

	private void PickLook()
	{
		if (look != Look.Ahead)
		{
			look = Look.Ahead;
			yawTo = pitchTo = rollTo = 0f;
			nextLook = Time.time + Random.Range(aheadMin, aheadMax);
			return;
		}
		nextLook = Time.time + Random.Range(lookMin, lookMax);
		float dice = Random.value;
		if (dice < 0.4f && Seen(Target(Look.Other)))
		{
			look = Look.Other;
		}
		else if (dice < 0.55f && Seen(Target(Look.Ket)))
		{
			look = Look.Ket;
		}
		else if (dice < 0.7f)
		{
			look = Look.Sky;
			yawTo = Random.Range(-sideMost * 0.5f, sideMost * 0.5f);
			pitchTo = -Random.Range(15f, pitchMost);
		}
		else if (dice < 0.8f)
		{
			look = Look.Feet;
			yawTo = Random.Range(-20f, 20f);
			pitchTo = Random.Range(12f, 22f);
		}
		else
		{
			look = Look.Side;
			yawTo = Random.value < 0.5f ? -Random.Range(25f, sideMost) : Random.Range(25f, sideMost);
			pitchTo = Random.Range(-8f, 6f);
		}
	}

	// someone she's looking at moves on
	private void Aim()
	{
		if ((look == Look.Other || look == Look.Ket) && !Seen(Target(look)))
		{
			nextLook = 0f;
		}
	}

	private Vector3? Target(Look what)
	{
		if (what == Look.Other)
		{
			Girl other = Partner();
			if (other != null && AngleUtils.FlatDistance(other.transform.position, transform.position) < otherWithin)
			{
				return other.transform.position + Vector3.up * 1.3f;
			}
			return null;
		}
		if (ket == null && !FindKet())
		{
			return null;
		}
		float away = AngleUtils.FlatDistance(ket.transform.position, transform.position);
		if (away > ketWithin || away < 2f)
		{
			return null;
		}
		return ket.transform.position + Vector3.up * 0.6f;
	}

	// within what she can turn to, and if so that's where she'll look
	private bool Seen(Vector3? target)
	{
		if (target == null)
		{
			return false;
		}
		Vector3 flat = Vector3.ProjectOnPlane(target.Value - head.position, ownSpine);
		if (Mathf.Abs(Vector3.SignedAngle(ownFront, flat, ownSpine)) > yawMost)
		{
			return false;
		}
		Gaze(target.Value);
		return true;
	}

}
