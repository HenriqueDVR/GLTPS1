using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// far view of the elevator tower and the level above. meshes-only copy of the pillar piece in thin fog,
// a broken ceiling, and a few far pillars that never get close
public class DistantPillar : MonoBehaviour
{
	// tower parts in nodeCOLUMN
	private static readonly string[] towerParts = { "Cylinder", "Thingys", "Elevator" };

	private const float swapDistance = 250f;

	// shortest camera far clip (first person), cinemachine sets it after we run
	private const float drawDistance = 1400f;

	// past this copies get pulled in and shrunk
	private const float farthest = 1200f;

	// share of the world's fog
	private const float fogShare = 0.12f;

	// ceiling sits this far under the column's top
	private const float ceilingSink = 45f;

	private const float ceilingSize = 7000f;

	private const float ceilingTile = 140f;

	private const float ceilingThickness = 45f;

	private const int extraPillars = 3;

	private static readonly Color structure = new Color(0.075f, 0.087f, 0.105f);

	private static DistantPillar instance;

	private sealed class Distant
	{
		public Transform Copy;

		public Material Material;

		// from the goal, layout space
		public Vector3 Offset;

		public bool Goal;

		// far ones never get closer than this
		public float Nearest;

		public float Faint;
	}

	private readonly List<Distant> pillars = new List<Distant>();

	private WorldGenerator world;

	private WorldLayout.PlacedNode pillar;

	private Transform ceiling;

	private Material ceilingMaterial;

	// cloud layers under the ceiling
	private readonly List<Transform> clouds = new List<Transform>();

	private readonly List<Material> cloudMaterials = new List<Material>();

	// depth under the ceiling, cover
	private static readonly Vector2[] cloudLayers = { new Vector2(60f, 0.45f), new Vector2(150f, 0.3f) };

	// foot and height of the column in the copy
	private float foot;

	private float height;

	private bool hidden;

