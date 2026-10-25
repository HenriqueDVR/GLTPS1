using System.Collections.Generic;
using UnityEngine;

// only the lights nearest the camera cast shadows, the rest share the atlas and come out blocky.
// also keeps the lamp glows from costing a constraint each every frame
public class GraphicsTuning : MonoBehaviour
{
	// 4 x 1024 fills the 2048 additional light shadow atlas
	public static int ShadowedLights = 4;

	private const float budgetInterval = 0.25f;

	private const float refreshInterval = 2f;

	private readonly Dictionary<Light, LightShadows> shadowing = new Dictionary<Light, LightShadows>();

	private readonly List<Light> lights = new List<Light>();

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
		obj.AddComponent<GraphicsTuning>();
	}

	private void Update()
	{
		UpdateGlows();
		if (Time.unscaledTime < nextBudget)
		{
			return;
		}
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
		lights.Sort((Light a, Light b) => (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude));
		int given = 0;
		foreach (Light light in lights)
		{
			bool cast = ShadowedLights <= 0 || (given < ShadowedLights && light.isActiveAndEnabled);
			if (cast && light.isActiveAndEnabled)
			{
				given++;
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
		foreach (Light light in FindObjectsOfType<Light>())
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
			}
		}
	}
}
