using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

// keeps a navmesh around one agent, or several when they're close: separate navmeshes don't join up, she couldn't path
// from hers onto the player's, and a hole only held where every one of them had it. builds run in the background (NavMeshBakeScheduler) and swap in when done,
// so the main thread never waits on one and there is always a navmesh under the agent.
// rebuilds when an agent walks off the middle of it, when a map piece shows up or goes in reach, or every maxInterval.
// the NavMeshSurface on the same object gives the settings: agent type, layers, geometry, area, the box (size/center)
public class NavMeshAreaBaker : MonoBehaviour
{
	[SerializeField] private NavMeshSurface surface;

	[SerializeField] private Transform navMeshAgent;

	// the surface box is grown by this on each side, the agent can walk this far before the next one is needed
	[SerializeField] private float rebuildDistance = 8f;

	// rebuild anyway this often, for things that move without telling (doors, the Kettenkrad)
	[SerializeField] private float maxInterval = 2f;

	// meshes over this many triangles are left out, a 258k triangle debris scatter cost 25ms a build.
	// a NavMeshModifier with override area on the object keeps it in regardless
	[SerializeField] private int triangleBudget = 50000;

	private NavMeshData data;

	private NavMeshDataInstance instance;

	// where instance was added, the transform moves on ahead of it while a build runs
	private Vector3 instancePosition;

	private NavMeshData pending;

	private AsyncOperation building;

	// bake box in world space, the one in use and the one being built
	private Bounds bakedBounds;

	private Bounds pendingBounds;

	private bool dirty;

	private float lastStart;

	private Vector3 lastPosition;

	// floating origin moves everything at once by thousands of meters
	private const float originShift = 1000f;

	private const float levelChange = 1f;

	// how far the navmesh goes past the agents on each side, what she can plan a route through
	private const float reach = 20f;

	// close enough to share. apart further than the first to split, so it doesn't flip at the edge, and not on another level
	private const float shareWithin = 25f;

	private const float shareUntil = 35f;

	private const float levelApart = 3f;

	private static readonly List<NavMeshAreaBaker> active = new List<NavMeshAreaBaker>();

	// the baker whose navmesh this agent's on, null when it's on its own
	private NavMeshAreaBaker lead;

	// room taken up by the agents sharing this one, on top of reach
	private Vector2 span;

	private readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();

	private readonly List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();

	private static readonly HashSet<Mesh> reported = new HashSet<Mesh>();

	private void Awake()
	{
		surface = GetComponent<NavMeshSurface>();
		// the surface only holds settings now, the data is ours. off with the reference too: one has a navmesh saved in the
		// scene, its OnEnable comes after this Awake and put it back, a stale one with no hole for the ket at the start
		surface.RemoveData();
		surface.navMeshData = null;
		// what's solid to the girls, not what's drawn: the sky scenery was going in, and invisible blockers weren't
		surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
		transform.position = navMeshAgent.position;
		data = NavMeshBuilder.BuildNavMeshData(surface.GetBuildSettings(), CollectSources(), LocalBounds(), transform.position, transform.rotation);
		bakedBounds = WorldBounds();
		instance = NavMesh.AddNavMeshData(data, transform.position, transform.rotation);
		instancePosition = transform.position;
		lastStart = Time.time;
		lastPosition = transform.position;
	}

	private void OnEnable()
	{
		FloatingOrigin.Shifted += OnOriginShifted;
		active.Add(this);
		// back from being switched off, what it had is out of date but better than nothing till the next build
		if (data != null && !instance.valid)
		{
			instance = NavMesh.AddNavMeshData(data, transform.position, transform.rotation);
			instancePosition = transform.position;
		}
	}

	private void OnDisable()
	{
		FloatingOrigin.Shifted -= OnOriginShifted;
		NavMeshBakeScheduler.Forget(this);
		active.Remove(this);
		lead = null;
		// Kanazawa's wakes up and gets switched off at the start. its navmesh stayed, floor under the ket whatever the others built
		instance.Remove();
	}

	private void OnDestroy()
	{
		reported.Clear();
		instance.Remove();
		if (data != null)
		{
			Destroy(data);
		}
		if (pending != null)
		{
			Destroy(pending);
		}
	}

