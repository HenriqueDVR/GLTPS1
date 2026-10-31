using UnityEngine;

// world gen v2: under the deck the light goes (episode 1's dark interior): the fog closes in and goes near black, the
// sky's light and the ambient fade, no snow or rain falls. eased in by how far under the deck the local girl is, so
// driving down a ramp the dark comes on with no cut, and back out the same. it works over whatever the weather last
// wrote, like EndingCinematic. local only, each peer its own camera
public class UndergroundAtmosphere : MonoBehaviour
{
	// dark from this far under the deck, all of it by full
	private const float from = 1f;

	private const float full = 6f;

	// and it changes at most this fast, a share a second
	private const float rate = 0.8f;

	private static readonly Color darkFog = new Color(0.025f, 0.027f, 0.03f);

	private const float darkDensity = 0.045f;

	private const float darkAmbient = 0.15f;

	private const float darkSun = 0.03f;

	// the weather's particles go once it's this dark
	private const float hideAt = 0.3f;

	private LayerPlan plan;

	private TileStreamer tiles;

	private FloatingOrigin floatingOrigin;

	private WeatherManager weather;

	private float dark;

	private Color baseFog;

	private float baseDensity;

	private float baseAmbient;

	private float baseSun;

	private Color writtenFog;

	private float writtenDensity = -1f;

	private float writtenAmbient = -1f;

	private float writtenSun = -1f;

	private bool hidden;

	public void Init(LayerPlan plan, TileStreamer tiles, FloatingOrigin origin)
	{
		this.plan = plan;
		this.tiles = tiles;
		floatingOrigin = origin;
	}

	private void Start()
	{
		weather = FindObjectOfType<WeatherManager>();
	}

	private Vector3 Offset()
	{
		return (floatingOrigin != null) ? floatingOrigin.TotalOffset : Vector3.zero;
	}

	private void LateUpdate()
	{
		// by where the girl is, not the camera, which swings up over her when looking down
		Girl girl = Girls.Local;
		Camera camera = Camera.main;
		if (plan == null || (girl == null && camera == null))
		{
			return;
		}
		Vector3 at = ((girl != null) ? girl.transform.position + Vector3.up * 1.5f : camera.transform.position) + Offset();
		float depth = tiles.GroundY - at.y;
		// the elevator goes far under a deck, out in the open
		float target = (!LayerTransition.Busy && plan.Underground.Under(at.x, at.z)) ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, full, depth)) : 0f;
		if (dark == 0f && target == 0f)
		{
			return;
		}
		Light sun = (weather != null) ? weather.Sun : null;
		// the weather writes its own while it changes: go off whatever it last set
		if (RenderSettings.fogColor != writtenFog)
		{
			baseFog = RenderSettings.fogColor;
		}
		if (!Mathf.Approximately(RenderSettings.fogDensity, writtenDensity))
		{
			baseDensity = RenderSettings.fogDensity;
		}
		if (!Mathf.Approximately(RenderSettings.ambientIntensity, writtenAmbient))
		{
			baseAmbient = RenderSettings.ambientIntensity;
		}
		if (sun != null && !Mathf.Approximately(sun.intensity, writtenSun))
		{
			baseSun = sun.intensity;
		}
		dark = Mathf.MoveTowards(dark, target, rate * Time.deltaTime);
		writtenFog = RenderSettings.fogColor = Color.Lerp(baseFog, darkFog, dark);
		writtenDensity = RenderSettings.fogDensity = Mathf.Lerp(baseDensity, Mathf.Max(baseDensity, darkDensity), dark);
		writtenAmbient = RenderSettings.ambientIntensity = Mathf.Lerp(baseAmbient, Mathf.Min(baseAmbient, darkAmbient), dark);
		if (sun != null)
		{
			writtenSun = sun.intensity = Mathf.Lerp(baseSun, Mathf.Min(baseSun, darkSun), dark);
		}
		Hide(dark >= hideAt);
	}

	private void Hide(bool hide)
	{
		if (hide == hidden || weather == null)
		{
			return;
		}
		hidden = hide;
		if (weather.SnowParticles != null)
		{
			weather.SnowParticles.GetComponent<ParticleSystemRenderer>().enabled = !hide;
		}
		if (weather.WeatherParticles != null)
		{
			foreach (ParticleSystem particles in weather.WeatherParticles)
			{
				if (particles != null)
				{
					particles.GetComponent<ParticleSystemRenderer>().enabled = !hide;
				}
			}
		}
	}
}