	private void Start()
	{
		world = GetComponent<WorldGenerator>();
		pillar = world.Layout != null ? world.Pillar() : null;
		if (pillar == null)
		{
			Destroy(this);
			return;
		}
		instance = this;
		Shader shader = Resources.Load<Shader>("DistantSilhouette");
		pillars.Add(Make(shader, Vector3.zero, Vector3.one, true, 0f, 1f));
		// same spots every time for a seed
		System.Random random = new System.Random((int)(world.Seed & 0x7fffffff));
		List<Vector3> extras = new List<Vector3>();
		for (int i = 0; i < extraPillars; i++)
		{
			float angle = (i + (float)random.NextDouble() * 0.6f) / extraPillars * Mathf.PI * 2f;
			float reach = 2300f + (float)random.NextDouble() * 900f;
			Vector3 offset = new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach);
			extras.Add(offset);
			pillars.Add(Make(shader, offset, new Vector3(0.6f, 1f, 0.6f), false, farthest, 0.55f));
		}
		ceilingMaterial = new Material(Resources.Load<Shader>("DistantCeiling"));
		ceiling = Ceiling(extras, random).transform;
		Shader cloudShader = Resources.Load<Shader>("DistantCloud");
		for (int i = 0; i < cloudLayers.Length; i++)
		{
			GameObject layer = GameObject.CreatePrimitive(PrimitiveType.Quad);
			Destroy(layer.GetComponent<Collider>());
			layer.name = "DistantCloud";
			layer.transform.localScale = Vector3.one * ceilingSize * 1.2f;
			Material material = new Material(cloudShader);
			material.SetFloat("_Cover", cloudLayers[i].y);
			material.SetFloat("_Scale", 380f + i * 220f);
			material.SetVector("_Drift", new Vector4(3f + i * 2f, 0f, 1.5f - i, 0f));
			MeshRenderer layerRenderer = layer.GetComponent<MeshRenderer>();
			layerRenderer.sharedMaterial = material;
			layerRenderer.shadowCastingMode = ShadowCastingMode.Off;
			layerRenderer.receiveShadows = false;
			clouds.Add(layer.transform);
			cloudMaterials.Add(material);
		}
	}

	private Distant Make(Shader shader, Vector3 offset, Vector3 shape, bool goal, float nearest, float faint)
	{
		Distant distant = new Distant
		{
			Material = new Material(shader),
			Offset = offset,
			Goal = goal,
			Nearest = nearest,
			Faint = faint
		};
		distant.Copy = Copy(pillar.Def.Prefab, distant.Material).transform;
		distant.Copy.GetChild(0).localScale = shape;
		return distant;
	}

	private void OnDestroy()
	{
		foreach (Distant distant in pillars)
		{
			if (distant.Copy != null)
			{
				Destroy(distant.Copy.gameObject);
			}
		}
		if (ceiling != null)
		{
			Destroy(ceiling.gameObject);
		}
		foreach (Transform layer in clouds)
		{
			if (layer != null)
			{
				Destroy(layer.gameObject);
			}
		}
	}

	// keep them out of the map picture
	public static void Hide(bool hide)
	{
		if (instance == null)
		{
			return;
		}
		instance.hidden = hide;
		foreach (Distant distant in instance.pillars)
		{
			distant.Copy.gameObject.SetActive(!hide && distant.Copy.gameObject.activeSelf);
		}
		instance.ceiling.gameObject.SetActive(!hide);
		foreach (Transform layer in instance.clouds)
		{
			layer.gameObject.SetActive(!hide);
		}
	}

	// under an inactive parent so its scripts never wake, then strip to meshes
	private GameObject Copy(GameObject prefab, Material material)
	{
		GameObject holder = new GameObject("DistantPillar");
		holder.SetActive(false);
		GameObject copy = Instantiate(prefab, holder.transform);
		copy.transform.localPosition = Vector3.zero;
		copy.transform.localRotation = Quaternion.identity;
		// a few passes, some components need others gone first
		for (int pass = 0; pass < 4; pass++)
		{
			foreach (Component component in copy.GetComponentsInChildren<Component>(includeInactive: true))
			{
				if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer))
				{
					DestroyImmediate(component);
				}
			}
		}
		// untag, and turn the parts on (the piece's scripts normally do)
		foreach (Transform part in copy.GetComponentsInChildren<Transform>(includeInactive: true))
		{
			part.gameObject.tag = "Untagged";
			part.gameObject.layer = 0;
			part.gameObject.SetActive(true);
		}
		bool any = false;
		Bounds bounds = default;
		foreach (MeshRenderer meshRenderer in copy.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
		{
			// just the tower, not the walls or the next level's slabs
			if (System.Array.IndexOf(towerParts, meshRenderer.name) < 0)
			{
				Destroy(meshRenderer.gameObject);
				continue;
			}
			Material[] materials = new Material[meshRenderer.sharedMaterials.Length];
			for (int i = 0; i < materials.Length; i++)
			{
				materials[i] = material;
			}
			meshRenderer.sharedMaterials = materials;
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = false;
			MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
			if (filter != null && filter.sharedMesh != null)
			{
				// in the holder's space, it sits at the origin
				foreach (Vector3 corner in Corners(filter.sharedMesh.bounds))
				{
					Vector3 point = holder.transform.InverseTransformPoint(meshRenderer.transform.TransformPoint(corner));
					if (!any)
					{
						bounds = new Bounds(point, Vector3.zero);
						any = true;
					}
					bounds.Encapsulate(point);
				}
			}
		}
		foot = bounds.min.y;
		height = Mathf.Max(1f, bounds.size.y);
		holder.SetActive(true);
		return holder;
	}

	private static Vector3[] Corners(Bounds b)
	{
		Vector3 min = b.min;
		Vector3 max = b.max;
		return new Vector3[8]
		{
			new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
			new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z)
		};
	}

	// slabs with holes, solid round the pillars. rim walls marked by vertex colour
	private GameObject Ceiling(List<Vector3> extras, System.Random random)
	{
		int count = Mathf.CeilToInt(ceilingSize / ceilingTile);
		float half = ceilingSize * 0.5f;
		float seedX = (float)random.NextDouble() * 1000f;
		float seedY = (float)random.NextDouble() * 1000f;
		bool[,] kept = new bool[count, count];
		for (int x = 0; x < count; x++)
		{
			for (int y = 0; y < count; y++)
			{
				Vector3 centre = new Vector3((x + 0.5f) * ceilingTile - half, 0f, (y + 0.5f) * ceilingTile - half);
				float torn = Mathf.PerlinNoise(seedX + x * 0.11f, seedY + y * 0.11f) + Mathf.PerlinNoise(seedX + x * 0.37f, seedY - y * 0.37f) * 0.35f;
				// thins out toward the edge
				float edge = Mathf.InverseLerp(half * 0.65f, half, centre.magnitude);
				bool keep = torn - edge * 0.7f > 0.5f;
				if (centre.magnitude < 600f)
				{
					keep = true;
				}
				foreach (Vector3 extra in extras)
				{
					keep |= (centre - extra).magnitude < 350f;
				}
				kept[x, y] = keep;
			}
		}
		List<Vector3> vertices = new List<Vector3>();
		List<Color> colours = new List<Color>();
		List<int> triangles = new List<int>();
		Color underside = Color.black;
		Color rim = Color.white;
		for (int x = 0; x < count; x++)
		{
			for (int y = 0; y < count; y++)
			{
				if (!kept[x, y])
				{
					continue;
				}
				Vector3 a = Corner(x, y, count, half, seedX);
				Vector3 b = Corner(x + 1, y, count, half, seedX);
				Vector3 c = Corner(x + 1, y + 1, count, half, seedX);
				Vector3 d = Corner(x, y + 1, count, half, seedX);
				Quad(vertices, colours, triangles, a, b, c, d, underside);
				Vector3 up = Vector3.up * ceilingThickness;
				if (x == 0 || !kept[x - 1, y])
				{
					Quad(vertices, colours, triangles, d, a, a + up, d + up, rim);
				}
				if (x == count - 1 || !kept[x + 1, y])
				{
					Quad(vertices, colours, triangles, b, c, c + up, b + up, rim);
				}
				if (y == 0 || !kept[x, y - 1])
				{
					Quad(vertices, colours, triangles, a, b, b + up, a + up, rim);
				}
				if (y == count - 1 || !kept[x, y + 1])
				{
					Quad(vertices, colours, triangles, c, d, d + up, c + up, rim);
				}
			}
		}
		Mesh mesh = new Mesh { name = "DistantCeiling", indexFormat = IndexFormat.UInt32 };
		mesh.SetVertices(vertices);
		mesh.SetColors(colours);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateBounds();
		GameObject obj = new GameObject("DistantCeiling", typeof(MeshFilter), typeof(MeshRenderer));
		obj.GetComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = ceilingMaterial;
		meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
		return obj;
	}

	// shared corners jitter the same, no cracks
	private static Vector3 Corner(int x, int y, int count, float half, float seed)
	{
		float jx = Mathf.PerlinNoise(seed + x * 1.7f, y * 1.3f) - 0.5f;
		float jy = Mathf.PerlinNoise(x * 1.1f, seed + y * 1.9f) - 0.5f;
		float sag = Mathf.PerlinNoise(seed + x * 0.5f, seed + y * 0.5f) - 0.5f;
		return new Vector3(x * ceilingTile - half + jx * ceilingTile * 0.6f, sag * 16f, y * ceilingTile - half + jy * ceilingTile * 0.6f);
	}

	private static void Quad(List<Vector3> vertices, List<Color> colours, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
	{
		int first = vertices.Count;
		vertices.Add(a);
		vertices.Add(b);
		vertices.Add(c);
		vertices.Add(d);
		for (int i = 0; i < 4; i++)
		{
			colours.Add(colour);
		}
		triangles.Add(first);
		triangles.Add(first + 1);
		triangles.Add(first + 2);
		triangles.Add(first);
		triangles.Add(first + 2);
		triangles.Add(first + 3);
	}

	private void LateUpdate()
	{
		Camera camera = Camera.main;
		if (camera == null || hidden)
		{
			return;
		}
		Vector3 eye = camera.transform.position;
		Vector3 goal = world.ToWorld(pillar.Pose.position);
		float density = RenderSettings.fog ? RenderSettings.fogDensity : 0f;
		Color fog = RenderSettings.fogColor;
		foreach (Distant distant in pillars)
		{
			Vector3 real = goal + distant.Offset;
			float distance = Vector3.Distance(eye, real);
			bool near = distant.Goal && pillar.Instance != null && pillar.Instance.activeInHierarchy && distance < swapDistance;
			distant.Copy.gameObject.SetActive(!near);
			float seems = Mathf.Max(distance, distant.Nearest);
			float drawn = Mathf.Min(seems, farthest);
			float pull = drawn / Mathf.Max(distance, 1f);
			float shrink = drawn / seems;
			distant.Copy.SetPositionAndRotation(eye + (real - eye) * pull, pillar.Pose.rotation);
			distant.Copy.localScale = Vector3.one * shrink;
			// thin fog far off, world fog up close so the swap doesn't pop
			float thin = Mathf.Exp(-density * fogShare * seems);
			float worldFog = Mathf.Exp(-density * seems);
			float visibility = Mathf.Lerp(worldFog, thin, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(swapDistance, 800f, seems)));
			distant.Material.SetColor("_FogColor", fog);
			distant.Material.SetColor("_Tint", structure);
			distant.Material.SetFloat("_Visibility", visibility * distant.Faint);
			distant.Material.SetFloat("_Base", distant.Copy.position.y + foot * shrink);
			distant.Material.SetFloat("_Height", height * shrink);
		}
		ceiling.position = goal + Vector3.up * (foot + height - ceilingSink);
		ceilingMaterial.SetColor("_FogColor", fog);
		// thinner still or it blends into the column
		ceilingMaterial.SetFloat("_Density", density * fogShare * 0.5f);
		ceilingMaterial.SetFloat("_FarFade", drawDistance);
		ceilingMaterial.SetFloat("_Strength", 0.7f);
		for (int i = 0; i < clouds.Count; i++)
		{
			clouds[i].SetPositionAndRotation(ceiling.position - Vector3.up * cloudLayers[i].x, Quaternion.Euler(90f, 0f, 0f));
			cloudMaterials[i].SetColor("_FogColor", fog);
			cloudMaterials[i].SetFloat("_FarFade", drawDistance);
		}
	}
}
