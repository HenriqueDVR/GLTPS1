using System.Collections.Generic;
using System.Text;
using UnityEngine;

// whole world layout from a seed, same rules as the original generator
// (GenerateEdge/GenerateNode/Node). original used physics triggers over several frames so it
// depended on timing. this does box overlap math + a fixed BFS order, one rng stream per node
public sealed class WorldLayout
{
	public sealed class LocalBox
	{
		public Vector3 Center;

		public Vector3 Size;
	}

	public sealed class PortDef
	{
		public Pose Local;

		public List<EdgeDef> Edges = new List<EdgeDef>();

		public List<GameObject> Barriers = new List<GameObject>();
	}

	public sealed class NodeDef
	{
		public GameObject Prefab;

		public List<PortDef> Ports = new List<PortDef>();

		public List<LocalBox> Boxes = new List<LocalBox>();
	}

	public sealed class EdgeDef
	{
		public GameObject Prefab;

		public Pose Entrance;

		public Pose Exit;

		public List<LocalBox> Boxes = new List<LocalBox>();

		public List<NodeDef> Nodes = new List<NodeDef>();

		public List<int> Weights = new List<int>();
	}

	public sealed class PlacedNode
	{
		public int Id;

		public NodeDef Def;

		public Pose Pose;

		// -1 for the start node
		public int EntryPort = -1;

		public PlacedEdge FromEdge;

		public PlacedEdge[] Edges;

		public GameObject[] Barriers;

		public GameObject Instance;

		public bool ChildrenQueued;
	}

	public sealed class PlacedEdge
	{
		public int Id;

		public EdgeDef Def;

		public Pose Pose;

		public PlacedNode From;

		public int FromPort;

		public PlacedNode To;

		public GameObject Instance;
	}

	private struct WorldBox
	{
		public Vector3 Center;

		public Vector3 AxisX;

		public Vector3 AxisY;

		public Vector3 AxisZ;

		public Vector3 Half;

		public Vector2 Min;

		public Vector2 Max;
	}

	private const float overlapTolerance = 0.05f;

	private const float gridCellSize = 256f;

	public readonly List<PlacedNode> Nodes = new List<PlacedNode>();

	public readonly List<PlacedEdge> Edges = new List<PlacedEdge>();

	private readonly List<WorldBox> boxes = new List<WorldBox>();

	private readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();

	private readonly List<int> candidates = new List<int>();

	public static WorldLayout Generate(NodeDef root, Pose rootPose, ulong seed, NodeDef pillar, int pillarWeight, int countThreshold, int maxNodes)
	{
		WorldLayout worldLayout = new WorldLayout();
		PlacedNode placedNode = worldLayout.AddNode(root, rootPose, null, -1);
		Queue<PlacedNode> queue = new Queue<PlacedNode>();
		queue.Enqueue(placedNode);
		int num = 0;
		while (queue.Count > 0)
		{
			PlacedNode placedNode2 = queue.Dequeue();
			WorldRandom random = new WorldRandom(WorldRandom.Derive(seed, (ulong)placedNode2.Id));
			bool includePillar = pillar != null && num >= countThreshold;
			for (int i = 0; i < placedNode2.Def.Ports.Count; i++)
			{
				if (i == placedNode2.EntryPort)
				{
					continue;
				}
				PlacedNode placedNode3 = null;
				if (worldLayout.Nodes.Count < maxNodes)
				{
					placedNode3 = worldLayout.TryExpandPort(placedNode2, i, random, includePillar ? pillar : null, pillarWeight);
				}
				if (placedNode3 != null)
				{
					queue.Enqueue(placedNode3);
				}
				else
				{
					List<GameObject> barriers = placedNode2.Def.Ports[i].Barriers;
					if (barriers.Count > 0)
					{
						placedNode2.Barriers[i] = barriers[random.Next(barriers.Count)];
					}
				}
			}
			num++;
		}
		return worldLayout;
	}

