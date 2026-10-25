using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// only the lights nearest the camera cast shadows, the rest share the atlas and come out blocky.
// also keeps the lamp glows from costing a constraint each every frame
public class GraphicsTuning : MonoBehaviour
{
	// lamp shadow res comes from its height so tall lamps aren't blockier. sun is 2048, 2 cascades
	public static int ShadowedLights = 4;

	private const float budgetInterval = 0.25f;

	private const float refreshInterval = 2f;

	private readonly Dictionary<Light, LightShadows> shadowing = new Dictionary<Light, LightShadows>();

	private readonly List<Light> lights = new List<Light>();

	private readonly HashSet<Light> fitted = new HashSet<Light>();

	// kept while switched off so turning one on doesn't wait for a refresh
	private readonly List<Light> headlights = new List<Light>();

	private int headlightsOn;

	private static GraphicsTuning instance;

	private float nextBudget;

	private float nextRefresh;

	// lamp glows face the camera with a LookAtConstraint each, hundreds of them.
	// near ones every frame, far ones a batch at a time since their angle barely moves
	public static bool ThrottleGlows = true;

	private const float glowNearDistance = 80f;

	private const int glowBatch = 40;

	private readonly List<UnityEngine.Animations.LookAtConstraint> glows = new List<UnityEngine.Animations.LookAtConstraint>();

	private int glowCursor;

	// kept here so the per frame pass doesn't ask the engine about every glow
	private readonly List<bool> glowNear = new List<bool>();