	private void Update()
	{
		if ((transform.position - lastPosition).sqrMagnitude > originShift * originShift)
		{
			dirty = true;
		}
		lastPosition = transform.position;
		if (building != null)
		{
			if (building.isDone)
			{
				SwapIn();
			}
			return;
		}
		FindLead();
		if (lead != null)
		{
			// its navmesh has to be under this agent before ours goes
			if (instance.valid && lead.Covers(navMeshAgent.position))
			{
				instance.Remove();
			}
			return;
		}
		// elevators, the box isn't grown upwards so a quarter of its height is the margin there
		bool climbed = Mathf.Abs(navMeshAgent.position.y - transform.position.y) > Mathf.Abs(surface.size.y) * 0.25f;
		if (dirty || climbed || WalkedOff() || Time.time - lastStart > maxInterval)
		{
			NavMeshBakeScheduler.Request(this);
			if (NavMeshBakeScheduler.TryStart(this))
			{
				StartBuild();
			}
		}
	}

	private void FindLead()
	{
		if (lead != null && (lead.lead != null || Apart(lead) > shareUntil))
		{
			lead = null;
			dirty = true;
		}
		if (lead != null)
		{
			return;
		}
		foreach (NavMeshAreaBaker baker in active)
		{
			if (baker == this)
			{
				return;
			}
			if (baker.lead == null && Apart(baker) < shareWithin)
			{
				lead = baker;
				baker.dirty = true;
				NavMeshBakeScheduler.Forget(this);
				return;
			}
		}
	}

	private float Apart(NavMeshAreaBaker other)
	{
		Vector3 apart = other.navMeshAgent.position - navMeshAgent.position;
		if (Mathf.Abs(apart.y) > levelApart)
		{
			return float.MaxValue;
		}
		apart.y = 0f;
		return apart.magnitude;
	}

	private bool Covers(Vector3 position)
	{
		return building == null && !dirty && Mathf.Abs(position.x - bakedBounds.center.x) < bakedBounds.extents.x - rebuildDistance && Mathf.Abs(position.z - bakedBounds.center.z) < bakedBounds.extents.z - rebuildDistance;
	}

	private bool WalkedOff()
	{
		foreach (NavMeshAreaBaker baker in active)
		{
			if (baker == this || baker.lead == this)
			{
				Vector3 offset = baker.navMeshAgent.position - transform.position;
				if (Mathf.Abs(offset.x) > span.x * 0.5f + rebuildDistance || Mathf.Abs(offset.z) > span.y * 0.5f + rebuildDistance)
				{
					return true;
				}
			}
		}
		return false;
	}

	private void StartBuild()
	{
		dirty = false;
		lastStart = Time.time;
		// the box is laid out around the transform, the transform waits there until the build is in
		// its height stays till she's really changed level (an elevator, stairs): built around her height every time,
		// each build's surface came out a voxel above the last and she rode it up, 4 cm a second, into the air
		Vector3 low = navMeshAgent.position;
		Vector3 high = low;
		foreach (NavMeshAreaBaker baker in active)
		{
			if (baker.lead == this)
			{
				low = Vector3.Min(low, baker.navMeshAgent.position);
				high = Vector3.Max(high, baker.navMeshAgent.position);
			}
		}
		span = new Vector2(high.x - low.x, high.z - low.z);
		Vector3 centre = 0.5f * (low + high);
		centre.y = navMeshAgent.position.y;
		if (Mathf.Abs(centre.y - transform.position.y) < levelChange)
		{
			centre.y = transform.position.y;
		}
		transform.position = centre;
		lastPosition = transform.position;
		pending = new NavMeshData(surface.agentTypeID)
		{
			position = transform.position,
			rotation = transform.rotation
		};
		pendingBounds = WorldBounds();
		building = NavMeshBuilder.UpdateNavMeshDataAsync(pending, surface.GetBuildSettings(), CollectSources(), LocalBounds());
	}

	private void SwapIn()
	{
		building = null;
		NavMeshBakeScheduler.Finished(this);
		// new one in before the old one goes, no frame without a navmesh. transform.position and not the
		// position it was built at, in case the floating origin moved everything meanwhile
		NavMeshDataInstance old = instance;
		instance = NavMesh.AddNavMeshData(pending, transform.position, transform.rotation);
		instancePosition = transform.position;
		old.Remove();
		Destroy(data);
		data = pending;
		pending = null;
		bakedBounds = pendingBounds;
	}

	// the navmesh moves with everything else straight away, so the agents have one under them until the rebuild
	private void OnOriginShifted(Vector3 offset)
	{
		instancePosition -= offset;
		// taken out while another one's lead
		if (instance.valid)
		{
			NavMeshDataInstance old = instance;
			instance = NavMesh.AddNavMeshData(data, instancePosition, transform.rotation);
			old.Remove();
		}
		bakedBounds.center -= offset;
		pendingBounds.center -= offset;
	}

