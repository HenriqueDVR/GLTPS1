#if DUMP_WORLDGEN
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// debug: dumps worldgen prefab structure to a txt
public static class WorldGenDump
{
	private static readonly HashSet<GameObject> visited = new HashSet<GameObject>();

	private static readonly Queue<GameObject> queue = new Queue<GameObject>();

	private static readonly StringBuilder sb = new StringBuilder();

	public static void Run(NodeData nodeData)
	{
		sb.AppendLine("NodeData: spawnThreshold=" + nodeData.spawnThreshold + " countThreshold=" + nodeData.countThreshold + " pillarSpawnChance=" + nodeData.pillarSpawnChance + " pillarEdge=" + Name(nodeData.pillarEdge));
		Enqueue(nodeData.pillarEdge);
		sb.AppendLine();
		sb.AppendLine("=== SCENE OBJECTS");
		foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>(includeInactive: true))
		{
			if (mb is Node || mb is GenerateEdge || mb is GenerateNode || mb is ProceduralEnvironmentGeneration || mb is ItemSpawnManager)
			{
				sb.AppendLine(mb.GetType().Name + " on " + Path(mb.transform) + " active=" + mb.gameObject.activeInHierarchy + " enabled=" + mb.enabled + " pos=" + V(mb.transform.position) + " rot=" + V(mb.transform.eulerAngles));
				CollectRefs(mb);
			}
		}
		while (queue.Count > 0)
		{
			DumpPrefab(queue.Dequeue());
		}
		string path = System.IO.Path.Combine(Application.persistentDataPath, "worldgen_dump.txt");
		File.WriteAllText(path, sb.ToString());
		Debug.Log("WorldGenDump written to " + path);
	}

	private static void DumpPrefab(GameObject prefab)
	{
		sb.AppendLine();
		sb.AppendLine("=== PREFAB " + prefab.name + " tag=" + prefab.tag + " layer=" + prefab.layer + " rootPos=" + V(prefab.transform.position) + " rootRot=" + V(prefab.transform.eulerAngles) + " scale=" + V(prefab.transform.lossyScale));
		DumpTree(prefab.transform, prefab.transform, 0);
		foreach (MonoBehaviour mb in prefab.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
		{
			if (mb != null)
			{
				DumpFields(mb, prefab.transform);
				CollectRefs(mb);
			}
		}
	}

	private static void DumpTree(Transform t, Transform root, int depth)
	{
		bool interesting = depth <= 2;
		List<string> parts = new List<string>();
		foreach (Component c in t.GetComponents<Component>())
		{
			if (c == null || c is Transform)
			{
				continue;
			}
			string text = c.GetType().Name;
			if (c is BoxCollider boxCollider)
			{
				text += "(trig=" + boxCollider.isTrigger + " c=" + V(boxCollider.center) + " s=" + V(boxCollider.size) + ")";
				interesting = interesting || boxCollider.isTrigger;
			}
			else if (c is Collider collider)
			{
				text += "(trig=" + collider.isTrigger + " b=" + V(collider.bounds.size) + ")";
				interesting = interesting || collider.isTrigger;
			}
			else if (c is MonoBehaviour)
			{
				interesting = true;
			}
			parts.Add(text);
		}
		if (interesting)
		{
			Vector3 v = root.InverseTransformPoint(t.position);
			Vector3 v2 = (Quaternion.Inverse(root.rotation) * t.rotation).eulerAngles;
			sb.AppendLine(new string(' ', depth * 2) + t.name + " [tag=" + t.tag + " layer=" + t.gameObject.layer + " active=" + t.gameObject.activeSelf + "] rel=" + V(v) + " relRot=" + V(v2) + " scale=" + V(t.localScale) + " : " + string.Join(", ", parts));
		}
		foreach (Transform item in t)
		{
			DumpTree(item, root, depth + 1);
		}
	}

	private static void DumpFields(MonoBehaviour mb, Transform root)
	{
		if (!(mb is Node) && !(mb is GenerateEdge) && !(mb is GenerateNode) && !(mb is ProceduralEnvironmentGeneration) && !(mb is ItemSpawnManager) && !(mb is KanazawaInstanceManager) && !(mb is StartEnvironmentTrace))
		{
			return;
		}
		sb.AppendLine("  # " + mb.GetType().Name + " on " + mb.name);
		foreach (FieldInfo field in mb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			object value = field.GetValue(mb);
			sb.AppendLine("    " + field.Name + " = " + Describe(value, root));
		}
	}

	private static void CollectRefs(MonoBehaviour mb)
	{
		foreach (FieldInfo field in mb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			object value = field.GetValue(mb);
			if (value is GameObject go)
			{
				Enqueue(go);
			}
			else if (value is IEnumerable enumerable && !(value is string))
			{
				foreach (object item in enumerable)
				{
					if (item is GameObject go2)
					{
						Enqueue(go2);
					}
				}
			}
		}
	}

	private static void Enqueue(GameObject go)
	{
		if (go != null && !go.scene.IsValid() && go.transform.parent == null && visited.Add(go))
		{
			queue.Enqueue(go);
		}
	}

	private static string Describe(object value, Transform root)
	{
		if (value == null)
		{
			return "null";
		}
		if (value is Object @object && @object == null)
		{
			return "null(destroyed)";
		}
		if (value is Transform transform)
		{
			return "T:" + transform.name + " rel=" + V(root.InverseTransformPoint(transform.position)) + " relRot=" + V((Quaternion.Inverse(root.rotation) * transform.rotation).eulerAngles);
		}
		if (value is GameObject go)
		{
			return "GO:" + Name(go) + (go.scene.IsValid() ? "(scene)" : "(prefab)");
		}
		if (value is Component component)
		{
			return component.GetType().Name + ":" + component.name;
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			List<string> list = new List<string>();
			foreach (object item in enumerable)
			{
				list.Add(Describe(item, root));
			}
			return "[" + string.Join("; ", list) + "]";
		}
		return value.ToString();
	}

	private static string Name(GameObject go)
	{
		if (!(go == null))
		{
			return go.name;
		}
		return "null";
	}

	private static string Path(Transform t)
	{
		if (!(t.parent == null))
		{
			return Path(t.parent) + "/" + t.name;
		}
		return t.name;
	}

	private static string V(Vector3 v)
	{
		return "(" + v.x.ToString("0.###") + "," + v.y.ToString("0.###") + "," + v.z.ToString("0.###") + ")";
	}
}
#endif

