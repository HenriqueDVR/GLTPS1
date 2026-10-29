using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;
using UnityEngine.Animations;

public class CharacterPossession : MonoBehaviour
{
    [SerializeField] private PlayerInputInitialize playerInputScript;
    [SerializeField] private GameObject CMFollowTarget;
    [SerializeField] private GameObject CMLookAtTarget;
    [SerializeField] private GameObject playerToPossess;

    [SerializeField] private Vector3 CMFollowTargetOffsetPos;
    [SerializeField] private Vector3 CMLookAtTargetOffsetPos;

    [SerializeField] private PositionConstraint outOfBoundsBox;
    [SerializeField] private PositionConstraint skybox;
    [SerializeField] private PositionConstraint weatherParticleSystem;
    [SerializeField] private PositionConstraint mapCamera;

    [SerializeField] private NodeData nodeDataScript;

    // downed player watches their partner
    public GameObject FollowTarget => CMFollowTarget;
    public GameObject LookAtTarget => CMLookAtTarget;
    public Vector3 FollowTargetOffset => CMFollowTargetOffsetPos;
    public Vector3 LookAtTargetOffset => CMLookAtTargetOffsetPos;

    // CameraRig moves these to whoever is played
    public PositionConstraint OutOfBoundsBox => outOfBoundsBox;
    public PositionConstraint Skybox => skybox;
    public PositionConstraint WeatherParticles => weatherParticleSystem;
    public PositionConstraint MapCamera => mapCamera;
    public NodeData NodeData => nodeDataScript;
    public GameObject Partner => playerToPossess;

    void OnEnable()
    {
        playerInputScript.actions.Player.Possess.performed += Possess;
    }

    void OnDisable()
    {
        playerInputScript.actions.Player.Possess.performed -= Possess;
    }

    private void Possess(InputAction.CallbackContext context)
    {
        if (context.performed &&
            !GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(1).IsTag("Ket") &&
            Time.timeScale == 1)
        {
            PossessCharacter(false);
        }
    }

    // hand control to the other girl. isDead: this one stays down instead of going to the AI
    public void PossessCharacter(bool isDead)
    {
        Girl self = GetComponent<Girl>();
        Girls.SwitchTo(Girls.Other(self), isDead);
    }
}