	private readonly List<bool> glowActive = new List<bool>();

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		GameObject obj = new GameObject("GraphicsTuning");
		DontDestroyOnLoad(obj);
		instance = obj.AddComponent<GraphicsTuning>();
	}

	// new pieces' lamps start without shadows or the atlas overflows while loading
	public static void Adopt(GameObject piece)
	{
		if (instance == null || ShadowedLights <= 0)
		{
			return;
		}
		foreach (Light light in piece.GetComponentsInChildren<Light>(true))
		{
			if ((light.type != LightType.Spot && light.type != LightType.Point) || instance.shadowing.ContainsKey(light))
			{
				continue;
			}
			instance.shadowing[light] = light.shadows;
			if (light.shadows != LightShadows.None)
			{
				instance.lights.Add(light);
				light.shadows = LightShadows.None;
			}
		}
		instance.nextBudget = 0f;
		// barriers and items spawn separately, refresh soon
		instance.nextRefresh = Mathf.Min(instance.nextRefresh, Time.unscaledTime + budgetInterval);
	}

	private void Update()
	{
		UpdateGlows();
		int on = 0;
		foreach (Light headlight in headlights)
		{
			if (headlight != null && headlight.isActiveAndEnabled)
			{
				on++;
			}
		}
		if (Time.unscaledTime < nextBudget && on == headlightsOn)
		{
			return;
		}
		headlightsOn = on;
		nextBudget = Time.unscaledTime + budgetInterval;
		if (Time.unscaledTime >= nextRefresh)
		{
			nextRefresh = Time.unscaledTime + refreshInterval;
			FindLights();
		}
		Camera camera = Camera.main;
		if (camera == null)
		{
			return;
		}
		Vector3 eye = camera.transform.position;
		float near = glowNearDistance * glowNearDistance;
		for (int i = 0; i < glows.Count; i++)
		{
			glowNear[i] = glows[i] != null && (glows[i].transform.position - eye).sqrMagnitude < near;
		}
		// destroyed since the last FindLights
		lights.RemoveAll((Light light) => light == null);
		lights.Sort(delegate (Light a, Light b)
		{
			bool headlightA = headlights.Contains(a);
			if (headlightA != headlights.Contains(b))
			{
				return headlightA ? -1 : 1;
			}
			return (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude);
		});
		int given = 0;
		foreach (Light light in lights)
		{
			bool cast = ShadowedLights <= 0 || (given < ShadowedLights && light.isActiveAndEnabled);
			if (cast && light.isActiveAndEnabled)
			{
				given++;
				// colliders aren't there yet right when a piece spawns
				if (fitted.Add(light))
				{
					Fit(light);
				}
			}
			LightShadows wanted = cast ? shadowing[light] : LightShadows.None;
			if (light.shadows != wanted)
			{
				light.shadows = wanted;
			}
		}
	}

	// a constraint left inactive keeps its last rotation, so far ones only switch on for their turn
	private void UpdateGlows()
	{
		if (!ThrottleGlows || glows.Count == 0)
		{
			return;
		}
		int batchEnd = glowCursor + glowBatch;
		for (int i = 0; i < glows.Count; i++)
		{
			bool turn = (i >= glowCursor && i < batchEnd) || (batchEnd > glows.Count && i < batchEnd - glows.Count);
			bool active = turn || glowNear[i];
			if (glowActive[i] != active && glows[i] != null)
			{
				glows[i].constraintActive = active;
				glowActive[i] = active;
			}
		}
		glowCursor = (batchEnd >= glows.Count) ? (batchEnd - glows.Count) : batchEnd;
	}

	private const float texelSize = 0.05f;

	private static readonly System.Reflection.FieldInfo tierField = typeof(UniversalAdditionalLightData).GetField("m_AdditionalLightsShadowResolutionTier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

	// shadow footprint on the ground grows with height
	private static void Fit(Light light)
	{
		if (!light.TryGetComponent(out UniversalAdditionalLightData data) || data.additionalLightsShadowResolutionTier != UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierCustom)
		{
			return;
		}
		// skip the lamp's own shade
		float height = -1f;
		foreach (RaycastHit hit in Physics.RaycastAll(light.transform.position, Vector3.down, light.range, ~0, QueryTriggerInteraction.Ignore))
		{
			if (hit.distance > 1f && (height < 0f || hit.distance < height))
			{
				height = hit.distance;
			}
		}
		bool guessed = height < 0f;
		if (guessed)
		{
			height = light.range * 0.5f;
		}
		float angle = light.type == LightType.Spot ? light.spotAngle : 90f;
		float footprint = 2f * height * Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad);
		float wanted = footprint / texelSize;
		// can't set a custom res from script, so asset tiers. 256 was too blocky
		int tier = wanted < 724f ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
		tierField.SetValue(data, tier);
	}

	// spot/point lights that came with shadows on
	private void FindLights()
	{
		glows.Clear();
		glowNear.Clear();
		glowActive.Clear();
		foreach (UnityEngine.Animations.LookAtConstraint glow in FindObjectsOfType<UnityEngine.Animations.LookAtConstraint>())
		{
			glows.Add(glow);
			glowNear.Add(true);
			glowActive.Add(glow.constraintActive);
		}
		if (glowCursor >= glows.Count)
		{
			glowCursor = 0;
		}
		lights.Clear();
		headlights.Clear();
		foreach (CharacterVehicleInteraction vehicle in FindObjectsOfType<CharacterVehicleInteraction>())
		{
			if (vehicle.Headlight != null)
			{
				headlights.AddRange(vehicle.Headlight.GetComponentsInChildren<Light>(true));
			}
		}
		List<Light> found = new List<Light>(FindObjectsOfType<Light>());
		foreach (Light headlight in headlights)
		{
			if (!found.Contains(headlight))
			{
				found.Add(headlight);
			}
		}
		foreach (Light light in found)
		{
			if (light.type != LightType.Spot && light.type != LightType.Point)
			{
				continue;
			}
			if (!shadowing.ContainsKey(light))
			{
				shadowing[light] = light.shadows;
			}
			if (shadowing[light] != LightShadows.None)
			{
				lights.Add(light);
			}
		}
		List<Light> gone = null;
		foreach (Light light in shadowing.Keys)
		{
			if (light == null)
			{
				(gone ??= new List<Light>()).Add(light);
			}
		}
		if (gone != null)
		{
			foreach (Light light in gone)
			{
				shadowing.Remove(light);
				fitted.Remove(light);
			}
		}
	}
}
