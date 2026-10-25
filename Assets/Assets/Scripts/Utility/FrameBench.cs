#if DUMP_WORLDGEN
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// debug only
// -bench           vsync off, hop along the first map pieces, log frame times, quit
// -gfx old         shadows and lamp glows as they were before GraphicsTuning (map camera stays fixed)
// -gfx oldglows    only the lamp glows as before
// -benchshots <path prefix>  a picture at each stop
// -benchstops <n>  how many map pieces to hop along (10)
// -hidefar <m>     WorldGenerator.HideDistance, 0 = never hide far pieces
// -benchhidecheck <path prefix>  at each stop a picture with far pieces hidden, then with all shown
// -benchlamps <path prefix>  instead of the route, stand under the nearest lamps and take pictures
public class FrameBench : MonoBehaviour
{
	private static int stops = 10;

	private const float settle = 1.5f;

	private const float measure = 3f;

	private readonly List<float> frames = new List<float>();

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		string[] args = System.Environment.GetCommandLineArgs();
		int gfx = System.Array.IndexOf(args, "-gfx");
		if (gfx >= 0 && gfx + 1 < args.Length)
		{
			string mode = args[gfx + 1];
			if (mode == "old")
			{
				OldShadows();
			}
			if (mode == "old" || mode == "oldglows")
			{
				GraphicsTuning.ThrottleGlows = false;
			}
		}
		int stopsArg = System.Array.IndexOf(args, "-benchstops");
		if (stopsArg >= 0 && stopsArg + 1 < args.Length && int.TryParse(args[stopsArg + 1], out int stopCount))
		{
			stops = stopCount;
		}
		int hideArg = System.Array.IndexOf(args, "-hidefar");
		if (hideArg >= 0 && hideArg + 1 < args.Length && float.TryParse(args[hideArg + 1], out float hide))
		{
			WorldGenerator.HideDistance = hide;
		}
		int menuArg = System.Array.IndexOf(args, "-menushots");
		if (menuArg >= 0 && menuArg + 1 < args.Length)
		{
			Application.runInBackground = true;
			GameObject shots = new GameObject("MenuShots");
			DontDestroyOnLoad(shots);
			shots.AddComponent<FrameBench>().menuShots = args[menuArg + 1];
			return;
		}
		if (System.Array.IndexOf(args, "-bench") >= 0)
		{
			// keeps going without focus
			Application.runInBackground = true;
			GameObject obj = new GameObject("FrameBench");
			DontDestroyOnLoad(obj);
			obj.AddComponent<FrameBench>();
		}
	}

	private static void OldShadows()
	{
		GraphicsTuning.ShadowedLights = 0;
		UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
		if (asset == null)
		{
			Debug.Log("FrameBench: no URP asset");
			return;
		}
		asset.shadowDistance = 749.2f;
		asset.shadowCascadeCount = 4;
		SetField(asset, "m_AdditionalLightsShadowmapResolution", 1024);
		SetField(asset, "m_AdditionalLightsShadowResolutionTierLow", 128);
		SetField(asset, "m_AdditionalLightsShadowResolutionTierMedium", 256);
		SetField(asset, "m_AdditionalLightsShadowResolutionTierHigh", 512);
		Debug.Log("FrameBench: old shadow settings");
	}

	private static void SetField(object target, string name, object value)
	{
		FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
		if (field == null)
		{
			Debug.Log("FrameBench: no field " + name);
			return;
		}
		field.SetValue(target, (field.FieldType.IsEnum) ? System.Enum.ToObject(field.FieldType, value) : value);
	}

	private string menuShots;

	// -menushots <path prefix>: the settings page on the title and in game, before and after two clicks on the fps limit
	private IEnumerator MenuShots()
	{
		yield return new WaitForSeconds(3f);
		yield return SettingsShot(menuShots + "_title");
		SetDifficultySetting difficulty = FindObjectOfType<SetDifficultySetting>(includeInactive: true);
		if (difficulty.save == null)
		{
			difficulty.save = new Save();
		}
		difficulty.SetEasyDifficulty();
		while (GameObject.Find("ChitoParent") == null)
		{
			yield return null;
		}
		yield return new WaitForSeconds(6f);
		yield return SettingsShot(menuShots + "_game");
		Application.Quit();
	}

	private IEnumerator SettingsShot(string name)
	{
		// the page holding the sliders, whichever UISettings points at it
		Transform page = null;
		foreach (Slider slider in Resources.FindObjectsOfTypeAll<Slider>())
		{
			if (slider.gameObject.scene.isLoaded && slider.name.StartsWith("ADS"))
			{
				page = slider.transform.parent;
				break;
			}
		}
		if (page == null)
		{
			Debug.Log("FrameBench no settings page");
			yield break;
		}
		for (Transform t = page; t != null; t = t.parent)
		{
			t.gameObject.SetActive(true);
		}
		yield return new WaitForSeconds(1f);
		ScreenCapture.CaptureScreenshot(name + "_0.png");
		yield return null;
		Transform frameLimit = page.Find("Frame Limit");
		Debug.Log("FrameBench settings page " + page.name + ", fps button " + (frameLimit != null));
		if (frameLimit == null)
		{
			yield break;
		}
		Button button = frameLimit.GetComponent<Button>();
		int before = FrameLimit.Current;
		button.onClick.Invoke();
		button.onClick.Invoke();
		Debug.Log("FrameBench fps limit " + before + " -> " + FrameLimit.Current + " (vsync " + QualitySettings.vSyncCount + ", target " + Application.targetFrameRate + ")");
		yield return new WaitForSeconds(0.5f);
		ScreenCapture.CaptureScreenshot(name + "_1.png");
		yield return null;
		while (FrameLimit.Current != before)
		{
			button.onClick.Invoke();
		}
	}

	private IEnumerator Start()
	{
		if (menuShots != null)
		{
			yield return MenuShots();
			yield break;
		}
		QualitySettings.vSyncCount = 0;
		Application.targetFrameRate = -1;
		string[] args = System.Environment.GetCommandLineArgs();
		int shotsArg = System.Array.IndexOf(args, "-benchshots");
		string shots = (shotsArg >= 0 && shotsArg + 1 < args.Length) ? args[shotsArg + 1] : null;
		int hideCheckArg = System.Array.IndexOf(args, "-benchhidecheck");
		string hideCheck = (hideCheckArg >= 0 && hideCheckArg + 1 < args.Length) ? args[hideCheckArg + 1] : null;
		// solo game straight from the title
		yield return new WaitForSeconds(2f);
		SetDifficultySetting difficulty = FindObjectOfType<SetDifficultySetting>(includeInactive: true);
		if (difficulty != null)
		{
			if (difficulty.save == null)
			{
				difficulty.save = new Save();
			}
			difficulty.SetEasyDifficulty();
		}
		WorldGenerator world = null;
		GameObject chito = null;
		while (world == null || chito == null)
		{
			world = FindObjectOfType<WorldGenerator>();
			chito = GameObject.Find("ChitoParent");
			yield return null;
		}
		yield return new WaitForSeconds(5f);
		Transform local = chito.transform;
		CharacterController controller = local.GetComponent<CharacterController>();
		int mapArg = System.Array.IndexOf(args, "-benchmap");
		if (mapArg >= 0 && mapArg + 1 < args.Length)
		{
			// same path as pressing the map key
			PlayerMapController map = local.GetComponent<PlayerMapController>();
			StartCoroutine((IEnumerator)typeof(PlayerMapController).GetMethod("RenderMap", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(map, new object[1] { 0f }));
			yield return new WaitForSeconds(1f);
			Texture2D picture = map.MapImage.texture as Texture2D;
			Camera mapCamera = (Camera)typeof(PlayerMapController).GetField("mapCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(map);
			Debug.Log("FrameBench map camera enabled " + mapCamera.enabled + ", picture " + ((picture != null) ? (picture.width + "x" + picture.height) : "none"));
			if (picture != null)
			{
				System.IO.File.WriteAllBytes(args[mapArg + 1], picture.EncodeToPNG());
			}
			Application.Quit();
			yield break;
		}
		int lampsArg = System.Array.IndexOf(args, "-benchlamps");
		if (lampsArg >= 0 && lampsArg + 1 < args.Length)
		{
			yield return Lamps(local, controller, args[lampsArg + 1]);
			Application.Quit();
			yield break;
		}
		List<float> all = new List<float>();
		string perStop = "";
		int done = 0;
		Node lastStop = null;
		for (int i = 0; i < world.Layout.Nodes.Count && done < stops; i++)
		{
			GameObject node = world.Layout.Nodes[i].Instance;
			if (node == null)
			{
				continue;
			}
			// frozen in the air while far pieces come back, then down onto whatever is there.
			// falling out of the world restarts the level
			PlayerMovement movement = local.GetComponent<PlayerMovement>();
			movement.enabled = false;
			controller.enabled = false;
			local.SetPositionAndRotation(node.transform.position + Vector3.up * 2f, node.transform.rotation);
			yield return new WaitForSeconds(settle);
			Vector3 spot = node.transform.position;
			if (!Physics.Raycast(spot + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, -1, QueryTriggerInteraction.Ignore))
			{
				Debug.Log("FrameBench no ground at " + node.name + ", skipped");
				continue;
			}
			local.position = hit.point + Vector3.up * 0.2f;
			lastStop = node.GetComponent<Node>();
			yield return new WaitForSeconds(0.5f);
			if (shots != null)
			{
				ScreenCapture.CaptureScreenshot(shots + "_" + done + ".png");
			}
			frames.Clear();
			float until = Time.unscaledTime + measure;
			while (Time.unscaledTime < until)
			{
				yield return null;
				frames.Add(Time.unscaledDeltaTime * 1000f);
				SampleTimings();
			}
			if (hideCheck != null)
			{
				// same moment, same weather: hidden as it plays, then everything shown
				ScreenCapture.CaptureScreenshot(hideCheck + "_" + done + "_hidden.png");
				yield return null;
				float hideDistance = WorldGenerator.HideDistance;
				WorldGenerator.HideDistance = 100000f;
				yield return new WaitForSeconds(3f);
				ScreenCapture.CaptureScreenshot(hideCheck + "_" + done + "_all.png");
				yield return null;
				WorldGenerator.HideDistance = hideDistance;
			}
			all.AddRange(frames);
			perStop += " " + Average(frames).ToString("F1");
			done++;
		}
		if (System.Array.IndexOf(args, "-benchtrace") >= 0)
		{
			// map path from where she ends up back to the start, through hidden pieces
			NodeData nodeData = world.GetComponent<NodeData>();
			StartEnvironmentTrace trace = local.GetComponent<PlayerMapController>().startEnvironmentTraceScript;
			// teleporting skips the trigger that tracks which node she's on
			trace.node = lastStop;
			// hide nearly everything so the walk back really goes through switched off nodes
			WorldGenerator.HideDistance = 60f;
			yield return new WaitForSeconds(4f);
			Node from = trace.node;
			int hiddenNodes = 0;
			for (Node walk = from; walk != null && walk.edge != null; walk = walk.edge.port.GetComponentInParent<Node>(true))
			{
				hiddenNodes += walk.gameObject.activeInHierarchy ? 0 : 1;
			}
			nodeData.istracePathFromPlayerNodeComplete = false;
			nodeData.edgesFromPlayer = new List<GenerateNode>();
			trace.triggerTraceFromPlayerNode = true;
			float giveUp = Time.unscaledTime + 10f;
			while (!nodeData.istracePathFromPlayerNodeComplete && Time.unscaledTime < giveUp)
			{
				yield return null;
			}
			Debug.Log("FrameBench trace from " + ((from != null) ? from.name : "none") + ": complete " + nodeData.istracePathFromPlayerNodeComplete + ", edges " + nodeData.edgesFromPlayer.Count + ", hidden nodes on the way " + hiddenNodes);
		}
		all.Sort();
		int shadowed = 0;
		int lights = 0;
		foreach (Light light in FindObjectsOfType<Light>())
		{
			if (light.type == LightType.Spot || light.type == LightType.Point)
			{
				lights++;
				shadowed += (light.shadows != LightShadows.None) ? 1 : 0;
			}
		}
		int pieces = 0;
		int shownPieces = 0;
		foreach (WorldLayout.PlacedNode placed in world.Layout.Nodes)
		{
			if (placed.Instance != null)
			{
				pieces++;
				shownPieces += placed.Instance.activeSelf ? 1 : 0;
			}
		}
		foreach (WorldLayout.PlacedEdge placed in world.Layout.Edges)
		{
			if (placed.Instance != null)
			{
				pieces++;
				shownPieces += placed.Instance.activeSelf ? 1 : 0;
			}
		}
		Debug.Log("FrameBench pieces spawned " + pieces + ", shown " + shownPieces + ", hide distance " + WorldGenerator.HideDistance);
		Debug.Log("FrameBench split: main thread " + (mainThread / timed).ToString("F2") + "ms, render thread " + (renderThread / timed).ToString("F2") + "ms, gpu " + (gpu / timed).ToString("F2") + "ms (" + timed + " frames) | draw calls " + (drawCalls / counted).ToString("F0") + ", batches " + (batches / counted).ToString("F0") + ", setpass " + (setPass / counted).ToString("F0") + ", triangles " + (triangles / counted / 1000).ToString("F0") + "k, shadow casters " + (shadowCasters / counted).ToString("F0") + ", renderers " + FindObjectsOfType<Renderer>().Length + ", behaviours " + FindObjectsOfType<MonoBehaviour>().Length + ", colliders " + FindObjectsOfType<Collider>().Length);
		Debug.Log("FrameBench markers (ms):" + MarkerSummary());
		Camera camera = Camera.main;
		Debug.Log("FrameBench seed " + world.Seed + " stops " + done + " frames " + all.Count + " avg " + Average(all).ToString("F2") + "ms (" + (1000f / Average(all)).ToString("F0") + " fps) p95 " + all[all.Count * 95 / 100].ToString("F2") + "ms p99 " + all[all.Count * 99 / 100].ToString("F2") + "ms | per stop" + perStop + " | lights " + lights + " shadowed " + shadowed + " farclip " + ((camera != null) ? camera.farClipPlane.ToString("F0") : "?") + " fog " + RenderSettings.fogMode + " " + RenderSettings.fogDensity);
		Application.Quit();
	}

	// a few steps from each lamp's pool of light, facing it, so her shadow and the lamp post's show
	private IEnumerator Lamps(Transform local, CharacterController controller, string prefix)
	{
		List<Light> lamps = new List<Light>();
		foreach (Light light in FindObjectsOfType<Light>())
		{
			if (light.type == LightType.Spot && light.range >= 20f)
			{
				lamps.Add(light);
			}
		}
		Vector3 start = local.position;
		lamps.Sort((Light a, Light b) => (a.transform.position - start).sqrMagnitude.CompareTo((b.transform.position - start).sqrMagnitude));
		for (int i = 0; i < lamps.Count && i < 4; i++)
		{
			Vector3 pool = lamps[i].transform.position;
			if (Physics.Raycast(pool, Vector3.down, out RaycastHit hit, 40f, -1, QueryTriggerInteraction.Ignore))
			{
				pool = hit.point;
			}
			Vector3 away = lamps[i].transform.right;
			away.y = 0f;
			Vector3 spot = pool + away.normalized * 3f;
			Vector3 look = pool - spot;
			controller.enabled = false;
			local.SetPositionAndRotation(spot + Vector3.up * 0.2f, Quaternion.LookRotation(look));
			controller.enabled = true;
			yield return new WaitForSeconds(2.5f);
			ScreenCapture.CaptureScreenshot(prefix + "_" + i + ".png");
			Debug.Log("FrameBench lamp " + i + " at " + pool + ", " + Vector3.Distance(pool, start).ToString("F0") + "m away");
			yield return new WaitForSeconds(0.5f);
		}
	}

	private readonly FrameTiming[] timing = new FrameTiming[1];

	private double mainThread;

	private double renderThread;

	private double gpu;

	private int timed;

	private Unity.Profiling.ProfilerRecorder drawCallsRecorder;

	private Unity.Profiling.ProfilerRecorder batchesRecorder;

	private Unity.Profiling.ProfilerRecorder setPassRecorder;

	private Unity.Profiling.ProfilerRecorder trianglesRecorder;

	private Unity.Profiling.ProfilerRecorder shadowCastersRecorder;

	private double drawCalls;

	private double batches;

	private double setPass;

	private double triangles;

	private double shadowCasters;

	private int counted = 1;

	// main thread pieces, only readable in a development build
	private static readonly string[] markerNames =
	{
		"PlayerLoop",
		"Update.ScriptRunBehaviourUpdate",
		"PreLateUpdate.ScriptRunBehaviourLateUpdate",
		"FixedUpdate.ScriptRunBehaviourFixedUpdate",
		"Update.ScriptRunDelayedDynamicFrameRate",
		"FixedUpdate.PhysicsFixedUpdate",
		"PreUpdate.AIUpdatePostScript",
		"PreLateUpdate.DirectorUpdateAnimationBegin",
		"PreLateUpdate.DirectorUpdateAnimationEnd",
		"PreLateUpdate.ParticleSystemBeginUpdateAll",
		"PostLateUpdate.UpdateAllRenderers",
		"PostLateUpdate.UpdateAllSkinnedMeshes",
		"PostLateUpdate.FinishFrameRendering",
		"PostLateUpdate.PlayerSendFrameComplete",
		"Gfx.WaitForPresentOnGfxThread",
		"GC.Collect",
		"PreLateUpdate.ConstraintManagerUpdate",
		"PostLateUpdate.UpdateAudio",
		"PostLateUpdate.PlayerUpdateCanvases",
		"PostLateUpdate.UpdateRectTransform",
		"PreUpdate.SendMouseEvents",
		"Update.DirectorUpdate",
		"EarlyUpdate.UpdateTextureStreamingManager",
		"PreLateUpdate.UpdateNetworkManager",
		"FixedUpdate.PhysicsFixedUpdate",
		"Physics.Simulate",
		"Physics.Processing",
		"Physics.ProcessReports",
		"PostLateUpdate.UpdateAllSkinnedMeshes",
		"UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera",
		"UniversalRenderPipeline.RenderSingleCameraInternal: UI Main Camera (1)",
		"UniversalRenderPipeline.RenderSingleCameraInternal: MapCamera",
		"UniversalRenderPipeline.RenderSingleCameraInternal: ScreenCamera",
		"CullScriptable",
		"Shadows.ExecuteShadowCasters"
	};

	private readonly List<Unity.Profiling.ProfilerRecorder> markers = new List<Unity.Profiling.ProfilerRecorder>();

	private double[] markerTotals;

	private string MarkerSummary()
	{
		string text = "";
		for (int i = 0; i < markerNames.Length; i++)
		{
			text += " " + markerNames[i] + " " + (markerTotals[i] / counted).ToString("F2");
		}
		return text;
	}

	private void OnEnable()
	{
		markerTotals = new double[markerNames.Length];
		foreach (string name in markerNames)
		{
			Unity.Profiling.ProfilerCategory category = name.StartsWith("UniversalRenderPipeline") ? Unity.Profiling.ProfilerCategory.Render : Unity.Profiling.ProfilerCategory.Internal;
			markers.Add(Unity.Profiling.ProfilerRecorder.StartNew(category, name));
		}
		drawCallsRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
		batchesRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
		setPassRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
		trianglesRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
		shadowCastersRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Shadow Casters Count");
	}

	private void OnDisable()
	{
		drawCallsRecorder.Dispose();
		batchesRecorder.Dispose();
		setPassRecorder.Dispose();
		trianglesRecorder.Dispose();
		shadowCastersRecorder.Dispose();
		foreach (Unity.Profiling.ProfilerRecorder marker in markers)
		{
			marker.Dispose();
		}
	}

	// needs enableFrameTimingStats in the player settings
	private void SampleTimings()
	{
		FrameTimingManager.CaptureFrameTimings();
		if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
		{
			mainThread += timing[0].cpuMainThreadFrameTime;
			renderThread += timing[0].cpuRenderThreadFrameTime;
			gpu += timing[0].gpuFrameTime;
			timed++;
		}
		drawCalls += drawCallsRecorder.LastValue;
		batches += batchesRecorder.LastValue;
		setPass += setPassRecorder.LastValue;
		triangles += trianglesRecorder.LastValue;
		shadowCasters += shadowCastersRecorder.LastValue;
		for (int i = 0; i < markers.Count; i++)
		{
			markerTotals[i] += markers[i].LastValue / 1e6;
		}
		counted++;
	}

	// UISettings turns vsync back on when the level loads
	private void Update()
	{
		QualitySettings.vSyncCount = 0;
	}

	private static float Average(List<float> values)
	{
		float sum = 0f;
		foreach (float value in values)
		{
			sum += value;
		}
		return (values.Count > 0) ? sum / values.Count : 0f;
	}
}
#endif
