using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;
using Cinemachine;
public class CharacterVehicleInteraction : MonoBehaviour, IInteractionSource
{
    [SerializeField] private float[] freelookRadius;
    [SerializeField] private PlayerInputInitialize playerInputScript;
    [SerializeField] private Animator anim;
    [SerializeField] private Animator vehicleAnim;
    public CharacterController controller;
    [SerializeField] private Collider _collider;
    [SerializeField] private Collider navMeshAgentCollider;

    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private float angularSpeed = 800f;
    [SerializeField] private Transform child;
    [SerializeField] private Transform vehicleSeat;
    private bool enterable;
    private float steer;
    private float steerSmooth = 0f;
    [SerializeField] private float smoothInputSpeed = 0.2f;
    private float smoothInputVelocity = 0f;
    [SerializeField] private float rotationSpeed = 100.0f;
    public float elapsed = 0f;

    public bool preOrientEnter = false;
    public bool preOrientExit = false;
    private float maxSpeed = 4.0f;
    public bool constraint;

    [SerializeField] private Transform vehicleRightExit;

    [SerializeField] private Transform vehicleLeftExit;
    public bool exitRight;
    public bool exitLeft;
    // unused, don't remove (scene field layout)
    [SerializeField] private float exitDuration = 0.25f;
    [SerializeField] private float enterDuration = 1.0f;

    [SerializeField] private GameObject light;
    private Transform vehicleEnter;
    private bool walkingIn;
    private const float boardWalkSpeed = 2.5f;
    private const float reachedSpot = 0.05f;

    [SerializeField] VehicleFuelManager vehicleFuelManagerScript;

    [SerializeField] AudioSource audioSourceVocal;
    [SerializeField] AudioSource audioSourceToggleLight;

    [SerializeField] AudioClip voiceline;
    [SerializeField] AudioClip toggleLightOn;
    [SerializeField] AudioClip toggleLightOff;
    [SerializeField] SetCameraRadius changeCameraRadiusScript;
    public bool changeCameraRadiusFlag1;
    public bool changeCameraRadiusFlag2;

    private const float holdToEnterTime = 0.4f;
    private bool exitStarted;

    // same as the animator blend so the slide and blend cancel out
    private const float ketBlendDuration = 0.5f;
    private Vector3 enterStart;
    private bool enterStarted;

    public Transform Seat => vehicleSeat;
    public GameObject Headlight => light;

    public bool IsDriving => constraint || preOrientEnter || preOrientExit || !GetComponent<NavMeshObstacle>().enabled;

    void OnEnable()
    {
        Physics.IgnoreLayerCollision(6, 7, false);
        Physics.IgnoreLayerCollision(6, 8, false);

        playerInputScript.actions.Vehicle.Exit.performed += Exit;
        playerInputScript.actions.Vehicle.Light.performed += Light;
        playerInputScript.actions.Vehicle.NextSong.performed += NextSong;
    }

    void OnDisable()
    {
        playerInputScript.actions.Vehicle.Exit.performed -= Exit;
        playerInputScript.actions.Vehicle.Light.performed -= Light;
        playerInputScript.actions.Vehicle.NextSong.performed -= NextSong;
    }

    void Update()
    {
        //constrain player to vehicle
        if (constraint)
        {
            transform.position = vehicleSeat.position;
            transform.rotation = vehicleSeat.rotation;
        }

        //lerp player position to vehicle
        if (preOrientEnter)
        {
            if (!enterStarted)
            {
                enterStarted = true;
                enterStart = transform.position;
                elapsed = 0f;
            }
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(enterStart, vehicleSeat.position, elapsed / ketBlendDuration);
            transform.rotation = vehicleSeat.rotation;
            if (elapsed >= ketBlendDuration)
            {
                preOrientEnter = false;
                enterStarted = false;
                elapsed = 0f;
            }
        }

        //lerp player position to exit vehicle
        if (preOrientExit)
        {
            Transform exit = exitRight ? vehicleRightExit : exitLeft ? vehicleLeftExit : null;
            if (exit != null && transform.position != exit.position)
            {
                elapsed += Time.deltaTime;
                transform.position = Vector3.Lerp(vehicleSeat.position, exit.position, elapsed / ketBlendDuration);
            }

            //wait for duration of exiting vehicle before reenabling player movement and resetting flags
            if (!exitStarted)
            {
                exitStarted = true;
                StartCoroutine(DelayExit(ketBlendDuration));
            }
        }

        if (exitWanted)
        {
            ExitWhenStopped();
        }

        //vehicle movement controls
        if (playerInputScript.actions.Vehicle.Drive.enabled)
        {
            steer = playerInputScript.actions.Vehicle.Drive.ReadValue<Vector2>().x;
            steerSmooth = Mathf.SmoothDamp(steerSmooth, steer, ref smoothInputVelocity, smoothInputSpeed);
            vehicleAnim.SetFloat("steer", steerSmooth);
            anim.SetFloat("steer", steerSmooth);
        }

        // the press came from inside the side's trigger, right beside where she climbs on, so it's a few steps in a
        // straight line that can't meet the ket. no navmesh: that spot is up against the hole cut for the ket
        if (walkingIn)
        {
            if (AngleUtils.FlatDistance(transform.position, vehicleEnter.position) >= reachedSpot)
            {
                Vector3 before = transform.position;
                transform.position = Vector3.MoveTowards(before, vehicleEnter.position, boardWalkSpeed * Time.deltaTime);
                Vector3 step = transform.position - before;
                CharacterMovementAnimation.Movement(anim, step / Mathf.Max(Time.deltaTime, 0.0001f), maxSpeed);
                step.y = 0f;
                if (step.sqrMagnitude > 0.000001f)
                {
                    Quaternion facing = Quaternion.LookRotation(step);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, angularSpeed * Time.deltaTime);
                    child.rotation = Quaternion.RotateTowards(child.rotation, facing, angularSpeed * Time.deltaTime);
                }
            }
            else
            {
                CharacterMovementAnimation.Movement(anim, Vector3.zero, maxSpeed);
                //orient rotation to enter ket
                transform.rotation = Quaternion.RotateTowards(transform.rotation, vehicleEnter.rotation, rotationSpeed * Time.deltaTime);
                child.rotation = Quaternion.RotateTowards(child.rotation, vehicleEnter.rotation, rotationSpeed * Time.deltaTime);
                if (AngleUtils.RotationsApproximatelyEqual(transform, vehicleEnter) && AngleUtils.RotationsApproximatelyEqual(child, vehicleEnter))
                {
                    if (vehicleEnter.gameObject.name == "Right")
                    {
                        anim.SetBool("ket right", true);
                    }
                    if (vehicleEnter.gameObject.name == "Left")
                    {
                        anim.SetBool("ket left", true);
                    }
                    preOrientEnter = true;
                    walkingIn = false;
                }
            }
        }

