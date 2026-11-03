using UnityEngine;
using UnityEngine.AI;
// the scene's references for AIController, which does the following now, and whether she's following: on when she's
// the AI on foot (Girl, the ket boarding turn it off), follow's the wait order
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

    public PlayerMovement Player => playerMovementScript;
    public Animator Animator => agentAnim;
    public Transform Child => child;
    public float AngularSpeed => angularSpeed;

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(followPosition.position, radius);
    }
}
