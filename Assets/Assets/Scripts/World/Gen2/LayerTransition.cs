using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

// world gen v2: the elevators between layers (01, transitions). the ride is the ending's (ElevatorSequence, its events
// in PillarNodeAnimationEvents), run up or down and stretched to the layer height. at its 35 s event the layer it goes
// to takes over (tiles, towers, backdrop) with the riding tower moved onto it; at its end they step out on the new deck.
// solo for now: the ket, the barrel and a girl left behind are brought along. co-op rides keep the ending (WP-WORLD-4)
[DefaultExecutionOrder(-20)]
public class LayerTransition : MonoBehaviour
{
	// the clip's lift: flat keys at these times, so an eased rise between (ElevatorSequence.anim)
	private const float liftStart = 4f;

	private const float liftEnd = 42.033333f;

	private static readonly int sequence = Animator.StringToHash("ElevatorSequence");

	// the ket and a loose barrel wait this far out from the car's door, on the new deck
	private const float ketOut = 7f;

	private static LayerTransition Current => GameSession.Get<LayerTransition>();

	public static bool Busy
	{
		get
		{
			LayerTransition current = Current;
			return current != null && current.elevator != null;
		}
	}

	private Animator elevator;

	private PillarNodeAnimationEvents events;

	private GameObject tower;

	private PillarStreamer.Spot spot;

	private int way;

	private LayerPlan to;

	private bool swapped;

	// the car's height over its tower's foot after the swap moved the tower
	private float carOffset;

	// the tower the car goes past that isn't the riding one: under the deck riding down till the swap, the old
	// layer's after the swap riding up
	private GameObject below;

	private GameObject characterManager;

	private readonly List<InputContextStack.Token> held = new List<InputContextStack.Token>();

	private Rigidbody ketBody;

	private bool ketWasKinematic;

	private Rigidbody barrelBody;

	private bool barrelWasKinematic;

	private readonly List<Girl> brought = new List<Girl>();

	// PauseGameState: a ride to another layer starts. false for the ending
	public static bool Begin(PillarNodeAnimationEvents events)
	{
		LayerTransition current = Current;
		return current != null && current.Depart(events);
	}

	// PlayEndingSequence, about 35 s in: the layer it goes to takes over
	public static bool Swap(PillarNodeAnimationEvents events)
	{
		LayerTransition current = Current;
		return current != null && current.events == events && current.DoSwap();
	}

	// DisableCamera, at the end: they're on the new deck
	public static bool Arrive(PillarNodeAnimationEvents events)
	{
		LayerTransition current = Current;
		return current != null && current.events == events && current.DoArrive();
	}

	private bool Depart(PillarNodeAnimationEvents rideOn)
	{
		if (events == rideOn)
		{
			return true;
		}
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		PillarStreamer streamer = GameSession.Get<PillarStreamer>();
		if (elevator != null || CoopSession.InGame || world == null || world.Layers == null || streamer == null)
		{
			return false;
		}
		PillarStreamer.Spot at = streamer.SpotOf(rideOn.transform.root.gameObject);
		int going = (at != null) ? PillarStreamer.Way(at.State) : 0;
		LayerPlan next = (going != 0) ? world.Layers.Ride(world.Layer, at.CellX, at.CellZ, going) : null;
		if (next == null)
		{
			return false;
		}
		events = rideOn;
		elevator = rideOn.GetComponent<Animator>();
		tower = rideOn.transform.root.gameObject;
		spot = at;
		way = going;
		to = next;
		swapped = false;
		carOffset = 0f;
		characterManager = GameObject.FindWithTag("CharacterManager");
		if (characterManager != null)
		{
			characterManager.SetActive(false);
		}
		held.Clear();
		brought.Clear();
		foreach (Girl girl in Girls.All)
		{
			if (girl.Input != null && girl.Input.Contexts != null)
			{
				held.Add(girl.Input.Contexts.Push(InputContext.Cinematic, "layer ride"));
			}
			// the agent would pull her back onto the navmesh, it's 600 m off
			if (girl.Agent != null)
			{
				girl.Agent.enabled = false;
			}
		}
		if (way < 0)
		{
			below = streamer.Bare(new Pose(spot.Pose.position - Vector3.up * (float)Gen2.LayerHeight, spot.Pose.rotation));
		}
		Debug.Log("LayerTransition: riding " + (way > 0 ? "up" : "down") + " from layer " + world.Layer + " to " + to.Layer + " at pillar " + spot.CellX + "," + spot.CellZ);
		return true;
	}

