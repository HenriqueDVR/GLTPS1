
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
    }

    void LateUpdate()
    {
        if (ReferenceObject == null)
            return;

        Vector3 referencePosition = ReferenceObject.position;

        if (Use2DDistance)
            referencePosition.y = 0f;

        if (referencePosition.magnitude > Threshold)
        {
            TotalOffset += referencePosition;
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
            WarpAgents();
            WarpCameras(referencePosition);
        }
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

    // otherwise the camera damping sees the jump and swings across
    private static void WarpCameras(Vector3 offset)
    {
        Cinemachine.CinemachineCore core = Cinemachine.CinemachineCore.Instance;
        for (int i = 0; i < core.VirtualCameraCount; i++)
        {
            Cinemachine.CinemachineVirtualCameraBase vcam = core.GetVirtualCamera(i);
            if (vcam.Follow != null)
                vcam.OnTargetObjectWarped(vcam.Follow, -offset);
            else if (vcam.LookAt != null)
                vcam.OnTargetObjectWarped(vcam.LookAt, -offset);
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