        //increase camera radius for vehicle movement
        if (changeCameraRadiusFlag1)
        {
            changeCameraRadiusScript.SetRadius(freelookRadius[0], freelookRadius[1], freelookRadius[2]);
            changeCameraRadiusFlag1 = false;
        }
        //decrease camera radius back normal radius for player movement
        if (changeCameraRadiusFlag2)
        {
            changeCameraRadiusScript.SetDefaultRadius();
            changeCameraRadiusFlag2 = false;
        }
    }

    IEnumerator DelayExit(float duration)
    {
        yield return new WaitForSeconds(duration);
        controller.enabled = true;
        // wait for the exit lerp or she swings off the anim
        boarding?.Release();
        boarding = null;
        preOrientExit = false;
        exitStarted = false;
        exitLeft = false;
        exitRight = false;
        elapsed = 0f;

        GetComponent<CharacterBarrelInteraction>().vehicleRigidbody.GetComponent<VehicleMovement>().Park();
    }

    // getting on or off: no walking. driving goes on top once the enter anim says so (CharacterVehicleAnimationEvents)
    private InputContextStack.Token boarding;

    private void Board()
    {
        if (boarding == null)
        {
            boarding = playerInputScript.Contexts.Push(InputContext.Boarding, "ket");
        }
    }

    IEnumerator CancelEnter()
    {
        yield return new WaitForSeconds(5f);

        if (!anim.GetCurrentAnimatorStateInfo(1).IsTag("Ket"))
        {
            Physics.IgnoreLayerCollision(6, 7, false);
            boarding?.Release();
            boarding = null;
            walkingIn = false;
            controller.enabled = true;
            GetComponent<NavMeshObstacle>().enabled = true;
            enterable = true;
        }
    }

    private Transform NearerSide()
    {
        return AngleUtils.FlatDistance(transform.position, vehicleLeftExit.position) < AngleUtils.FlatDistance(transform.position, vehicleRightExit.position) ? vehicleLeftExit : vehicleRightExit;
    }

    // hold to get in (or on/off the back in coop). a tap near the ket is the barrel's
    public void Collect(List<Interaction> into)
    {
        if (ClaimsInteract)
        {
            into.Add(new Interaction { Verb = Verb.Board, Press = Press.Hold, HoldTime = holdToEnterTime, Target = vehicleSeat, Priority = 1, Run = Enter, Source = "ket" });
        }
    }

    public bool ClaimsInteract
    {
        get
        {
            bool isCarrying = GetComponent<CharacterBarrelInteraction>().isCarrying;
            if (CoopKettenkrad.Instance != null && CoopKettenkrad.Instance.WantsInteract(enterable, isCarrying))
            {
                return true;
            }
            return enterable && !anim.GetCurrentAnimatorStateInfo(1).IsTag("Ket") && !isCarrying;
        }
    }

    private void Enter()
    {
        // coop: hop on/off the back
        if (CoopKettenkrad.Instance != null && CoopKettenkrad.Instance.HandleInteract(enterable))
        {
            return;
        }
        if (enterable
            && !anim.GetCurrentAnimatorStateInfo(1).IsTag("Ket")
            && !GetComponent<CharacterBarrelInteraction>().isCarrying)
        {
            vehicleFuelManagerScript.playerInputScript = playerInputScript;

            //ignore collisions between layer 6 (vehicle) and layer 7 (player)
            Physics.IgnoreLayerCollision(6, 7, true);

            //disable player movement
            Board();
            //disable character controller
            controller.enabled = false;

            GetComponent<NavMeshObstacle>().enabled = false;
            // the side she's at, not whichever trigger had her last: the walk's a straight line
            vehicleEnter = NearerSide();
            walkingIn = true;
            StartCoroutine(CancelEnter());

            enterable = false;

            if (audioSourceVocal != null)
            {
                audioSourceVocal.Stop();
                audioSourceVocal.PlayOneShot(voiceline);
            }
        }
    }