	private bool DoSwap()
	{
		if (swapped)
		{
			return true;
		}
		swapped = true;
		GameSession session = GameSession.Current;
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		TileStreamer tiles = GameSession.Get<TileStreamer>();
		PillarStreamer streamer = GameSession.Get<PillarStreamer>();
		float rise = way * (float)Gen2.LayerHeight;
		float deck = tiles.GroundY + rise;
		int groundLayer = tiles.GroundLayer;
		// anyone not aboard rides along in the car, and the ket and a loose barrel wait for them up there
		foreach (Girl girl in Girls.All)
		{
			if (!girl.transform.IsChildOf(tower.transform))
			{
				brought.Add(girl);
				Board(girl);
			}
		}
		VehicleMovement ket = FindObjectOfType<VehicleMovement>();
		ketBody = (ket != null) ? ket.GetComponent<Rigidbody>() : null;
		if (ketBody != null)
		{
			ketWasKinematic = ketBody.isKinematic;
			ketBody.isKinematic = true;
		}
		GameObject barrel = GameObject.FindWithTag("Barrel");
		ParentConstraint carried = (barrel != null) ? barrel.GetComponent<ParentConstraint>() : null;
		barrelBody = (barrel != null && (carried == null || !carried.constraintActive)) ? barrel.GetComponent<Rigidbody>() : null;
		if (barrelBody != null)
		{
			barrelWasKinematic = barrelBody.isKinematic;
			barrelBody.isKinematic = true;
		}
		if (below != null)
		{
			Destroy(below);
			below = null;
		}
		if (way > 0)
		{
			FloatingOrigin origin = GameSession.Get<FloatingOrigin>();
			below = streamer.Bare(new Pose(tower.transform.position + ((origin != null) ? origin.TotalOffset : Vector3.zero), tower.transform.rotation));
		}
		streamer.Release(spot);
		session.EndLayer();
		world.SetLayer(to);
		// the tower goes over to the new layer, the car stays where it is
		tower.transform.position += Vector3.up * rise;
		carOffset = -rise;
		Ride();
		// a spot of its own: the old streamer clears its spots' towers when it goes, at the end of the frame
		PillarStreamer.Spot arrived = new PillarStreamer.Spot { CellX = spot.CellX, CellZ = spot.CellZ, Instance = tower };
		session.BeginLayer(to, deck, groundLayer, arrived);
		Physics.SyncTransforms();
		Debug.Log("LayerTransition: now on layer " + to.Layer + " (" + to.Def.Name + "), deck " + deck.ToString("F1") + ", origin " + to.Origin + (brought.Count > 0 ? ", brought " + brought.Count + " girl(s) aboard" : ""));
		return true;
	}

