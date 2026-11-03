using UnityEngine;
using UnityEngine.AI;

// the AI girl: walks beside the player at the player's pace, stands when she stands, gets on and off the ket's back seat.
// one phase at a time, only that phase moves her. added at runtime (Girl.Init). NavMeshAgentFollowPlayer and
// NavMeshAgentVehicleInteraction hold the scene's references, and Girl turns them on when she's the AI. solo only: a
// remote girl has them off
public class AIController : MonoBehaviour
{
	private enum Phase
	{
		OnFoot,
		// to the spot behind the seat
		Walking,
		// the climb up, gliding to the seat
		Climbing,
		Seated,
		// the clip of climbing off, then down onto the spot
		Leaving
	}

	// same as CoopKettenkrad.Mount, snapping to the seat dragged her through the ket
	private const float glideDuration = 0.6f;

	// no route to the spot behind the seat (the ket across a narrow way, her boxed in in front of it) and she's close:
	// she walks round the hull herself, past its corners. never a jump
	private const float walkReach = 4f;

	private const float walkSpeed = 3f;

	private const float hullHalfX = 0.9f;

	private const float hullHalfZ = 1.8f;

	private const float cornerReached = 0.05f;

	private const float exitMaxTime = 1f;

	// the climb off's clip never said it started
	private const float leavingMaxWait = 5f;

	// beside the player and a bit behind, so she's in the corner of your eye
	private const float besideBy = 2f;

	private const float behindBy = 1.2f;

	// a narrow way: in single file
	private const float fileBy = 2.2f;

	private const float aimPast = 1.5f;

	private const float movingAbove = 0.4f;

	// the player's stopped: this close and she's fine where she is. she stays put till the player's really gone
	// somewhere, a step or a shuffle on the spot had her off and walking
	private const float standWithin = 4f;

	private const float startAfter = 2f;

	private const float tooClose = 1.5f;

	private const float arrived = 1f;

	private const float retargetEvery = 0.2f;

	private const float catchUpRate = 1.5f;

	private const float speedChange = 8f;

	// running to catch up from this far back, till she's this close
	private const float runGap = 4f;

	private const float runUntil = 2f;

	// walking with the player she only hurries this much: the legs stay in a walk
	private const float walkHurry = 1.3f;

	// the gait's picked, not read off her speed. her speed moves all the time, and the blend tree between walk and run
	// showed every bit of it
	private const float gaitChange = 6f;

	private const float standBelow = 0.3f;

	private const float turnRate = 240f;

	// she stops and starts like someone walking, not a boat
	private const float acceleration = 24f;

	private const float stopWithin = 0.25f;

	private const float groundReach = 0.5f;

	private const float groundSpeed = 1f;

	// stuck: not moving this long while she should be
	private const float stuckAfter = 1f;

	private const float stuckBelow = 0.2f;

	private const float sideFlipEvery = 3f;

	// lost: out of sight she comes back near the player, never where it shows
	private const float lostNear = 12f;

	private const float lostAfter = 4f;

	private const float lostFar = 40f;

	private const float catchUpTryEvery = 1f;

	private static readonly float[] catchUpDistances = { 8f, 12f, 6f };

	private static readonly float[] catchUpAngles = { 180f, 140f, 220f, 100f, 260f };

	private NavMeshAgent agent;

	private NavMeshAgentFollowPlayer orders;

	private PlayerMovement player;

	private Animator animator;

	private Transform child;

	private int groundMask;

	private Girl girl;

	private NavMeshAgentVehicleInteraction seat;

	private PlayerInputInitialize playerInput;

	private bool listening;

	private bool on;

	private Phase phase;

	private bool walkingIn;

	private bool wantBoard;

	private bool wantOff;

	private bool climbingOff;

	private float elapsed;

	private float leavingSince;

	private Transform rootBone;

	private Vector3 bodyBefore;

	private Vector3 glideStart;

	private bool measureGlide;

	private bool going;

	private bool running;

	private Vector3 settledAt;

	private Vector3 smoothVelocity;

	private float gait;

	private float side = 1f;

	private Vector3 slot;

