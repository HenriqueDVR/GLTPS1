using UnityEngine;

// what picks your stop from a way off (17 section 3): steam over a pump house, a lamp lit over a doorway or on a depot's
// tower once it's getting dark. far tiles have no props, so these are their own small pool, kept on the places nearest
// the girl out to 1.5 km: a halo where the lamp's bulb is (the street lamps' mint), and a real light on the nearest few
// so the doorway's lit when you get there. local, a pure function of the plan and the clock
public class PlacePulls : MonoBehaviour
{
	private const int steamCount = 6;

	private const int lightCount = 12;

	// the nearest this many get a real light
	private const int litCount = 3;

	private const float everySeconds = 1f;

	private const double reach = 1500.0;

	private const int cellsOut = 3;

	private const float haloSize = 1.4f;

	private LayerPlan plan;

	private TileStreamer tiles;

	private Material steamMaterial;

	private Material haloMaterial;

	private ParticleSystem[] steam;

	private Transform[] lights;

	private Light[] lamps;

	private float next;

	private readonly System.Collections.Generic.List<Place> near = new System.Collections.Generic.List<Place>();

	private void Start()
	{
		Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
		steam = new ParticleSystem[steamCount];
		lights = new Transform[lightCount];
		lamps = new Light[lightCount];
		if (particles != null)
		{
			steamMaterial = Transparent(particles, Puff(), Color.white);
			for (int i = 0; i < steamCount; i++)
			{
				steam[i] = MakeSteam();
			}
			haloMaterial = Transparent(particles, Halo(), new Color(0.74f, 1f, 0.9f, 0.85f));
			for (int i = 0; i < lightCount; i++)
			{
				GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
				Destroy(quad.GetComponent<Collider>());
				quad.name = "place light";
				quad.transform.SetParent(transform, false);
				quad.transform.localScale = Vector3.one * haloSize;
				MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
				renderer.sharedMaterial = haloMaterial;
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				if (i < litCount)
				{
					GameObject holder = new GameObject("place lamp");
					holder.transform.SetParent(quad.transform, false);
					Light lamp = holder.AddComponent<Light>();
					lamp.type = LightType.Point;
					lamp.color = new Color(0.86f, 1f, 0.93f);
					lamp.range = 9f;
					lamp.intensity = 1.6f;
					lamp.shadows = LightShadows.None;
					lamps[i] = lamp;
				}
				quad.SetActive(false);
				lights[i] = quad.transform;
			}
		}
	}

	private static Material Transparent(Shader shader, Texture2D texture, Color colour)
	{
		Material material = new Material(shader);
		material.SetFloat("_Surface", 1f);
		material.SetFloat("_Blend", 0f);
		material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
		material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
		material.SetFloat("_ZWrite", 0f);
		material.renderQueue = 3000;
		material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		material.SetOverrideTag("RenderType", "Transparent");
		material.SetColor("_BaseColor", colour);
		material.SetTexture("_BaseMap", texture);
		return material;
	}

	// a lamp's halo: a bright middle, a soft edge, few steps of it like the street lamps' bloom
	private Texture2D halo;

