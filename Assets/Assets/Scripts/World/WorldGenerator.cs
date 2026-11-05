using System;
using UnityEngine;

// the world's seed and its layers (LayerStack). the tiles are TileStreamer's, each a pure function of the plan
public class WorldGenerator : MonoBehaviour
{
	public static bool Active { get; private set; }

	// 0 = random. -seed on the command line
	public static ulong RequestedSeed;

	// what saves and co-op's Start carry. there's one generator
	public const int GenVersion = Gen2.GenVersion;

	// a co-op friend (Start) or a loaded save: the origins of the layers past 0 and the layer they're on
	public static XZ[] RequestedOrigins;

	public static int RequestedLayer;

	public static void ClearRequested()
	{
		RequestedSeed = 0uL;
		RequestedOrigins = null;
		RequestedLayer = 0;
	}

	private NodeData nodeData;

	private FloatingOrigin floatingOrigin;

	public ulong Seed { get; private set; }

	// the layer the girls are on
	public LayerPlan Plan { get; private set; }

	// every layer reached so far
	public LayerStack Layers { get; private set; }

	public int Layer => (Plan != null) ? Plan.Layer : 0;

	private uint layoutHash;

	private bool hashStale;

	// costs tens of ms, and only a co-op peer asks: not worked out until then
	public uint LayoutHash
	{
		get
		{
			if (hashStale)
			{
				hashStale = false;
				layoutHash = Fold(PlanHash(Plan));
			}
			return layoutHash;
		}
	}

	// nodeCOLUMN, the tower piece
	public GameObject PillarPrefab => (nodeData != null) ? nodeData.pillarEdge : null;

	private void Awake()
	{
		Active = true;
		nodeData = GetComponent<NodeData>();
		floatingOrigin = UnityEngine.Object.FindObjectOfType<FloatingOrigin>();
		Seed = ChooseSeed();
		Node node = FindStartNode();
		if (node == null)
		{
			Debug.LogError("WorldGenerator: no start node found");
			Active = false;
			base.enabled = false;
			return;
		}
		// the girls start in the episode 01 tunnel, built where they stand in the scene. layer 0's origin is
		// the middle of its embankment. the start node stays for its scripts, its walkway goes
		Vector3 girls = (Girls.Chito != null) ? Girls.Chito.transform.position : node.transform.position;
		girls.y = node.transform.position.y;
		Vector3 centre = OriginTunnel.Centre(girls + Offset(), node.transform.rotation);
		Vector3 facing = node.transform.rotation * Vector3.forward;
		Plan = new LayerPlan(Seed, 0, new XZ(centre.x, centre.z), null, new XZ(facing.x, facing.z));
		Layers = new LayerStack(Plan);
		Debug.Log("WorldGenerator: gen2 seed=" + Seed + " origin=" + Plan.Origin);
		if (RequestedOrigins != null)
		{
			foreach (XZ origin in RequestedOrigins)
			{
				Layers.Add(origin);
			}
			Plan = Layers.Get(Mathf.Clamp(RequestedLayer, 0, Layers.Count - 1));
			Debug.Log("WorldGenerator: on layer " + Plan.Layer + " of " + Layers.Count + " (the host's or the save's)");
		}
		hashStale = true;
#if DUMP_WORLDGEN
		base.gameObject.AddComponent<WorldGenOverheadView>();
#endif
		SeedItemSpawners(node.gameObject, 0uL);
		node.MarkPlaced();
		OriginTunnel.Build(node, girls, node.transform.rotation, Layers.Get(0).Underground.Start(out _));
	}

	private void OnDestroy()
	{
		Active = false;
	}

	// LayerTransition, on the ride's swap
	public void SetLayer(LayerPlan plan)
	{
		Plan = plan;
		hashStale = true;
	}

	// roads, voids, viaducts, the underground, a spread of whole tiles and the kit, so peers on different world code
	// or assets see the mismatch warning. round the layer's origin, which both peers know
	public static ulong PlanHash(LayerPlan plan)
	{
		WorldKit kit = WorldKit.Load();
		return plan.PlanHash(plan.Origin, 1) ^ plan.Roads.Hash(plan.Origin, 1) ^ plan.Chasms.Hash(plan.Origin, 1) ^ plan.Underground.Hash(plan.Origin, 3) ^ plan.TilesHash() ^ ((kit != null) ? kit.Hash() : 0uL);
	}

	private static uint Fold(ulong hash)
	{
		return (uint)(hash ^ (hash >> 32));
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

	public Vector3 ToWorld(Vector3 layout)
	{
		return layout - Offset();
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
}