	private Vector3 playerLast;

	private Vector3 playerVelocity;

	private Vector3 heading;

	private float nextRetarget;

	private float nextSideFlip;

	private float stuckFor;

	private int stuckStep;

	private float lostFor;

	private float nextCatchUp;

	private float nextGroundCheck;

	private float floatingSince = -1f;

	public void Init(Girl girl, NavMeshAgent agent, NavMeshAgentFollowPlayer orders, NavMeshAgentVehicleInteraction seat)
	{
		this.girl = girl;
		this.agent = agent;
		this.orders = orders;
		this.seat = seat;
		playerInput = orders.Player.GetComponentInParent<PlayerInputInitialize>();
		player = orders.Player;
		animator = orders.Animator;
		child = orders.Child;
		groundMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Vehicle", "Player", "AI", "Items", "Barrel");
	}

	private void OnDestroy()
	{
		if (listening)
		{
			playerInput.actions.Player.AgentWait.performed -= OnWaitOrder;
		}
	}

	// the player's wait/follow key
	private void OnWaitOrder(UnityEngine.InputSystem.InputAction.CallbackContext context)
	{
		if (girl.Control != GirlControl.AI)
		{
			return;
		}
		orders.follow = !orders.follow;
		if (!orders.follow && phase == Phase.OnFoot && agent.isActiveAndEnabled && agent.isOnNavMesh)
		{
			agent.ResetPath();
			going = false;
			running = false;
		}
	}

	// the driver's getting on: she heads for the back seat
	public void Board()
	{
		if (phase == Phase.OnFoot)
		{
			wantOff = false;
			phase = Phase.Walking;
			on = false;
			if (agent.isActiveAndEnabled)
			{
				// the agent steers her there
				agent.updateRotation = true;
			}
		}
		else if (phase == Phase.Leaving)
		{
			wantBoard = true;
		}
	}

	// the driver's getting off
	public void GetOff()
	{
		wantBoard = false;
		switch (phase)
		{
		case Phase.Walking:
			// never got on
			walkingIn = false;
			agent.enabled = true;
			phase = Phase.OnFoot;
			break;
		case Phase.Climbing:
			seat.agentAnim.SetTrigger("ket exit");
			wantOff = true;
			break;
		case Phase.Seated:
			seat.agentAnim.SetTrigger("ket exit");
			StartLeaving();
			break;
		}
	}

	// her climbing off clip's begun
	public void ClimbingOff()
	{
		climbingOff = true;
	}

	private void StartLeaving()
	{
		wantOff = false;
		phase = Phase.Leaving;
		elapsed = 0f;
		leavingSince = Time.time;
	}