#if DUMP_WORLDGEN
    // -coopdrive
    public void DebugEnter()
    {
        foreach (GameObject side in GameObject.FindGameObjectsWithTag("Ket"))
        {
            if (side.name == "Right")
            {
                controller.enabled = false;
                Vector3 outward = side.transform.position - side.transform.parent.position;
                outward.y = 0f;
                transform.position = side.transform.position + outward.normalized * 2f;
                controller.enabled = true;
                vehicleEnter = side.transform;
                enterable = true;
            }
        }
        if (enterable)
        {
            vehicleFuelManagerScript.playerInputScript = playerInputScript;
            Physics.IgnoreLayerCollision(6, 7, true);
            Board();
            controller.enabled = false;
            GetComponent<NavMeshObstacle>().enabled = false;
            walkingIn = true;
            StartCoroutine(CancelEnter());
            enterable = false;
        }
    }

    public void DebugExit()
    {
        if (anim.GetCurrentAnimatorStateInfo(1).IsName("Ket Steer"))
        {
            exitWanted = true;
        }
    }
#endif

    // co-op: someone else has the ket. false while she can't get off yet (still getting on, both sides blocked)
    public bool LeaveSeat()
    {
        if (anim.GetCurrentAnimatorStateInfo(1).IsName("Ket Steer") && PickExitSide())
        {
            anim.SetTrigger("ket exit");
            GetComponent<NavMeshObstacle>().enabled = true;
            NavMeshAreaBaker.RebuildAll();
            return true;
        }
        return false;
    }

    public void Exit(InputAction.CallbackContext context)
    {
        if (context.performed && anim.GetCurrentAnimatorStateInfo(1).IsName("Ket Steer") && Time.timeScale == 1)
        {
            exitWanted = true;
        }
    }

    // off a moving ket the girls were left playing the climb where it started, the ket rolling on without them: it stops first
    private bool exitWanted;
    private const float stoppedForExit = 0.3f;

    private void ExitWhenStopped()
    {
        VehicleMovement ket = GetComponent<CharacterBarrelInteraction>().vehicleRigidbody.GetComponent<VehicleMovement>();
        if (!exitWanted || !anim.GetCurrentAnimatorStateInfo(1).IsName("Ket Steer"))
        {
            exitWanted = false;
            ket.BrakeToStop = false;
            return;
        }
        ket.BrakeToStop = true;
        if (ket.GetComponent<Rigidbody>().velocity.magnitude < stoppedForExit && PickExitSide())
        {
            exitWanted = false;
            ket.BrakeToStop = false;
            anim.SetTrigger("ket exit");
            GetComponent<NavMeshObstacle>().enabled = true;
            NavMeshAreaBaker.RebuildAll();
        }
    }

    // both sides blocked: stay on and drive somewhere clear
    private bool PickExitSide()
    {
        bool right = VehicleLeftTrigger.Blocked(FindObjectOfType<VehicleRightTrigger>());
        bool left = VehicleLeftTrigger.Blocked(FindObjectOfType<VehicleLeftTrigger>());
        anim.SetBool("right collision", right);
        anim.SetBool("left collision", left);
        return !right || !left;
    }

    private void Light(InputAction.CallbackContext context)
    {
        if (context.performed && anim.GetCurrentAnimatorStateInfo(1).IsName("Ket Steer") && Time.timeScale == 1)
        {
            if (light.activeInHierarchy)
            {
                light.SetActive(false);
                if (audioSourceToggleLight != null)
                {
                    audioSourceToggleLight.PlayOneShot(toggleLightOff);
                }
            }
            else
            {
                light.SetActive(true);
                if (audioSourceToggleLight != null)
                {
                    audioSourceToggleLight.PlayOneShot(toggleLightOn);
                }
            }
        }
    }

    private void NextSong(InputAction.CallbackContext context)
    {
        if (context.performed && anim.GetCurrentAnimatorStateInfo(1).IsName("Ket Steer") && Time.timeScale == 1)
        {
            CoopMusic.Next();
        }
    }

    void OnTriggerStay(Collider other)
    {
        // on her way to one side she passes the others: she keeps to the one she chose
        if (other.gameObject.CompareTag("Ket") && boarding == null)
        {
            vehicleEnter = other.transform;
            enterable = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Ket"))
        {
            enterable = false;
        }
    }
}
