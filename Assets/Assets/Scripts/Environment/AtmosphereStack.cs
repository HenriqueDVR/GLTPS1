using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// the only thing that writes the fog and the sun. each system sets its own layer here and this puts them together last
// thing each frame: time of day, then weather, then district, then underground, then a ride's camera. local, each peer
// works it out from the same clock
[DefaultExecutionOrder(1000)]
public class AtmosphereStack : MonoBehaviour
{
	// weather, absolute as its presets are
	public Color WeatherFog;

	public float WeatherDensity;

	public float WeatherAmbient;

	public float WeatherLight;

	// how far in a storm is, 0 clear
	public float WeatherAmount;

	// district
	public float ZoneDensity = 1f;

	public Color ZoneTint = Color.clear;

	// underground, 0 up top. time of day stops mattering as it goes to 1
	public float Dark;

	public Color DarkFog;

	public float DarkDensity;

	public float DarkAmbient;

	public float DarkLight;

	// ride camera
	public float RideFog = 1f;

	private Light sun;

	private Quaternion middaySun;

	private Color sunColour = Color.white;

	private SphericalHarmonicsL2 baseProbe;

	private float probeAmount = -1f;

	private Color probeColour;

	private bool fullLight;

	private bool hasProbe;

	private struct Flake
	{
		public ParticleSystemRenderer Renderer;

		public int Property;

		public Color Colour;

		public Color Glow;
	}

	private const float nightFlakes = 0.2f;

	private static readonly int glowId = Shader.PropertyToID("_EmissionColor");

	private readonly List<Flake> flakes = new List<Flake>();

	private MaterialPropertyBlock flakeBlock;

	private float flakeAmount = -1f;

	public DayLook Look { get; private set; }

	private static readonly int starsId = Shader.PropertyToID("_SkyStars");

	// one pixel a shader got very wrong can come in huge, and bloom spreads it into a flash over the screen. plain to see
	// at night. stopNaN only takes the ones that aren't numbers
	private const float bloomClamp = 8f;

	private void Awake()
	{
		flakeBlock = new MaterialPropertyBlock();
		foreach (Volume volume in FindObjectsOfType<Volume>(true))
		{
			if (volume.profile != null && volume.profile.TryGet(out Bloom bloom))
			{
				bloom.clamp.Override(bloomClamp);
			}
		}
		WeatherFog = RenderSettings.fogColor;
		WeatherDensity = RenderSettings.fogDensity;
		WeatherAmbient = RenderSettings.ambientIntensity;
		Look = DayCurve.Noon;
	}

	public void UseSun(Light light)
	{
		sun = light;
		middaySun = light.transform.rotation;
		sunColour = light.color;
		WeatherLight = light.intensity;
	}


	// snow and rain draw unlit, at night they'd stay white and read as stars in front of the buildings
	public void UseFlakes(ParticleSystem snow, ParticleSystem[] weather)
	{
		flakes.Clear();
		AddFlakes(snow);
		foreach (ParticleSystem particles in weather)
		{
			AddFlakes(particles);
		}
	}

	private void AddFlakes(ParticleSystem particles)
	{
		ParticleSystemRenderer renderer = (particles != null) ? particles.GetComponent<ParticleSystemRenderer>() : null;
		Material material = (renderer != null) ? renderer.sharedMaterial : null;
		if (material == null)
		{
			return;
		}
		int colour = material.HasProperty("_BaseColor") ? Shader.PropertyToID("_BaseColor") : material.HasProperty("_Color") ? Shader.PropertyToID("_Color") : material.HasProperty("_TintColor") ? Shader.PropertyToID("_TintColor") : 0;
		if (colour != 0)
		{
			flakes.Add(new Flake { Renderer = renderer, Property = colour, Colour = material.GetColor(colour), Glow = material.HasProperty(glowId) ? material.GetColor(glowId) : Color.clear });
		}
	}

	private void ApplyFlakes(float amount)
	{
		if (Mathf.Abs(amount - flakeAmount) < 0.01f)
		{
			return;
		}
		flakeAmount = amount;
		foreach (Flake flake in flakes)
		{
			flakeBlock.SetColor(flake.Property, new Color(flake.Colour.r * amount, flake.Colour.g * amount, flake.Colour.b * amount, flake.Colour.a));
			// the snow's mostly this
			flakeBlock.SetColor(glowId, flake.Glow * amount);
			flake.Renderer.SetPropertyBlock(flakeBlock);
		}
	}

