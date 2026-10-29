using System;
using System.Collections.Generic;
using UnityEngine;

// seeded generator replacing the physics based one (see WorldLayout).
// layout is computed on load, pieces spawn near the player like before
public class WorldGenerator : MonoBehaviour
{
	public static bool Active { get; private set; }

	// 0 = random. -seed on the command line
	public static ulong RequestedSeed;

	private const int maxNodes = 300;

	private const float proximityCheckInterval = 0.5f;

	private NodeData nodeData;

	private FloatingOrigin floatingOrigin;

	private readonly Queue<Action> spawnQueue = new Queue<Action>();

	private float nextProximityCheck;

	// pieces this far from every player get switched off, and come back once someone is
	// showMargin nearer. as far as spawning reaches (NodeData.spawnThreshold, 1400m) so they come in deep in the fog, no popping
	public static float HideDistance = 1400f;

	private const float showMargin = 100f;

	private const int togglesPerFrame = 2;

	private readonly Queue<KeyValuePair<GameObject, bool>> toggleQueue = new Queue<KeyValuePair<GameObject, bool>>();

	private readonly HashSet<GameObject> queuedToggles = new HashSet<GameObject>();

	private readonly List<Vector3> playerPoints = new List<Vector3>();

	private bool builtAround;

	public ulong Seed { get; private set; }

	// a piece spawned, or was switched on or off. world space, NavMeshAreaBaker rebuilds if it's in its box
	public static event Action<Bounds> PieceChanged;

	public WorldLayout Layout { get; private set; }

	public uint LayoutHash { get; private set; }

	private void Awake()
	{
		Active = true;
		KanazawaInstanceManager.ResetLevel();
		nodeData = GetComponent<NodeData>();
		floatingOrigin = UnityEngine.Object.FindObjectOfType<FloatingOrigin>();
		Seed = ChooseSeed();
		Node node = FindStartNode();
		if (node == null)
		{
			Debug.LogError("WorldGenerator: no start node found, falling back to the original generator");
			Active = false;
			base.enabled = false;
			return;
		}
		Pose rootPose = new Pose(node.transform.position + Offset(), node.transform.rotation);
		Layout = BuildLayout(node, nodeData, rootPose, Seed);
		string text = Layout.Describe();
		LayoutHash = WorldLayout.Hash(text);
		Debug.Log("WorldGenerator: seed=" + Seed + " nodes=" + Layout.Nodes.Count + " edges=" + Layout.Edges.Count + " hash=" + LayoutHash.ToString("x8"));
#if DUMP_WORLDGEN
		System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "worldgen_layout_" + Seed + ".txt"), text);
		base.gameObject.AddComponent<WorldGenOverheadView>();