#if DUMP_WORLDGEN
// debug: F9 top down cam, F10/F11 zoom
public class WorldGenOverheadView : MonoBehaviour
{
	private Camera overheadCamera;

	private NodeData nodeData;

	private bool fogWasOn;

	private float size = 1500f;

	private void Start()
	{
		nodeData = GetComponent<NodeData>();
	}

	private float nextPositionLog;

	private void Update()
	{
		if (Time.time >= nextPositionLog && nodeData.player != null)
		{
			nextPositionLog = Time.time + 2f;
			Debug.Log("WorldGenPlayer " + nodeData.player.name + " at " + nodeData.player.transform.position);
		}
		UnityEngine.InputSystem.Keyboard current = UnityEngine.InputSystem.Keyboard.current;
		if (current == null)
		{
			return;
		}
		if (current.f9Key.wasPressedThisFrame)
		{
			if (overheadCamera == null)
			{
				GameObject gameObject = new GameObject("WorldGenOverheadCamera");
				overheadCamera = gameObject.AddComponent<Camera>();
				overheadCamera.orthographic = true;
				overheadCamera.depth = 100f;
				overheadCamera.farClipPlane = 6000f;
				overheadCamera.clearFlags = CameraClearFlags.SolidColor;
				overheadCamera.backgroundColor = Color.black;
				fogWasOn = RenderSettings.fog;
				RenderSettings.fog = false;
			}
			else
			{
				Object.Destroy(overheadCamera.gameObject);
				overheadCamera = null;
				RenderSettings.fog = fogWasOn;
			}
		}
		if (current.f10Key.wasPressedThisFrame)
		{
			size *= 0.5f;
		}
		if (current.f11Key.wasPressedThisFrame)
		{
			size *= 2f;
		}
		if (overheadCamera != null && nodeData.player != null)
		{
			overheadCamera.orthographicSize = size;
			overheadCamera.transform.SetPositionAndRotation(nodeData.player.transform.position + Vector3.up * 3000f, Quaternion.Euler(90f, 0f, 0f));
		}
	}
}
#endif
