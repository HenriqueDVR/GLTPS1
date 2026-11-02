using UnityEngine;
using UnityEngine.Rendering;

// world gen v2: the district the local girl is in thickens or thins the weather's fog (DistrictDef.Fog, 04 weather:
// day/night, then the weather, then the zone). eased, so crossing into the next district there's no step.
// it goes first, so the underground and the ride camera work over its fog. a change the weather made is told from
// what everything left at the end of the last frame, so it never scales its own or anyone else's writing again.
// local only, each peer its own
[DefaultExecutionOrder(-50)]
public class ZoneAtmosphere : MonoBehaviour
{
	// a share a second
	private const float rate = 0.15f;

	private LayerPlan plan;

	private FloatingOrigin floatingOrigin;

	private float scale = -1f;

	private float weatherDensity = -1f;

	// the fog as the frame was drawn
	private float drawn = -1f;

	// the district's hue the same way (DistrictDef.Tint), over the weather's colour
	private Color tint = Color.clear;

	private Color weatherColour;

	private Color drawnColour;

	private bool hasWeatherColour;

	public void Init(LayerPlan plan, FloatingOrigin origin)
	{
		this.plan = plan;
		floatingOrigin = origin;
	}

	private void OnEnable()
	{
		RenderPipelineManager.beginContextRendering += Drawn;
	}

	private void OnDisable()
	{
		RenderPipelineManager.beginContextRendering -= Drawn;
	}

	private void Drawn(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
	{
		drawn = RenderSettings.fogDensity;
		drawnColour = RenderSettings.fogColor;
	}

	private void LateUpdate()
	{
		Girl girl = Girls.Local;
		if (plan == null || girl == null)
		{
			return;
		}
		Vector3 at = girl.transform.position + ((floatingOrigin != null) ? floatingOrigin.TotalOffset : Vector3.zero);
		DistrictDef district = plan.Def.Districts[plan.DistrictAt(at.x, at.z)];
		float target = (float)district.Fog;
		scale = (scale < 0f) ? target : Mathf.MoveTowards(scale, target, rate * Time.deltaTime);
		if (weatherDensity < 0f || !Mathf.Approximately(RenderSettings.fogDensity, drawn))
		{
			weatherDensity = RenderSettings.fogDensity;
		}
		RenderSettings.fogDensity = weatherDensity * scale;
		Color hue = new Color(((district.Tint >> 16) & 0xFF) / 255f, ((district.Tint >> 8) & 0xFF) / 255f, (district.Tint & 0xFF) / 255f, 1f);
		tint = (tint.a <= 0f) ? hue : new Color(Mathf.MoveTowards(tint.r, hue.r, rate * Time.deltaTime), Mathf.MoveTowards(tint.g, hue.g, rate * Time.deltaTime), Mathf.MoveTowards(tint.b, hue.b, rate * Time.deltaTime), 1f);
		if (!hasWeatherColour || RenderSettings.fogColor != drawnColour)
		{
			weatherColour = RenderSettings.fogColor;
			hasWeatherColour = true;
		}
		RenderSettings.fogColor = Tinted(weatherColour, tint);
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

	private void OnDestroy()
	{
		if (weatherDensity >= 0f)
		{
			RenderSettings.fogDensity = weatherDensity;
		}
		if (hasWeatherColour)
		{
			RenderSettings.fogColor = weatherColour;
		}
	}
}