#endif
		Layout.Nodes[0].Instance = node.gameObject;
		base.gameObject.AddComponent<DistantPillar>();
		SeedItemSpawners(node.gameObject, 0uL);
		node.MarkPlaced();
		if (CoopSession.Active)
		{
			if (CoopSession.Instance.IsHost || CoopSession.Instance.Connected)
			{
				base.gameObject.AddComponent<CoopPlayers>();
				base.gameObject.AddComponent<CoopSupplies>();
				base.gameObject.AddComponent<CoopKettenkrad>();
				base.gameObject.AddComponent<CoopBarrel>();
				base.gameObject.AddComponent<CoopDowned>();
				base.gameObject.AddComponent<CoopKanazawa>();
#if DUMP_WORLDGEN
				base.gameObject.AddComponent<CoopDebugTour>();
#endif
			}
			CoopSession.Instance.OnWorldGenerated(this);
		}
		else
		{
			CoopItem.ResetLevel();
		}
		CoopItem.TrackScenePickups();
	}

	private void OnDestroy()
	{
		Active = false;
	}

	private void Update()
	{
		if (!builtAround)
		{
			builtAround = true;
			BuildAround();
		}
		if (Time.time >= nextProximityCheck)
		{
			nextProximityCheck = Time.time + proximityCheckInterval;
			foreach (WorldLayout.PlacedNode node in Layout.Nodes)
			{
				if (node.Instance != null && !node.ChildrenQueued && IsPlayerWithin(node.Instance.transform.position, nodeData.spawnThreshold))
				{
					QueueChildren(node);
				}
			}
			if (HideDistance > 0f)
			{
				CheckHidden();
			}
		}
		// one per frame, instantiating several hitches
		if (spawnQueue.Count > 0)
		{
			spawnQueue.Dequeue()();
		}
		for (int i = 0; i < togglesPerFrame && toggleQueue.Count > 0; i++)
		{
			KeyValuePair<GameObject, bool> toggle = toggleQueue.Dequeue();
			queuedToggles.Remove(toggle.Key);
			if (toggle.Key != null && toggle.Key.activeSelf != toggle.Value)
			{
				toggle.Key.SetActive(toggle.Value);
				RaisePieceChanged(toggle.Key);
				if (toggle.Value)
				{
					KanazawaInstanceManager.RemoveLeftovers(toggle.Key);
				}
			}
		}
	}

	// spawn everything in reach before the first frame, one a frame showed a half built world
	private void BuildAround()
	{
		bool queued = true;
		while (queued)
		{
			queued = false;
			foreach (WorldLayout.PlacedNode node in Layout.Nodes)
			{
				if (node.Instance != null && !node.ChildrenQueued && IsPlayerWithin(node.Instance.transform.position, nodeData.spawnThreshold))
				{
					QueueChildren(node);
					queued = true;
				}
			}
			while (spawnQueue.Count > 0)
			{
				spawnQueue.Dequeue()();
			}
		}
	}

	private void CheckHidden()
	{
		playerPoints.Clear();
		Vector3 offset = Offset();
		if (nodeData.player != null)
		{
			playerPoints.Add(nodeData.player.transform.position + offset);
		}
		// coop: the world stays around both girls
		if (CoopPlayers.Instance != null)
		{
			if (CoopPlayers.Instance.Local != null)
			{
				playerPoints.Add(CoopPlayers.Instance.Local.position + offset);
			}
			if (CoopPlayers.Instance.Remote != null)
			{
				playerPoints.Add(CoopPlayers.Instance.Remote.position + offset);
			}
		}
		if (playerPoints.Count == 0)
		{
			return;
		}
		// the start node carries StartEnvironmentTrace, which draws the map path, so it never goes
		for (int i = 1; i < Layout.Nodes.Count; i++)
		{
			ConsiderHiding(Layout.Nodes[i].Instance, Layout.Nodes[i].Pose, Layout.Nodes[i].Def.Boxes);
		}
		foreach (WorldLayout.PlacedEdge edge in Layout.Edges)
		{
			ConsiderHiding(edge.Instance, edge.Pose, edge.Def.Boxes);
		}
#if DUMP_WORLDGEN
		if (Time.time >= nextHideLog)
		{
			nextHideLog = Time.time + 5f;
			int shown = 0;
			int spawned = 0;
			string under = "";
			foreach (WorldLayout.PlacedNode node in Layout.Nodes)
			{
				if (node.Instance != null)
				{
					spawned++;
					shown += node.Instance.activeSelf ? 1 : 0;
				}
			}
			foreach (Vector3 point in playerPoints)
			{
				WorldLayout.PlacedNode nearest = null;
				float best = float.MaxValue;
				foreach (WorldLayout.PlacedNode node in Layout.Nodes)
				{
					if (node.Instance != null)
					{
						float distance = PieceDistance(node.Pose, node.Def.Boxes, point);
						if (distance < best)
						{
							best = distance;
							nearest = node;
						}
					}
				}
				under += " " + point.ToString("F0") + (nearest != null ? (" on #" + nearest.Id + " (" + best.ToString("F0") + "m, " + (nearest.Instance.activeSelf ? "shown" : "HIDDEN") + ")") : "");
			}
			Debug.Log("WorldGenHide nodes shown " + shown + "/" + spawned + " players" + under);
		}
#endif
	}

#if DUMP_WORLDGEN
	private float nextHideLog;
