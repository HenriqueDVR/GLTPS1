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
			TileStreamer tiles = Add<TileStreamer>();
			tiles.Init(world.Plan, floatingOrigin);
			if (world.PillarPrefab != null)
			{
				Add<PillarStreamer>().Init(world.Plan, tiles, floatingOrigin, world.PillarPrefab);
				gameObject.AddComponent<DistantPillar>();
			}
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
