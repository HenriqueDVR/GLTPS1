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

	private readonly Dictionary<GameObject, WorldLayout.NodeDef> nodeDefs = new Dictionary<GameObject, WorldLayout.NodeDef>();

	private readonly Dictionary<GameObject, WorldLayout.EdgeDef> edgeDefs = new Dictionary<GameObject, WorldLayout.EdgeDef>();

	private readonly Queue<Action> spawnQueue = new Queue<Action>();

	private float nextProximityCheck;

	public ulong Seed { get; private set; }

	public WorldLayout Layout { get; private set; }

	public uint LayoutHash { get; private set; }

	private void Awake()
	{
		Active = true;
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
		WorldLayout.NodeDef root = GetNodeDef(node.gameObject);
		WorldLayout.NodeDef pillar = ((nodeData.pillarEdge != null) ? GetNodeDef(nodeData.pillarEdge) : null);
		Pose rootPose = new Pose(node.transform.position + Offset(), node.transform.rotation);
		Layout = WorldLayout.Generate(root, rootPose, Seed, pillar, nodeData.pillarSpawnChance, nodeData.countThreshold, maxNodes);
		string text = Layout.Describe();
		LayoutHash = WorldLayout.Hash(text);
		Debug.Log("WorldGenerator: seed=" + Seed + " nodes=" + Layout.Nodes.Count + " edges=" + Layout.Edges.Count + " hash=" + LayoutHash.ToString("x8"));
#if DUMP_WORLDGEN
		System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "worldgen_layout_" + Seed + ".txt"), text);
		base.gameObject.AddComponent<WorldGenOverheadView>();
#endif
		Layout.Nodes[0].Instance = node.gameObject;
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
			CoopItem.TrackScenePickups();
		}
	}

	private void OnDestroy()
	{
		Active = false;
	}

	private void Update()
	{
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
		}
		// one per frame, instantiating several hitches
		if (spawnQueue.Count > 0)
		{
			spawnQueue.Dequeue()();
		}
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
	}

	private GameObject Spawn(GameObject prefab, Pose layoutPose, string name)
	{
		GameObject obj = UnityEngine.Object.Instantiate(prefab, layoutPose.position - Offset(), layoutPose.rotation);
		obj.name = name;
		return obj;
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

	private static Node FindStartNode()
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

	private WorldLayout.NodeDef GetNodeDef(GameObject prefab)
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
