using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

// world gen v2: the elevators between layers (01, transitions). the ride is the ending's (ElevatorSequence, its events
// in PillarNodeAnimationEvents), run up or down and stretched to the layer height. the layer it goes to is built as
// it starts, so it's there to see all the way; at its 35 s event that layer becomes the one they're on and the riding
// tower moves onto it; at its end they step out on the new deck and the layer they left goes.
// the ket, the barrel and a girl left behind are brought along. co-op: each peer rides its own copy, the host decides
// the swap and sends LayerChange; a friend that gets there first waits for it, one that missed the ride jumps over.
// before CoopPlayers (-100), which stands the other player's girl on the car's floor
[DefaultExecutionOrder(-110)]
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

	// the layer they're leaving and the one they're going to (GameSession.BeginLayer), and the tower cell held there
	private GameObject from;

	private GameObject next;

	private PillarStreamer.Spot arriving;

	private readonly Dictionary<Girl, bool> agents = new Dictionary<Girl, bool>();

	// co-op friend: the host's LayerChange for this ride came, and our own ride got to its swap or its end first
	private bool hostSwapped;

	private bool swapDue;

	private bool arriveDue;

	private static bool Friend => CoopSession.Together && !CoopSession.Instance.IsHost;

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
		if (current == null || current.events != events)
		{
			return false;
		}
		if (Friend && !current.hostSwapped)
		{
			current.swapDue = true;
			return true;
		}
		return current.DoSwap();
	}

	// DisableCamera, at the end: they're on the new deck
	public static bool Arrive(PillarNodeAnimationEvents events)
	{
		LayerTransition current = Current;
		if (current == null || current.events != events)
		{
			return false;
		}
		if (Friend && !current.swapped)
		{
			current.arriveDue = true;
			return true;
		}
		return current.DoArrive();
	}

	// co-op friend: the host's layer changed (CoopSession, LayerChange)
	public static void OnLayerChange(int layer, int cellX, int cellZ)
	{
		LayerTransition current = Current;
		if (current != null)
		{
			current.HostChanged(layer, cellX, cellZ);
		}
	}

	private void HostChanged(int layer, int cellX, int cellZ)
	{
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		if (world == null || world.Layers == null)
		{
			return;
		}
		if (elevator != null && to.Layer == layer && spot.CellX == cellX && spot.CellZ == cellZ)
		{
			hostSwapped = true;
			if (arriveDue)
			{
				// our ride's over already: over, and out of the car a moment later
				DoSwap();
				StartCoroutine(ArriveBuilt());
			}
			else if (swapDue)
			{
				DoSwap();
			}
			return;
		}
		if (world.Layer != layer)
		{
			Jump(world, layer, cellX, cellZ);
		}
	}

	// no ride here (the gate didn't close on this side): straight over to the host's layer, out of that pillar's car
	private void Jump(WorldGenerator world, int layer, int cellX, int cellZ)
	{
		int going = (layer > world.Layer) ? 1 : -1;
		LayerPlan next = world.Layers.Ride(world.Layer, cellX, cellZ, going);
		if (next == null || next.Layer != layer)
		{
			Debug.LogWarning("LayerTransition: can't follow the host to layer " + layer + " by pillar " + cellX + "," + cellZ + " from layer " + world.Layer);
			return;
		}
		if (elevator != null)
		{
			Abandon();
		}
		GameSession session = GameSession.Current;
		TileStreamer tiles = GameSession.Get<TileStreamer>();
		float deck = tiles.GroundY + going * (float)Gen2.LayerHeight;
		GameObject left = session.Layer;
		// a girl standing in a tower's car would go with it
		foreach (Girl girl in Girls.All)
		{
			if (girl.transform.parent != null && girl.transform.root.name.StartsWith("pillar"))
			{
				girl.transform.SetParent(null, true);
			}
		}
		GameObject layerObject = session.BeginLayer(next, deck, tiles.GroundLayer, null, true);
		world.SetLayer(next);
		session.UseLayer(layerObject);
		session.EndLayer(left);
		session.Backdrop(layerObject);
		PillarStreamer arrived = layerObject.GetComponent<PillarStreamer>();
		Vector3 car = arrived.CarPoint(cellX, cellZ) - Offset();
		Vector3 outward = arrived.Outward(cellX, cellZ);
		foreach (Girl girl in Girls.All)
		{
			if (girl.Control != GirlControl.Remote)
			{
				Put(girl, car + outward * 4f + Vector3.up * 0.3f);
			}
		}
		VehicleMovement ket = FindObjectOfType<VehicleMovement>();
		if (ket != null && !CoopKettenkrad.FollowsRemote)
		{
			Rigidbody body = ket.GetComponent<Rigidbody>();
			body.position = car + outward * (2f + ketOut) + Vector3.up * 1f;
			body.rotation = Quaternion.LookRotation(outward, Vector3.up);
			ket.transform.SetPositionAndRotation(body.position, body.rotation);
			body.velocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
		}
		Physics.SyncTransforms();
		Debug.Log("LayerTransition: jumped to the host's layer " + layer + " at pillar " + cellX + "," + cellZ);
		CoopSession.Instance.OnLayerChanged(world, cellX, cellZ);
	}

	// a ride of ours that isn't the host's: let it go, it never swaps
	private void Abandon()
	{
		foreach (InputContextStack.Token token in held)
		{
			token.Release();
		}
		held.Clear();
		RestoreAgents();
		if (characterManager != null)
		{
			characterManager.SetActive(true);
		}
		if (below != null)
		{
			Destroy(below);
			below = null;
		}
		if (next != null && next != GameSession.Current.Layer)
		{
			GameSession.Current.EndLayer(next);
		}
		next = null;
		from = null;
		EndingCinematic.End();
		elevator = null;
		events = null;
		tower = null;
		spot = null;
		to = null;
	}

	private void RestoreAgents()
	{
		foreach (KeyValuePair<Girl, bool> agent in agents)
		{
			if (agent.Key == null || agent.Key.Agent == null)
			{
				continue;
			}
			if (agent.Key.Control == GirlControl.AI)
			{
				agent.Key.Agent.enabled = true;
				agent.Key.Agent.Warp(agent.Key.transform.position);
				if (agent.Key.Follow != null)
				{
					agent.Key.Follow.follow = true;
				}
			}
			else
			{
				agent.Key.Agent.enabled = agent.Value;
			}
		}
		agents.Clear();
	}

	private static Vector3 Offset()
	{
		FloatingOrigin origin = GameSession.Get<FloatingOrigin>();
		return (origin != null) ? origin.TotalOffset : Vector3.zero;
	}

	private static void Put(Girl girl, Vector3 at)
	{
		CharacterController controller = girl.GetComponentInChildren<CharacterController>();
		if (controller != null)
		{
			controller.enabled = false;
		}
		girl.transform.position = at;
		Physics.SyncTransforms();
		if (controller != null)
		{
			controller.enabled = true;
		}
	}

	private bool Depart(PillarNodeAnimationEvents rideOn)
	{
		if (events == rideOn)
		{
			return true;
		}
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		PillarStreamer streamer = GameSession.Get<PillarStreamer>();
		if (elevator != null || world == null || world.Layers == null || streamer == null)
		{
			return false;
		}
		PillarStreamer.Spot at = streamer.SpotOf(rideOn.transform.root.gameObject);
		int going = (at != null) ? PillarStreamer.Way(at.State) : 0;
		LayerPlan target = (going != 0) ? world.Layers.Ride(world.Layer, at.CellX, at.CellZ, going) : null;
		if (target == null)
		{
			return false;
		}
		events = rideOn;
		elevator = rideOn.GetComponent<Animator>();
		tower = rideOn.transform.root.gameObject;
		spot = at;
		way = going;
		to = target;
		swapped = false;
		hostSwapped = false;
		swapDue = false;
		arriveDue = false;
		carOffset = 0f;
		characterManager = GameObject.FindWithTag("CharacterManager");
		if (characterManager != null)
		{
			characterManager.SetActive(false);
		}
		held.Clear();
		brought.Clear();
		agents.Clear();
		foreach (Girl girl in Girls.All)
		{
			if (girl.Input != null && girl.Input.Contexts != null)
			{
				held.Add(girl.Input.Contexts.Push(InputContext.Cinematic, "layer ride"));
			}
			// the agent would pull her back onto the navmesh, it's 600 m off
			if (girl.Agent != null)
			{
				agents[girl] = girl.Agent.enabled;
				girl.Agent.enabled = false;
			}
		}
		if (way < 0)
		{
			below = streamer.Bare(new Pose(spot.Pose.position - Vector3.up * (float)Gen2.LayerHeight, spot.Pose.rotation));
		}
		// the layer it goes to, from now: below it's in view all the way down, above it's a ceiling over the tower.
		// its far city waits till they're over its deck, from under it the boxes would hang in the air
		TileStreamer tiles = GameSession.Get<TileStreamer>();
		from = GameSession.Current.Layer;
		arriving = new PillarStreamer.Spot { CellX = spot.CellX, CellZ = spot.CellZ };
		next = GameSession.Current.BeginLayer(to, tiles.GroundY + way * (float)Gen2.LayerHeight, tiles.GroundLayer, arriving);
		if (way > 0)
		{
			next.GetComponent<FarCity>().Show(false);
		}
		Debug.Log("LayerTransition: riding " + (way > 0 ? "up" : "down") + " from layer " + world.Layer + " to " + to.Layer + " at pillar " + spot.CellX + "," + spot.CellZ);
		return true;
	}

	private System.Collections.IEnumerator ArriveBuilt()
	{
		yield return null;
		yield return null;
		if (elevator != null)
		{
			DoArrive();
		}
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
		PillarStreamer streamer = GameSession.Get<PillarStreamer>();
		float rise = way * (float)Gen2.LayerHeight;
		float deck = next.GetComponent<TileStreamer>().GroundY;
		// anyone of ours not aboard rides along in the car, and the ket and a loose barrel wait for them up there.
		// the other player's girl, ket and barrel are theirs to bring
		foreach (Girl girl in Girls.All)
		{
			if (girl.Control != GirlControl.Remote && !girl.transform.IsChildOf(tower.transform))
			{
				brought.Add(girl);
				Board(girl);
			}
		}
		VehicleMovement ket = FindObjectOfType<VehicleMovement>();
		ketBody = (ket != null && !CoopKettenkrad.FollowsRemote) ? ket.GetComponent<Rigidbody>() : null;
		if (ketBody != null)
		{
			ketWasKinematic = ketBody.isKinematic;
			ketBody.isKinematic = true;
		}
		GameObject barrel = GameObject.FindWithTag("Barrel");
		ParentConstraint carried = (barrel != null) ? barrel.GetComponent<ParentConstraint>() : null;
		barrelBody = (barrel != null && !CoopBarrel.RemoteOwned && (carried == null || !carried.constraintActive)) ? barrel.GetComponent<Rigidbody>() : null;
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
		world.SetLayer(to);
		// the tower goes over to the new layer, the car stays where it is
		tower.transform.position += Vector3.up * rise;
		carOffset = -rise;
		Ride();
		arriving.Instance = tower;
		session.UseLayer(next);
		next.GetComponent<FarCity>().Show(true);
		Physics.SyncTransforms();
		Debug.Log("LayerTransition: now on layer " + to.Layer + " (" + to.Def.Name + "), deck " + deck.ToString("F1") + ", origin " + to.Origin + (brought.Count > 0 ? ", brought " + brought.Count + " girl(s) aboard" : ""));
		if (CoopSession.Together)
		{
			CoopSession.Instance.OnLayerChanged(world, spot.CellX, spot.CellZ);
		}
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
		// nothing of the layer they left shows now: under the deck, or the ceiling stands in for it
		GameSession.Current.EndLayer(from);
		GameSession.Current.Backdrop(next);
		from = null;
		next = null;
		arriving = null;
		RestoreAgents();
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
		// a friend who joined during the ride gets the world now
		if (CoopSession.InGame)
		{
			CoopSession.Instance.OnLayerArrived();
		}
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
