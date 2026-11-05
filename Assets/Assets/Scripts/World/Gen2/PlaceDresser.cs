using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

// a place's things once its tile's whole: containers, relics, wheels, a valve. a pure function of the place, opened
// state in WorldFlags, so both peers see the same and a friend coming back sees it opened. the host decides and grants
// what's shared (food, water, fuel), whoever pressed gets ammo and says the lines
public static class PlaceDresser
{
	// indexes into a place's own ids: what's done for good under PlacePlan.Id
	private const int relicIndex = 40;

	private const int wheelA = 50;

	private const int wheelB = 51;

	private const int hotWater = 52;

	private const int valve = 60;

	private const int chainFlag = 70;

	private const int hungCrate = 71;

	// a beam by the door, a crate on a chain: the rifle's first targets, and a thing the rider can do from the seat
	private const double hungShare = 0.5;

	private const float beamHeight = 5f;

	private const float hotWaterAmount = 240f;

	private const float depotShare = 0.5f;

	private const float valveHold = 3f;

	private const float wheelHold = 2.5f;

	// one wall of lockers, a metre each
	private const float lockerWidth = 0.9f;

	private const float lockerDepth = 0.45f;

	private const float lockerHeight = 1.9f;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		NetRegistry.RegisterDecider(NetRegistry.Kind.Place, OnAsked);
	}

	// host or alone: first to ask gets it, and what's shared goes in here
	private static bool OnAsked(ulong id, NetDataReader reader)
	{
		int food = reader.GetInt();
		float water = reader.GetFloat();
		float fuelShare = reader.GetFloat();
		if (!WorldFlags.Set(id))
		{
			return false;
		}
		Grant(food, water, fuelShare);
		return true;
	}

	private static void Grant(int food, float water, float fuelShare)
	{
		if (food > 0 && Girls.Local != null)
		{
			Girls.Local.Items.AddFood(food);
		}
		if (water > 0f)
		{
			Object.FindObjectOfType<CharacterStatus>().waterLevel += water;
		}
		if (fuelShare > 0f)
		{
			VehicleFuelManager fuel = Object.FindObjectOfType<VehicleFuelManager>();
			fuel.currentFuel = Mathf.Min(fuel.maxFuel, fuel.currentFuel + fuel.maxFuel * fuelShare);
		}
	}

	private struct Frame
	{
		public Place Place;

		public Vector3 Centre;

		public Vector3 U;

		public Vector3 V;

		public Transform Parent;

		public Material Metal;

		public Material Concrete;

		public int Layer;

		public int LayerIndex;

		public Vector3 At(float u, float v, float y)
		{
			return Centre + U * u + V * v + Vector3.up * y;
		}
	}

	public static void Dress(Place place, int layerIndex, float deckY, Vector3 offset, Transform props, Material metal, Material concrete, int groundLayer)
	{
		Vector3 u = new Vector3((float)place.Axis.X, 0f, (float)place.Axis.Z);
		Frame frame = new Frame
		{
			Place = place,
			Centre = new Vector3((float)place.Center.X, deckY, (float)place.Center.Z) - offset,
			U = u,
			V = new Vector3(-u.z, 0f, u.x),
			Parent = props,
			Metal = metal,
			Concrete = concrete,
			Layer = groundLayer,
			LayerIndex = layerIndex
		};
		switch (place.Kind)
		{
		case PlaceKind.TankHull:
			Crate(frame, 0, -2.6f, 1.1f);
			Crate(frame, 1, 2.6f, -1.1f);
			Relic(frame, 0f, -1.4f);
			break;
		case PlaceKind.DarkRoom:
			Lockers(frame);
			Relic(frame, 0f, 0f);
			break;
		case PlaceKind.PumpHouse:
			Pump(frame);
			break;
		case PlaceKind.FuelDepot:
			Depot(frame);
			break;
		}
		if (place.Kind != PlaceKind.FuelDepot && Gen2.Unit(WorldRandom.Derive(place.Seed, Gen2.StreamPlace, 6uL)) < hungShare)
		{
			Hung(frame);
		}
	}

	private static void Hung(Frame frame)
	{
		Place place = frame.Place;
		XZ door = place.DoorAt;
		Vector3 u = frame.U;
		Vector3 v = frame.V;
		Vector3 doorAt = new Vector3((float)door.X, frame.Centre.y, (float)door.Z) + (frame.Centre - new Vector3((float)place.Center.X, frame.Centre.y, (float)place.Center.Z));
		Vector3 outward = doorAt - frame.Centre;
		outward.y = 0f;
		outward.Normalize();
		Vector3 along = Vector3.Cross(Vector3.up, outward);
		Vector3 middle = doorAt + outward * 5f + along * 3f;
		Cube(frame, "beam post", middle - along * 1.6f + Vector3.up * beamHeight * 0.5f, new Vector3(0.25f, beamHeight, 0.25f), frame.Metal);
		Cube(frame, "beam post", middle + along * 1.6f + Vector3.up * beamHeight * 0.5f, new Vector3(0.25f, beamHeight, 0.25f), frame.Metal);
		GameObject beam = Cube(frame, "beam", middle + Vector3.up * beamHeight, Vector3.one * 0.25f, frame.Metal);
		beam.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
		beam.transform.localScale = new Vector3(0.25f, 0.25f, 3.5f);
		float crateY = beamHeight - 2.1f;
		GameObject chain = Cube(frame, "chain", middle + Vector3.up * (beamHeight - 0.8f), new Vector3(0.14f, 1.6f, 0.14f), frame.Metal);
		chain.layer = 0;
		GameObject crate = Cube(frame, "hung crate", middle + Vector3.up * crateY, new Vector3(1f, 0.7f, 0.7f), frame.Metal);
		crate.layer = 0;
		HangingCrate hanging = crate.AddComponent<HangingCrate>();
		ulong flag = PlacePlan.Id(frame.LayerIndex, place.CellX, place.CellZ, chainFlag);
		PlaceLoot loot = default(PlaceLoot);
		loot.Food = 2;
		loot.Ammo = false;
		Interactable open = Once(crate, frame, hungCrate, "open", Verb.Use, loot.Food, 0f, 0f, girl => Got(girl, loot), null);
		open.Gone = delegate
		{
			crate.transform.localScale = new Vector3(1f, 0.45f, 0.7f);
		};
		open.Can = girl => hanging.Down;
		float floorY = frame.Centre.y + 0.35f;
		bool already = WorldFlags.Has(flag);
		chain.AddComponent<Breakable>().Init(flag, delegate
		{
			hanging.Drop(floorY, already);
			chain.SetActive(false);
			if (!already)
			{
				Speaker.Say(Speaker.Yuuri, "Got it.");
			}
		});
	}

	private static GameObject Cube(Frame frame, string name, Vector3 at, Vector3 size, Material material)
	{
		GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
		cube.name = name;
		cube.layer = frame.Layer;
		cube.transform.SetParent(frame.Parent, true);
		cube.transform.SetPositionAndRotation(at, Quaternion.LookRotation(frame.V, Vector3.up));
		cube.transform.localScale = size;
		Renderer renderer = cube.GetComponent<Renderer>();
		renderer.sharedMaterial = material;
		GraphicsTuning.Adopt(cube);
		return cube;
	}

	// a thing that opens for good: the host says yes, then it's open for both, saved
	private static Interactable Once(GameObject thing, Frame frame, int index, string label, Verb verb, int food, float water, float fuelShare, System.Action<Girl> run, System.Action gone)
	{
		Interactable interact = thing.AddComponent<Interactable>();
		interact.Verb = verb;
		interact.Label = label;
		interact.Range = 1.8f;
		interact.Authority = Authority.Host;
		interact.Kind = NetRegistry.Kind.Place;
		interact.AskBody = delegate(NetDataWriter writer)
		{
			writer.Put(food);
			writer.Put(water);
			writer.Put(fuelShare);
		};
		interact.Run = run;
		interact.Gone = gone;
		interact.UseFlag(PlacePlan.Id(frame.LayerIndex, frame.Place.CellX, frame.Place.CellZ, index));
		return interact;
	}

	private static void Crate(Frame frame, int index, float u, float v)
	{
		PlaceLoot loot = PlacePlan.Loot(frame.Place, index);
		Vector3 at = frame.At(u, v, 0.3f);
		GameObject crate = Cube(frame, "crate", at, new Vector3(0.9f, 0.6f, 0.6f), frame.Metal);
		GameObject lid = Cube(frame, "lid", at + Vector3.up * 0.34f, new Vector3(0.95f, 0.08f, 0.65f), frame.Metal);
		Once(crate, frame, index, "open", Verb.Use, loot.Food, 0f, 0f, girl => Got(girl, loot), delegate
		{
			lid.transform.position += frame.V * 0.25f - Vector3.up * 0.06f;
			lid.transform.rotation *= Quaternion.Euler(14f, 0f, 0f);
		});
	}

	private static void Lockers(Frame frame)
	{
		Place place = frame.Place;
		int count = Mathf.Clamp((int)(place.RoomU * 2.0 / lockerWidth) - 2, 3, 6);
		// along the wall away from the door, or the first long one that hasn't got it
		float side = (place.Door == 2) ? -1f : 1f;
		float back = (float)place.RoomV - 0.25f - lockerDepth * 0.5f;
		for (int i = 0; i < count; i++)
		{
			PlaceLoot loot = PlacePlan.Loot(place, 10 + i);
			float along = (i - (count - 1) * 0.5f) * lockerWidth;
			Vector3 at = frame.At(along, side * back, lockerHeight * 0.5f);
			GameObject locker = Cube(frame, "locker", at, new Vector3(lockerWidth * 0.94f, lockerHeight, lockerDepth), frame.Concrete);
			GameObject door = Cube(frame, "locker door", at - frame.V * side * (lockerDepth * 0.5f + 0.02f), new Vector3(lockerWidth * 0.86f, lockerHeight * 0.92f, 0.04f), frame.Metal);
			Vector3 hinge = at - frame.V * side * (lockerDepth * 0.5f) - frame.U * (lockerWidth * 0.43f);
			float swing = side * 95f;
			Once(locker, frame, 10 + i, "open", Verb.Use, loot.Food, 0f, 0f, girl => Got(girl, loot), delegate
			{
				door.transform.RotateAround(hinge, Vector3.up, swing);
			});
		}
	}

	private static void Got(Girl girl, PlaceLoot loot)
	{
		if (girl == null)
		{
			return;
		}
		if (loot.Ammo)
		{
			girl.Items.GiveAmmo();
		}
		else if (loot.Food > 0)
		{
			girl.Items.PlayPickupSound();
		}
		else
		{
			Speaker.Say(GirlNeeds.NameOf(girl), (girl.Id == GirlId.Chito) ? "Nothing." : "Somebody ate it.");
		}
	}

	// a small old-world thing on a shelf, and what they make of it. once
	private static void Relic(Frame frame, float u, float v)
	{
		int index = (int)(WorldRandom.Derive(frame.Place.Seed, Gen2.StreamPlace, 5uL) % (ulong)PlaceLines.Relics.Length);
		GameObject table = Cube(frame, "shelf", frame.At(u, v, 0.45f), new Vector3(0.8f, 0.9f, 0.5f), frame.Concrete);
		GameObject thing = Cube(frame, "relic", frame.At(u, v, 0.98f), new Vector3(0.16f, 0.16f, 0.16f), frame.Metal);
		Interactable look = Once(table, frame, relicIndex, "look", Verb.Use, 0, 0f, 0f, delegate
		{
			foreach (PlaceLines.Line line in PlaceLines.Relics[index])
			{
				Speaker.SayToBoth(line.Who, line.Text);
			}
		}, delegate
		{
			// seen: she keeps it, or it's put back. either way nothing more to say
			thing.transform.localScale *= 0.8f;
		});
		look.Range = 2.2f;
	}

	private static void Pump(Frame frame)
	{
		float across = (float)frame.Place.RoomV - 1.0f;
		ulong a = PlacePlan.Id(frame.LayerIndex, frame.Place.CellX, frame.Place.CellZ, wheelA);
		ulong b = PlacePlan.Id(frame.LayerIndex, frame.Place.CellX, frame.Place.CellZ, wheelB);
		ulong done = PlacePlan.Id(frame.LayerIndex, frame.Place.CellX, frame.Place.CellZ, hotWater);
		GameObject one = Wheel(frame, wheelA, -2.2f, across);
		GameObject two = Wheel(frame, wheelB, 2.2f, across);
		GameObject works = Cube(frame, "pump", frame.At(0f, across, 1f), new Vector3(2.4f, 2f, 0.8f), frame.Metal);
		PumpHouse pump = works.AddComponent<PumpHouse>();
		pump.Init(a, b, done, one.transform, two.transform, hotWaterAmount, frame.Place.RoomU, frame.At(0f, 0f, 0f));
	}

	private static GameObject Wheel(Frame frame, int index, float u, float v)
	{
		GameObject post = Cube(frame, "wheel post", frame.At(u, v, 0.55f), new Vector3(0.18f, 1.1f, 0.18f), frame.Metal);
		GameObject wheel = Cube(frame, "wheel", frame.At(u, v, 1.2f) - frame.V * 0.1f, new Vector3(0.7f, 0.7f, 0.08f), frame.Metal);
		Interactable turn = Once(post, frame, index, "turn", Verb.Use, 0, 0f, 0f, null, delegate
		{
			wheel.transform.Rotate(0f, 0f, 45f, Space.Self);
		});
		turn.Press = Press.Hold;
		turn.HoldTime = wheelHold;
		return post;
	}

	private static void Depot(Frame frame)
	{
		Vector3 at = frame.At(-(float)frame.Place.RoomU * 0.4f, 0f, 0.7f);
		GameObject pipe = Cube(frame, "valve post", at, new Vector3(0.25f, 1.4f, 0.25f), frame.Metal);
		GameObject wheel = Cube(frame, "valve", at + Vector3.up * 0.8f, new Vector3(0.5f, 0.06f, 0.5f), frame.Metal);
		Interactable open = Once(pipe, frame, valve, "open the valve", Verb.Use, 0, 0f, depotShare, null, delegate
		{
			wheel.transform.Rotate(0f, 60f, 0f, Space.Self);
		});
		open.Press = Press.Hold;
		open.HoldTime = valveHold;
		// the ket's tank's what it fills. no point with the ket left miles off
		open.Can = girl => KetNear(pipe.transform.position);
	}

	private const float ketReach = 30f;

	private static bool KetNear(Vector3 at)
	{
		VehicleFuelManager ket = Object.FindObjectOfType<VehicleFuelManager>();
		return ket != null && Vector3.Distance(ket.transform.position, at) < ketReach;
	}
}

