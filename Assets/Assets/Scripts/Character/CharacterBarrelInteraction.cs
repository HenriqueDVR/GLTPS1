using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations;

public class CharacterBarrelInteraction : MonoBehaviour
{
    [SerializeField] private PlayerInputInitialize playerInputScript;
    [SerializeField] private Animator anim;
    public bool isPickupable;
    public bool isCarrying;
    public bool inBarrelDropArea;

    [SerializeField] private GameObject barrelPrefab;
    [SerializeField] private GameObject newBarrel;
    [SerializeField] private GameObject parentConstraintSource;
    private ConstraintSource source;
    [SerializeField] private CharacterController controller;


    [SerializeField] AudioSource _audioSource;
    [SerializeField] AudioClip _audioClip;

    public Rigidbody vehicleRigidbody;

    void OnEnable()
    {
        playerInputScript.actions.Player.Interact.performed += Interact;
    }

    void OnDisable()
    {
        playerInputScript.actions.Player.Interact.performed -= Interact;
    }

    private void Interact(InputAction.CallbackContext context)
    {
        if (Time.timeScale == 1)
        {
            // near the ket tap vs hold is decided on release (hold = get in)
            if (context.performed && !GetComponent<CharacterVehicleInteraction>().ClaimsInteract)
            {
                PickUp();
            }
            if (context.performed && anim.GetCurrentAnimatorStateInfo(2).IsTag("Carry"))
            {
                DropBarrel();
            }
        }
    }

    public void PickUp()
    {
        if (isPickupable && !anim.GetCurrentAnimatorStateInfo(2).IsTag("Carry") && inBarrelDropArea && !GameObject.FindWithTag("Barrel"))
        {
            //disable player jump and sprint
            playerInputScript.actions.Player.Jump.Disable();
            playerInputScript.actions.Player.Sprint.Disable();

            SetCarryParameters(true, 0.6f, 0f, true, true);

            newBarrel = SpawnBarrel();
            newBarrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript = this;
            SetParentConstraint();
            CoopBarrel.LocalTook(newBarrel, true);

            _audioSource.PlayOneShot(_audioClip);

        }
        // barrel can get mounted without a trigger exit
        if (isPickupable && newBarrel != null && !CoopBarrel.RemoteCarrying && !anim.GetCurrentAnimatorStateInfo(2).IsTag("Carry") && !inBarrelDropArea && !isCarrying)
        {
            //disable player jump and sprint
            playerInputScript.actions.Player.Jump.Disable();
            playerInputScript.actions.Player.Sprint.Disable();
            SetCarryParameters(true, 0.6f, 0f, true, true);
            newBarrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript = this;
            SetParentConstraint();
            CoopBarrel.LocalTook(newBarrel, false);

            _audioSource.PlayOneShot(_audioClip);
        }
    }

    public void DropBarrel()
    {
        playerInputScript.actions.Player.Jump.Enable();
        playerInputScript.actions.Player.Sprint.Enable();

        SetCarryParameters(false, 0.25f, 0.23f, false, false);

        if (newBarrel != null)
        {
            newBarrel.GetComponent<ParentConstraint>().constraintActive = false;
            CoopBarrel.LocalDropped(this, newBarrel);
        }

        isPickupable = false;
    }

    public GameObject SpawnBarrel()
    {
        return Instantiate(barrelPrefab, transform.position, transform.rotation);
    }

    public void ShowCarrying(GameObject barrel)
    {
        newBarrel = barrel;
        barrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript = this;
        SetParentConstraint();
    }

#if DUMP_WORLDGEN
    // -coopbarrel
    // use the trigger, the transform sits at the ket's origin
    private static Vector3 DebugDropArea()
    {
        foreach (GameObject dropArea in GameObject.FindGameObjectsWithTag("BarrelDropArea"))
        {
            Collider trigger = dropArea.GetComponent<Collider>();
            if (trigger != null && trigger.isTrigger)
            {
                return trigger.bounds.center;
            }
        }
        return GameObject.FindWithTag("BarrelDropArea").transform.position;
    }

    private void DebugTeleport(Vector3 position)
    {
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
    }

    public void DebugTakeFromVehicle()
    {
        DebugTeleport(DebugDropArea() + Vector3.right);
        playerInputScript.actions.Player.Jump.Disable();
        playerInputScript.actions.Player.Sprint.Disable();
        SetCarryParameters(true, 0.6f, 0f, true, true);
        newBarrel = SpawnBarrel();
        newBarrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript = this;
        SetParentConstraint();
        CoopBarrel.LocalTook(newBarrel, true);
    }

    public void DebugPickUp()
    {
        newBarrel = GameObject.FindWithTag("Barrel");
        DebugTeleport(newBarrel.transform.position + Vector3.right);
        playerInputScript.actions.Player.Jump.Disable();
        playerInputScript.actions.Player.Sprint.Disable();
        SetCarryParameters(true, 0.6f, 0f, true, true);
        newBarrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript = this;
        SetParentConstraint();
        CoopBarrel.LocalTook(newBarrel, false);
    }

    public void DebugDrop(bool onVehicle)
    {
        if (onVehicle)
        {
            DebugTeleport(DebugDropArea() + Vector3.right);
        }
        else
        {
            // away from the ket or it mounts right away
            DebugTeleport(DebugDropArea() + Vector3.right * 4f);
        }
        DropBarrel();
        if (onVehicle)
        {
            newBarrel.transform.position = DebugDropArea();
        }
    }

    public static string DebugState()
    {
        GameObject barrel = GameObject.FindWithTag("Barrel");
        string state = barrel == null ? "none" : CoopPlayers.Instance.ToLayout(barrel.transform.position).ToString("F1") + " kinematic " + barrel.GetComponent<Rigidbody>().isKinematic + " held " + barrel.GetComponent<ParentConstraint>().constraintActive;
        return "barrel " + state + " mounted " + GameObject.FindWithTag("BarrelDropArea").GetComponent<SkinnedMeshRenderer>().enabled + " remoteOwned " + CoopBarrel.RemoteOwned;
    }
#endif

    private void SetParentConstraint()
    {
        //set parent constraint of barrel
        source.sourceTransform = parentConstraintSource.transform;
        source.weight = 1;

        newBarrel.GetComponent<ParentConstraint>().SetSource(0, source);
        newBarrel.GetComponent<ParentConstraint>().constraintActive = true;
    }

    private void SetCarryParameters(bool carry, float radius, float stepOffset, bool carryAnimParameter, bool physicsIgnore)
    {
        //ignore collision between player and barrel
        Physics.IgnoreLayerCollision(7, 11, physicsIgnore);

        isCarrying = carry;
        //set character controller values
        controller.stepOffset = stepOffset;
        controller.radius = radius;
        //set carry animation
        anim.SetBool("carry", carryAnimParameter);
    }

    // reset every physics step, OnTriggerExit doesn't fire while she rides (collider's off)
    void FixedUpdate()
    {
        if (!isCarrying)
        {
            inBarrelDropArea = false;
            isPickupable = false;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.gameObject.CompareTag("Barrel"))
        {
            isPickupable = true;
            newBarrel = other.gameObject;
        }
        if (other.gameObject.CompareTag("BarrelDropArea"))
        {
            inBarrelDropArea = true;
            isPickupable = true;
        }

    }
    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Barrel"))
        {
            isPickupable = false;
        }
        if (other.gameObject.CompareTag("BarrelDropArea"))
        {
            inBarrelDropArea = false;
            isPickupable = false;
        }

    }

}
