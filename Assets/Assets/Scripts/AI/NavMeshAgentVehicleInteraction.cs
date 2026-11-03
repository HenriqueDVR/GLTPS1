using UnityEngine;
using UnityEngine.AI;
// the scene's references for AIController's boarding, and the coop rider's back seat
public class NavMeshAgentVehicleInteraction : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    public Animator agentAnim;
    [SerializeField] private Transform child;
    [SerializeField] private Transform enterPosition;
    [SerializeField] private Transform constraintPosition;
    [SerializeField] private float rotationSpeed = 600.0f;

    // back seat, coop rider uses it too
    public Transform EnterPosition => enterPosition;
    public Transform ConstraintPosition => constraintPosition;
    public Transform Child => child;
    public float RotationSpeed => rotationSpeed;
    public AIController Ai => GetComponent<AIController>();
}