// two wheels and hot water comes (17): anyone can turn either one, in solo the AI goes to the other. then the room's warm
// for good and the water cans fill once. the wheels are flags, so it works over a save and for a friend who comes back
public class PumpHouse : MonoBehaviour
{
	private const float checkEvery = 0.5f;

	private const float warmPerSecond = 2f;

	private const float aiTurn = 3f;

	private ulong a;

	private ulong b;

	private ulong done;

	private Transform one;

	private Transform two;

	private float water;

	private float reach;

	private Vector3 middle;

	private float nextCheck;

	private float aiFor;

	public void Init(ulong wheelA, ulong wheelB, ulong hotWater, Transform first, Transform second, float amount, double room, Vector3 centre)
	{
		a = wheelA;
		b = wheelB;
		done = hotWater;
		one = first;
		two = second;
		water = amount;
		reach = (float)room + 1f;
		middle = centre;
	}

	public bool Open => WorldFlags.Has(a) && WorldFlags.Has(b);

	private void Update()
	{
		if (Open)
		{
			Warm();
		}
		if (Time.time < nextCheck)
		{
			return;
		}
		nextCheck = Time.time + checkEvery;
		if (!NetRegistry.HostOrSolo)
		{
			return;
		}
		if (Open)
		{
			if (WorldFlags.Set(done))
			{
				FindObjectOfType<CharacterStatus>().waterLevel += water;
				Speaker.SayToBoth(Speaker.Yuuri, "It's warm. Chi-chan, it's warm.");
			}
			Release();
			return;
		}
		Helper();
	}