	private Texture2D Halo()
	{
		const int size = 32;
		halo = new Texture2D(size, size, TextureFormat.RGBA32, false);
		halo.filterMode = FilterMode.Point;
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float d = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f).magnitude * 2f;
				float a = Mathf.Clamp01(1f - d);
				a = Mathf.Round(a * a * 6f) / 6f;
				halo.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a + ((d < 0.18f) ? 0.5f : 0f))));
			}
		}
		halo.Apply();
		return halo;
	}

	// a soft round blot
	private Texture2D puff;

	private Texture2D Puff()
	{
		const int size = 32;
		puff = new Texture2D(size, size, TextureFormat.RGBA32, false);
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float d = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f).magnitude * 2f;
				puff.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
			}
		}
		puff.Apply();
		return puff;
	}

	private ParticleSystem MakeSteam()
	{
		GameObject holder = new GameObject("place steam");
		holder.transform.SetParent(transform, false);
		ParticleSystem system = holder.AddComponent<ParticleSystem>();
		system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		ParticleSystem.MainModule main = system.main;
		main.loop = true;
		main.startLifetime = 9f;
		main.startSpeed = 2.4f;
		main.startSize = new ParticleSystem.MinMaxCurve(6f, 13f);
		main.startColor = new Color(0.97f, 0.97f, 0.95f, 0.85f);
		main.maxParticles = 60;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		ParticleSystem.EmissionModule emission = system.emission;
		emission.rateOverTime = 5f;
		ParticleSystem.ShapeModule shape = system.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 0.8f;
		ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
		size.enabled = true;
		size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
		ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
		colour.enabled = true;
		Gradient fade = new Gradient();
		fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
		colour.color = fade;
		ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
		renderer.sharedMaterial = steamMaterial;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		return system;
	}

	private void OnDestroy()
	{
		if (steamMaterial != null)
		{
			Destroy(steamMaterial);
		}
		if (puff != null)
		{
			Destroy(puff);
		}
		if (haloMaterial != null)
		{
			Destroy(haloMaterial);
		}
		if (halo != null)
		{
			Destroy(halo);
		}
	}

	// the halos face the camera
	private void LateUpdate()
	{
		Camera eye = Camera.main;
		if (eye == null)
		{
			return;
		}
		Quaternion facing = eye.transform.rotation;
		for (int i = 0; i < lights.Length; i++)
		{
			if (lights[i] != null && lights[i].gameObject.activeSelf)
			{
				lights[i].rotation = facing;
			}
		}
	}

	private void Update()
	{
		if (Time.time < next)
		{
			return;
		}
		next = Time.time + everySeconds;
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		tiles = GameSession.Get<TileStreamer>();
		if (world == null || world.Plan == null || tiles == null || Girls.Local == null)
		{
			return;
		}
		plan = world.Plan;
		Vector3 offset = SaveScene.Offset();
		Vector3 at = Girls.Local.transform.position + offset;
		// under the deck you can't see any of it
		bool under = at.y < tiles.GroundY - 4f;
		XZ here = new XZ(at.x, at.z);
		near.Clear();
		int cx = Gen2.Cell(here.X, PlacePlan.Cell);
		int cz = Gen2.Cell(here.Z, PlacePlan.Cell);
		for (int x = cx - cellsOut; x <= cx + cellsOut; x++)
		{
			for (int z = cz - cellsOut; z <= cz + cellsOut; z++)
			{
				if (plan.Places.At(x, z, out Place place) && place.Center.DistanceTo(here) < reach)
				{
					near.Add(place);
				}
			}
		}
		near.Sort((a, b) => a.Center.DistanceTo(here).CompareTo(b.Center.DistanceTo(here)));
		DayPhase phase = WorldClock.Phase;
		bool dark = phase == DayPhase.Dusk || phase == DayPhase.Night;
		int s = 0;
		int l = 0;
		foreach (Place place in near)
		{
			if (under)
			{
				break;
			}
			Vector3 u = new Vector3((float)place.Axis.X, 0f, (float)place.Axis.Z);
			Vector3 v = new Vector3(-u.z, 0f, u.x);
			Vector3 middle = new Vector3((float)place.Center.X, tiles.GroundY, (float)place.Center.Z);
			if (place.Kind == PlaceKind.PumpHouse && s < steamCount && steam[s] != null)
			{
				// out of the stack's rain cap
				Vector3 stack = middle + u * ((float)place.RoomU * 0.5f) + Vector3.up * (PlaceArt.PumpHeight + 0.2f + PlaceArt.StackTop + 0.3f) - offset;
				steam[s].transform.position = stack;
				if (!steam[s].isPlaying)
				{
					steam[s].Play();
				}
				s++;
			}
			else if (dark && l < lightCount && lights[l] != null)
			{
				Vector3 bulb;
				if (place.Kind == PlaceKind.FuelDepot)
				{
					// on the tower's top
					float top = 16f;
					float along = 0f;
					if (tiles.LandmarkSize(PlaceArt.DepotTower, out Vector3 size))
					{
						top = size.y * PlaceArt.DepotFit(place, size, out along, out _);
					}
					bulb = middle + u * along + Vector3.up * (top + 0.6f);
				}
				else
				{
					PlaceArt.Lamp(place, out Vector3 mount, out Vector3 facing);
					Vector3 b = PlaceArt.Bulb(mount, facing);
					bulb = middle + u * b.x + v * b.z + Vector3.up * b.y;
				}
				lights[l].position = bulb - offset;
				lights[l].gameObject.SetActive(true);
				l++;
			}
		}
		for (; s < steamCount; s++)
		{
			if (steam[s] != null && steam[s].isPlaying)
			{
				steam[s].Stop(true, ParticleSystemStopBehavior.StopEmitting);
			}
		}
		for (; l < lightCount; l++)
		{
			if (lights[l] != null && lights[l].gameObject.activeSelf)
			{
				lights[l].gameObject.SetActive(false);
			}
		}
	}
}
