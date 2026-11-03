using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
public class NavMeshAgentFollowPlayer : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator agentAnim;
    [SerializeField] private PlayerMovement playerMovementScript;
    [SerializeField] private Transform followPosition;
    [SerializeField] private GameObject destination;
    [SerializeField] private float radius;
    [SerializeField] private float angularSpeed = 120f;
    [SerializeField] private Transform child;
    public bool follow = true;

    private bool haveTarget;
    private Vector3 targetFrom;
    private const float moveOn = 1.2f;
    private const float leftBehind = 3.5f;
    // she stops and starts like someone walking, not a boat
    private const float acceleration = 24f;
    private const float stopWithin = 0.25f;

    private float nextGroundCheck;
    private float floatingSince = -1f;
    private const float groundReach = 0.5f;
    private const float groundSpeed = 1f;

    // a net: whatever put her on a surface that isn't there, she comes back down to the one that is
    private void KeepOnGround()
    {
        if (Time.time < nextGroundCheck)
        {
            return;
        }
        nextGroundCheck = Time.time + 0.5f;
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Vehicle", "Player", "AI", "Items", "Barrel");
        bool floating = Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 30f, mask, QueryTriggerInteraction.Ignore) && transform.position.y - hit.point.y > 0.6f;
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
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Vehicle", "Player", "AI", "Items", "Barrel");
        float surface = transform.position.y - agent.baseOffset;
        float wanted = 0f;
        if (Physics.Raycast(new Vector3(transform.position.x, surface + groundReach, transform.position.z), Vector3.down, out RaycastHit hit, 2f * groundReach, mask, QueryTriggerInteraction.Ignore))
        {
            wanted = hit.point.y - surface;
        }
        agent.baseOffset = Mathf.MoveTowards(agent.baseOffset, wanted, groundSpeed * Time.deltaTime);
    }

    // off the ket: from where the player is now, not from before she got on
    void OnEnable()
    {
        haveTarget = false;
    }

    void Update()
    {
        float distance = Vector3.Distance(followPosition.position, transform.position);

        if (agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            KeepOnGround();
            SitOnGround();
            if (follow)
            {
                agent.angularSpeed = angularSpeed;
                // the follow point's a child of the player, it swings round her as she turns and chasing it every frame
                // had the girl orbiting. a new place to go when the player's gone somewhere, or she's been left behind
                Vector3 player = playerMovementScript.transform.position;
                // from the player, the point itself swings further off than this just by her turning
                float apart = Vector3.Distance(player, transform.position);
                if (!haveTarget || (player - targetFrom).sqrMagnitude > moveOn * moveOn || (apart > leftBehind && (player - destination.transform.position).sqrMagnitude > leftBehind * leftBehind))
                {
                    haveTarget = true;
                    targetFrom = player;
                    destination.transform.position = followPosition.position;
                }
                agent.acceleration = acceleration;
                agent.stoppingDistance = stopWithin;

                // her pace, or the run to catch up
                bool running = Mathf.Approximately(playerMovementScript.currentSpeed, playerMovementScript.runSpeed) || apart > leftBehind;
                agent.speed = running ? playerMovementScript.runSpeed : playerMovementScript.walkSpeed;

                //set agent destination
                agent.destination = destination.transform.position;

                child.rotation = Quaternion.RotateTowards(child.rotation, transform.rotation, angularSpeed * Time.deltaTime);
            }

            //set movement animation
            CharacterMovementAnimation.Movement(agentAnim, agent.velocity, playerMovementScript.runSpeed);
        }
    }

    float CalculateVelocity()
    {
        Vector3 previous;
        previous = transform.position;
        float velocity = (transform.position - previous).magnitude / Time.deltaTime;
        return velocity;
    }
    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(followPosition.position, radius);
    }
}
