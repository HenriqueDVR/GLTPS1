using System.Collections;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.AI;

// host is chito, friend is yuuri. the other girl is a puppet replaying what they send
// runs early so puppet/ket are placed before the cameras follow
[DefaultExecutionOrder(-100)]
public class CoopPlayers : MonoBehaviour
{
#if DUMP_WORLDGEN
	private int debugMountFrames;

	private Vector3 debugMountLast;
#endif

	private struct Snapshot
	{
		// sender's clock so uneven delivery doesn't look jerky
		public float Time;

		public Vector3 Position;

		public float RootYaw;

		public float ModelYaw;

		public float[] Parameters;

		public int[] States;

		// per layer: time in state, blend length, blend progress
		public float[] StateTimes;

		public float[] BlendDurations;

		public float[] BlendProgress;

		public CoopKettenkrad.SeatState Seat;

		public bool HasVehicle;

		public Vector3 VehiclePosition;

		public Quaternion VehicleRotation;

		public float VehicleSpeed;

		public float VehicleSteer;

		public bool Headlight;

		// NaN when not in an elevator
		public float ElevatorHeight;

		public float AimPitch;

		public float AimYaw;
	}

	private const float sendInterval = 0.05f;

	// render a bit in the past so there's always 2 snapshots. grows with jitter
	private const float minDelay = 0.08f;

	private const float maxDelay = 0.4f;

	private float renderDelay = 0.12f;

	// bumped each time we run out of snapshots, fades back
	private float underrunExtra;

	private bool wasOver;

	// keep her moving this long when packets stop
	private const float maxExtrapolation = 0.25f;

	private const float maxExtrapolationSpeed = 12f;

	// blends out the jump when real data replaces a guess
	private const float correctionTime = 0.12f;

	private Vector3 correction;

	private Vector3 vehicleCorrection;

	private bool wasExtrapolating;

	private bool vehicleWasExtrapolating;

	private readonly List<float> lateness = new List<float>();

	private CharacterController localController;

	private SetPositionConstraintElevator localElevator;

	private PlayerGunMovement localGun;

	private PlayerGunMovement remoteGun;

	// clock offset = smallest (arrival - send) seen in this window
	private const float clockWindow = 3f;

	private const float snapDistance = 30f;

	private const string speedParameter = "velocityXZ";

	// short mismatches are normal, both sides take the same transitions at different times
	private const float stateMismatchGrace = 0.2f;

	private const float stateCrossFade = 0.15f;

	private float[] stateMismatch = new float[0];

	private readonly List<Snapshot> snapshots = new List<Snapshot>();

	private Vector3 localSafe;

	private bool hasLocalSafe;

	private Vector3 remoteSafe;

	private bool hasRemoteSafe;

	private float remoteSteadySince;

	private float lastRemoteY;

	private float lastRescue = -100f;

	private bool applyingRejoin;

	private readonly List<Vector2> clockSamples = new List<Vector2>();

	private float clockOffset;

	private AnimatorControllerParameter[] localParameters;

	private readonly Dictionary<int, AnimatorControllerParameterType> remoteParameterTypes = new Dictionary<int, AnimatorControllerParameterType>();

	private int[] parameterHashes = new int[0];

	private readonly NetDataWriter writer = new NetDataWriter();

	private CoopSession session;

	private FloatingOrigin floatingOrigin;

	private Animator localAnimator;

	private Animator remoteAnimator;

	private float nextSend;

	public static CoopPlayers Instance { get; private set; }

	public Transform Local { get; private set; }

	public Transform Remote { get; private set; }

	private void Awake()
	{
		Instance = this;
		session = CoopSession.Instance;
		session.PlayerStateReceived += OnPlayerState;
		session.PartnerLeft += OnPartnerLeft;
		floatingOrigin = Object.FindObjectOfType<FloatingOrigin>();
	}

	private void OnPartnerLeft()
	{
		snapshots.Clear();
		CoopKettenkrad.Instance?.OnPartnerLeft();
		CoopBarrel.OnPartnerLeft();
	}

