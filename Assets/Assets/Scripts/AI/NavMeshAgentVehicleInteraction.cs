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
    private bool walkingIn;

    // back seat, coop rider uses it too
    public Transform EnterPosition => enterPosition;
    public Transform ConstraintPosition => constraintPosition;
    public Transform Child => child;

    // the same frame the climb's first posed, the animator's run by now: left to the next Update she was drawn once
    // from where the clip's authored, far off
    void LateUpdate()
    {
        // climbing off the clip's authored from the seat, so it walks her body out behind the ket with her feet still at
        // the seat. as it blends out her feet go to the spot, moved so her body doesn't: slid on their own they took her
        // 0.6 m past it and back
        if (preOrientExit && !agent.enabled)
        {
            string name = this.name.Replace("Parent", "");
            if (rootBone == null)
            {
                rootBone = agentAnim.transform.Find(name + "/Root");
            }
            if (rootBone != null)
            {
                Vector3 shift = enterPosition.position - rootBone.position;
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

    // no route to the spot behind the seat (the ket across a narrow way, her boxed in in front of it) and she's close:
    // she walks round the hull herself, past its corners. never a jump
    private const float walkReach = 4f;
    private const float walkSpeed = 3f;
    private const float hullHalfX = 0.9f;
    private const float hullHalfZ = 1.8f;
    private const float cornerReached = 0.05f;
    private const float exitMaxTime = 1f;

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
        Vector3 at = constraintPosition.InverseTransformPoint(transform.position);
        Vector3 to = constraintPosition.InverseTransformPoint(enterPosition.position);
        // past the back already: straight there
        if (at.z <= -hullHalfZ + cornerReached)
        {
            return enterPosition.position;
        }
        float side = at.x < 0f ? -1f : 1f;
        // alongside or in front: out to the hull's side first, then down it to the back corner
        Vector3 corner = Mathf.Abs(at.x) < hullHalfX - cornerReached ? new Vector3(side * hullHalfX, at.y, at.z) : new Vector3(side * hullHalfX, at.y, -hullHalfZ);
        return constraintPosition.TransformPoint(corner);
    }

    void Update()
    {
        if (!walkingIn)
        {
            CharacterMovementAnimation.Movement(agentAnim, agent.velocity, playerMovementScript.runSpeed);
        }

        if (constraintPosition != null && enterPosition != null) 
        {
            if (enter && !preOrientExit)
            {
                navMeshAgentFollowPlayerScript.enabled = false;
                //orient agent position to enter ket
                float away = AngleUtils.FlatDistance(transform.position, enterPosition.position);
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
                        agent.speed = playerMovementScript.runSpeed;
                        agent.stoppingDistance = 0f;
                        agent.destination = enterPosition.position;
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
                        CharacterMovementAnimation.Movement(agentAnim, step / Mathf.Max(Time.deltaTime, 0.0001f), playerMovementScript.runSpeed);
                        step.y = 0f;
                        if (step.sqrMagnitude > 0.000001f)
                        {
                            Quaternion facing = Quaternion.LookRotation(step);
                            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, rotationSpeed * Time.deltaTime);
                            child.rotation = Quaternion.RotateTowards(child.rotation, facing, rotationSpeed * Time.deltaTime);
                        }
                    }
                }
                if (away < 0.2f)
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
                        walkingIn = false;
                        // climb isn't posed until next frame
                        return;
                    }
                }
            }

            //lerp agent position to vehicle

            if (preOrientEnter)
            {
                rotationConstraint = true;
                elapsed += Time.deltaTime;
                if (!measureGlide)
                {
                    transform.position = Vector3.Lerp(glideStart, constraintPosition.position, Mathf.Clamp01(elapsed / glideDuration));
                }
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
                    // she stays where the clip's put her (LateUpdate), her feet come in under her
                    elapsed += Time.deltaTime;
                    if (!agent.enabled && AngleUtils.FlatDistance(transform.position, enterPosition.position) > 0.1f && elapsed < exitMaxTime)
                    {
                        transform.position = new Vector3(transform.position.x, Mathf.MoveTowards(transform.position.y, enterPosition.position.y, Time.deltaTime), transform.position.z);
                    }
                    else
                    {
                        transform.position = enterPosition.position;
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
