using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterVehicleAnimationEvents : MonoBehaviour
{

    [SerializeField] private PlayerInputInitialize playerInputScript;

    [SerializeField] private CharacterVehicleInteraction characterVehicleInteractionScript;
    [SerializeField] private NavMeshAgentVehicleInteraction navMeshAgentVehicleInteractionScript;
    [SerializeField] private NavMeshAgentVehicleInteraction navMeshAgentSelfVehicleInteractionScript;
    [SerializeField] private VehicleMovement vehicleMovementScript;
    [SerializeField] private VehicleFuelManager vehicleFuelManagerScript;
    [SerializeField] private AudioSource vehicleAudioSource;
    [SerializeField] private Collider characterCollider;
    [SerializeField] private AudioAnimationEvents audioAnimationEventsScript;

    //animation events
    public void EnableVehicle()
    {
        // remote girl just replays the anim, her side drives the ket
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        vehicleMovementScript.playerInputScript = playerInputScript;
        playerInputScript.actions.Vehicle.Enable();
        vehicleAudioSource.Play();

        // reset flags for footstep sound when exiting vehicle
        characterCollider.enabled = false;
        audioAnimationEventsScript.isGroundedConcrete = false;
        audioAnimationEventsScript.isGroundedMetal = false;
    }
    public void DisableVehicle()
    {
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        playerInputScript.actions.Vehicle.Disable();
        vehicleAudioSource.Stop();
    }

    public void ExitVehicle()
    {
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        // movement comes back in DelayExit
        characterVehicleInteractionScript.preOrientExit = true;
    }
    public void SetConstraintTrue()
    {
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        characterVehicleInteractionScript.constraint = true;
        characterVehicleInteractionScript.changeCameraRadiusFlag1 = true;
    }
    public void SetConstraintFalse()
    {
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        characterVehicleInteractionScript.constraint = false;
        characterVehicleInteractionScript.changeCameraRadiusFlag2 = true;
    }
    public void EnableCharacterCollider()
    {
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        characterCollider.enabled = true;
    }

    public void ExitRight()
    {
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        characterVehicleInteractionScript.exitRight = true;
    }
    public void ExitLeft()
    {
        if (CoopPlayers.IsPuppet(this))
        {
            return;
        }
        characterVehicleInteractionScript.exitLeft = true;
    }
    public void AgentEnter()
    {
        // no AI companion in coop
        if (CoopSession.InGame)
        {
            return;
        }
        navMeshAgentVehicleInteractionScript.enter = true;
    }
    public void AgentExitBack()
    {
        if (CoopSession.InGame)
        {
            return;
        }
        navMeshAgentVehicleInteractionScript.exit = true;
        navMeshAgentVehicleInteractionScript.agentAnim.SetTrigger("ket exit");
    }
    public void AgentExitVehicle()
    {
        if (CoopSession.InGame)
        {
            return;
        }
        navMeshAgentSelfVehicleInteractionScript.preOrientExit = true;
    }
}
