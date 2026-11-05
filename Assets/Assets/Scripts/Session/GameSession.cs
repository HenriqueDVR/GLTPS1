using System;
using System.Collections.Generic;
using UnityEngine;

// one per gameplay scene load. creates the session systems in order and is where they're looked up
// (GameSession.Get<T>()), so new code never needs Find. goes away with the scene
public class GameSession : MonoBehaviour
{
	public static GameSession Current { get; private set; }

	private readonly Dictionary<Type, Component> services = new Dictionary<Type, Component>();

	public static GameSession Begin(GameObject host)
	{
		return host.AddComponent<GameSession>();
	}

	public static T Get<T>() where T : Component
	{
		if (Current == null || !Current.services.TryGetValue(typeof(T), out Component service) || service == null)
		{
			return null;
		}
		return (T)service;
	}

	public void Register<T>(T service) where T : Component
	{
		services[typeof(T)] = service;
	}

	public void Unregister<T>(T service) where T : Component
	{
		if (services.TryGetValue(typeof(T), out Component current) && current == service)
		{
			services.Remove(typeof(T));
		}
	}

	private T Add<T>() where T : Component
	{
		T service = gameObject.AddComponent<T>();
		Register(service);
		return service;
	}

	private void Awake()
	{
		Current = this;
		// scene object, wired here once so nothing else has to look for it
		FloatingOrigin floatingOrigin = FindObjectOfType<FloatingOrigin>();
		if (floatingOrigin != null)
		{
			Register(floatingOrigin);
		}
		KanazawaInstanceManager.ResetLevel();
		Girls.Begin();
		Add<WorldClock>();
		Add<AtmosphereStack>();
		Add<CameraRig>();
		WorldGenerator world = Add<WorldGenerator>();
		if (!WorldGenerator.Active)
		{
			return;
		}
		GameObject layer = BeginLayer(world.Plan, float.NaN, 0, null);
		TileStreamer tiles = layer.GetComponent<TileStreamer>();
		Add<SafeGround>().Init(world.Plan, tiles, floatingOrigin);
		Add<UndergroundAtmosphere>().Init(world.Plan, tiles, floatingOrigin);
		Add<ZoneAtmosphere>().Init(world.Plan, floatingOrigin);
		Add<WakeFlow>();
		Add<MapSheet>();
		UseLayer(layer);
		Backdrop(layer);
		Add<LayerTransition>();
		if (floatingOrigin != null)
		{
			floatingOrigin.UseFor(Camera.main != null ? Camera.main.transform : null);
		}
		if (CoopSession.Active)
		{
			if (CoopSession.Instance.IsHost || CoopSession.Instance.Connected)
			{
				Add<CoopPlayers>();
				Add<CoopSupplies>();
				Add<CoopKettenkrad>();
				Add<CoopBarrel>();
				Add<CoopDowned>();
				Add<CoopKanazawa>();
#if DUMP_WORLDGEN
				Add<CoopDebugTour>();
#endif
			}
			CoopSession.Instance.OnWorldGenerated(world);
		}
		else
		{
			CoopItem.ResetLevel();
		}
		CoopItem.TrackScenePickups();
		// no Kanazawa out there to give it yet: a plain sketch from the start (12)
		foreach (PlayerMapController map in FindObjectsOfType<PlayerMapController>(true))
		{
			map.enabled = true;
		}
		StartCoroutine(SaveScene.Register());
	}

	// v2: the layer the girls are on, its own object (tiles, towers, the backdrop)
	public GameObject Layer { get; private set; }

	// v2: one layer's world. deck NaN = from the floor under Chito (the start). a ride builds the next one while it's
	// still going, so the layer it goes to is there to see; the tower it rides takes the arrival cell
	public GameObject BeginLayer(LayerPlan plan, float deck, int groundLayer, PillarStreamer.Spot arrived, bool buildAll = false)
	{
		WorldGenerator world = Get<WorldGenerator>();
		FloatingOrigin floatingOrigin = Get<FloatingOrigin>();
		GameObject layer = new GameObject("layer " + plan.Layer);
		TileStreamer tiles = layer.AddComponent<TileStreamer>();
		tiles.Init(plan, floatingOrigin, deck, groundLayer, buildAll);
		if (world.PillarPrefab != null)
		{
			layer.AddComponent<PillarStreamer>().Init(plan, tiles, floatingOrigin, world.PillarPrefab, arrived);
		}
		return layer;
	}

	// the girls are on it now
	public void UseLayer(GameObject layer)
	{
		Layer = layer;
		TileStreamer tiles = layer.GetComponent<TileStreamer>();
		Register(tiles);
		PillarStreamer towers = layer.GetComponent<PillarStreamer>();
		if (towers != null)
		{
			Register(towers);
		}
		FloatingOrigin floatingOrigin = Get<FloatingOrigin>();
		SafeGround safe = Get<SafeGround>();
		if (safe != null)
		{
			safe.Init(tiles.Plan, tiles, floatingOrigin);
		}
		UndergroundAtmosphere under = Get<UndergroundAtmosphere>();
		if (under != null)
		{
			under.Init(tiles.Plan, tiles, floatingOrigin);
		}
		ZoneAtmosphere zone = Get<ZoneAtmosphere>();
		if (zone != null)
		{
			zone.Init(tiles.Plan, floatingOrigin);
		}
	}

	// its far towers and the deck above, once nothing of the one before shows (one at a time)
	public void Backdrop(GameObject layer)
	{
		if (layer.GetComponent<PillarStreamer>() != null)
		{
			layer.AddComponent<DistantPillar>();
		}
	}

	// a layer they've left: everything of it goes
	public void EndLayer(GameObject layer)
	{
		if (layer != null)
		{
			Destroy(layer);
		}
	}

	// work that can wait and shouldn't all land in one frame (a layer's tiles going), a bit each frame: destroying a
	// gpu-only mesh stalls the frame, 40 tiles a frame held arrivals at 100-280 ms frames for seconds
	private readonly Queue<Action> later = new Queue<Action>();

	private readonly System.Diagnostics.Stopwatch laterWatch = new System.Diagnostics.Stopwatch();

	private const double laterBudgetMs = 1.0;

	public void Later(Action work)
	{
		later.Enqueue(work);
	}

	private void Update()
	{
		laterWatch.Restart();
		while (later.Count > 0 && laterWatch.Elapsed.TotalMilliseconds < laterBudgetMs)
		{
			later.Dequeue()();
		}
	}

	private void OnDestroy()
	{
		SaveScene.Unregister();
		Girls.End();
		services.Clear();
		if (Current == this)
		{
			Current = null;
		}
	}
}