	private void Update()
	{
		if (orders == null)
		{
			return;
		}
		if (!listening && playerInput != null && playerInput.actions != null)
		{
			listening = true;
			playerInput.actions.Player.AgentWait.performed += OnWaitOrder;
		}
		if (phase != Phase.OnFoot)
		{
			if (seat.enabled)
			{
				UpdateKet();
			}
			return;
		}
		if (!orders.enabled || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
		{
			return;
		}
		if (!on)
		{
			on = true;
			Begin();
		}
		KeepOnGround();
		SitOnGround();
		WatchPlayer();
		if (orders.follow)
		{
			Follow();
		}
		Move();
	}

	// climbing off the clip's authored from the seat, so it walks her body out behind the ket with her feet still at
	// the seat. as it blends out her feet go to the spot, moved so her body doesn't: slid on their own they took her
	// 0.6 m past it and back. the same frame the climb's first posed, the animator's run by now: left to the next Update
	// she was drawn once from where the clip's authored, far off
	private void LateUpdate()
	{
		if (seat == null || !seat.enabled)
		{
			return;
		}
		if (phase == Phase.Leaving && climbingOff && !agent.enabled)
		{
			FindRootBone();
			if (rootBone != null)
			{
				Vector3 shift = seat.EnterPosition.position - rootBone.position;
				shift.y = 0f;
				transform.position += shift;
			}
		}
		if (!measureGlide)
		{
			return;
		}
		measureGlide = false;
		if (rootBone != null)
		{
			// sideways only
			Vector3 shift = bodyBefore - rootBone.position;
			shift.y = 0f;
			glideStart = transform.position + shift;
			transform.position = glideStart;
		}
	}

	private void FindRootBone()
	{
		if (rootBone == null)
		{
			rootBone = seat.agentAnim.transform.Find(name.Replace("Parent", "") + "/Root");
		}
	}

	private void UpdateKet()
	{
		if (!walkingIn)
		{
			CharacterMovementAnimation.Movement(animator, agent.velocity, player.runSpeed);
		}
		switch (phase)
		{
		case Phase.Walking:
			WalkToSeat();
			break;
		case Phase.Climbing:
			elapsed += Time.deltaTime;
			if (!measureGlide)
			{
				transform.position = Vector3.Lerp(glideStart, seat.ConstraintPosition.position, Mathf.Clamp01(elapsed / glideDuration));
			}
			transform.rotation = seat.ConstraintPosition.rotation;
			if (elapsed >= glideDuration)
			{
				phase = Phase.Seated;
				if (wantOff)
				{
					StartLeaving();
				}
			}
			break;
		case Phase.Seated:
			transform.position = seat.ConstraintPosition.position;
			transform.rotation = seat.ConstraintPosition.rotation;
			break;
		case Phase.Leaving:
			Leave();
			break;
		}
	}

	private bool NoRoute()
	{
		if (!agent.isOnNavMesh)
		{
			return true;
		}
		return !agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete && agent.remainingDistance < 0.3f;
	}

	private Vector3 NextOnFoot()
	{
		Transform frame = seat.ConstraintPosition;
		Vector3 at = frame.InverseTransformPoint(transform.position);
		// past the back already: straight there
		if (at.z <= -hullHalfZ + cornerReached)
		{
			return seat.EnterPosition.position;
		}
		float side = at.x < 0f ? -1f : 1f;
		// alongside or in front: out to the hull's side first, then down it to the back corner
		Vector3 corner = Mathf.Abs(at.x) < hullHalfX - cornerReached ? new Vector3(side * hullHalfX, at.y, at.z) : new Vector3(side * hullHalfX, at.y, -hullHalfZ);
		return frame.TransformPoint(corner);
	}

	private void WalkToSeat()
	{
		Transform spot = seat.EnterPosition;
		float away = AngleUtils.FlatDistance(transform.position, spot.position);
		if (agent.enabled)
		{
			// before asking again: right after it the path's always pending
			if (NoRoute() && away < walkReach)
			{
				agent.enabled = false;
				walkingIn = true;
			}
			else if (agent.isOnNavMesh)
			{
				// the driver's waiting. right up to the spot, following stops short of where it's going
				agent.autoBraking = true;
				agent.speed = player.runSpeed;
				agent.stoppingDistance = 0f;
				agent.destination = spot.position;
			}
		}
		else if (walkingIn && away >= 0.2f)
		{
			if (away > walkReach * 1.5f)
			{
				// the ket's driven off
				walkingIn = false;
				agent.enabled = true;
			}
			else
			{
				Vector3 before = transform.position;
				Vector3 next = NextOnFoot();
				transform.position = Vector3.MoveTowards(before, next, walkSpeed * Time.deltaTime);
				Vector3 step = transform.position - before;
				CharacterMovementAnimation.Movement(animator, step / Mathf.Max(Time.deltaTime, 0.0001f), player.runSpeed);
				step.y = 0f;
				if (step.sqrMagnitude > 0.000001f)
				{
					Quaternion facing = Quaternion.LookRotation(step);
					transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, seat.RotationSpeed * Time.deltaTime);
					child.rotation = Quaternion.RotateTowards(child.rotation, facing, seat.RotationSpeed * Time.deltaTime);
				}
			}
		}
		if (away >= 0.2f)
		{
			return;
		}
		// on the spot: turn to the seat, then up
		agent.enabled = false;
		child.rotation = Quaternion.RotateTowards(child.rotation, spot.rotation, seat.RotationSpeed * Time.deltaTime);
		transform.rotation = Quaternion.RotateTowards(transform.rotation, spot.rotation, seat.RotationSpeed * Time.deltaTime);
		if (AngleUtils.RotationsApproximatelyEqual(transform, spot) && AngleUtils.RotationsApproximatelyEqual(child, spot))
		{
			FindRootBone();
			bodyBefore = rootBone != null ? rootBone.position : transform.position;
			transform.rotation = seat.ConstraintPosition.rotation;
			child.rotation = seat.ConstraintPosition.rotation;
			seat.agentAnim.Play(name.Replace("Parent", "") + "_Enter_Ket_Back", 1, 0f);
			// the climb isn't posed until next frame
			glideStart = transform.position;
			measureGlide = true;
			elapsed = 0f;
			phase = Phase.Climbing;
			walkingIn = false;
		}
	}

