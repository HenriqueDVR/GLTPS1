using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
public class NavMeshAgentVehicleInteraction : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    public Animator agentAnim;
    [SerializeField] private NavMeshAgentFollowPlayer navMeshAgentFollowPlayerScript;
    [SerializeField] private Transform child;
    [SerializeField] private Transform enterPosition;
    [SerializeField] private Transform constraintPosition;
    [SerializeField] private PlayerMovement playerMovementScript;
    public bool enter;
    public bool exit;
    private bool positionConstraint;
    public bool rotationConstraint;
    private bool preOrientEnter;
    public bool preOrientExit;
    [SerializeField] private float rotationSpeed = 600.0f;
    private float elapsed = 0f;
    [SerializeField] private float duration = 0.25f;

    [SerializeField] private CharacterDeath characterDeathScript;

    // same as CoopKettenkrad.Mount, snapping to the seat dragged her through the ket
    private const float glideDuration = 0.6f;
    private Transform rootBone;
    private Vector3 bodyBefore;
    private Vector3 glideStart;
    private bool measureGlide;

    // back seat, coop rider uses it too
    public Transform EnterPosition => enterPosition;
    public Transform ConstraintPosition => constraintPosition;
    public Transform Child => child;

    void Update()
    {
        CharacterMovementAnimation.Movement(agentAnim, agent.velocity, playerMovementScript.runSpeed);

        if (constraintPosition != null && enterPosition != null) 
        {
            if (enter && !preOrientExit)
            {
                navMeshAgentFollowPlayerScript.enabled = false;
                //orient agent position to enter ket
                if (agent.enabled)
                {
                    agent.destination = enterPosition.position;
                }
                if (Vector3.Distance(transform.position, enterPosition.position) < 0.2f)
                {
                    //orient rotation to enter ket
                    agent.enabled = false;
                    child.rotation = Quaternion.RotateTowards(child.rotation, enterPosition.rotation, rotationSpeed * Time.deltaTime);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, enterPosition.rotation, rotationSpeed * Time.deltaTime);
                    if (AngleUtils.RotationsApproximatelyEqual(transform, enterPosition) && AngleUtils.RotationsApproximatelyEqual(child, enterPosition))
                    {
                        string girl = name.Replace("Parent", "");
                        rootBone = agentAnim.transform.Find(girl + "/Root");
                        bodyBefore = rootBone != null ? rootBone.position : transform.position;
                        transform.rotation = constraintPosition.rotation;
                        child.rotation = constraintPosition.rotation;
                        agentAnim.Play(girl + "_Enter_Ket_Back", 1, 0f);
                        glideStart = transform.position;
                        measureGlide = true;
                        elapsed = 0f;
                        preOrientEnter = true;
                        enter = false;
                        // climb isn't posed until next frame
                        return;
                    }
                }
            }

            //lerp agent position to vehicle

            if (preOrientEnter)
            {
                if (measureGlide)
                {
                    measureGlide = false;
                    if (rootBone != null)
                    {
                        // sideways only
                        Vector3 shift = bodyBefore - rootBone.position;
                        shift.y = 0f;
                        glideStart = transform.position + shift;
                    }
                }
                rotationConstraint = true;
                elapsed += Time.deltaTime;
                transform.position = Vector3.Lerp(glideStart, constraintPosition.position, Mathf.Clamp01(elapsed / glideDuration));
                if (elapsed >= glideDuration)
                {
                    positionConstraint = true;
                    preOrientEnter = false;
                    elapsed = 0f;
                }
            }

            if (exit)
            {
                positionConstraint = false;
                rotationConstraint = false;

                //lerp agent position to exit
                if (preOrientExit)
                {
                    if (Vector3.Distance(transform.position, enterPosition.position) > 0.1f && !agent.enabled)
                    {
                        elapsed += Time.deltaTime;
                        transform.position = Vector3.Lerp(transform.position, enterPosition.position, elapsed / duration);
                    }
                    else
                    {
                        agent.enabled = true;
                        navMeshAgentFollowPlayerScript.enabled = true;
                        preOrientExit = false;

                        exit = false;
                        elapsed = 0f;
                    }
                }
            }

            if (positionConstraint)
            {
                transform.position = constraintPosition.position;
            }
            if (rotationConstraint)
            {
                transform.rotation = constraintPosition.rotation;
            }
        }
    }
}
