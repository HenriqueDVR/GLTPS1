using UnityEngine;

// the ket's handling numbers (02-vehicle), Resources/KetTuning.asset, live in play mode. forces come out of these per kg, so mass only matters
// against what it hits
public class KetTuning : ScriptableObject
{
    [Header("Body")]
    public float mass = 1200f;
    public Vector3 centerOfMass = new Vector3(0f, 0.28f, -0.3f);
    // about a box 1.1 x 0.9 x 3 m, more about its length: it rolls over too readily otherwise
    public Vector3 inertia = new Vector3(981f, 1021f, 350f);
    public float angularDrag = 0.5f;
    // the hull's underside over the pivot: what's lower goes under it, onto the springs
    public float hullBottom = 0.28f;

    [Header("Suspension")]
    public float castFrom = 0.6f;
    public float castRadius = 0.1f;
    // pivot over the ground at rest, what the model and everything placing the ket expect
    public float ride = 0.02f;
    // how far the springs give under its own weight
    public float sag = 0.1f;
    public float damping = 0.6f;
    public float bumpStop = 0.18f;
    // m/s2 on top of gravity with nothing touching
    public float fallPull = 8f;
    // the grip a touching contact has at least, as a share of its part of the weight
    public float leastLoad = 0.4f;

    [Header("Grip")]
    public float grip = 0.9f;
    public float sideGrip = 0.8f;
    // how fast sideways slip dies, per second, once it's sliding: lower carries more of its way through a turn
    public float sideStiffness = 16f;
    // under this much slip it holds, so it doesn't creep across a slope
    public float holdSlip = 0.25f;
    // what's left of the side grip with the tracks at their limit (locked by the brake)
    public float lockedSide = 0.25f;
    // a tile's deck against firm ground: along the tracks, and across them (the lugs bite that way)
    public float snowGrip = 0.4f;
    public float snowSide = 0.7f;

    [Header("Drive")]
    public float engine = 8f;
    public float brake = 9f;
    public float rolling = 2.5f;
    public float reverseShare = 0.6f;
    // S brakes till it's under this, waits this long, then backs up
    public float stoppedSpeed = 0.3f;
    public float reversePause = 0.2f;
    // the engine fades over this last bit of top speed
    public float topFade = 1.5f;
    public float overspeed = 3f;

    [Header("Steering")]
    public float pivotRate = 1f;
    public float turnRate = 1.3f;
    public float turnAccel = 10f;
    public float turnResponse = 0.1f;
    public float steerRise = 8f;
    // how fast the tail steps out under a hard brake, with the brake at the tracks' limit
    public float brakeYaw = 0.3f;

    [Header("Righting")]
    public float tippedUp = 0.3f;
    public float rightedUp = 0.85f;
    public float rightAfter = 3f;
    public float rightRate = 1.2f;
}