	private void Leave()
	{
		if (!climbingOff)
		{
			if (Time.time - leavingSince < leavingMaxWait)
			{
				return;
			}
			climbingOff = true;
			elapsed = exitMaxTime;
		}
		// she stays where the clip's put her (LateUpdate), her feet come in under her
		elapsed += Time.deltaTime;
		Vector3 spot = seat.EnterPosition.position;
		if (!agent.enabled && AngleUtils.FlatDistance(transform.position, spot) > 0.1f && elapsed < exitMaxTime)
		{
			transform.position = new Vector3(transform.position.x, Mathf.MoveTowards(transform.position.y, spot.y, Time.deltaTime), transform.position.z);
			return;
		}
		transform.position = spot;
		agent.enabled = true;
		climbingOff = false;
		elapsed = 0f;
		phase = Phase.OnFoot;
		if (wantBoard)
		{
			wantBoard = false;
			Board();
		}
	}

	private void Move()
	{
		smoothVelocity = Vector3.Lerp(smoothVelocity, agent.velocity, 1f - Mathf.Exp(-10f * Time.deltaTime));
		float speed = smoothVelocity.magnitude;
		float wanted = speed < standBelow ? 0f : running || playerVelocity.magnitude > 0.5f * (player.walkSpeed + player.runSpeed) ? player.runSpeed : Mathf.Min(speed, player.walkSpeed);
		if (speed >= standBelow && wanted < player.walkSpeed && going && playerVelocity.magnitude > movingAbove)
		{
			wanted = player.walkSpeed;
		}
		gait = Mathf.MoveTowards(gait, wanted, gaitChange * Time.deltaTime);
		animator.SetFloat("velocityXZ", gait / player.runSpeed);
		// faces where she's going, not each frame's wobble in it
		if (speed > 0.5f)
		{
			Vector3 flat = smoothVelocity;
			flat.y = 0f;
			transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), turnRate * Time.deltaTime);
		}
		child.rotation = Quaternion.RotateTowards(child.rotation, transform.rotation, turnRate * Time.deltaTime);
	}

	// off the ket, or just became the AI: from where everyone is now
	private void Begin()
	{
		playerLast = player.transform.position;
		playerVelocity = Vector3.zero;
		heading = player.transform.forward;
		heading.y = 0f;
		side = Vector3.Dot(transform.position - player.transform.position, Vector3.Cross(Vector3.up, heading)) < 0f ? -1f : 1f;
		going = false;
		running = false;
		settledAt = playerLast;
		smoothVelocity = Vector3.zero;
		stuckFor = 0f;
		stuckStep = 0;
		lostFor = 0f;
		nextRetarget = 0f;
		agent.updateRotation = false;
		agent.acceleration = acceleration;
		agent.stoppingDistance = stopWithin;
		agent.angularSpeed = orders.AngularSpeed;
	}

	private void WatchPlayer()
	{
		Vector3 moved = player.transform.position - playerLast;
		playerLast = player.transform.position;
		moved.y = 0f;
		// the floating origin, or put somewhere
		if (moved.sqrMagnitude > 25f || Time.deltaTime <= 0f)
		{
			moved = Vector3.zero;
		}
		playerVelocity = Vector3.Lerp(playerVelocity, moved / Mathf.Max(Time.deltaTime, 0.0001f), 1f - Mathf.Exp(-8f * Time.deltaTime));
		if (playerVelocity.magnitude > movingAbove)
		{
			heading = Vector3.Slerp(heading, playerVelocity.normalized, 1f - Mathf.Exp(-3f * Time.deltaTime));
			heading.y = 0f;
			heading.Normalize();
		}
	}

	private void Follow()
	{
		Vector3 here = player.transform.position;
		float apart = AngleUtils.FlatDistance(here, transform.position);
		bool moving = playerVelocity.magnitude > movingAbove;
		if (WatchLost(apart))
		{
			return;
		}
		if (!going && (apart >= standWithin || AngleUtils.FlatDistance(here, settledAt) > startAfter))
		{
			going = true;
			nextRetarget = 0f;
		}
		if (going && Time.time >= nextRetarget)
		{
			nextRetarget = Time.time + retargetEvery;
			slot = PickSlot(here);
			// a bit past it while the player's moving: reaching the spot she stopped, and started again when it moved on
			agent.SetDestination(moving && Clear(slot, slot + heading * aimPast, out Vector3 past) ? past : slot);
		}
		float gap = AngleUtils.FlatDistance(transform.position, slot);
		if (going && !moving && (gap < arrived || apart < tooClose))
		{
			going = false;
			settledAt = here;
		}
		if (!going)
		{
			if (agent.hasPath)
			{
				agent.ResetPath();
			}
			stuckFor = 0f;
			stuckStep = 0;
			running = false;
			return;
		}
		// slowing for a spot that's moving on with the player was the stop-start
		agent.autoBraking = !moving;
		agent.speed = Mathf.MoveTowards(agent.speed, WantedSpeed(gap, moving), speedChange * Time.deltaTime);
		WatchStuck(gap);
	}

	// the player's speed, plus or minus what closes the gap
	private float WantedSpeed(float gap, bool moving)
	{
		if (gap > runGap)
		{
			running = true;
		}
		else if (gap < runUntil)
		{
			running = false;
		}
		if (running)
		{
			return player.runSpeed;
		}
		if (!moving)
		{
			return player.walkSpeed;
		}
		float along = Vector3.Dot(slot - transform.position, heading);
		float wanted = playerVelocity.magnitude + (along >= 0f ? gap : along) * catchUpRate;
		float most = Mathf.Max(playerVelocity.magnitude, player.walkSpeed) * walkHurry;
		return Mathf.Clamp(wanted, player.walkSpeed * 0.5f, Mathf.Min(most, player.runSpeed));
	}

	private Vector3 PickSlot(Vector3 here)
	{
		if (!NavMesh.SamplePosition(here, out NavMeshHit from, 2f, NavMesh.AllAreas))
		{
			return here;
		}
		// nothing else has worked
		if (stuckStep >= 3)
		{
			return from.position;
		}
		Vector3 right = Vector3.Cross(Vector3.up, heading);
		Vector3 spot = here + right * side * besideBy - heading * behindBy;
		if (stuckStep < 2 && Clear(from.position, spot, out Vector3 clear))
		{
			return clear;
		}
		if (stuckStep < 2 && Time.time >= nextSideFlip && Clear(from.position, here - right * side * besideBy - heading * behindBy, out clear))
		{
			side = -side;
			nextSideFlip = Time.time + sideFlipEvery;
			return clear;
		}
		// walls both sides, a corridor, the ket alongside
		NavMesh.Raycast(from.position, here - heading * fileBy, out NavMeshHit behind, NavMesh.AllAreas);
		return behind.position;
	}

	private static bool Clear(Vector3 from, Vector3 to, out Vector3 spot)
	{
		spot = to;
		if (!NavMesh.SamplePosition(to, out NavMeshHit near, 0.5f, NavMesh.AllAreas))
		{
			return false;
		}
		spot = near.position;
		return !NavMesh.Raycast(from, near.position, out NavMeshHit _, NavMesh.AllAreas);
	}

	private void WatchStuck(float gap)
	{
		if (gap < tooClose)
		{
			stuckFor = 0f;
			stuckStep = 0;
			return;
		}
		if (agent.pathPending || agent.velocity.magnitude >= stuckBelow)
		{
			stuckFor = 0f;
			return;
		}
		stuckFor += Time.deltaTime;
		if (stuckFor < stuckAfter)
		{
			return;
		}
		stuckFor = 0f;
		stuckStep++;
		// first the other side, then behind the player, then the player
		if (stuckStep == 1)
		{
			side = -side;
			nextSideFlip = Time.time + sideFlipEvery;
		}
		nextRetarget = 0f;
	}

	// true when she's just been brought back
	private bool WatchLost(float apart)
	{
		bool noWay = !agent.pathPending && going && (agent.pathStatus != NavMeshPathStatus.PathComplete || stuckStep >= 2);
		lostFor = apart > lostNear && noWay ? lostFor + Time.deltaTime : 0f;
		if ((lostFor < lostAfter && apart < lostFar) || Time.time < nextCatchUp)
		{
			return false;
		}
		nextCatchUp = Time.time + catchUpTryEvery;
		return CatchUp();
	}

	private bool CatchUp()
	{
		Camera view = Camera.main;
		if (view == null || Shows(view, transform.position))
		{
			return false;
		}
		Vector3 here = player.transform.position;
		NavMeshPath path = new NavMeshPath();
		foreach (float distance in catchUpDistances)
		{
			foreach (float angle in catchUpAngles)
			{
				Vector3 away = Quaternion.AngleAxis(angle, Vector3.up) * heading * distance;
				if (!NavMesh.SamplePosition(here + away, out NavMeshHit near, 1f, NavMesh.AllAreas) || Shows(view, near.position))
				{
					continue;
				}
				if (!NavMesh.CalculatePath(near.position, here, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
				{
					continue;
				}
				agent.Warp(near.position);
				going = true;
				stuckStep = 0;
				stuckFor = 0f;
				lostFor = 0f;
				nextRetarget = 0f;
				return true;
			}
		}
		return false;
	}

	private bool Shows(Camera view, Vector3 position)
	{
		Vector3 head = position + Vector3.up;
		Vector3 screen = view.WorldToViewportPoint(head);
		if (screen.z <= 0f || screen.x < -0.1f || screen.x > 1.1f || screen.y < -0.1f || screen.y > 1.1f)
		{
			return false;
		}
		return !Physics.Linecast(view.transform.position, head, groundMask, QueryTriggerInteraction.Ignore);
	}

	// a net: whatever put her on a surface that isn't there, she comes back down to the one that is
	private void KeepOnGround()
	{
		if (Time.time < nextGroundCheck)
		{
			return;
		}
		nextGroundCheck = Time.time + 0.5f;
		bool floating = Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 30f, groundMask, QueryTriggerInteraction.Ignore) && transform.position.y - hit.point.y > 0.6f;
		if (!floating)
		{
			floatingSince = -1f;
		}
		else if (floatingSince < 0f)
		{
			floatingSince = Time.time;
		}
		else if (Time.time - floatingSince > 1f && NavMesh.SamplePosition(hit.point, out NavMeshHit near, 1.5f, NavMesh.AllAreas))
		{
			Debug.Log("AI girl: floating " + (transform.position.y - hit.point.y).ToString("F2") + " m, put back down");
			floatingSince = -1f;
			agent.Warp(near.position);
		}
	}

	// the navmesh sits a voxel or so above the real ground (never below it), and she stood on it with her feet in the air
	private void SitOnGround()
	{
		float surface = transform.position.y - agent.baseOffset;
		float wanted = 0f;
		if (Physics.Raycast(new Vector3(transform.position.x, surface + groundReach, transform.position.z), Vector3.down, out RaycastHit hit, 2f * groundReach, groundMask, QueryTriggerInteraction.Ignore))
		{
			wanted = hit.point.y - surface;
		}
		agent.baseOffset = Mathf.MoveTowards(agent.baseOffset, wanted, groundSpeed * Time.deltaTime);
	}

}
