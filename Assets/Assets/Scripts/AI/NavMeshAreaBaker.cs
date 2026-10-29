using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

// keeps a navmesh around one agent. builds run in the background (NavMeshBakeScheduler) and swap in when done,
// so the main thread never waits on one and there is always a navmesh under the agent.
// rebuilds when the agent walks off the middle of it, when a map piece shows up or goes in reach, or every maxInterval.
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

	private readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();

	private readonly List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();

	private static readonly Dictionary<Mesh, int> triangleCounts = new Dictionary<Mesh, int>();

	private static readonly HashSet<Mesh> reported = new HashSet<Mesh>();

	private void Awake()
	{
		surface = GetComponent<NavMeshSurface>();
		// the surface only holds settings now, the data is ours
		surface.RemoveData();
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
		WorldGenerator.PieceChanged += OnPieceChanged;
		FloatingOrigin.Shifted += OnOriginShifted;
	}

	private void OnDisable()
	{
		WorldGenerator.PieceChanged -= OnPieceChanged;
		FloatingOrigin.Shifted -= OnOriginShifted;
		NavMeshBakeScheduler.Forget(this);
	}

	private void OnDestroy()
	{
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
		Vector3 offset = navMeshAgent.position - transform.position;
		// elevators, the box isn't grown upwards so a quarter of its height is the margin there
		bool climbed = Mathf.Abs(offset.y) > Mathf.Abs(surface.size.y) * 0.25f;
		offset.y = 0f;
		if (dirty || climbed || offset.sqrMagnitude > rebuildDistance * rebuildDistance || Time.time - lastStart > maxInterval)
		{
			NavMeshBakeScheduler.Request(this);
			if (NavMeshBakeScheduler.TryStart(this))
			{
				StartBuild();
			}
		}
	}

	private void StartBuild()
	{
		dirty = false;
		lastStart = Time.time;
		// the box is laid out around the transform, the transform waits there until the build is in
		transform.position = navMeshAgent.position;
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
		NavMeshDataInstance old = instance;
		instance = NavMesh.AddNavMeshData(data, instancePosition, transform.rotation);
		old.Remove();
		bakedBounds.center -= offset;
		pendingBounds.center -= offset;
	}

	private void OnPieceChanged(Bounds bounds)
	{
		if (bounds.Intersects(bakedBounds) || (building != null && bounds.Intersects(pendingBounds)))
		{
			dirty = true;
		}
	}

	private Bounds LocalBounds()
	{
		Vector3 size = surface.size;
		size = new Vector3(Mathf.Abs(size.x) + 2f * rebuildDistance, Mathf.Abs(size.y), Mathf.Abs(size.z) + 2f * rebuildDistance);
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
		return sources;
	}

	private bool Drop(NavMeshBuildSource source)
	{
		Component component = source.component;
		if (component != null)
		{
			if (surface.ignoreNavMeshAgent && component.TryGetComponent(out NavMeshAgent _))
			{
				return true;
			}
			if (surface.ignoreNavMeshObstacle && component.TryGetComponent(out NavMeshObstacle _))
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
		if (!triangleCounts.TryGetValue(mesh, out int count))
		{
			long indices = 0;
			for (int i = 0; i < mesh.subMeshCount; i++)
			{
				indices += mesh.GetIndexCount(i);
			}
			count = (int)(indices / 3);
			triangleCounts[mesh] = count;
		}
		return count;
	}
}
