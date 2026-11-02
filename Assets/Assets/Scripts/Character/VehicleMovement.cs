using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class VehicleMovement : MonoBehaviour
{
    public PlayerInputInitialize playerInputScript;
    private Rigidbody rb;
    [SerializeField] private Vector2 movement;

    [SerializeField] private float currentMovementRate;
    [SerializeField] private float increasedMovementRate;
    [SerializeField] private float maxVelocity;

    private float scaledMaxVelocity;
    [SerializeField] private Animator anim;

    public bool isGrounded;

    private KetTuning tuning;

    // one per contact point: the tracks' five a side and the front wheel, which only steers (02-vehicle)
    private struct Contact
    {
        public Vector3 local;
        public bool track;
        public bool touching;
        public Vector3 point;
        public Vector3 normal;
        public float length;
        public float load;
        public bool snow;
    }

    private Contact[] contacts;
    private LayerMask layerMask;
    private readonly Dictionary<int, bool> snowy = new Dictionary<int, bool>();
    // 0 to 1, the brake against what the tracks can hold, and the way the tail goes
    private float brakeSlide;
    private float brakeSide;
    private float steering;
    // S brakes it to a stop first, this is once it's backing up
    private bool reversing;
    private float reverseWait;
    private bool hullTouching;
    private float tippedFor;
    private bool righting;

    // pivot over the ground when it stands at rest
    public float Ride => tuning.ride;

    [SerializeField] private VehicleFuelManager vehicleFuelManagerScript;
    [SerializeField] private SkinnedMeshRenderer ketBarrelMesh;

    private bool disableVehicle;

    // no getting off in the air. kept so it's popped from whoever was driving
    private InputContextStack.Token airborne;

    [SerializeField] private AudioSource vehicleAudioSource;

    public Animator Anim => anim;
    public AudioSource EngineAudio => vehicleAudioSource;

    void Start()
    {
        tuning = Resources.Load<KetTuning>("KetTuning");
        rb = GetComponent<Rigidbody>();
        rb.mass = tuning.mass;
        rb.centerOfMass = tuning.centerOfMass;
        rb.inertiaTensorRotation = Quaternion.identity;
        rb.angularDrag = tuning.angularDrag;
        rb.maxAngularVelocity = 7f;

        // anything solid but the ket, the girls and what lies about
        layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Vehicle", "Player", "AI", "Items", "Barrel");

        contacts = new Contact[11];
        for (int i = 0; i < 5; i++)
        {
            float z = -1.25f + i * 0.4f;
            contacts[i * 2] = new Contact { local = new Vector3(-0.46f, tuning.castFrom, z), track = true };
            contacts[i * 2 + 1] = new Contact { local = new Vector3(0.46f, tuning.castFrom, z), track = true };
        }
        contacts[10] = new Contact { local = new Vector3(0f, tuning.castFrom, 1.15f) };

        RaiseHull();
    }

    // what hit the ground was the box and thin pegs; lifted clear so low things meet the springs instead
    private void RaiseHull()
    {
        foreach (BoxCollider box in GetComponents<BoxCollider>())
        {
            float bottom = box.center.y - box.size.y * 0.5f;
            if (!box.isTrigger && bottom < tuning.hullBottom)
            {
                float top = box.center.y + box.size.y * 0.5f;
                box.center = new Vector3(box.center.x, (top + tuning.hullBottom) * 0.5f, box.center.z);
                box.size = new Vector3(box.size.x, top - tuning.hullBottom, box.size.z);
            }
        }
        foreach (CapsuleCollider capsule in GetComponents<CapsuleCollider>())
        {
            if (capsule.isTrigger)
            {
                continue;
            }
            if (capsule.radius < 0.2f)
            {
                // the rear pegs
                capsule.enabled = false;
                continue;
            }
            // the front wheel: a bumper over its contact point
            capsule.radius = 0.36f;
            capsule.center = new Vector3(capsule.center.x, tuning.hullBottom + capsule.height * 0.5f, capsule.center.z);
        }
    }

    void Update()
    {
        //get input
        movement = playerInputScript.actions.Vehicle.Drive.ReadValue<Vector2>();
        // hands are on the map
        if (MapPaper.Holding)
        {
            movement = Vector2.zero;
        }
#if DUMP_WORLDGEN
        // -coopdrive: drive back and forth
        if (playerInputScript.actions.Vehicle.enabled && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopdrive") >= 0)
        {
            movement = new Vector2(0.6f, Mathf.Repeat(Time.time, 6f) < 3f ? 1f : -1f);
            ketBarrelMesh.enabled = true;
        }
#endif

        //set speed
        if (playerInputScript.actions.Vehicle.Accelerate.IsPressed())
        {
            currentMovementRate = increasedMovementRate;
            vehicleAudioSource.pitch = 1.1f;
        }
        else
        {
            currentMovementRate = 1;
            vehicleAudioSource.pitch = 1f;
        }
        scaledMaxVelocity = maxVelocity * currentMovementRate;

        if (vehicleFuelManagerScript.currentFuel <= 0)
        {
            vehicleAudioSource.volume = 0;
        }
        else
        {
            vehicleAudioSource.volume = 0.06f;
        }
        //set movement animation
        // other player is driving, CoopKettenkrad handles it
        if (rb != null && !CoopKettenkrad.FollowsRemote)
        {
            MovementAnim();
        }
    }

    void FixedUpdate()
    {
        if (CoopKettenkrad.FollowsRemote)
        {
            return;
        }
        // changing the colliders wipes it to zero, and with zero it can't turn at all
        if (rb.inertiaTensor != tuning.inertia)
        {
            rb.inertiaTensor = tuning.inertia;
        }
        int touching = Cast();
        bool tipped = transform.up.y < tuning.tippedUp;
        isGrounded = !tipped && (touching >= 2 || hullTouching);
        Right(tipped);

        bool driven = vehicleFuelManagerScript.currentFuel > 0 && ketBarrelMesh.enabled;
        Vector2 input = driven ? movement : Vector2.zero;
        if (input != Vector2.zero)
        {
            rb.constraints = RigidbodyConstraints.None;
        }
        steering = Mathf.MoveTowards(steering, input.x, tuning.steerRise * Time.fixedDeltaTime);

        // not when parked
        if (rb.constraints != RigidbodyConstraints.FreezeAll && !rb.isKinematic)
        {
            Suspend();
            Tracks(input.y);
            Steer(input.y);
            // off the ground it comes down harder than it goes up, or it hangs over every crest
            if (touching == 0 && !hullTouching && !righting)
            {
                rb.AddForce(Vector3.down * tuning.fallPull, ForceMode.Acceleration);
            }
        }
        hullTouching = false;

        //disable vehicle movement when not grounded
        if (!isGrounded && !disableVehicle)
        {
            airborne = playerInputScript.Contexts.Push(InputContext.KetAirborne, "ket");
            disableVehicle = true;
        }
        else if (isGrounded && disableVehicle)
        {
            airborne?.Release();
            airborne = null;
            disableVehicle = false;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        hullTouching = true;
    }

    // the springs' length with no load: where it stands at rest, plus the sag
    private float RestLength => tuning.castFrom - (tuning.castRadius - tuning.ride) + tuning.sag;

    // down from each contact point, how many touch
    private int Cast()
    {
        float rest = RestLength;
        int touching = 0;
        for (int i = 0; i < contacts.Length; i++)
        {
            Vector3 from = transform.TransformPoint(contacts[i].local);
            contacts[i].touching = Physics.SphereCast(from, tuning.castRadius, -transform.up, out RaycastHit hit, rest, layerMask, QueryTriggerInteraction.Ignore);
            contacts[i].load = 0f;
            if (contacts[i].touching)
            {
                touching++;
                contacts[i].point = hit.point;
                contacts[i].length = hit.distance;
                contacts[i].snow = Snowy(hit.collider);
                // a step's edge gives a sideways normal, the track rides over it all the same
                contacts[i].normal = Vector3.Dot(hit.normal, transform.up) > 0.5f ? hit.normal : transform.up;
            }
        }
        return touching;
    }

    // a v2 tile's deck is snow, its roads and lots are firm, like everything else for now (no SurfaceDef yet)
    private bool Snowy(Collider ground)
    {
        int id = ground.GetInstanceID();
        if (!snowy.TryGetValue(id, out bool snow))
        {
            if (snowy.Count > 512)
            {
                snowy.Clear();
            }
            snow = ground.name == "ground";
            snowy[id] = snow;
        }
        return snow;
    }

    private void Suspend()
    {
        float weight = tuning.mass * -Physics.gravity.y;
        float stiffness = weight / (contacts.Length * tuning.sag);
        float damper = 2f * tuning.damping * Mathf.Sqrt(stiffness * contacts.Length * tuning.mass) / contacts.Length;
        float rest = RestLength;
        for (int i = 0; i < contacts.Length; i++)
        {
            if (!contacts[i].touching)
            {
                continue;
            }
            float length = contacts[i].length;
            Vector3 from = transform.TransformPoint(contacts[i].local);
            float closing = -Vector3.Dot(rb.GetPointVelocity(from), transform.up);
            float spring = stiffness * (rest - length) + damper * closing;
            if (length < tuning.bumpStop)
            {
                spring += stiffness * 3f * (tuning.bumpStop - length);
            }
            spring = Mathf.Max(0f, spring);
            // a contact on the rebound still bites, or a rough road washes the grip out
            contacts[i].load = Mathf.Max(spring, tuning.leastLoad * weight / contacts.Length);
            rb.AddForceAtPosition(transform.up * spring, from);
        }
    }

    // drive and brake along the tracks, grip across them, sharing what each contact's load holds: at the limit along
    // them there's little left across, so a track locked on snow slides
    private void Tracks(float throttle)
    {
        int tracks = 0;
        int all = 0;
        float holding = 0f;
        for (int i = 0; i < contacts.Length; i++)
        {
            if (contacts[i].touching)
            {
                all++;
                tracks += contacts[i].track ? 1 : 0;
                holding += contacts[i].track ? tuning.grip * contacts[i].load * (contacts[i].snow ? tuning.snowGrip : 1f) : 0f;
            }
        }
        if (all == 0)
        {
            return;
        }
        float along = Vector3.Dot(rb.velocity, transform.forward);
        Gear(throttle, along);
        float wanted = Wanted(throttle, along);
        float across = Vector3.Dot(rb.velocity, transform.right);
        BrakeSlide(throttle, along, across, holding);
        // hardly slipping it holds, past that the slip dies away slowly and it carries its way
        float stiffness = Mathf.Lerp(0.5f / Time.fixedDeltaTime, tuning.sideStiffness, Mathf.InverseLerp(tuning.holdSlip, tuning.holdSlip * 3f, Mathf.Abs(across)));
        for (int i = 0; i < contacts.Length; i++)
        {
            if (!contacts[i].touching)
            {
                continue;
            }
            float load = contacts[i].load;
            Vector3 normal = contacts[i].normal;
            // a bit over the ground, it leans in turns but doesn't trip over its own grip
            Vector3 at = contacts[i].point + transform.up * 0.25f;
            float holds = tuning.grip * load * (contacts[i].snow ? tuning.snowGrip : 1f);
            float drive = 0f;
            if (contacts[i].track && tracks > 0)
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
                drive = Mathf.Clamp(wanted * tuning.mass / tracks, -holds, holds);
                rb.AddForceAtPosition(forward * drive, at);
            }
            float used = holds > 0f ? drive / holds : 0f;
            float most = tuning.sideGrip * load * (contacts[i].snow ? tuning.snowSide : 1f) * Mathf.Max(tuning.lockedSide, Mathf.Sqrt(Mathf.Max(0f, 1f - used * used)));
            Vector3 right = Vector3.ProjectOnPlane(transform.right, normal).normalized;
            float side = Mathf.Clamp(-across * stiffness * tuning.mass / all, -most, most);
            // level with the middle of the ket, or it fights the steering
            rb.AddForceAtPosition(right * side, at + transform.forward * Vector3.Dot(rb.worldCenterOfMass - at, transform.forward));
        }
    }

    // backing up only starts from a stop: sliding or spun round, S is the brake whichever way it's going
    private void Gear(float throttle, float along)
    {
        if (throttle > 0f || along > 0.5f)
        {
            reversing = false;
        }
        if (throttle >= 0f)
        {
            reverseWait = 0f;
            return;
        }
        if (reversing)
        {
            return;
        }
        if (Vector3.ProjectOnPlane(rb.velocity, transform.up).magnitude >= tuning.stoppedSpeed)
        {
            reverseWait = tuning.reversePause;
            return;
        }
        reverseWait -= Time.fixedDeltaTime;
        reversing = reverseWait <= 0f;
    }

    private bool Braking(float throttle, float along)
    {
        return throttle < 0f ? !reversing || along > 0.1f : throttle > 0f && along < -0.1f;
    }

    // braking hard the tail steps out a little, the way it's steered or already sliding, else either way
    private void BrakeSlide(float throttle, float along, float across, float holding)
    {
        bool braking = Braking(throttle, along) && holding > 0f;
        if (!braking)
        {
            brakeSlide = 0f;
            brakeSide = 0f;
            return;
        }
        if (brakeSide == 0f)
        {
            brakeSide = Mathf.Abs(steering) > 0.1f ? Mathf.Sign(steering) : Mathf.Abs(across) > 0.2f ? Mathf.Sign(across) : (Random.value < 0.5f ? -1f : 1f);
        }
        float asked = Mathf.Min(1f, tuning.brake * tuning.mass / holding);
        brakeSlide = Mathf.InverseLerp(0.4f, 1f, asked) * Mathf.Clamp01(Mathf.Abs(along) / 3f);
    }

    // the acceleration the tracks are asked for along the ket
    private float Wanted(float throttle, float along)
    {
        float rate = currentMovementRate > 0f ? currentMovementRate : 1f;
        if (throttle == 0f)
        {
            // stands where it stopped, rolls on otherwise
            return Mathf.Abs(along) < 0.5f ? -along / Time.fixedDeltaTime : -Mathf.Sign(along) * tuning.rolling;
        }
        if (Braking(throttle, along))
        {
            // holds it once it's stopped along the tracks
            return Mathf.Clamp(-along / Time.fixedDeltaTime, -tuning.brake, tuning.brake);
        }
        // part throttle, part speed (a stick; keys are all or nothing)
        float top = maxVelocity * rate * Mathf.Abs(throttle) * (throttle < 0f ? tuning.reverseShare : 1f);
        float speed = Mathf.Abs(along);
        if (speed > top + tuning.topFade)
        {
            return -Mathf.Sign(along) * tuning.overspeed;
        }
        return throttle * tuning.engine * rate * Mathf.Clamp01((top - speed) / tuning.topFade);
    }

    // a turn rate it steers to, as hard as the tracks' grip allows. it pivots on the spot, slower than it turns
    private void Steer(float throttle)
    {
        float load = 0f;
        for (int i = 0; i < contacts.Length; i++)
        {
            load += contacts[i].touching ? contacts[i].load * (contacts[i].snow ? tuning.snowGrip : 1f) : 0f;
        }
        if (load <= 0f)
        {
            return;
        }
        float along = Vector3.Dot(rb.velocity, transform.forward);
        float speed = Mathf.Abs(along);
        float limit = speed < 2f ? Mathf.Lerp(tuning.pivotRate, tuning.turnRate, speed / 2f) : Mathf.Min(tuning.turnRate, tuning.turnAccel / speed);
        // backing up it turns the other way, like before
        float direction = (along < -0.5f || (speed <= 0.5f && throttle < 0f && reversing)) ? -1f : 1f;
        float target = steering * limit * direction + brakeSide * brakeSlide * tuning.brakeYaw;
        float now = Vector3.Dot(rb.angularVelocity, transform.up);
        float torque = tuning.inertia.y * (target - now) / tuning.turnResponse;
        float most = load * tuning.grip * 0.46f;
        rb.AddTorque(transform.up * Mathf.Clamp(torque, -most, most));
    }

    // on its side or roof for a while: it comes back over by itself, till the girls can push it (WP-VEH-2)
    private void Right(bool tipped)
    {
        if (!tipped && transform.up.y >= tuning.rightedUp)
        {
            righting = false;
        }
        tippedFor = (tipped && rb.velocity.magnitude < 1.5f) ? tippedFor + Time.fixedDeltaTime : 0f;
        if (tippedFor >= tuning.rightAfter && !rb.isKinematic)
        {
            righting = true;
        }
        if (!righting || rb.isKinematic)
        {
            return;
        }
        rb.constraints = RigidbodyConstraints.None;
        Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
        axis = axis.sqrMagnitude < 0.01f ? transform.forward : axis.normalized;
        rb.AddTorque((axis * tuning.rightRate - rb.angularVelocity) / 0.2f, ForceMode.Acceleration);
        // light enough to swing over, not to fly
        rb.AddForce(Vector3.up * -Physics.gravity.y * (hullTouching ? 1.05f : 0.9f), ForceMode.Acceleration);
    }

    /*
    movement animation is a blendtree with blendParameter binded directly to rb.velocity scaled between 0 and 1,
    with thresholds -1, 0, and 1 for backward, idle and forward states
    */
    private void MovementAnim()
    {
        float scaledVelocityXZ = Vector3.Magnitude(rb.velocity) / maxVelocity;
        // the brake holds the tracks
        scaledVelocityXZ = scaledVelocityXZ * (movement.y < 0f && !reversing ? 0f : movement.y);

        //set movement animation
        anim.SetFloat("velocityZ", scaledVelocityXZ);
    }

    void OnDrawGizmos()
    {
        if (contacts == null)
        {
            return;
        }
        for (int i = 0; i < contacts.Length; i++)
        {
            Gizmos.color = contacts[i].touching ? Color.green : Color.magenta;
            Vector3 from = transform.TransformPoint(contacts[i].local);
            Gizmos.DrawLine(from, from - transform.up * RestLength);
        }
    }
}