	// the AI goes to whichever wheel's left
	private void Helper()
	{
		if (CoopSession.InGame || Girls.Local == null)
		{
			return;
		}
		Girl other = Girls.Other(Girls.Local);
		bool firstDone = WorldFlags.Has(a);
		if (other == null || other.Ai == null || firstDone == WorldFlags.Has(b) || Vector3.Distance(Girls.Local.transform.position, middle) > reach * 2f)
		{
			Release();
			return;
		}
		Transform wheel = firstDone ? two : one;
		other.Ai.Tend = wheel;
		if (Vector3.Distance(other.transform.position, wheel.position) < 1.6f)
		{
			aiFor += checkEvery;
			if (aiFor >= aiTurn)
			{
				aiFor = 0f;
				WorldFlags.Set(firstDone ? b : a);
			}
		}
	}

	private void Release()
	{
		if (!CoopSession.InGame && Girls.Local != null)
		{
			Girl other = Girls.Other(Girls.Local);
			if (other != null && other.Ai != null && (other.Ai.Tend == one || other.Ai.Tend == two))
			{
				other.Ai.Tend = null;
			}
		}
	}

	// warmth for whoever's in here, each player's own girl and the AI
	private void Warm()
	{
		foreach (Girl girl in Girls.All)
		{
			if (girl.Control != GirlControl.Remote && girl.Items != null && Vector3.Distance(girl.transform.position, middle) < reach)
			{
				girl.Items.temperatureLevel = Mathf.Min(100f, girl.Items.temperatureLevel + warmPerSecond * Time.deltaTime);
			}
		}
	}

