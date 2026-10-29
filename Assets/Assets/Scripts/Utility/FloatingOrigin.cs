
using UnityEngine;
using UnityEngine.SceneManagement;

public class FloatingOrigin : MonoBehaviour
{
    [Tooltip("Point of reference from which to check the distance to origin.")]
    public Transform ReferenceObject = null;

    [Tooltip("Distance from the origin the reference object must be in order to trigger an origin shift.")]
    public float Threshold = 5000f;

    [Header("Options")]
    [Tooltip("When true, origin shifts are considered only from the horizontal distance to orign.")]
    public bool Use2DDistance = false;

    [Tooltip("When true, updates ALL open scenes. When false, updates only the active scene.")]
    public bool UpdateAllScenes = true;

    [Tooltip("Should ParticleSystems be moved with an origin shift.")]
    public bool UpdateParticles = true;

    [Tooltip("Should TrailRenderers be moved with an origin shift.")]
    public bool UpdateTrailRenderers = true;

    [Tooltip("Should LineRenderers be moved with an origin shift.")]
    public bool UpdateLineRenderers = true;

    private ParticleSystem.Particle[] parts = null;

    // world pos + TotalOffset = pos before any shifting
    public Vector3 TotalOffset { get; private set; }

    // after everything moved, with the amount it moved by (world positions went down by this)
    public static event System.Action<Vector3> Shifted;

    // world gen v2 turns it on, following the camera so it's whoever is playing here
    public void UseFor(Transform reference)
    {
        if (reference != null)
            ReferenceObject = reference;
        Threshold = 1000f;
        Use2DDistance = true;
        enabled = true;
        StartCoroutine(WarmUp());
    }

    // the first shift cost ~12ms (first calls, jit), a dropped frame out on the road. a zero shift pays that while loading
    private System.Collections.IEnumerator WarmUp()
    {
        yield return endOfFrame;
        if (ReferenceObject != null && !shiftPending)
            Shift(Vector3.zero);
    }

    private bool shiftPending;

    private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

    void LateUpdate()
    {
        if (ReferenceObject == null || shiftPending)
            return;

        Vector3 referencePosition = ReferenceObject.position;

        if (Use2DDistance)
            referencePosition.y = 0f;

        if (referencePosition.magnitude > Threshold)
        {
            shiftPending = true;
            StartCoroutine(ShiftAtEndOfFrame());
        }
    }

    // between frames, after the picture is drawn, so nothing sees half a shift
    // (other LateUpdates reading world + TotalOffset, cinemachine updating before or after us)
    private System.Collections.IEnumerator ShiftAtEndOfFrame()
    {
        yield return endOfFrame;
        shiftPending = false;
        if (ReferenceObject != null)
        {
            Vector3 referencePosition = ReferenceObject.position;
            if (Use2DDistance)
                referencePosition.y = 0f;
            Shift(referencePosition);
        }
    }

    private void Shift(Vector3 referencePosition)
    {

#if DUMP_WORLDGEN
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
#endif
        TotalOffset += referencePosition;
        PauseInterpolation();
        MoveRootTransforms(referencePosition);

        if (UpdateParticles)
            MoveParticles(referencePosition);

        if (UpdateTrailRenderers)
            MoveTrailRenderers(referencePosition);

        if (UpdateLineRenderers)
            MoveLineRenderers(referencePosition);

        // character controllers and rigidbodies only see transform moves after a sync
        Physics.SyncTransforms();
        Shifted?.Invoke(referencePosition);
        // not on the warm up, an agent that isn't on the navmesh yet would complain
        if (referencePosition != Vector3.zero)
            WarpAgents();
        WarpCameras(referencePosition);
#if DUMP_WORLDGEN
        Debug.Log("FloatingOrigin: shift by " + referencePosition + " took " + watch.Elapsed.TotalMilliseconds.ToString("F2") + "ms");
#endif
    }

    // agents keep their own position and would walk back, the navmesh under them moved in Shifted
    private void WarpAgents()
    {
        foreach (UnityEngine.AI.NavMeshAgent agent in FindObjectsOfType<UnityEngine.AI.NavMeshAgent>())
        {
            if (agent.enabled)
                agent.Warp(agent.transform.position);
        }
    }

    // an interpolated rigidbody (the ket) would draw a frame between the old and new place and the camera follows it.
    // off for the shift, back on after the next physics step
    private readonly System.Collections.Generic.Dictionary<Rigidbody, RigidbodyInterpolation> paused = new System.Collections.Generic.Dictionary<Rigidbody, RigidbodyInterpolation>();