	private PlacedNode TryExpandPort(PlacedNode from, int portIndex, WorldRandom random, NodeDef pillar, int pillarWeight)
	{
		Pose port = Combine(from.Pose, from.Def.Ports[portIndex].Local);
		List<EdgeDef> list = new List<EdgeDef>(from.Def.Ports[portIndex].Edges);
		while (list.Count > 0)
		{
			int index = random.Next(list.Count);
			EdgeDef edgeDef = list[index];
			list.RemoveAt(index);
			Pose pose = new Pose(port.position - port.rotation * edgeDef.Entrance.position, port.rotation);
			List<WorldBox> list2 = ToWorld(edgeDef.Boxes, pose);
			if (Overlaps(list2, null))
			{
				continue;
			}
			Pose exit = Combine(pose, edgeDef.Exit);
			List<NodeDef> list3 = new List<NodeDef>(edgeDef.Nodes);
			List<int> list4 = new List<int>(edgeDef.Weights);
			if (pillar != null)
			{
				list3.Add(pillar);
				list4.Add(pillarWeight);
			}
			while (list3.Count > 0)
			{
				int index2 = random.NextWeightedIndex(list4);
				NodeDef nodeDef = list3[index2];
				list3.RemoveAt(index2);
				list4.RemoveAt(index2);
				List<int> list5 = new List<int>();
				for (int i = 0; i < nodeDef.Ports.Count; i++)
				{
					list5.Add(i);
				}
				while (list5.Count > 0)
				{
					int index3 = random.Next(list5.Count);
					int num = list5[index3];
					list5.RemoveAt(index3);
					Pose pose2 = NodePoseForPort(nodeDef, num, exit);
					if (!Overlaps(ToWorld(nodeDef.Boxes, pose2), list2))
					{
						PlacedEdge placedEdge = new PlacedEdge
						{
							Id = Edges.Count,
							Def = edgeDef,
							Pose = pose,
							From = from,
							FromPort = portIndex
						};
						Edges.Add(placedEdge);
						AddBoxes(list2);
						from.Edges[portIndex] = placedEdge;
						PlacedNode placedNode = AddNode(nodeDef, pose2, placedEdge, num);
						placedEdge.To = placedNode;
						return placedNode;
					}
				}
			}
		}
		return null;
	}

	private PlacedNode AddNode(NodeDef def, Pose pose, PlacedEdge fromEdge, int entryPort)
	{
		PlacedNode placedNode = new PlacedNode
		{
			Id = Nodes.Count,
			Def = def,
			Pose = pose,
			FromEdge = fromEdge,
			EntryPort = entryPort,
			Edges = new PlacedEdge[def.Ports.Count],
			Barriers = new GameObject[def.Ports.Count]
		};
		Nodes.Add(placedNode);
		AddBoxes(ToWorld(def.Boxes, pose));
		return placedNode;
	}

	// same as GenerateNode.CalculateNodeEntranceOffset
	private static Pose NodePoseForPort(NodeDef node, int portIndex, Pose exit)
	{
		Pose local = node.Ports[portIndex].Local;
		Quaternion quaternion = exit.rotation * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(local.rotation);
		return new Pose(exit.position - quaternion * local.position, quaternion);
	}

	public static Pose Combine(Pose parent, Pose local)
	{
		return new Pose(parent.position + parent.rotation * local.position, parent.rotation * local.rotation);
	}

	private static List<WorldBox> ToWorld(List<LocalBox> local, Pose pose)
	{
		List<WorldBox> list = new List<WorldBox>(local.Count);
		foreach (LocalBox item in local)
		{
			WorldBox worldBox = new WorldBox
			{
				Center = pose.position + pose.rotation * item.Center,
				AxisX = pose.rotation * Vector3.right,
				AxisY = pose.rotation * Vector3.up,
				AxisZ = pose.rotation * Vector3.forward,
				Half = new Vector3(Mathf.Abs(item.Size.x), Mathf.Abs(item.Size.y), Mathf.Abs(item.Size.z)) * 0.5f
			};
			float num = Mathf.Abs(worldBox.AxisX.x) * worldBox.Half.x + Mathf.Abs(worldBox.AxisY.x) * worldBox.Half.y + Mathf.Abs(worldBox.AxisZ.x) * worldBox.Half.z;
			float num2 = Mathf.Abs(worldBox.AxisX.z) * worldBox.Half.x + Mathf.Abs(worldBox.AxisY.z) * worldBox.Half.y + Mathf.Abs(worldBox.AxisZ.z) * worldBox.Half.z;
			worldBox.Min = new Vector2(worldBox.Center.x - num, worldBox.Center.z - num2);
			worldBox.Max = new Vector2(worldBox.Center.x + num, worldBox.Center.z + num2);
			list.Add(worldBox);
		}
		return list;
	}