	private void OnDestroy()
	{
		if (session != null)
		{
			session.PlayerStateReceived -= OnPlayerState;
			session.PartnerLeft -= OnPartnerLeft;
		}
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private IEnumerator Start()
	{
		yield return null;
		Transform transform = GameObject.Find("ChitoParent").transform;
		Transform transform2 = GameObject.Find("YuuriParent").transform;
		if (session.IsHost)
		{
			Local = transform;
			Remote = transform2;
		}
		else
		{
			transform.GetComponent<CharacterPossession>().PossessCharacter(isDead: false);
			Local = transform2;
			Remote = transform;
		}
		Local.GetComponent<CharacterPossession>().enabled = false;
		MakePuppet(Remote);
		AddWaterCatcher(Remote, Local);
		localAnimator = Local.GetComponentInChildren<Animator>();
		remoteAnimator = Remote.GetComponentInChildren<Animator>();
		localController = Local.GetComponent<CharacterController>();
		localElevator = Local.GetComponent<SetPositionConstraintElevator>();
		localGun = Local.GetComponentInChildren<PlayerGunMovement>(true);
		remoteGun = Remote.GetComponentInChildren<PlayerGunMovement>(true);
		localParameters = System.Array.FindAll(localAnimator.parameters, (AnimatorControllerParameter p) => p.type != AnimatorControllerParameterType.Trigger);
		AnimatorControllerParameter[] parameters = remoteAnimator.parameters;
		foreach (AnimatorControllerParameter animatorControllerParameter in parameters)
		{
			remoteParameterTypes[animatorControllerParameter.nameHash] = animatorControllerParameter.type;
		}
		if (floatingOrigin != null)
		{
			floatingOrigin.ReferenceObject = Local;
		}
	}

	private static void MakePuppet(Transform girl)
	{
		Behaviour[] array = new Behaviour[9]
		{
			girl.GetComponent<NavMeshAgentFollowPlayer>(),
			girl.GetComponent<NavMeshAgentVehicleInteraction>(),
			girl.GetComponent<NavMeshAgentCommand>(),
			girl.GetComponent<SetPositionConstraintElevator>(),
			girl.GetComponent<NavMeshAgent>(),
			girl.GetComponent<NavMeshObstacle>(),
			girl.GetComponent<PlayerMovement>(),
			girl.GetComponent<CharacterPossession>(),
			// idle branch would holster the rifle every frame
			girl.GetComponentInChildren<PlayerGunMovement>()
		};
		foreach (Behaviour behaviour in array)
		{
			if (behaviour != null)
			{
				behaviour.enabled = false;
			}
		}
		CharacterController component = girl.GetComponent<CharacterController>();
		if (component != null)
		{
			component.enabled = false;
		}
	}

	// pipe water went through her with the CharacterController off
	private static void AddWaterCatcher(Transform girl, Transform local)
	{
		CharacterController controller = girl.GetComponent<CharacterController>();
		GameObject catcher = new GameObject("CoopWaterCatcher");
		catcher.layer = LayerMask.NameToLayer("Player");
		catcher.transform.SetParent(girl, false);
		CapsuleCollider capsule = catcher.AddComponent<CapsuleCollider>();
		capsule.center = (controller != null) ? controller.center : new Vector3(0f, 0.8f, 0f);
		capsule.radius = (controller != null) ? controller.radius : 0.3f;
		capsule.height = (controller != null) ? controller.height : 1.6f;
		capsule.excludeLayers = ~0;
		CharacterController localController = local.GetComponent<CharacterController>();
		if (localController != null)
		{
			Physics.IgnoreCollision(capsule, localController);
		}
	}

	private void Update()
	{
		if (Local == null || !Local.gameObject.activeInHierarchy)
		{
			return;
		}
		CoopShots.Tick(Local);
		if (CoopGirlState.Pending)
		{
			if (!applyingRejoin)
			{
				applyingRejoin = true;
				StartCoroutine(ApplyRejoin());
			}
			return;
		}
		if (Time.unscaledTime >= nextSend && !(CoopKettenkrad.Instance != null && CoopKettenkrad.Instance.MountPending))
		{
			nextSend = Time.unscaledTime + sendInterval;
			SendState();
		}
#if DUMP_WORLDGEN
		DebugWalkAndLog();
		if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopdrive") >= 0 && Time.unscaledTime >= nextKetLog)
		{
			nextKetLog = Time.unscaledTime + 0.5f;
			Debug.Log("CoopKet " + System.DateTime.Now.ToString("HH:mm:ss.f") + " " + ToLayout(Object.FindObjectOfType<VehicleMovement>().transform.position).ToString("F2"));
		}
#endif
	}

#if DUMP_WORLDGEN
	private float nextKetLog;
#endif

	// after the ket's Update so seated girls stay on it
	private void LateUpdate()
	{
		if (Local != null && Local.gameObject.activeInHierarchy)
		{
			ApplyRemote();
			if (CoopKettenkrad.Instance != null)
			{
				CoopKettenkrad.Instance.PinLocalRider();
			}
			TrackSafeGround();
			if (session.IsHost && session.Connected && snapshots.Count > 0 && Remote != null)
			{
				CoopGirlState.HostKept = CoopGirlState.Capture(Remote, ToLayout(Remote.position));
			}
#if DUMP_WORLDGEN
			DebugSmoothness();
#endif
		}
	}

#if DUMP_WORLDGEN
	private Vector3 lastRemote;

	private Vector3 lastVehicle;

	private readonly List<float> remoteSpeeds = new List<float>();

	private readonly List<float> vehicleSpeeds = new List<float>();

	private float nextSmoothLog;

	// frames past the newest snapshot, and how far past
	private int debugOverFrames;

	private float debugMaxOver;

	// -coopsmooth
	private void DebugSmoothness()
	{
		if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopsmooth") < 0 || Remote == null)
		{
			return;
		}
		Vector3 vector = ToLayout(Remote.position);
		Vector3 vector2 = ToLayout(Object.FindObjectOfType<VehicleMovement>().transform.position);
		if (Time.deltaTime > 0f)
		{
			remoteSpeeds.Add(Vector3.Distance(vector, lastRemote) / Time.deltaTime);
			vehicleSpeeds.Add(Vector3.Distance(vector2, lastVehicle) / Time.deltaTime);
		}
		lastRemote = vector;
		lastVehicle = vector2;
		if (Time.unscaledTime >= nextSmoothLog)
		{
			nextSmoothLog = Time.unscaledTime + 2f;
			Debug.Log("CoopSmooth girl " + Summary(remoteSpeeds) + " ket " + Summary(vehicleSpeeds) + " fps " + (remoteSpeeds.Count / 2f).ToString("F0") + " delay " + renderDelay.ToString("F2") + " over " + (remoteSpeeds.Count > 0 ? (100f * debugOverFrames / remoteSpeeds.Count) : 0f).ToString("F0") + "% maxover " + debugMaxOver.ToString("F2") + " ping " + session.Ping + " t " + Time.timeSinceLevelLoad.ToString("F0"));
			debugOverFrames = 0;
			debugMaxOver = 0f;
			remoteSpeeds.Clear();
			vehicleSpeeds.Clear();
		}
	}

	private static string Summary(List<float> speeds)
	{
		if (speeds.Count < 2)
		{
			return "-";
		}
		speeds.Sort();
		float num = 0f;
		foreach (float speed in speeds)
		{
			num += speed;
		}
		num /= (float)speeds.Count;
		float num2 = 0f;
		foreach (float speed2 in speeds)
		{
			num2 += (speed2 - num) * (speed2 - num);
		}
		return "mean " + num.ToString("F2") + " sd " + Mathf.Sqrt(num2 / (float)speeds.Count).ToString("F2") + " p10 " + speeds[speeds.Count / 10].ToString("F2") + " p90 " + speeds[speeds.Count * 9 / 10].ToString("F2");
	}
#endif

	public static bool IsPuppet(Component component)
	{
		if (Instance != null && Instance.Remote != null)
		{
			return component.transform.IsChildOf(Instance.Remote);
		}
		return false;
	}

#if DUMP_WORLDGEN
	private float nextDebugLog;

	private bool debugPlaced;

	private bool debugPutOut;

	private int debugDriveStep;

	private bool debugItemsDumped;

	private void DebugDumpItems()
	{
		int num = 0;
		int num2 = 0;
		string[] array = new string[2] { "Food", "Ammo" };
		foreach (string text in array)
		{
			GameObject[] array2 = GameObject.FindGameObjectsWithTag(text);
			foreach (GameObject gameObject in array2)
			{
				num++;
				CoopItem componentInParent = gameObject.GetComponentInParent<CoopItem>();
				if (componentInParent != null)
				{
					num2++;
					continue;
				}
				Transform transform = gameObject.transform;
				string text2 = transform.name;
				while (transform.parent != null)
				{
					transform = transform.parent;
					text2 = transform.name + "/" + text2;
				}
				Debug.Log("CoopItems untracked " + text + " " + text2 + " at " + ToLayout(gameObject.transform.position).ToString("F1") + " " + Vector3.Distance(gameObject.transform.position, Local.position).ToString("F0") + "m from me, spawner " + (gameObject.GetComponentInParent<ItemSpawnManager>() != null));
			}
		}
		Debug.Log("CoopItems total " + num + " tracked " + num2);
	}

	private void DebugDumpInputs()
	{
		Transform[] array = new Transform[2] { Local, Remote };
		foreach (Transform transform in array)
		{
			MonoBehaviour[] componentsInChildren = transform.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
			foreach (MonoBehaviour monoBehaviour in componentsInChildren)
			{
				System.Reflection.FieldInfo field = monoBehaviour.GetType().GetField("playerInputScript", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				if (field != null)
				{
					PlayerInputInitialize playerInputInitialize = field.GetValue(monoBehaviour) as PlayerInputInitialize;
					Debug.Log("CoopInputs " + transform.name + " " + monoBehaviour.GetType().Name + " enabled " + monoBehaviour.enabled + " listens to " + ((playerInputInitialize != null) ? playerInputInitialize.gameObject.name : "none"));
				}
			}
		}
	}

	// -coopwalk: walk in a circle
	private void DebugWalkAndLog()
	{
		if (!debugItemsDumped && Time.timeSinceLevelLoad > 8f)
		{
			debugItemsDumped = true;
			DebugDumpItems();
			DebugDumpInputs();
		}
		if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopwalk") >= 0)
		{
			CharacterController component = Local.GetComponent<CharacterController>();
			// tight circle, wider one reaches the hole left of spawn
			Local.Rotate(0f, 45f * Time.deltaTime, 0f);
			component.Move((Local.forward * 4f + Vector3.down * 2f) * Time.deltaTime);
			localAnimator.SetFloat(speedParameter, 0.6f);
		}
		if (Time.unscaledTime >= nextDebugLog)
		{
			nextDebugLog = Time.unscaledTime + 2f;
			Debug.Log("CoopDebug local " + Local.name + " " + ToLayout(Local.position).ToString("F1") + " remote " + Remote.name + " " + ToLayout(Remote.position).ToString("F1") + " snapshots " + snapshots.Count);
			CharacterItemInteraction componentInChildren = Local.GetComponentInChildren<CharacterItemInteraction>();
			CharacterItemInteraction componentInChildren2 = Remote.GetComponentInChildren<CharacterItemInteraction>();
			Debug.Log("CoopDebug supplies " + componentInChildren.DebugSupplies() + " " + Local.name + " hunger " + componentInChildren.hungerLevel.ToString("F1") + " " + Remote.name + " hunger " + componentInChildren2.hungerLevel.ToString("F1") + " t " + Time.timeSinceLevelLoad.ToString("F0"));
			bool flag = session.Connected && Time.timeSinceLevelLoad < 40f;
			// -cooptake: both grab items every 2s
			if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cooptake") >= 0 && flag)
			{
				CoopItem.DebugTakeNearest(Local);
			}
			if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopsupplies") >= 0 && flag)
			{
				componentInChildren.DebugEatAndDrink();
			}
			// -coopdown
			if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopdown") >= 0 && session.Connected)
			{
				DebugDown();
			}
			// -coopmisc
			if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopmisc") >= 0 && session.Connected)
			{
				DebugMisc();
			}
			CoopPipes.DebugTick(session.IsHost, Remote);
			// -coopdrive
			if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopdrive") >= 0 && session.Connected)
			{
				DebugDrive();
			}
			// -coopbarrel
			if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopbarrel") >= 0 && session.Connected)
			{
				DebugBarrel();
			}
			// -coopcampfire
			if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopcampfire") >= 0 && session.Connected)
			{
				Debug.Log("CoopDebug " + componentInChildren.DebugCampfire());
				if (!debugPlaced && Time.timeSinceLevelLoad >= (session.IsHost ? 10f : 30f))
				{
					debugPlaced = true;
					componentInChildren.DebugPlaceCampfire();
				}
				if (!debugPutOut && Time.timeSinceLevelLoad >= (session.IsHost ? 40f : 20f))
				{
					debugPutOut = true;
					componentInChildren.DebugPutOutCampfire();
				}
			}
		}
	}
#endif

#if DUMP_WORLDGEN
	private void DebugDrive()
	{
		CoopKettenkrad instance = CoopKettenkrad.Instance;
		VehicleMovement vehicleMovement = Object.FindObjectOfType<VehicleMovement>();
		Debug.Log("CoopDebug ket local " + instance.LocalSeat.ToString() + " remote " + ((snapshots.Count > 0) ? snapshots[snapshots.Count - 1].Seat.ToString() : "?") + " follows " + CoopKettenkrad.FollowsRemote + " kinematic " + vehicleMovement.GetComponent<Rigidbody>().isKinematic + " at " + ToLayout(vehicleMovement.transform.position).ToString("F1") + " speed " + vehicleMovement.Anim.GetFloat("velocityZ").ToString("F2") + " localL1 " + localAnimator.GetCurrentAnimatorStateInfo(1).fullPathHash + " remoteL1 " + remoteAnimator.GetCurrentAnimatorStateInfo(1).fullPathHash + " remoteAt " + ToLayout(Remote.position).ToString("F1") + " t " + Time.timeSinceLevelLoad.ToString("F0"));
		float timeSinceLevelLoad = Time.timeSinceLevelLoad;
		if (session.IsHost)
		{
			if (debugDriveStep == 0 && timeSinceLevelLoad >= 8f)
			{
				debugDriveStep = 1;
				Local.GetComponent<CharacterVehicleInteraction>().DebugEnter();
			}
			else if (debugDriveStep == 1 && timeSinceLevelLoad >= 50f)
			{
				debugDriveStep = 2;
				Local.GetComponent<CharacterVehicleInteraction>().DebugExit();
			}
		}
		else if (debugDriveStep == 0 && timeSinceLevelLoad >= 20f)
		{
			debugDriveStep = 1;
			instance.DebugMount();
		}
		else if (debugDriveStep == 1 && timeSinceLevelLoad >= 40f)
		{
			debugDriveStep = 2;
			instance.DebugDismount();
		}
	}

	private int debugDownStep;

	private void DebugDown()
	{
		CoopDowned instance = CoopDowned.Instance;
		CharacterItemInteraction componentInChildren = Local.GetComponentInChildren<CharacterItemInteraction>();
		CharacterItemInteraction componentInChildren2 = Remote.GetComponentInChildren<CharacterItemInteraction>();
		Debug.Log("CoopDebug down local " + instance.LocalDown + " remote " + instance.DebugRemoteDown + " progress " + instance.DebugProgress.ToString("F1") + " " + componentInChildren.DebugSupplies() + " myHunger " + componentInChildren.hungerLevel.ToString("F1") + " theirHunger " + componentInChildren2.hungerLevel.ToString("F1") + " input " + Local.GetComponent<PlayerInputInitialize>().actions.Player.enabled + " L0 " + localAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash + " remoteL0 " + remoteAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash + " apart " + Vector3.Distance(Local.position, Remote.position).ToString("F1") + " t " + Time.timeSinceLevelLoad.ToString("F0"));
		float timeSinceLevelLoad = Time.timeSinceLevelLoad;
		if (!session.IsHost)
		{
			if ((debugDownStep == 0 && timeSinceLevelLoad >= 10f) || (debugDownStep == 1 && timeSinceLevelLoad >= 58f))
			{
				debugDownStep++;
				componentInChildren.hungerLevel = 0f;
			}
			return;
		}
		PlayerMapController component3 = Local.GetComponent<PlayerMapController>();
		if (debugDownStep == 0 && timeSinceLevelLoad >= 12f)
		{
			debugDownStep = -1;
			DebugTeleport(Remote.position + Remote.right * 10f);
			component3.enabled = true;
			component3.isMapEnabled = true;
			StartCoroutine((System.Collections.IEnumerator)typeof(PlayerMapController).GetMethod("RenderMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(component3, new object[1] { 0f }));
		}
		else if (debugDownStep == -1 && timeSinceLevelLoad >= 40f)
		{
			debugDownStep = 1;
			component3.isMapEnabled = false;
			DebugTeleport(Remote.position + Remote.right * 1.2f);
		}
		else if (debugDownStep == 1 && timeSinceLevelLoad >= 44f)
		{
			debugDownStep = 2;
			componentInChildren.AddFood(3);
		}
		else if (debugDownStep == 2 && timeSinceLevelLoad >= 46f)
		{
			debugDownStep = 3;
			CoopDowned.DebugHoldInteract = true;
		}
		else if (debugDownStep == 3 && timeSinceLevelLoad >= 54f)
		{
			debugDownStep = 4;
			CoopDowned.DebugHoldInteract = false;
			componentInChildren.hungerLevel = 0f;
		}
	}

	private void DebugTeleport(Vector3 position)
	{
		CharacterController component = Local.GetComponent<CharacterController>();
		component.enabled = false;
		Local.position = position;
		component.enabled = true;
	}

	private int debugMiscStep;

	private void DebugMisc()
	{
		CharacterVehicleInteraction component = Local.GetComponent<CharacterVehicleInteraction>();
		PlayerInputInitialize component2 = Local.GetComponent<PlayerInputInitialize>();
		Debug.Log("CoopDebug misc headlight " + component.Headlight.activeSelf + " timeScale " + Time.timeScale + " playerInput " + component2.actions.Player.enabled + " bulletHoles " + Object.FindObjectsOfType<BulletEffect>().Length + " t " + Time.timeSinceLevelLoad.ToString("F0"));
		float timeSinceLevelLoad = Time.timeSinceLevelLoad;
		if (session.IsHost)
		{
			if (debugMiscStep == 0 && timeSinceLevelLoad >= 20f)
			{
				debugMiscStep = 1;
				component.Headlight.SetActive(!component.Headlight.activeSelf);
			}
			else if (debugMiscStep == 1 && timeSinceLevelLoad >= 30f)
			{
				debugMiscStep = 2;
				component.Headlight.SetActive(!component.Headlight.activeSelf);
			}
			return;
		}
		UIManager uIManager = Object.FindObjectOfType<UIManager>();
		if (debugMiscStep == 0 && timeSinceLevelLoad >= 10f)
		{
			debugMiscStep = 1;
			uIManager.OpenMainMenu();
		}
		else if (debugMiscStep == 1 && timeSinceLevelLoad >= 14f)
		{
			debugMiscStep = 2;
			uIManager.CloseMainMenu();
		}
		else if (debugMiscStep >= 2 && debugMiscStep < 5 && timeSinceLevelLoad >= 18f)
		{
			debugMiscStep++;
			Local.GetComponentInChildren<PlayerGunController>().DebugFire();
		}
	}

	private int debugBarrelStep;

	private void DebugBarrel()
	{
		CharacterBarrelInteraction component = Local.GetComponent<CharacterBarrelInteraction>();
		Debug.Log("CoopDebug " + CharacterBarrelInteraction.DebugState() + " remoteL2 " + remoteAnimator.GetCurrentAnimatorStateInfo(2).fullPathHash + " localL2 " + localAnimator.GetCurrentAnimatorStateInfo(2).fullPathHash + " t " + Time.timeSinceLevelLoad.ToString("F0"));
		float timeSinceLevelLoad = Time.timeSinceLevelLoad;
		if (session.IsHost)
		{
			if (debugBarrelStep == 0 && timeSinceLevelLoad >= 10f)
			{
				debugBarrelStep = 1;
				component.DebugTakeFromVehicle();
			}
			else if (debugBarrelStep == 1 && timeSinceLevelLoad >= 16f)
			{
				debugBarrelStep = 2;
				component.DebugDrop(onVehicle: false);
			}
		}
		else if (debugBarrelStep == 0 && timeSinceLevelLoad >= 24f)
		{
			debugBarrelStep = 1;
			component.DebugPickUp();
		}
		else if (debugBarrelStep == 1 && timeSinceLevelLoad >= 32f)
		{
			debugBarrelStep = 2;
			component.DebugDrop(onVehicle: true);
		}
	}
#endif

	private void SendState()
	{
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.PlayerState);
		Vector3 vector = ToLayout(Local.position);
		writer.Put(vector.x);
		writer.Put(vector.y);
		writer.Put(vector.z);
		writer.Put(Local.eulerAngles.y);
		writer.Put(ModelYaw(Local, localAnimator.transform));
		writer.Put(Time.unscaledTime);
		// bools like carry matter, without them the puppet walks right back out
		writer.Put((byte)localParameters.Length);
		AnimatorControllerParameter[] array = localParameters;
		foreach (AnimatorControllerParameter animatorControllerParameter in array)
		{
			writer.Put(animatorControllerParameter.nameHash);
			switch (animatorControllerParameter.type)
			{
			case AnimatorControllerParameterType.Float:
				writer.Put(localAnimator.GetFloat(animatorControllerParameter.nameHash));
				break;
			case AnimatorControllerParameterType.Int:
				writer.Put((float)localAnimator.GetInteger(animatorControllerParameter.nameHash));
				break;
			default:
				writer.Put(localAnimator.GetBool(animatorControllerParameter.nameHash) ? 1f : 0f);
				break;
			}
		}
		writer.Put((byte)localAnimator.layerCount);
		for (int i = 0; i < localAnimator.layerCount; i++)
		{
			bool blending = localAnimator.IsInTransition(i);
			AnimatorStateInfo state = blending ? localAnimator.GetNextAnimatorStateInfo(i) : localAnimator.GetCurrentAnimatorStateInfo(i);
			writer.Put(state.fullPathHash);
			writer.Put(state.normalizedTime * state.length);
			AnimatorTransitionInfo transition = localAnimator.GetAnimatorTransitionInfo(i);
			float blendSeconds = !blending ? 0f : (transition.durationUnit == DurationUnit.Fixed ? transition.duration : transition.duration * localAnimator.GetCurrentAnimatorStateInfo(i).length);
			writer.Put(blendSeconds);
			writer.Put(blending ? transition.normalizedTime : 0f);
		}
		CoopKettenkrad coopKettenkrad = CoopKettenkrad.Instance;
		CoopKettenkrad.SeatState seatState = ((coopKettenkrad != null) ? coopKettenkrad.LocalSeat : CoopKettenkrad.SeatState.Off);
		writer.Put((byte)seatState);
		bool flag = (seatState == CoopKettenkrad.SeatState.Boarding || seatState == CoopKettenkrad.SeatState.Driver) && !CoopKettenkrad.FollowsRemote;
		writer.Put(flag);
		if (flag)
		{
			coopKettenkrad.WriteVehicle(writer, ToLayout(coopKettenkrad.VehiclePosition));
		}
		Transform elevator = localElevator.target;
		writer.Put(elevator != null ? Local.position.y - elevator.position.y : float.NaN);
		Vector3 aim = localGun != null ? localGun.AimCamera.eulerAngles : Vector3.zero;
		writer.Put(aim.x);
		writer.Put(aim.y);
		session.SendPlayerState(writer);
	}

	// relative to the root so it holds on a tilted seat
	private static float ModelYaw(Transform root, Transform model)
	{
		return (Quaternion.Inverse(root.rotation) * model.rotation).eulerAngles.y;
	}

	private void OnPlayerState(NetDataReader reader)
	{
		Snapshot item = new Snapshot
		{
			Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat()),
			RootYaw = reader.GetFloat(),
			ModelYaw = reader.GetFloat(),
			Time = reader.GetFloat()
		};
		UpdateClockOffset(item.Time);
#if DUMP_WORLDGEN
		// -coopoldclock
		if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopoldclock") >= 0)
		{
			item.Time = Time.unscaledTime;
			clockOffset = 0f;
		}
#endif
		int @byte = reader.GetByte();
		if (parameterHashes.Length != @byte)
		{
			parameterHashes = new int[@byte];
		}
		item.Parameters = new float[@byte];
		for (int j = 0; j < @byte; j++)
		{
			parameterHashes[j] = reader.GetInt();
			item.Parameters[j] = reader.GetFloat();
		}
		int layers = reader.GetByte();
		item.States = new int[layers];
		item.StateTimes = new float[layers];
		item.BlendDurations = new float[layers];
		item.BlendProgress = new float[layers];
		for (int i = 0; i < layers; i++)
		{
			item.States[i] = reader.GetInt();
			item.StateTimes[i] = reader.GetFloat();
			item.BlendDurations[i] = reader.GetFloat();
			item.BlendProgress[i] = reader.GetFloat();
		}
		item.Seat = (CoopKettenkrad.SeatState)reader.GetByte();
		item.HasVehicle = reader.GetBool();
		if (item.HasVehicle)
		{
			item.VehiclePosition = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
			item.VehicleRotation = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
			item.VehicleSpeed = reader.GetFloat();
			item.VehicleSteer = reader.GetFloat();
			item.Headlight = reader.GetBool();
		}
		item.ElevatorHeight = reader.GetFloat();
		item.AimPitch = reader.GetFloat();
		item.AimYaw = reader.GetFloat();
		if (snapshots.Count > 0 && item.Time <= snapshots[snapshots.Count - 1].Time)
		{
			// late one, newer already here
			if (snapshots[snapshots.Count - 1].Time - item.Time < 1f)
			{
				return;
			}
			// their clock went back, they restarted
			snapshots.Clear();
		}
		snapshots.Add(item);
		if (snapshots.Count > 32)
		{
			snapshots.RemoveAt(0);
		}
	}

	private void ApplyRemote()
	{
		if (Remote == null || snapshots.Count == 0)
		{
			return;
		}
		UpdateRenderDelay();
		float num = Time.unscaledTime - clockOffset - renderDelay;
		while (snapshots.Count > 2 && snapshots[1].Time <= num)
		{
			snapshots.RemoveAt(0);
		}
		Snapshot snapshot = snapshots[0];
		Snapshot snapshot2 = ((snapshots.Count > 1) ? snapshots[1] : snapshot);
		float t = ((snapshot2.Time > snapshot.Time) ? Mathf.Clamp01((num - snapshot.Time) / (snapshot2.Time - snapshot.Time)) : 1f);
		// climbing onto the back teleports her, take the new spot at once
		if (snapshot2.Seat == CoopKettenkrad.SeatState.Mounting && snapshot.Seat != CoopKettenkrad.SeatState.Mounting)
		{
			t = 1f;
		}
		// past the newest one: packets are late
		float over = (snapshots.Count > 1) ? (num - snapshot2.Time) : 0f;
		if (over > 0f && !wasOver)
		{
			underrunExtra = Mathf.Min(underrunExtra + 0.02f, 0.15f);
		}
		wasOver = over > 0f;
#if DUMP_WORLDGEN
		if (over > 0f)
		{
			debugOverFrames++;
			debugMaxOver = Mathf.Max(debugMaxOver, over);
		}
#endif
		Vector3 layout = Vector3.Lerp(snapshot.Position, snapshot2.Position, t);
		bool extrapolating = over > 0f && snapshot2.Seat == CoopKettenkrad.SeatState.Off && float.IsNaN(snapshot2.ElevatorHeight);
		if (extrapolating)
		{
			layout += Extrapolate(snapshot.Position, snapshot2.Position, snapshot2.Time - snapshot.Time, over);
		}
		Vector3 vector = FromLayout(layout);
		if (Vector3.Distance(Remote.position, vector) > snapDistance)
		{
			// teleport, snap
			snapshots.RemoveRange(0, snapshots.Count - 1);
			vector = FromLayout(snapshots[0].Position);
			correction = Vector3.zero;
		}
		else
		{
			vector = Smooth(vector, Remote.position, extrapolating, ref wasExtrapolating, ref correction);
		}
		PinRemoteToElevator(ref vector, snapshot2.ElevatorHeight);
		Remote.position = vector;
		Remote.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(snapshot.RootYaw, snapshot2.RootYaw, t), 0f);
		ApplyRemoteVehicle(snapshot, snapshot2, t, over);
		remoteAnimator.transform.rotation = Remote.rotation * Quaternion.Euler(0f, Mathf.LerpAngle(snapshot.ModelYaw, snapshot2.ModelYaw, t), 0f);
		SyncParameters(snapshot, snapshot2, t);
		SyncStates(snapshot2);
		SyncGun(snapshot, snapshot2, t);
#if DUMP_WORLDGEN
		// -coopdrive
		Transform debugRoot = remoteAnimator.transform.Find(Remote.name.Replace("Parent", "") + "/Root");
		if (debugRoot != null)
		{
			debugMountFrames = snapshot2.Seat == CoopKettenkrad.SeatState.Mounting ? 6 : debugMountFrames - 1;
			if (debugMountFrames > 0)
			{
				Vector3 step = debugRoot.position - debugMountLast;
				step.y = 0f;
				Debug.Log("CoopMount remote body step " + step.magnitude.ToString("F3") + " seat " + snapshot2.Seat + " state " + remoteAnimator.GetCurrentAnimatorStateInfo(1).shortNameHash);
			}
			debugMountLast = debugRoot.position;
		}
#endif
	}

	private static Vector3 Extrapolate(Vector3 from, Vector3 to, float span, float over)
	{
		if (span <= 0f)
		{
			return Vector3.zero;
		}
		Vector3 velocity = Vector3.ClampMagnitude((to - from) / span, maxExtrapolationSpeed);
		return velocity * Mathf.Min(over, maxExtrapolation);
	}

	// when the guess ends, start from where she's shown and ease onto the real path
	private static Vector3 Smooth(Vector3 target, Vector3 shown, bool extrapolating, ref bool wasExtrapolating, ref Vector3 offset)
	{
		if (wasExtrapolating && !extrapolating)
		{
			offset = shown - target;
			if (offset.magnitude > 3f)
			{
				offset = Vector3.zero;
			}
		}
		wasExtrapolating = extrapolating;
		offset *= Mathf.Exp(-Time.deltaTime / correctionTime);
		return target + offset;
	}

	// enough behind to cover the worst recent lateness
	private void UpdateRenderDelay()
	{
		lateness.Clear();
		foreach (Vector2 clockSample in clockSamples)
		{
			lateness.Add(clockSample.y - clockOffset);
		}
		float worst = 0f;
		if (lateness.Count > 0)
		{
			lateness.Sort();
			worst = lateness[Mathf.Min(lateness.Count - 1, lateness.Count * 95 / 100)];
		}
		underrunExtra = Mathf.Max(0f, underrunExtra - 0.01f * Time.unscaledDeltaTime);
		float target = Mathf.Clamp(sendInterval * 1.5f + worst + 0.02f + underrunExtra, minDelay, maxDelay);
		// slow drift so she doesn't visibly speed up or slow down much
		renderDelay = Mathf.MoveTowards(renderDelay, target, ((target > renderDelay) ? 0.25f : 0.05f) * Time.unscaledDeltaTime);
	}

	// use our elevator floor, theirs is a bit ahead/behind and she'd sink or float
	private void PinRemoteToElevator(ref Vector3 position, float heightAboveFloor)
	{
		if (float.IsNaN(heightAboveFloor))
		{
			return;
		}
		Transform elevator = ElevatorUnder(position);
		if (elevator != null)
		{
			position.y = elevator.position.y + Mathf.Max(heightAboveFloor, Remote.GetComponent<SetPositionConstraintElevator>().offsetY);
		}
	}

	// x/z only, height is what's off
	private static Transform ElevatorUnder(Vector3 position)
	{
		foreach (GameObject gameObject in GameObject.FindGameObjectsWithTag("Elevator"))
		{
			foreach (Collider collider in gameObject.GetComponents<Collider>())
			{
				Bounds bounds = collider.bounds;
				if (collider.isTrigger && position.x >= bounds.min.x && position.x <= bounds.max.x && position.z >= bounds.min.z && position.z <= bounds.max.z)
				{
					return gameObject.transform;
				}
			}
		}
		return null;
	}

	private void SyncGun(Snapshot from, Snapshot to, float t)
	{
		PlayerGunMovement component = remoteGun;
		if (component != null)
		{
			bool aiming = remoteAnimator.GetBool("ADS");
			component.ShowAiming(aiming);
			if (aiming)
			{
				component.SpineBone.rotation = Quaternion.Euler(Mathf.LerpAngle(from.AimPitch, to.AimPitch, t), Mathf.LerpAngle(from.AimYaw, to.AimYaw, t), 0f);
			}
		}
	}

	private void UpdateClockOffset(float remoteTime)
	{
		float unscaledTime = Time.unscaledTime;
		clockSamples.Add(new Vector2(unscaledTime, unscaledTime - remoteTime));
		while (clockSamples.Count > 1 && clockSamples[0].x < unscaledTime - clockWindow)
		{
			clockSamples.RemoveAt(0);
		}
		clockOffset = float.MaxValue;
		foreach (Vector2 clockSample in clockSamples)
		{
			clockOffset = Mathf.Min(clockOffset, clockSample.y);
		}
	}

	private void SyncParameters(Snapshot from, Snapshot to, float t)
	{
		if (to.Parameters == null)
		{
			return;
		}
		bool flag = from.Parameters != null && from.Parameters.Length == to.Parameters.Length;
		for (int i = 0; i < to.Parameters.Length && i < parameterHashes.Length; i++)
		{
			int num = parameterHashes[i];
			if (remoteParameterTypes.TryGetValue(num, out var value))
			{
				switch (value)
				{
				case AnimatorControllerParameterType.Float:
					remoteAnimator.SetFloat(num, flag ? Mathf.Lerp(from.Parameters[i], to.Parameters[i], t) : to.Parameters[i]);
					break;
				case AnimatorControllerParameterType.Int:
					remoteAnimator.SetInteger(num, Mathf.RoundToInt(to.Parameters[i]));
					break;
				case AnimatorControllerParameterType.Bool:
					remoteAnimator.SetBool(num, to.Parameters[i] > 0.5f);
					break;
				}
			}
		}
	}

	private void ApplyRemoteVehicle(Snapshot from, Snapshot to, float t, float over)
	{
		CoopKettenkrad instance = CoopKettenkrad.Instance;
		if (instance == null)
		{
			return;
		}
		instance.SetRemoteDriving(to.Seat == CoopKettenkrad.SeatState.Boarding || to.Seat == CoopKettenkrad.SeatState.Driver);
		instance.SetRemoteSeat(to.Seat);
		if (to.HasVehicle)
		{
			if (!from.HasVehicle)
			{
				from = to;
			}
			Vector3 layout = Vector3.Lerp(from.VehiclePosition, to.VehiclePosition, t);
			bool extrapolating = over > 0f && from.Time < to.Time;
			if (extrapolating)
			{
				layout += Extrapolate(from.VehiclePosition, to.VehiclePosition, to.Time - from.Time, over);
			}
			Vector3 position = Smooth(FromLayout(layout), instance.VehiclePosition, extrapolating, ref vehicleWasExtrapolating, ref vehicleCorrection);
			instance.ApplyRemoteVehicle(position, Quaternion.Slerp(from.VehicleRotation, to.VehicleRotation, t), Mathf.Lerp(from.VehicleSpeed, to.VehicleSpeed, t), Mathf.Lerp(from.VehicleSteer, to.VehicleSteer, t), to.Headlight);
		}
		else
		{
			vehicleCorrection = Vector3.zero;
			vehicleWasExtrapolating = false;
		}
		if (to.Seat == CoopKettenkrad.SeatState.Driver || to.Seat == CoopKettenkrad.SeatState.Back)
		{
			CoopKettenkrad.Pin(Remote, to.Seat);
		}
	}

	private void SyncStates(Snapshot to)
	{
		int[] states = to.States;
		if (states == null)
		{
			return;
		}
		int num = Mathf.Min(states.Length, remoteAnimator.layerCount);
		if (stateMismatch.Length != num)
		{
			stateMismatch = new float[num];
		}
		for (int i = 0; i < num; i++)
		{
			int num2 = states[i];
			if (num2 == remoteAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash || (remoteAnimator.IsInTransition(i) && num2 == remoteAnimator.GetNextAnimatorStateInfo(i).fullPathHash))
			{
				stateMismatch[i] = 0f;
				continue;
			}
			stateMismatch[i] += Time.deltaTime;
			if (to.BlendDurations[i] > 0f)
			{
				remoteAnimator.CrossFadeInFixedTime(num2, to.BlendDurations[i], i, to.StateTimes[i], to.BlendProgress[i]);
				stateMismatch[i] = 0f;
			}
			// Animator.Play with no blend, follow right away
			else if (to.StateTimes[i] < stateMismatchGrace && stateMismatch[i] <= Time.deltaTime)
			{
				remoteAnimator.PlayInFixedTime(num2, i, to.StateTimes[i]);
				remoteAnimator.Update(0f);
				stateMismatch[i] = 0f;
			}
			else if (stateMismatch[i] > stateMismatchGrace)
			{
				remoteAnimator.CrossFadeInFixedTime(num2, stateCrossFade, i, to.StateTimes[i]);
				stateMismatch[i] = 0f;
			}
		}
	}

	private IEnumerator ApplyRejoin()
	{
		yield return CoopGirlState.ApplyPending(this);
		applyingRejoin = false;
	}

	private void TrackSafeGround()
	{
		CharacterController controller = localController;
		if (controller.enabled && controller.isGrounded)
		{
			localSafe = ToLayout(Local.position);
			hasLocalSafe = true;
		}
		if (Remote != null && snapshots.Count > 0)
		{
			if (Mathf.Abs(Remote.position.y - lastRemoteY) > 0.05f)
			{
				remoteSteadySince = Time.unscaledTime;
			}
			else if (Time.unscaledTime - remoteSteadySince > 0.5f)
			{
				remoteSafe = ToLayout(Remote.position);
				hasRemoteSafe = true;
			}
			lastRemoteY = Remote.position.y;
		}
	}

	// respawn by the partner, or where she last stood if they fell too
	public void RescueLocalPlayer()
	{
		Vector3 target;
		if (hasRemoteSafe && Time.unscaledTime - lastRescue > 3f)
		{
			target = FromLayout(remoteSafe) - Remote.forward * 2f;
		}
		else if (hasLocalSafe)
		{
			target = FromLayout(localSafe);
		}
		else
		{
			target = Remote.position - Remote.forward * 2f;
		}
		lastRescue = Time.unscaledTime;
		CharacterController component = Local.GetComponent<CharacterController>();
		component.enabled = false;
		Local.position = target + Vector3.up * 0.5f;
		component.enabled = true;
	}

	// layout space so floating origin doesn't matter
	public Vector3 ToLayout(Vector3 world)
	{
		if (!(floatingOrigin != null))
		{
			return world;
		}
		return world + floatingOrigin.TotalOffset;
	}

	public Vector3 FromLayout(Vector3 layout)
	{
		if (!(floatingOrigin != null))
		{
			return layout;
		}
		return layout - floatingOrigin.TotalOffset;
	}
}