	private void OnDestroy()
	{
		Release();
	}
}

public static class PlaceLines
{
	public struct Line
	{
		public string Who;

		public string Text;

		public Line(string who, string text)
		{
			Who = who;
			Text = text;
		}
	}

	private const string Y = Speaker.Yuuri;

	private const string C = Speaker.Chito;

	// 17, starter relics. mostly writing, meant to be rewritten
	public static readonly Line[][] Relics =
	{
		new[] { new Line(Y, "It says ten past four."), new Line(C, "It's said that for a long time."), new Line(Y, "So it's right sometimes."), new Line(C, "...That's the most sensible thing you've said this week.") },
		new[] { new Line(Y, "Where's the other one?"), new Line(C, "With the other person, probably."), new Line(Y, "Can we eat it?"), new Line(C, "No.") },
		new[] { new Line(Y, "Tiny broom."), new Line(C, "It's for teeth."), new Line(Y, "Teeth have floors?") },
		new[] { new Line(C, "You gave these to people and they gave you food."), new Line(Y, "People just gave you food?"), new Line(C, "If you had coins."), new Line(Y, "We've got one. Who do I give it to?") },
		new[] { new Line(C, "It keeps the rain off."), new Line(Y, "Why not go inside?"), new Line(C, "You had somewhere to be."), new Line(Y, "Nobody has anywhere to be."), new Line(C, "They did then.") },
		new[] { new Line(Y, "What's that in the sky?"), new Line(C, "The sun, I think."), new Line(Y, "It's got a face. Was it friendlier back then?") },
		new[] { new Line(Y, "What does it open?"), new Line(C, "We'll never know."), new Line(Y, "Then it opens nothing."), new Line(C, "Keep it anyway.") },
		new[] { new Line(Y, "Everything's gone wobbly."), new Line(C, "Give those back, someone needed them."), new Line(Y, "Who?") },
		new[] { new Line(Y, "What's it say?"), new Line(C, "\"Shake well.\""), new Line(Y, "Nothing's happening.") }
	};
}
