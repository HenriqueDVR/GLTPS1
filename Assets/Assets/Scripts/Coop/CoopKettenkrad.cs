using LiteNetLib.Utils;
using UnityEngine;

// driver simulates it and sends the pose, other side is kinematic and plays it back.
// other player can ride on the back like the AI companion did
public class CoopKettenkrad : MonoBehaviour
{
	public enum SeatState : byte
	{
		Off,
		Boarding,
		Driver,
		Back,
		Mounting
	}

	private const float mountDuration = 0.25f;

	private const float backReach = 2.5f;

	private const string velocityParameter = "velocityZ";

	private const string steerParameter = "steer";

	private VehicleMovement vehicle;

	private Rigidbody body;

	private Animator vehicleAnimator;

	private bool remoteDriving;

	private RigidbodyInterpolation interpolation;

	private bool riding;

	private float mountElapsed;

	private Vector3 mountStart;

	// climb anim is authored from the seat. snap her on the first frame then glide over this long
	private const float mountGlideDuration = 0.6f;

	private Transform mountRootBone;

	private Vector3 mountBodyBefore;

	private bool mountMeasuring;

	// don't send state between starting the climb and the snap in LateUpdate
	public bool MountPending => mountMeasuring;

#if DUMP_WORLDGEN
	private int mountLogFrames;
#endif

	// seated until the climb off ends, then slide to where she stands
	private bool leaving;

	private float leaveElapsed;

	private bool landing;

	private float landElapsed;

	private Vector3 landStart;

	private Vector3 landTarget;

	private float landDuration;

	// longer than the anim in case it's cut short
	private const float maxLeaveDuration = 3f;

	public static CoopKettenkrad Instance { get; private set; }

	public static bool FollowsRemote
	{
		get
		{
			if (Instance != null)
			{
				return Instance.remoteDriving;
			}
			return false;
		}
	}

	public SeatState LocalSeat
	{
		get
		{
			if (riding)
			{
				return (mountElapsed < mountGlideDuration) ? SeatState.Mounting : SeatState.Back;
			}
			CharacterVehicleInteraction characterVehicleInteraction = LocalVehicleInteraction();
			if (characterVehicleInteraction == null || !characterVehicleInteraction.IsDriving)
			{
				return SeatState.Off;
			}
			if (!characterVehicleInteraction.constraint)
			{
				return SeatState.Boarding;
			}
			return SeatState.Driver;
		}
	}

	private void Awake()
	{
		Instance = this;
	}