	private bool DoArrive()
	{
		if (!swapped)
		{
			DoSwap();
		}
		carOffset = 0f;
		if (below != null)
		{
			Destroy(below);
			below = null;
		}
		Vector3 car = events.transform.localPosition;
		events.transform.localPosition = new Vector3(car.x, 0f, car.z);
		// gate open, ready to go back
		elevator.Rebind();
		elevator.Update(0f);
		events.index = 0;
		Physics.SyncTransforms();
		Vector3 door = tower.transform.TransformPoint(Door(ketOut));
		Quaternion outward = Quaternion.LookRotation(tower.transform.TransformDirection(Vector3.right), Vector3.up);
		if (ketBody != null)
		{
			Vector3 at = Ground(door);
			ketBody.position = at + Vector3.up * 0.5f;
			ketBody.rotation = outward;
			ketBody.transform.SetPositionAndRotation(ketBody.position, ketBody.rotation);
			ketBody.isKinematic = ketWasKinematic;
			ketBody.velocity = Vector3.zero;
			ketBody.angularVelocity = Vector3.zero;
			ketBody.WakeUp();
		}
		if (barrelBody != null)
		{
			barrelBody.transform.position = Ground(door + outward * Vector3.right * 2.5f) + Vector3.up * 0.6f;
			barrelBody.isKinematic = barrelWasKinematic;
			barrelBody.velocity = Vector3.zero;
		}
		Physics.SyncTransforms();
		foreach (Girl girl in Girls.All)
		{
			if (girl.Agent != null && girl.Control == GirlControl.AI)
			{
				girl.Agent.enabled = true;
				girl.Agent.Warp(girl.transform.position);
				if (girl.Follow != null)
				{
					girl.Follow.follow = true;
				}
			}
		}
		foreach (InputContextStack.Token token in held)
		{
			token.Release();
		}
		held.Clear();
		if (characterManager != null)
		{
			characterManager.SetActive(true);
		}
		GameObject freeLook = GameObject.FindWithTag("CMFreeLookCamera");
		SetCameraRadius radius = (freeLook != null) ? freeLook.GetComponent<SetCameraRadius>() : null;
		if (radius != null)
		{
			radius.SetDefaultRadius();
			radius.SetDefaultHeight();
		}
		EndingCinematic.End();
		TileStreamer tiles = GameSession.Get<TileStreamer>();
		FloatingOrigin origin = GameSession.Get<FloatingOrigin>();
		float offset = (origin != null) ? origin.TotalOffset.y : 0f;
		Debug.Log("LayerTransition: arrived on layer " + to.Layer + ", car at " + (events.transform.position.y + offset - tiles.GroundY).ToString("F2") + " over the deck, tiles " + tiles.TileCount);
		elevator = null;
		events = null;
		tower = null;
		spot = null;
		to = null;
		ketBody = null;
		barrelBody = null;
		brought.Clear();
		return true;
	}

	// the clip writes the car's height first, this puts it where the layers are
	private void LateUpdate()
	{
		if (elevator != null)
		{
			Ride();
		}
	}

	private void Ride()
	{
		AnimatorStateInfo state = elevator.GetCurrentAnimatorStateInfo(0);
		if (state.shortNameHash != sequence)
		{
			return;
		}
		float t = Mathf.Min(state.normalizedTime, 1f) * state.length;
		float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(liftStart, liftEnd, t));
		Vector3 car = events.transform.localPosition;
		events.transform.localPosition = new Vector3(car.x, way * (float)Gen2.LayerHeight * lift + carOffset, car.z);
	}

	// in the tower's space: out from the middle of the car's door side, on the deck
	private Vector3 Door(float distance)
	{
		Vector3 car = tower.transform.InverseTransformPoint(events.transform.position);
		return new Vector3(car.x + 2f + distance, 0f, car.z);
	}

	private static Vector3 Ground(Vector3 at)
	{
		if (Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
		{
			return hit.point;
		}
		return at;
	}

	// into the car, on its floor
	private void Board(Girl girl)
	{
		CharacterController controller = girl.GetComponentInChildren<CharacterController>();
		if (controller != null)
		{
			controller.enabled = false;
		}
		Vector3 floor = events.transform.position + tower.transform.TransformDirection(new Vector3(0f, 0f, 1.5f)) + Vector3.up * 0.2f;
		girl.transform.SetParent(events.transform, true);
		girl.transform.position = floor;
		SetPositionConstraintElevator constraint = girl.GetComponent<SetPositionConstraintElevator>();
		if (constraint != null)
		{
			constraint.target = events.transform;
		}
		if (controller != null)
		{
			controller.enabled = true;
		}
	}
}