	// something big just stopped somewhere new (the ket): now, not at the next interval
	public static void RebuildAll()
	{
		foreach (NavMeshAreaBaker baker in FindObjectsOfType<NavMeshAreaBaker>())
		{
			baker.dirty = true;
		}
	}

	private Bounds LocalBounds()
	{
		Vector3 size = surface.size;
		size = new Vector3(span.x + 2f * reach, Mathf.Abs(size.y), span.y + 2f * reach);
		return new Bounds(surface.center, size);
	}

	private Bounds WorldBounds()
	{
		Bounds local = LocalBounds();
		Matrix4x4 matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
		Vector3 x = matrix.MultiplyVector(new Vector3(local.extents.x, 0f, 0f));
		Vector3 y = matrix.MultiplyVector(new Vector3(0f, local.extents.y, 0f));
		Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, local.extents.z));
		Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
		return new Bounds(matrix.MultiplyPoint(local.center), 2f * extents);
	}

	// what NavMeshSurface does for a volume, plus the triangle budget
	private List<NavMeshBuildSource> CollectSources()
	{
		markups.Clear();
		foreach (NavMeshModifier modifier in NavMeshModifier.activeModifiers)
		{
			if ((surface.layerMask & (1 << modifier.gameObject.layer)) == 0 || !modifier.AffectsAgentType(surface.agentTypeID))
			{
				continue;
			}
			markups.Add(new NavMeshBuildMarkup
			{
				root = modifier.transform,
				overrideArea = modifier.overrideArea,
				area = modifier.area,
				ignoreFromBuild = modifier.ignoreFromBuild
			});
		}
		NavMeshBuilder.CollectSources(WorldBounds(), surface.layerMask, surface.useGeometry, surface.defaultArea, markups, sources);
		for (int i = sources.Count - 1; i >= 0; i--)
		{
			if (Drop(sources[i]))
			{
				sources.RemoveAt(i);
			}
		}
		// the ket as a solid box in every build, so the navmesh goes round it with its usual margin (the agent's radius).
		// the scene's carving obstacles left all but a band of its footprint walkable, seen on a map of it
		if (ket == null)
		{
			ket = FindObjectOfType<VehicleMovement>();
		}
		if (ket != null)
		{
			sources.Add(new NavMeshBuildSource
			{
				shape = NavMeshBuildSourceShape.Box,
				transform = Matrix4x4.TRS(ket.transform.TransformPoint(VehicleMovement.FootprintCentre), ket.transform.rotation, Vector3.one),
				size = VehicleMovement.FootprintSize,
				// walkable: the build drops the triangles of anything not, and it'd block nothing. its top's too narrow to
				// survive the agent's radius
				area = surface.defaultArea
			});
		}
		return sources;
	}

	private VehicleMovement ket;

	private bool Drop(NavMeshBuildSource source)
	{
		Component component = source.component;
		if (component != null)
		{
			if (component is Collider collider && collider.isTrigger)
			{
				return true;
			}
			if (surface.ignoreNavMeshAgent && component.TryGetComponent(out NavMeshAgent _))
			{
				return true;
			}
			if (surface.ignoreNavMeshObstacle && component.TryGetComponent(out NavMeshObstacle _))
			{
				return true;
			}
			// pickups are on Default too: she'd walk up onto a crate, and be left standing on the air once it's taken
			if (component.GetComponentInParent<CoopItem>() != null || component.GetComponentInParent<CampfireFuelLevel>() != null)
			{
				return true;
			}
		}
		if (triangleBudget <= 0 || source.shape != NavMeshBuildSourceShape.Mesh)
		{
			return false;
		}
		Mesh mesh = source.sourceObject as Mesh;
		if (mesh == null || Triangles(mesh) <= triangleBudget)
		{
			return false;
		}
		// marked as ground on purpose
		if (component != null && component.TryGetComponent(out NavMeshModifier modifier) && modifier.overrideArea)
		{
			return false;
		}
		if (reported.Add(mesh))
		{
			Debug.Log("NavMeshAreaBaker: skipped heavy navmesh source " + (component != null ? component.name : mesh.name) + " (" + Triangles(mesh) + " tris, budget " + triangleBudget + ")");
		}
		return true;
	}

	private static int Triangles(Mesh mesh)
	{
		long indices = 0;
		for (int i = 0; i < mesh.subMeshCount; i++)
		{
			indices += mesh.GetIndexCount(i);
		}
		return (int)(indices / 3);
	}
}
