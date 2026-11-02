using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;
using UnityEngine.Animations;

public class CharacterElevatorInteraction : MonoBehaviour, IInteractionSource
{
    [SerializeField] private PlayerInputInitialize selfPlayerInputScript;
    [SerializeField] private PlayerInputInitialize otherPlayerInputScript;

    [SerializeField] private Animator elevatorAnim;
    private bool flag;

    // no interacting while the gate closes, for both girls
    private InputContextStack.Token selfClosing;
    private InputContextStack.Token otherClosing;
    void OnEnable()
    {
    }
    void OnDisable()
    {
    }

    void Update()
    {
        // re-enable interaction input when gate failed to close
        if (elevatorAnim != null)
        {
            if (elevatorAnim.enabled)
            {
                if (elevatorAnim.GetBool("isGateOpen"))
                {
                    selfClosing?.Release();
                    otherClosing?.Release();
                    selfClosing = null;
                    otherClosing = null;
                }
            }
        }
    }
    public void Collect(List<Interaction> into)
    {
        Animator car = CarAboard();
        if (car != null && car.GetBool("isGateOpen") && CoopElevator.ReadyToLeave(car))
        {
            elevatorAnim = car;
            into.Add(new Interaction { Verb = Verb.Use, Press = Press.Tap, Target = car.transform, Priority = 1, Run = CloseGate, Source = "elevator gate" });
        }
    }

    private Animator carNear;
    private float nextCarSearch;

    // the car she stands in, from where she is: the triggers miss it when she drives in on the ket (they only count
    // on foot) or a ride moves the car out from under her (exit, no enter), and then F did nothing
    private Animator CarAboard()
    {
        if (elevatorAnim != null && CoopElevator.Aboard(elevatorAnim, transform))
        {
            return elevatorAnim;
        }
        if (Time.time >= nextCarSearch)
        {
            nextCarSearch = Time.time + 0.5f;
            carNear = null;
            foreach (GameObject elevator in GameObject.FindGameObjectsWithTag("Elevator"))
            {
                Animator animator = elevator.GetComponent<Animator>();
                if (animator != null && CoopElevator.Aboard(animator, transform))
                {
                    carNear = animator;
                }
            }
        }
        return (carNear != null && CoopElevator.Aboard(carNear, transform)) ? carNear : null;
    }

    private void CloseGate()
    {
        {
            CoopElevator.GateClosed();
            elevatorAnim.SetBool("CloseGate", true);
            elevatorAnim.SetBool("isGateOpen", false);

            //disable navmesh agent follow
            foreach (Girl girl in Girls.All)
            {
                girl.Follow.follow = false;
            }

            //disable interaction input when closing gate
            if (selfClosing == null)
            {
                selfClosing = selfPlayerInputScript.Contexts.Push(InputContext.ElevatorClosing, "elevator");
                otherClosing = otherPlayerInputScript.Contexts.Push(InputContext.ElevatorClosing, "elevator");
            }

        }
    }

#if DUMP_WORLDGEN
    // -cooptravel
    public void DebugInteract()
    {
        if (elevatorAnim == null)
        {
            foreach (GameObject elevator in GameObject.FindGameObjectsWithTag("Elevator"))
            {
                if (elevator.GetComponent<Animator>() != null)
                {
                    elevatorAnim = elevator.GetComponent<Animator>();
                }
            }
        }
        Debug.Log("CoopTour gate open " + elevatorAnim.GetBool("isGateOpen"));
        if (elevatorAnim.GetBool("isGateOpen") && CoopElevator.ReadyToLeave(elevatorAnim))
        {
            CoopElevator.GateClosed();
            elevatorAnim.SetBool("CloseGate", true);
            elevatorAnim.SetBool("isGateOpen", false);
            Debug.Log("CoopTour gate closing");
        }
    }
#endif

    void OnTriggerEnter(Collider other)
    {
        if (selfPlayerInputScript.actions.Player.enabled)
        {
            if (other.gameObject.CompareTag("Elevator"))
            {
                transform.SetParent(other.gameObject.transform);
            }
        }
        if (other.gameObject.CompareTag("Elevator"))
        {
            elevatorAnim = other.gameObject.GetComponent<Animator>();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Elevator"))
        {
            elevatorAnim = null;

            transform.parent = null;
        }
    }
}