	private void LateUpdate()
	{
		Apply();
	}

	private void Apply()
	{
		// the sky's lighting isn't worked out yet in Awake, it reads grey
		if (!hasProbe)
		{
			hasProbe = true;
			DynamicGI.UpdateEnvironment();
			baseProbe = RenderSettings.ambientProbe;
		}
		Look = DayCurve.At(WorldClock.TimeOfDay);
		DayLook look = Look;
		Color fog = WeatherFog * look.Fog;
		fog.a = WeatherFog.a;
		float density = WeatherDensity * ZoneDensity;
		if (ZoneTint.a > 0f)
		{
			fog = Tinted(fog, ZoneTint);
		}
		RenderSettings.fogColor = Color.Lerp(fog, DarkFog, Dark);
		RenderSettings.fogDensity = Mathf.Lerp(density, Mathf.Max(density, DarkDensity), Dark) * RideFog;
		RenderSettings.ambientIntensity = Mathf.Lerp(WeatherAmbient, Mathf.Min(WeatherAmbient, DarkAmbient), Dark);
		Shader.SetGlobalFloat(starsId, look.Stars * (1f - WeatherAmount) * (1f - Dark));
		ApplyFlakes(Mathf.Lerp(nightFlakes, 1f, look.Ambient));
		if (fullLight)
		{
			return;
		}
		ApplyAmbient(Mathf.Lerp(look.Ambient, 1f, Dark), Color.Lerp(look.AmbientColour, Color.white, Dark));
		if (sun != null)
		{
			float light = WeatherLight * look.Light;
			sun.intensity = Mathf.Lerp(light, Mathf.Min(light, DarkLight), Dark);
			sun.color = sunColour * look.LightColour;
			sun.transform.rotation = Direction(look);
		}
	}

	// the sun moves round its midday spot and sinks toward the ends of the day, the moon's somewhere else, swapped while
	// neither gives any light
	private Quaternion Direction(DayLook look)
	{
		Vector3 forward = middaySun * Vector3.forward;
		float height = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;
		Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
		flat = Quaternion.AngleAxis(look.Round, Vector3.up) * flat;
		Vector3 side = Vector3.Cross(Vector3.up, flat);
		return Quaternion.LookRotation(Quaternion.AngleAxis(height * look.Height, side) * flat, Vector3.up);
	}

	private void ApplyAmbient(float amount, Color colour)
	{
		if (Mathf.Abs(amount - probeAmount) < 0.002f && colour == probeColour)
		{
			return;
		}
		probeAmount = amount;
		probeColour = colour;
		SphericalHarmonicsL2 probe = baseProbe;
		for (int coefficient = 0; coefficient < 9; coefficient++)
		{
			probe[0, coefficient] *= amount * colour.r;
			probe[1, coefficient] *= amount * colour.g;
			probe[2, coefficient] *= amount * colour.b;
		}
		RenderSettings.ambientProbe = probe;
	}

	// the map's camera photographs the ground for the paper, it has to read at night too
	public void FullLight(bool on)
	{
		fullLight = on;
		if (on)
		{
			ApplyAmbient(1f, Color.white);
			if (sun != null)
			{
				sun.intensity = Mathf.Max(WeatherLight, sun.intensity);
				sun.color = sunColour;
			}
		}
		else
		{
			Apply();
		}
	}

	// the hue moves, the brightness doesn't: a darker haze read as dusk
	private static Color Tinted(Color colour, Color hue)
	{
		Color tinted = new Color(colour.r * hue.r, colour.g * hue.g, colour.b * hue.b, colour.a);
		float before = colour.grayscale;
		float after = tinted.grayscale;
		if (after > 0.0001f)
		{
			float lift = before / after;
			tinted = new Color(Mathf.Clamp01(tinted.r * lift), Mathf.Clamp01(tinted.g * lift), Mathf.Clamp01(tinted.b * lift), colour.a);
		}
		return tinted;
	}
}
