using UnityEngine;

// what picks your stop from a way off (17 section 3): steam over a pump house, a light in a doorway or on a depot's crane
// once it's getting dark. far tiles have no props, so these are their own small pool, kept on the places nearest the
// girl out to 1.5 km. local, a pure function of the plan and the clock
public class PlacePulls : MonoBehaviour
{
	private const int steamCount = 6;

	private const int lightCount = 12;

	private const float everySeconds = 1f;

	private const double reach = 1500.0;

	private const int cellsOut = 3;

	private LayerPlan plan;

	private TileStreamer tiles;

	private Material steamMaterial;

	private Material lightMaterial;

	private ParticleSystem[] steam;

	private Transform[] lights;

	private float next;

	private readonly System.Collections.Generic.List<Place> near = new System.Collections.Generic.List<Place>();

	private void Start()
	{
		Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
		Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
		steam = new ParticleSystem[steamCount];
		lights = new Transform[lightCount];
		if (particles != null)
		{
			steamMaterial = new Material(particles);
			steamMaterial.SetFloat("_Surface", 1f);
			steamMaterial.SetFloat("_Blend", 0f);
			steamMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
			steamMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			steamMaterial.SetFloat("_ZWrite", 0f);
			steamMaterial.renderQueue = 3000;
			steamMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			steamMaterial.SetOverrideTag("RenderType", "Transparent");
			steamMaterial.SetColor("_BaseColor", Color.white);
			steamMaterial.SetTexture("_BaseMap", Puff());
			for (int i = 0; i < steamCount; i++)
			{
				steam[i] = MakeSteam();
			}
		}
		if (unlit != null)
		{
			lightMaterial = new Material(unlit);
			lightMaterial.SetColor("_BaseColor", new Color(1f, 0.85f, 0.55f));
			for (int i = 0; i < lightCount; i++)
			{
				GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Sphere);
				Destroy(quad.GetComponent<Collider>());
				quad.name = "place light";
				quad.transform.SetParent(transform, false);
				quad.transform.localScale = Vector3.one * 0.9f;
				MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
				renderer.sharedMaterial = lightMaterial;
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				quad.SetActive(false);
				lights[i] = quad.transform;
			}
		}
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
		if (lightMaterial != null)
		{
			Destroy(lightMaterial);
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
			if (place.Kind == PlaceKind.PumpHouse && s < steamCount && steam[s] != null)
			{
				Vector3 u = new Vector3((float)place.Axis.X, 0f, (float)place.Axis.Z);
				Vector3 stack = new Vector3((float)place.Center.X, tiles.GroundY + (float)place.Height + 4.8f, (float)place.Center.Z) + u * ((float)place.RoomU * 0.5f) - offset;
				steam[s].transform.position = stack;
				if (!steam[s].isPlaying)
				{
					steam[s].Play();
				}
				s++;
			}
			else if (dark && l < lightCount && lights[l] != null)
			{
				XZ door = place.DoorAt;
				float high = (place.Kind == PlaceKind.FuelDepot) ? 16.8f : (float)place.Height + 0.5f;
				Vector3 lamp = (place.Kind == PlaceKind.FuelDepot) ? new Vector3((float)place.Center.X, tiles.GroundY + high, (float)place.Center.Z) : new Vector3((float)door.X, tiles.GroundY + high, (float)door.Z);
				lights[l].position = lamp - offset;
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