	private void AddBoxes(List<WorldBox> newBoxes)
	{
		foreach (WorldBox newBox in newBoxes)
		{
			int count = boxes.Count;
			boxes.Add(newBox);
			ForEachCell(newBox, delegate(long key)
			{
				if (!grid.TryGetValue(key, out var value))
				{
					value = new List<int>();
					grid.Add(key, value);
				}
				value.Add(count);
			});
		}
	}

	private bool Overlaps(List<WorldBox> test, List<WorldBox> extra)
	{
		foreach (WorldBox item in test)
		{
			candidates.Clear();
			ForEachCell(item, delegate(long key)
			{
				if (grid.TryGetValue(key, out var value))
				{
					candidates.AddRange(value);
				}
			});
			foreach (int candidate in candidates)
			{
				if (Intersects(item, boxes[candidate]))
				{
					return true;
				}
			}
			if (extra == null)
			{
				continue;
			}
			foreach (WorldBox item2 in extra)
			{
				if (Intersects(item, item2))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static void ForEachCell(WorldBox box, System.Action<long> action)
	{
		int num = Mathf.FloorToInt(box.Min.x / gridCellSize);
		int num2 = Mathf.FloorToInt(box.Max.x / gridCellSize);
		int num3 = Mathf.FloorToInt(box.Min.y / gridCellSize);
		int num4 = Mathf.FloorToInt(box.Max.y / gridCellSize);
		for (int i = num; i <= num2; i++)
		{
			for (int j = num3; j <= num4; j++)
			{
				action(((long)i << 32) ^ (uint)j);
			}
		}
	}

	// SAT for 2 obbs, touching doesn't count
	private static bool Intersects(WorldBox a, WorldBox b)
	{
		Vector3[] array = new Vector3[3] { a.AxisX, a.AxisY, a.AxisZ };
		Vector3[] array2 = new Vector3[3] { b.AxisX, b.AxisY, b.AxisZ };
		Vector3 d = b.Center - a.Center;
		for (int i = 0; i < 3; i++)
		{
			if (Separated(array[i], a, b, d) || Separated(array2[i], a, b, d))
			{
				return false;
			}
		}
		for (int j = 0; j < 3; j++)
		{
			for (int k = 0; k < 3; k++)
			{
				Vector3 vector = Vector3.Cross(array[j], array2[k]);
				if (vector.sqrMagnitude > 1E-06f && Separated(vector.normalized, a, b, d))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool Separated(Vector3 axis, WorldBox a, WorldBox b, Vector3 d)
	{
		float num = Mathf.Abs(Vector3.Dot(a.AxisX, axis)) * a.Half.x + Mathf.Abs(Vector3.Dot(a.AxisY, axis)) * a.Half.y + Mathf.Abs(Vector3.Dot(a.AxisZ, axis)) * a.Half.z;
		float num2 = Mathf.Abs(Vector3.Dot(b.AxisX, axis)) * b.Half.x + Mathf.Abs(Vector3.Dot(b.AxisY, axis)) * b.Half.y + Mathf.Abs(Vector3.Dot(b.AxisZ, axis)) * b.Half.z;
		return Mathf.Abs(Vector3.Dot(d, axis)) >= num + num2 - overlapTolerance;
	}

	// one line per node for diffing layouts
	public string Describe()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (PlacedNode node in Nodes)
		{
			stringBuilder.Append(node.Id).Append(' ').Append(node.Def.Prefab.name);
			stringBuilder.Append(" from=").Append((node.FromEdge != null) ? node.FromEdge.From.Id : (-1));
			stringBuilder.Append(" edge=").Append((node.FromEdge != null) ? node.FromEdge.Def.Prefab.name : "-");
			stringBuilder.Append(" entry=").Append(node.EntryPort);
			stringBuilder.Append(" pos=").Append(Mathf.RoundToInt(node.Pose.position.x)).Append(',').Append(Mathf.RoundToInt(node.Pose.position.z));
			stringBuilder.Append(" yaw=").Append(Mathf.RoundToInt(node.Pose.rotation.eulerAngles.y) % 360);
			stringBuilder.Append(" barriers=");
			for (int i = 0; i < node.Barriers.Length; i++)
			{
				stringBuilder.Append((node.Barriers[i] != null) ? '#' : ((node.Edges[i] != null) ? '-' : ((i == node.EntryPort) ? '<' : '.')));
			}
			stringBuilder.Append('\n');
		}
		return stringBuilder.ToString();
	}

	public static uint Hash(string text)
	{
		uint num = 2166136261u;
		foreach (char c in text)
		{
			num = (num ^ c) * 16777619;
		}
		return num;
	}
}