    private void PauseInterpolation()
    {
        foreach (Rigidbody body in FindObjectsOfType<Rigidbody>())
        {
            if (body.interpolation != RigidbodyInterpolation.None && !paused.ContainsKey(body))
            {
                paused.Add(body, body.interpolation);
                body.interpolation = RigidbodyInterpolation.None;
            }
        }
    }

    void FixedUpdate()
    {
        foreach (System.Collections.Generic.KeyValuePair<Rigidbody, RigidbodyInterpolation> body in paused)
        {
            if (body.Key != null)
                body.Key.interpolation = body.Value;
        }
        paused.Clear();
    }

    // otherwise the camera damping sees the jump and swings across.
    // only top level cameras, a freelook passes it on to its rigs itself.
    // the body only warps for its Follow and the aim only for its LookAt, so both when they differ
    private static void WarpCameras(Vector3 offset)
    {
        Cinemachine.CinemachineCore core = Cinemachine.CinemachineCore.Instance;
        for (int i = 0; i < core.VirtualCameraCount; i++)
        {
            Cinemachine.CinemachineVirtualCameraBase vcam = core.GetVirtualCamera(i);
            if (vcam.ParentCamera != null)
                continue;
            if (vcam.Follow != null)
                vcam.OnTargetObjectWarped(vcam.Follow, -offset);
            if (vcam.LookAt != null && vcam.LookAt != vcam.Follow)
                vcam.OnTargetObjectWarped(vcam.LookAt, -offset);
            foreach (Cinemachine.CinemachineExtension extension in vcam.GetComponents<Cinemachine.CinemachineExtension>())
                WarpExtension(extension, -offset);
        }
    }

    private static readonly System.Reflection.FieldInfo extraStates = typeof(Cinemachine.CinemachineExtension).GetField("mExtraState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    // cinemachine 2.9's collider keeps the camera's last position per vcam and doesn't warp it, so the next frame
    // turned the camera towards a point 1km away (a ~20 degree snap at speed). private, so by reflection
    private static void WarpExtension(Cinemachine.CinemachineExtension extension, Vector3 delta)
    {
        if (extraStates == null || !(extraStates.GetValue(extension) is System.Collections.IDictionary states))
            return;
        foreach (object state in states.Values)
        {
            if (state == null)
                continue;
            System.Reflection.FieldInfo field = state.GetType().GetField("previousCameraPosition");
            if (field != null && field.FieldType == typeof(Vector3))
                field.SetValue(state, (Vector3)field.GetValue(state) + delta);
        }
    }

    private void MoveRootTransforms(Vector3 offset)
    {
        if (UpdateAllScenes)
        {
            for (int z = 0; z < SceneManager.sceneCount; z++)
            {
                foreach (GameObject g in SceneManager.GetSceneAt(z).GetRootGameObjects())
                    g.transform.position -= offset;
            }
        }
        else
        {
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                g.transform.position -= offset;
        }
    }

    private void MoveTrailRenderers(Vector3 offset)
    {
        var trails = FindObjectsOfType<TrailRenderer>() as TrailRenderer[];
        foreach (var trail in trails)
        {
            Vector3[] positions = new Vector3[trail.positionCount];

            int positionCount = trail.GetPositions(positions);
            for (int i = 0; i < positionCount; ++i)
                positions[i] -= offset;

            trail.SetPositions(positions);
        }
    }

    private void MoveLineRenderers(Vector3 offset)
    {
        var lines = FindObjectsOfType<LineRenderer>() as LineRenderer[];
        foreach (var line in lines)
        {
            Vector3[] positions = new Vector3[line.positionCount];

            int positionCount = line.GetPositions(positions);
            for (int i = 0; i < positionCount; ++i)
                positions[i] -= offset;

            line.SetPositions(positions);
        }
    }

    private void MoveParticles(Vector3 offset)
    {
        var particles = FindObjectsOfType<ParticleSystem>() as ParticleSystem[];
        foreach (ParticleSystem system in particles)
        {
            if (system.main.simulationSpace != ParticleSystemSimulationSpace.World)
                continue;

            int particlesNeeded = system.main.maxParticles;

            if (particlesNeeded <= 0)
                continue;

            // ensure a sufficiently large array in which to store the particles
            if (parts == null || parts.Length < particlesNeeded)
            {
                parts = new ParticleSystem.Particle[particlesNeeded];
            }

            // now get the particles
            int num = system.GetParticles(parts);

            for (int i = 0; i < num; i++)
            {
                parts[i].position -= offset;
            }

            system.SetParticles(parts, num);
        }
    }
}