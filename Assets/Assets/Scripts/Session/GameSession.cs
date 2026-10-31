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
		Add<CameraRig>();
		WorldGenerator world = Add<WorldGenerator>();
		if (!WorldGenerator.Active)
		{
			return;
		}
		if (WorldGenerator.GenVersion == 2)
		{
			TileStreamer tiles = BeginLayer(world.Plan, float.NaN, 0, null);
			Add<SafeGround>().Init(world.Plan, tiles, floatingOrigin);
			Add<UndergroundAtmosphere>().Init(world.Plan, tiles, floatingOrigin);
			Add<LayerTransition>();
			// an endless world needs it, v1 never goes far enough
			if (floatingOrigin != null)
			{
				floatingOrigin.UseFor(Camera.main != null ? Camera.main.transform : null);
			}
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
		StartCoroutine(SaveScene.Register());
	}

	// v2: what's built for the layer the girls are on. deck NaN = from the floor under Chito (the start).
	// a tower the ride brought along goes on standing in its cell
	public TileStreamer BeginLayer(LayerPlan plan, float deck, int groundLayer, PillarStreamer.Spot arrived)
	{
		WorldGenerator world = Get<WorldGenerator>();
		FloatingOrigin floatingOrigin = Get<FloatingOrigin>();
		TileStreamer tiles = Add<TileStreamer>();
		tiles.Init(plan, floatingOrigin, deck, groundLayer);
		if (world.PillarPrefab != null)
		{
			Add<PillarStreamer>().Init(plan, tiles, floatingOrigin, world.PillarPrefab, arrived);
			gameObject.AddComponent<DistantPillar>();
		}
		SafeGround safe = Get<SafeGround>();
		if (safe != null)
		{
			safe.Init(plan, tiles, floatingOrigin);
		}
		UndergroundAtmosphere under = Get<UndergroundAtmosphere>();
		if (under != null)
		{
			under.Init(plan, tiles, floatingOrigin);
		}
		return tiles;
	}

	// the layer's tiles, towers and backdrop go
	public void EndLayer()
	{
		Drop<TileStreamer>();
		Drop<PillarStreamer>();
		DistantPillar backdrop = GetComponent<DistantPillar>();
		if (backdrop != null)
		{
			Destroy(backdrop);
		}
	}

	private void Drop<T>() where T : Component
	{
		T service = Get<T>();
		if (service != null)
		{
			services.Remove(typeof(T));
			Destroy(service);
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