	private void Start()
	{
		vehicle = Object.FindObjectOfType<VehicleMovement>();
		body = vehicle.GetComponent<Rigidbody>();
		vehicleAnimator = vehicle.Anim;
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void WriteVehicle(NetDataWriter writer, Vector3 layoutPosition)
	{
		Quaternion rotation = vehicle.transform.rotation;
		writer.Put(layoutPosition.x);
		writer.Put(layoutPosition.y);
		writer.Put(layoutPosition.z);
		writer.Put(rotation.x);
		writer.Put(rotation.y);
		writer.Put(rotation.z);
		writer.Put(rotation.w);
		writer.Put(vehicleAnimator.GetFloat(velocityParameter));
		writer.Put(vehicleAnimator.GetFloat(steerParameter));
		GameObject headlight = Headlight();
		writer.Put(headlight != null && headlight.activeSelf);
	}

	private static GameObject Headlight()
	{
		CharacterVehicleInteraction characterVehicleInteraction = LocalVehicleInteraction();
		if (!(characterVehicleInteraction != null))
		{
			return null;
		}
		return characterVehicleInteraction.Headlight;
	}

	public bool Ready => vehicle != null;

	public Vector3 VehiclePosition => vehicle.transform.position;

	public Quaternion VehicleRotation => vehicle.transform.rotation;

	public void Place(Vector3 position, Quaternion rotation)
	{
		if (remoteDriving)
		{
			return;
		}
		body.position = position;
		body.rotation = rotation;
		vehicle.transform.SetPositionAndRotation(position, rotation);
		body.velocity = Vector3.zero;
		body.angularVelocity = Vector3.zero;
	}

	public void OnPartnerLeft()
	{
		SetRemoteDriving(false);
		remoteSeat = SeatState.Off;
	}

	private SeatState remoteSeat;

	public void SetRemoteSeat(SeatState seat)
	{
		remoteSeat = seat;
	}

	public void SetRemoteDriving(bool driving)
	{
		if (driving == remoteDriving || (driving && LocalSeat == SeatState.Driver && CoopSession.Instance.IsHost))
		{
			return;
		}
		remoteDriving = driving;
		body.isKinematic = driving;
		if (driving)
		{
			// interpolation drags it back between our updates
			interpolation = body.interpolation;
			body.interpolation = RigidbodyInterpolation.None;
			vehicle.EngineAudio.Play();
		}
		else
		{
			body.interpolation = interpolation;
			body.constraints = RigidbodyConstraints.FreezeAll;
			vehicle.EngineAudio.Stop();
			vehicleAnimator.SetFloat(velocityParameter, 0f);
		}
	}

	public void ApplyRemoteVehicle(Vector3 position, Quaternion rotation, float velocity, float steer, bool headlightOn)
	{
		if (!remoteDriving)
		{
			return;
		}
		GameObject headlight = Headlight();
		if (headlight != null && headlight.activeSelf != headlightOn)
		{
			headlight.SetActive(headlightOn);
		}
		vehicle.transform.SetPositionAndRotation(position, rotation);
		body.position = position;
		body.rotation = rotation;
		vehicleAnimator.SetFloat(velocityParameter, velocity);
		vehicleAnimator.SetFloat(steerParameter, steer);
	}

	public static void Pin(Transform girl, SeatState seat)
	{
		Transform transform = SeatOf(girl, seat);
		if (transform != null)
		{
			girl.SetPositionAndRotation(transform.position, transform.rotation);
		}
	}

	private static Transform SeatOf(Transform girl, SeatState seat)
	{
		switch (seat)
		{
		case SeatState.Driver:
			return girl.GetComponent<CharacterVehicleInteraction>().Seat;
		case SeatState.Back:
			return girl.GetComponent<NavMeshAgentVehicleInteraction>().ConstraintPosition;
		default:
			return null;
		}
	}

	public bool WantsInteract(bool nearKettenkrad, bool isCarrying)
	{
		if (riding || landing)
		{
			return true;
		}
		return !isCarrying && (NearBack() || (remoteDriving && nearKettenkrad));
	}

	private static bool NearBack()
	{
		return Vector3.Distance(CoopPlayers.Instance.Local.position, CoopPlayers.Instance.Local.GetComponent<NavMeshAgentVehicleInteraction>().EnterPosition.position) < backReach;
	}

	public bool HandleInteract(bool nearKettenkrad)
	{
		if (leaving || landing)
		{
			return true;
		}
		if (riding)
		{
			StartLeaving();
			return true;
		}
		// back seat always free, side doors are for driving
		if (remoteDriving ? (nearKettenkrad || NearBack()) : (NearBack() && !nearKettenkrad))
		{
			if (remoteSeat == SeatState.Back || remoteSeat == SeatState.Mounting)
			{
				CoopNotice.Show(CoopPlayers.Instance.Remote.name.Replace("Parent", "") + " is already on the back.");
				return true;
			}
			Mount();
			return true;
		}
		return false;
	}

	public void GetOff()
	{
		if (riding || landing)
		{
			leaving = false;
			landing = false;
			Dismount();
		}
	}

	private void Mount()
	{
		Transform local = CoopPlayers.Instance.Local;
		local.GetComponent<CharacterController>().enabled = false;
		local.GetComponent<PlayerMovement>().enabled = false;
		// keep controls on so she can hop off / eat / drink
		local.GetComponent<PlayerInputInitialize>().actions.Player.Enable();
		// "ket back" trigger blends for a whole second and she'd stand inside it
		Animator animator = local.GetComponentInChildren<Animator>();
		string girl = local.name.Replace("Parent", "");
		mountRootBone = animator.transform.Find(girl + "/Root");
		mountBodyBefore = (mountRootBone != null) ? mountRootBone.position : local.position;
		NavMeshAgentVehicleInteraction seat = local.GetComponent<NavMeshAgentVehicleInteraction>();
		local.rotation = seat.ConstraintPosition.rotation;
		seat.Child.rotation = seat.ConstraintPosition.rotation;
		animator.Play(girl + "_Enter_Ket_Back", 1, 0f);
		mountStart = local.position;
		mountElapsed = 0f;
		mountMeasuring = true;
		riding = true;
	}

	private void StartLeaving()
	{
		leaving = true;
		leaveElapsed = 0f;
		CoopPlayers.Instance.Local.GetComponentInChildren<Animator>().SetTrigger("ket exit");
	}

	// slide over exactly the blend time so they cancel out
	private void UpdateLeaving(Transform local)
	{
		leaveElapsed += Time.deltaTime;
		Animator animator = local.GetComponentInChildren<Animator>();
		string girl = local.name.Replace("Parent", "");
		bool blendingOut = animator.IsInTransition(1) && animator.GetCurrentAnimatorStateInfo(1).IsName(girl + "_Exit_Ket_Back");
		bool seated = animator.GetCurrentAnimatorStateInfo(1).IsTag("Ket") || animator.IsInTransition(1);
		if (!blendingOut && (seated || leaveElapsed < 0.2f) && leaveElapsed < maxLeaveDuration)
		{
			return;
		}
		Transform enterPosition = local.GetComponent<NavMeshAgentVehicleInteraction>().EnterPosition;
		landTarget = enterPosition.position;
		landDuration = mountDuration;
		if (blendingOut)
		{
			AnimatorTransitionInfo transition = animator.GetAnimatorTransitionInfo(1);
			float blendSeconds = transition.durationUnit == DurationUnit.Fixed ? transition.duration : transition.duration * animator.GetCurrentAnimatorStateInfo(1).length;
			landDuration = Mathf.Max(0.05f, blendSeconds * (1f - transition.normalizedTime));
			Transform root = animator.transform.Find(girl + "/Root");
			if (root != null)
			{
				landTarget = new Vector3(root.position.x, enterPosition.position.y, root.position.z);
			}
		}
		leaving = false;
		riding = false;
		landing = true;
		landElapsed = 0f;
		landStart = local.position;
	}

	private void UpdateLanding(Transform local)
	{
		landElapsed += Time.deltaTime;
		local.position = Vector3.Lerp(landStart, landTarget, landElapsed / landDuration);
		if (landElapsed >= landDuration)
		{
			landing = false;
			local.GetComponent<CharacterController>().enabled = true;
			local.GetComponent<PlayerMovement>().enabled = true;
		}
	}

	private void Dismount()
	{
		Transform local = CoopPlayers.Instance.Local;
		riding = false;
		local.GetComponentInChildren<Animator>().SetTrigger("ket exit");
		Transform enterPosition = local.GetComponent<NavMeshAgentVehicleInteraction>().EnterPosition;
		local.SetPositionAndRotation(enterPosition.position, enterPosition.rotation);
		local.GetComponent<CharacterController>().enabled = true;
		local.GetComponent<PlayerMovement>().enabled = true;
	}

	// called right after CoopPlayers moves it so she's on this frame's position
	public void PinLocalRider()
	{
		Transform local = CoopPlayers.Instance.Local;
		if (landing)
		{
			UpdateLanding(local);
			return;
		}
		if (!riding)
		{
			return;
		}
		if (leaving)
		{
			UpdateLeaving(local);
			if (landing)
			{
				UpdateLanding(local);
				return;
			}
		}
		NavMeshAgentVehicleInteraction component = local.GetComponent<NavMeshAgentVehicleInteraction>();
		Transform constraintPosition = component.ConstraintPosition;
		if (mountMeasuring)
		{
			mountMeasuring = false;
#if DUMP_WORLDGEN
			mountLogFrames = 8;
#endif
			if (mountRootBone != null)
			{
				// sideways only
				Vector3 shift = mountBodyBefore - mountRootBone.position;
				shift.y = 0f;
				mountStart = local.position + shift;
			}
		}
#if DUMP_WORLDGEN
		if (mountLogFrames > 0 && mountRootBone != null)
		{
			mountLogFrames--;
			Vector3 off = mountRootBone.position - mountBodyBefore;
			off.y = 0f;
			Debug.Log("CoopMount local frame " + (8 - mountLogFrames) + " body moved " + off.magnitude.ToString("F3") + " (shift applied " + (mountStart - local.position).magnitude.ToString("F3") + ")");
		}
#endif
		if (mountElapsed < mountGlideDuration)
		{
			mountElapsed += Time.deltaTime;
			local.SetPositionAndRotation(Vector3.Lerp(mountStart, constraintPosition.position, Mathf.Clamp01(mountElapsed / mountGlideDuration)), constraintPosition.rotation);
		}
		else
		{
			local.SetPositionAndRotation(constraintPosition.position, constraintPosition.rotation);
		}
		component.Child.rotation = constraintPosition.rotation;
	}

	private static CharacterVehicleInteraction LocalVehicleInteraction()
	{
		if (CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
		{
			return null;
		}
		return CoopPlayers.Instance.Local.GetComponent<CharacterVehicleInteraction>();
	}

#if DUMP_WORLDGEN
	public bool DebugRiding => riding;

	public void DebugMount()
	{
		if (remoteDriving && !riding)
		{
			Mount();
		}
	}

	public void DebugDismount()
	{
		if (riding && !leaving)
		{
			StartLeaving();
		}
	}
#endif
}
