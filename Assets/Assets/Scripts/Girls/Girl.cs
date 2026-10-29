using UnityEngine;
using UnityEngine.AI;

public enum GirlId
{
	Chito,
	Yuuri
}

// who drives a girl: the local player, the AI companion, the other player over the network,
// or nobody (dead in solo, the partner took over)
public enum GirlControl
{
	Player,
	AI,
	Remote,
	Down
}

// one per girl, added at runtime (Girls.Begin). for now it wraps the old scripts: a control mode
// is just which of them are on, the same sets CharacterPossession and CoopPlayers.MakePuppet toggled
public class Girl : MonoBehaviour
{
	public GirlId Id { get; private set; }

	public GirlControl Control { get; private set; }

	public Animator Animator { get; private set; }

	public CharacterItemInteraction Items { get; private set; }

	public CharacterPossession Possession { get; private set; }

	public PlayerInputInitialize Input { get; private set; }

	private PlayerMovement movement;

	private CharacterBarrelInteraction barrel;

	private CharacterFuelInteraction fuel;

	private CharacterVehicleInteraction vehicle;

	private NavMeshObstacle obstacle;

	private NavMeshAgent agent;

	private NavMeshAgentVehicleInteraction agentVehicle;

	private NavMeshAgentFollowPlayer follow;

	private NavMeshAgentCommand command;

	private CharacterStatusScreenEffect screenEffect;

	private NPCInteraction npc;

	private CharacterElevatorInteraction elevator;

	private SetPositionConstraintElevator elevatorConstraint;

	private CharacterController characterController;

	private PlayerGunMovement gunMovement;

	private CapsuleCollider capsule;

	public AudioSource Footsteps { get; private set; }

	public PlayerMovement Movement => movement;

	public CharacterBarrelInteraction Barrel => barrel;

	public NavMeshAgent Agent => agent;

	public NavMeshAgentVehicleInteraction AgentVehicle => agentVehicle;

	public NavMeshAgentFollowPlayer Follow => follow;

	public NPCInteraction Npc => npc;

	public void Init(GirlId id, GirlControl control)
	{
		Id = id;
		Control = control;
		Animator = GetComponentInChildren<Animator>();
		Items = GetComponentInChildren<CharacterItemInteraction>();
		Possession = GetComponentInChildren<CharacterPossession>();
		Input = GetComponent<PlayerInputInitialize>();
		movement = GetComponentInChildren<PlayerMovement>();
		barrel = GetComponentInChildren<CharacterBarrelInteraction>();
		fuel = GetComponentInChildren<CharacterFuelInteraction>();
		vehicle = GetComponentInChildren<CharacterVehicleInteraction>();
		obstacle = GetComponentInChildren<NavMeshObstacle>();
		agent = GetComponentInChildren<NavMeshAgent>();
		agentVehicle = GetComponentInChildren<NavMeshAgentVehicleInteraction>();
		follow = GetComponentInChildren<NavMeshAgentFollowPlayer>();
		command = GetComponentInChildren<NavMeshAgentCommand>();
		screenEffect = GetComponentInChildren<CharacterStatusScreenEffect>();
		npc = GetComponentInChildren<NPCInteraction>();
		elevator = GetComponentInChildren<CharacterElevatorInteraction>();
		elevatorConstraint = GetComponentInChildren<SetPositionConstraintElevator>();
		characterController = GetComponent<CharacterController>();
		gunMovement = GetComponentInChildren<PlayerGunMovement>(true);
		capsule = GetComponentInChildren<CapsuleCollider>();
		Footsteps = GetComponentInChildren<AudioSource>();
	}

	public void SetControl(GirlControl control)
	{
		Control = control;
		switch (control)
		{
		case GirlControl.Player:
			Set(movement, true);
			Set(Possession, true);
			Set(Items, true);
			Set(barrel, true);
			Set(fuel, true);
			Set(vehicle, true);
			Set(agent, false);
			Set(obstacle, true);
			Set(agentVehicle, false);
			Set(follow, false);
			Set(screenEffect, true);
			Set(npc, true);
			Set(command, true);
			Set(elevator, true);
			Set(elevatorConstraint, false);
			break;
		case GirlControl.AI:
			Set(movement, false);
			Set(Possession, false);
			Set(Items, false);
			Set(barrel, false);
			Set(fuel, false);
			Set(vehicle, false);
			Set(obstacle, false);
			Set(agent, true);
			Set(agentVehicle, true);
			Set(follow, true);
			Set(screenEffect, false);
			Set(npc, false);
			Set(command, false);
			Set(elevator, false);
			// she rides the elevator by constraint, the player walks on
			Set(elevatorConstraint, true);
			break;
		case GirlControl.Down:
			Set(capsule, false);
			Set(movement, false);
			Set(Possession, false);
			Set(Items, false);
			Set(barrel, false);
			Set(fuel, false);
			Set(vehicle, false);
			Set(obstacle, false);
			Set(screenEffect, false);
			Set(npc, false);
			Set(command, false);
			Set(elevator, false);
			break;
		case GirlControl.Remote:
			// CoopPlayers moves her from snapshots
			Set(follow, false);
			Set(agentVehicle, false);
			Set(command, false);
			Set(elevatorConstraint, false);
			Set(agent, false);
			Set(obstacle, false);
			Set(movement, false);
			Set(Possession, false);
			// idle branch would holster the rifle every frame
			Set(gunMovement, false);
			if (characterController != null)
			{
				characterController.enabled = false;
			}
			break;
		}
	}

	private static void Set(Behaviour behaviour, bool on)
	{
		if (behaviour != null)
		{
			behaviour.enabled = on;
		}
	}

	private static void Set(Collider collider, bool on)
	{
		if (collider != null)
		{
			collider.enabled = on;
		}
	}
}