#endif

	private void ConsiderHiding(GameObject piece, Pose pose, List<WorldLayout.LocalBox> boxes)
	{
		if (piece == null || queuedToggles.Contains(piece))
		{
			return;
		}
		float distance = float.MaxValue;
		foreach (Vector3 point in playerPoints)
		{
			distance = Mathf.Min(distance, PieceDistance(pose, boxes, point));
		}
		bool shown = piece.activeSelf;
		bool wanted = shown ? (distance < HideDistance) : (distance < HideDistance - showMargin);
		if (wanted != shown)
		{
			queuedToggles.Add(piece);
			toggleQueue.Enqueue(new KeyValuePair<GameObject, bool>(piece, wanted));
		}
	}

	// to the nearest edge of the piece's footprint boxes, in layout space
	private static float PieceDistance(Pose pose, List<WorldLayout.LocalBox> boxes, Vector3 point)
	{
		Vector3 local = Quaternion.Inverse(pose.rotation) * (point - pose.position);
		if (boxes == null || boxes.Count == 0)
		{
			return local.magnitude;
		}
		float best = float.MaxValue;
		foreach (WorldLayout.LocalBox box in boxes)
		{
			Vector3 outside = local - box.Center;
			outside = new Vector3(Mathf.Max(Mathf.Abs(outside.x) - box.Size.x * 0.5f, 0f), Mathf.Max(Mathf.Abs(outside.y) - box.Size.y * 0.5f, 0f), Mathf.Max(Mathf.Abs(outside.z) - box.Size.z * 0.5f, 0f));
			best = Mathf.Min(best, outside.magnitude);
		}
		return best;
	}

	private bool IsPlayerWithin(Vector3 position, float distance)
	{
		if (nodeData.player != null && Vector3.Distance(position, nodeData.player.transform.position) < distance)
		{
			return true;
		}
		// coop: world has to exist around the other player too
		if (CoopPlayers.Instance != null && CoopPlayers.Instance.Remote != null)
		{
			return Vector3.Distance(position, CoopPlayers.Instance.Remote.position) < distance;
		}
		return false;
	}

	private void QueueChildren(WorldLayout.PlacedNode node)
	{
		node.ChildrenQueued = true;
		for (int i = 0; i < node.Def.Ports.Count; i++)
		{
			int port = i;
			if (node.Edges[port] != null)
			{
				spawnQueue.Enqueue(delegate
				{
					SpawnEdge(node.Edges[port]);
				});
			}
			else if (node.Barriers[port] != null)
			{
				spawnQueue.Enqueue(delegate
				{
					SpawnBarrier(node, port);
				});
			}
		}
	}

	private void SpawnEdge(WorldLayout.PlacedEdge edge)
	{
		GameObject gameObject = Spawn(edge.Def.Prefab, edge.Pose, edge.Def.Prefab.name + " #e" + edge.Id);
		SeedItemSpawners(gameObject, 1000000uL + (ulong)edge.Id);
		GenerateEdge component = edge.From.Instance.GetComponent<Node>().portPrefab[edge.FromPort].GetComponent<GenerateEdge>();
		gameObject.GetComponent<GenerateNode>().MarkPlaced(component, edge.To.EntryPort);
		edge.Instance = gameObject;
		spawnQueue.Enqueue(delegate
		{
			SpawnNode(edge.To);
		});
	}

	private void SpawnNode(WorldLayout.PlacedNode node)
	{
		GameObject gameObject = Spawn(node.Def.Prefab, node.Pose, node.Def.Prefab.name + " #" + node.Id);
		SeedItemSpawners(gameObject, (ulong)node.Id);
		GenerateNode component = node.FromEdge.Instance.GetComponent<GenerateNode>();
		Node component2 = gameObject.GetComponent<Node>();
		component2.edge = component;
		component.newNode = gameObject;
		component2.MarkPlaced();
		node.Instance = gameObject;
#if DUMP_WORLDGEN
		Debug.Log("WorldGenSpawn " + gameObject.name + " at " + gameObject.transform.position);
#endif
	}

	// same as GenerateEdge.GenerateRandomBarrier
	private void SpawnBarrier(WorldLayout.PlacedNode node, int port)
	{
		Node component = node.Instance.GetComponent<Node>();
		Transform transform = component.portPrefab[port];
		UnityEngine.Object.Instantiate(node.Barriers[port], transform.position, transform.rotation).transform.SetParent(transform);
		component.numBlockedPorts++;
		PieceChanged?.Invoke(new Bounds(transform.position, Vector3.one * barrierReach));
	}

	private GameObject Spawn(GameObject prefab, Pose layoutPose, string name)
	{
		GameObject obj = UnityEngine.Object.Instantiate(prefab, layoutPose.position - Offset(), layoutPose.rotation);
		obj.name = name;
		DistantPillar.HideBrokenParts(obj);
		GraphicsTuning.Adopt(obj);
		RaisePieceChanged(obj);
		return obj;
	}

	private const float barrierReach = 20f;

	private static void RaisePieceChanged(GameObject piece)
	{
		if (PieceChanged != null)
		{
			PieceChanged(PieceBounds(piece));
		}
	}

	// from the footprint trigger boxes like GetBoxes, they're on the root so this works while it's switched off too
	private static Bounds PieceBounds(GameObject piece)
	{
		Transform root = piece.transform;
		Bounds bounds = new Bounds(root.position, Vector3.zero);
		bool any = false;
		foreach (BoxCollider box in piece.GetComponents<BoxCollider>())
		{
			if (!box.isTrigger)
			{
				continue;
			}
			Vector3 extents = box.size * 0.5f;
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = box.center + new Vector3(((i & 1) == 0) ? (0f - extents.x) : extents.x, ((i & 2) == 0) ? (0f - extents.y) : extents.y, ((i & 4) == 0) ? (0f - extents.z) : extents.z);
				Vector3 world = root.TransformPoint(corner);
				if (any)
				{
					bounds.Encapsulate(world);
				}
				else
				{
					bounds = new Bounds(world, Vector3.zero);
					any = true;
				}
			}
		}
		if (!any)
		{
			bounds.size = Vector3.one * 200f;
		}
		return bounds;
	}

	private void SeedItemSpawners(GameObject instance, ulong stream)
	{
		ItemSpawnManager[] componentsInChildren = instance.GetComponentsInChildren<ItemSpawnManager>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			componentsInChildren[i].random = new WorldRandom(WorldRandom.Derive(Seed, stream, (ulong)(i + 1)));
			componentsInChildren[i].itemId = (stream << 16) | (uint)(i + 1);
		}
	}

	// route to the first pillar from the layout, so unspawned pieces count too
	public List<Vector3> RouteToPillar(Vector3 from)
	{
		List<Vector3> route = new List<Vector3>();
		WorldLayout.PlacedNode pillar = Pillar();
		if (pillar == null)
		{
			return route;
		}
		Vector3 offset = Offset();
		Vector3 point = from + offset;
		WorldLayout.PlacedNode here = null;
		float best = float.MaxValue;
		foreach (WorldLayout.PlacedNode node in Layout.Nodes)
		{
			float distance = PieceDistance(node.Pose, node.Def.Boxes, point);
			if (distance < best)
			{
				best = distance;
				here = node;
			}
		}
		// up to where it meets the pillar's branch, then down
		List<WorldLayout.PlacedNode> up = Ancestors(here);
		List<WorldLayout.PlacedNode> down = Ancestors(pillar);
		route.Add(from);
		WorldLayout.PlacedNode meet = null;
		foreach (WorldLayout.PlacedNode node in up)
		{
			if (down.Contains(node))
			{
				meet = node;
				break;
			}
			route.Add(WorldLayout.Combine(node.FromEdge.Pose, node.FromEdge.Def.Exit).position - offset);
			route.Add(WorldLayout.Combine(node.FromEdge.Pose, node.FromEdge.Def.Entrance).position - offset);
		}
		for (int i = down.IndexOf(meet) - 1; i >= 0; i--)
		{
			WorldLayout.PlacedEdge edge = down[i].FromEdge;
			route.Add(WorldLayout.Combine(edge.Pose, edge.Def.Entrance).position - offset);
			route.Add(WorldLayout.Combine(edge.Pose, edge.Def.Exit).position - offset);
		}
		if (pillar.Def.Boxes.Count > 0)
		{
			route.Add(WorldLayout.Combine(pillar.Pose, new Pose(pillar.Def.Boxes[0].Center, Quaternion.identity)).position - offset);
		}
		return route;
	}

	// first pillar in the layout, spawned or not
	public WorldLayout.PlacedNode Pillar()
	{
		WorldLayout.PlacedNode pillar = null;
		foreach (WorldLayout.PlacedNode node in Layout.Nodes)
		{
			if (node.Def.Prefab == nodeData.pillarEdge && (pillar == null || node.Id < pillar.Id))
			{
				pillar = node;
			}
		}
		return pillar;
	}

	// every pillar in the layout, spawned or not
	public List<WorldLayout.PlacedNode> Pillars()
	{
		List<WorldLayout.PlacedNode> pillars = new List<WorldLayout.PlacedNode>();
		foreach (WorldLayout.PlacedNode node in Layout.Nodes)
		{
			if (node.Def.Prefab == nodeData.pillarEdge)
			{
				pillars.Add(node);
			}
		}
		return pillars;
	}

	public Vector3 ToWorld(Vector3 layout)
	{
		return layout - Offset();
	}

	// the node, its parent, ... the start
	private static List<WorldLayout.PlacedNode> Ancestors(WorldLayout.PlacedNode node)
	{
		List<WorldLayout.PlacedNode> chain = new List<WorldLayout.PlacedNode>();
		for (; node != null; node = node.FromEdge != null ? node.FromEdge.From : null)
		{
			chain.Add(node);
		}
		return chain;
	}

	// layout coords = world coords before floating origin
	private Vector3 Offset()
	{
		if (!(floatingOrigin != null))
		{
			return Vector3.zero;
		}
		return floatingOrigin.TotalOffset;
	}

	private static ulong ChooseSeed()
	{
		if (RequestedSeed == 0L)
		{
			string[] commandLineArgs = Environment.GetCommandLineArgs();
			for (int i = 0; i < commandLineArgs.Length - 1; i++)
			{
				if (commandLineArgs[i] == "-seed" && ulong.TryParse(commandLineArgs[i + 1], out var result))
				{
					RequestedSeed = result;
				}
			}
		}
		if (RequestedSeed != 0L)
		{
			return RequestedSeed;
		}
		return (ulong)new System.Random().Next(1, int.MaxValue);
	}

	// layout only, nothing spawned. the golden hash tests use this too
	public static WorldLayout BuildLayout(Node start, NodeData data, Pose rootPose, ulong seed)
	{
		LayoutDefs defs = new LayoutDefs();
		WorldLayout.NodeDef root = defs.GetNodeDef(start.gameObject);
		WorldLayout.NodeDef pillar = ((data.pillarEdge != null) ? defs.GetNodeDef(data.pillarEdge) : null);
		return WorldLayout.Generate(root, rootPose, seed, pillar, data.pillarSpawnChance, data.countThreshold, maxNodes);
	}

	public static Node FindStartNode()
	{
		Node result = null;
		Node[] array = UnityEngine.Object.FindObjectsOfType<Node>();
		foreach (Node node in array)
		{
			if (node.edge == null)
			{
				if (result != null)
				{
					Debug.LogWarning("WorldGenerator: more than one start node, using " + result.name);
					break;
				}
				result = node;
			}
		}
		return result;
	}

	// node/edge defs read off the pool prefabs, one per prefab
	private sealed class LayoutDefs
	{
		private readonly Dictionary<GameObject, WorldLayout.NodeDef> nodeDefs = new Dictionary<GameObject, WorldLayout.NodeDef>();

		private readonly Dictionary<GameObject, WorldLayout.EdgeDef> edgeDefs = new Dictionary<GameObject, WorldLayout.EdgeDef>();

		public WorldLayout.NodeDef GetNodeDef(GameObject prefab)
		{
			if (nodeDefs.TryGetValue(prefab, out var value))
			{
				return value;
			}
			value = new WorldLayout.NodeDef
			{
				Prefab = prefab,
				Boxes = GetBoxes(prefab)
			};
			nodeDefs.Add(prefab, value);
			foreach (Transform item in prefab.GetComponent<Node>().portPrefab)
			{
				WorldLayout.PortDef portDef = new WorldLayout.PortDef
				{
					Local = LocalPose(prefab.transform, item)
				};
				GenerateEdge component = item.GetComponent<GenerateEdge>();
				if (component != null)
				{
					foreach (GameObject edgePrefab in component.EdgePrefabs)
					{
						portDef.Edges.Add(GetEdgeDef(edgePrefab));
					}
					portDef.Barriers.AddRange(component.BarrierPrefabs);
				}
				value.Ports.Add(portDef);
			}
			return value;
		}

		private WorldLayout.EdgeDef GetEdgeDef(GameObject prefab)
		{
			if (edgeDefs.TryGetValue(prefab, out var value))
			{
				return value;
			}
			GenerateNode component = prefab.GetComponent<GenerateNode>();
			value = new WorldLayout.EdgeDef
			{
				Prefab = prefab,
				Entrance = LocalPose(prefab.transform, prefab.transform.GetChild(1)),
				Exit = LocalPose(prefab.transform, component.edgeExit),
				Boxes = GetBoxes(prefab)
			};
			edgeDefs.Add(prefab, value);
			for (int i = 0; i < component.NodePrefabs.Count; i++)
			{
				value.Nodes.Add(GetNodeDef(component.NodePrefabs[i]));
				value.Weights.Add((i < component.SpawnChance.Count) ? component.SpawnChance[i] : 1);
			}
			return value;
		}
	}

	private static List<WorldLayout.LocalBox> GetBoxes(GameObject prefab)
	{
		List<WorldLayout.LocalBox> list = new List<WorldLayout.LocalBox>();
		Vector3 localScale = prefab.transform.localScale;
		BoxCollider[] components = prefab.GetComponents<BoxCollider>();
		foreach (BoxCollider boxCollider in components)
		{
			if (boxCollider.isTrigger)
			{
				list.Add(new WorldLayout.LocalBox
				{
					Center = Vector3.Scale(boxCollider.center, localScale),
					Size = Vector3.Scale(boxCollider.size, localScale)
				});
			}
		}
		return list;
	}

	private static Pose LocalPose(Transform root, Transform child)
	{
		return new Pose(root.InverseTransformPoint(child.position), Quaternion.Inverse(root.rotation) * child.rotation);
	}
}
