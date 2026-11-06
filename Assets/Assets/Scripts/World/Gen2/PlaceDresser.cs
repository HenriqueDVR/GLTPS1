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

		// the place frame's turn: local x along u, z along v
		public Quaternion Turn;

		public Transform Parent;

		public int Layer;

		public int LayerIndex;

		public Vector3 At(float u, float v, float y)
		{
			return Centre + U * u + V * v + Vector3.up * y;
		}

		// a direction in the place frame (x along u, z along v) as a world turn
		public Quaternion Facing(Vector3 placeDirection)
		{
			return Quaternion.LookRotation(U * placeDirection.x + V * placeDirection.z, Vector3.up);
		}

		public GameObject Spawn(string name, float u, float v, float y, Vector3 placeFacing, string child = null)
		{
			return PlaceArt.Spawn(name, Parent, At(u, v, y), Facing(placeFacing), Layer, child);
		}

		public GameObject Spawn(string name, Vector3 placeAt, Quaternion placeTurn)
		{
			return PlaceArt.Spawn(name, Parent, At(placeAt.x, placeAt.z, placeAt.y), Turn * placeTurn, Layer);
		}
	}

	public static void Dress(Place place, int layerIndex, float deckY, Vector3 offset, Transform props, int groundLayer, TileStreamer tiles)
	{
		Vector3 u = new Vector3((float)place.Axis.X, 0f, (float)place.Axis.Z);
		Vector3 v = new Vector3(-u.z, 0f, u.x);
		Frame frame = new Frame
		{
			Place = place,
			Centre = new Vector3((float)place.Center.X, deckY, (float)place.Center.Z) - offset,
			U = u,
			V = v,
			Turn = Quaternion.LookRotation(v, Vector3.up),
			Parent = props,
			Layer = groundLayer,
			LayerIndex = layerIndex
		};
		switch (place.Kind)
		{
		case PlaceKind.TankHull:
			Tank(frame);
			Crate(frame, 0, -2.6f, 1.1f);
			Crate(frame, 1, 2.6f, -1.1f);
			Relic(frame, 0f, -1.4f, Vector3.forward);
			break;
		case PlaceKind.DarkRoom:
			Room(frame);
			break;
		case PlaceKind.PumpHouse:
			Shell(frame);
			Pump(frame);
			break;
		case PlaceKind.FuelDepot:
			Depot(frame, tiles);
			break;
		}
		if (place.Kind != PlaceKind.FuelDepot && Gen2.Unit(WorldRandom.Derive(place.Seed, Gen2.StreamPlace, 6uL)) < hungShare)
		{
			Hung(frame);
		}
	}

	// the wreck itself, its way in at the door's end (the model's +x), a lamp left hanging on the torn plate
	private static void Tank(Frame frame)
	{
		frame.Spawn("tankhull", 0f, 0f, 0f, (frame.Place.Door == 0) ? Vector3.forward : Vector3.back);
		Lamp(frame);
	}

	private static void Lamp(Frame frame)
	{
		PlaceArt.Lamp(frame.Place, out Vector3 mount, out Vector3 facing);
		frame.Spawn("lamp_wall", mount.x, mount.z, mount.y, facing);
	}

	// the hook a crate hangs from: an i-beam gantry by the door; shoot the chain and it drops
	private static void Hung(Frame frame)
	{
		Place place = frame.Place;
		XZ door = place.DoorAt;
		Vector3 doorAt = new Vector3((float)door.X, frame.Centre.y, (float)door.Z) + (frame.Centre - new Vector3((float)place.Center.X, frame.Centre.y, (float)place.Center.Z));
		Vector3 outward = doorAt - frame.Centre;
		outward.y = 0f;
		outward.Normalize();
		Vector3 along = Vector3.Cross(Vector3.up, outward);
		// its nearer post well off the line in
		Vector3 middle = doorAt + outward * 5f + along * 4.2f;
		Quaternion turn = Quaternion.LookRotation(outward, Vector3.up);
		GameObject gantry = PlaceArt.Spawn("gantry", frame.Parent, middle, turn, frame.Layer);
		GameObject crate = PlaceArt.Spawn("crate_hung", frame.Parent, middle + Vector3.up * crateHung, turn, frame.Layer);
		if (gantry == null || crate == null)
		{
			return;
		}
		crate.layer = 0;
		HangingCrate hanging = crate.AddComponent<HangingCrate>();
		ulong flag = PlacePlan.Id(frame.LayerIndex, place.CellX, place.CellZ, chainFlag);
		PlaceLoot loot = default(PlaceLoot);
		loot.Food = 2;
		loot.Ammo = false;
		Transform lid = PlaceArt.Find(crate, "lid");
		Transform sling = PlaceArt.Find(crate, "sling");
		Interactable open = Once(crate, frame, hungCrate, "open", Verb.Use, loot.Food, 0f, 0f, girl => Got(girl, loot), delegate
		{
			OpenLid(lid);
			if (sling != null)
			{
				sling.gameObject.SetActive(false);
			}
		});
		open.Can = girl => hanging.Down;
		open.PromptHeight = 0.9f;
		float floorY = frame.Centre.y;
		bool already = WorldFlags.Has(flag);
		Transform chain = PlaceArt.Find(gantry, "chain");
		if (chain == null)
		{
			return;
		}
		// shots hit what they hit: the chain's own box, a good deal fatter than its links
		GameObject links = chain.gameObject;
		links.layer = 0;
		BoxCollider box = links.AddComponent<BoxCollider>();
		MeshFilter filter = links.GetComponent<MeshFilter>();
		Bounds bounds = (filter != null && filter.sharedMesh != null) ? filter.sharedMesh.bounds : new Bounds(Vector3.down * 0.5f, Vector3.one);
		box.center = bounds.center;
		box.size = new Vector3(Mathf.Max(0.3f, bounds.size.x), bounds.size.y, Mathf.Max(0.3f, bounds.size.z));
		links.AddComponent<Breakable>().Init(flag, delegate
		{
			hanging.Drop(floorY, already);
			links.SetActive(false);
			if (!already)
			{
				Speaker.Say(Speaker.Yuuri, "Got it.");
			}
		});
	}

	// the crate's base under the hook, where the sling's ring meets it (place_models.py: gantry, crate_hung)
	private const float crateHung = 2.45f;

	private static void OpenLid(Transform lid)
	{
		if (lid != null)
		{
			lid.localRotation = Quaternion.Euler(-105f, 0f, 0f);
		}
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
		GameObject crate = frame.Spawn("crate", u, v, 0f, Vector3.forward);
		if (crate == null)
		{
			return;
		}
		Transform lid = PlaceArt.Find(crate, "lid");
		Interactable open = Once(crate, frame, index, "open", Verb.Use, loot.Food, 0f, 0f, girl => Got(girl, loot), delegate
		{
			OpenLid(lid);
		});
		open.PromptHeight = 0.9f;
	}

	// a dark room: its lining inside LotMesh's walls, a steel door frame and a sign, a lamp over it, a wall of drawers
	// at one end, lockers along the far side, a shelf with the relic on the near one
	private static void Room(Frame frame)
	{
		Place place = frame.Place;
		float hu = (float)place.RoomU;
		float hv = (float)place.RoomV;
		PlaceArt.RoomLining(place, frame.Parent, frame.Centre, frame.Turn, frame.Layer);
		Vector3 n = PlaceArt.Out(place.Door);
		float reach = (place.Door < 2) ? hu : hv;
		frame.Spawn("door_frame", n.x * reach, n.z * reach, 0f, n);
		Lamp(frame);
		float end = (place.Door == 0) ? -1f : 1f;
		float wallU = end * (hu - PlaceArt.Wall - 0.01f - drawerDepth * 0.5f);
		int units = Mathf.Max(1, Mathf.FloorToInt((2f * (hv - PlaceArt.Wall) - 0.4f) / 2f));
		for (int i = 0; i < units; i++)
		{
			float at = (i - (units - 1) * 0.5f) * 2f;
			frame.Spawn("drawers", wallU, at, 0f, new Vector3(-end, 0f, 0f));
		}
		Lockers(frame);
		float side = (place.Door == 2) ? -1f : 1f;
		Relic(frame, -end * hu * 0.45f, -side * (hv - PlaceArt.Wall - 0.01f - 0.21f), new Vector3(0f, 0f, side));
	}

	private const float drawerDepth = 0.45f;

	private static void Lockers(Frame frame)
	{
		Place place = frame.Place;
		int count = Mathf.Clamp((int)(place.RoomU * 2.0 / lockerWidth) - 2, 3, 6);
		// along the wall away from the door, or the first long one that hasn't got it
		float side = (place.Door == 2) ? -1f : 1f;
		float back = (float)place.RoomV - PlaceArt.Wall - 0.01f - lockerDepth * 0.5f;
		for (int i = 0; i < count; i++)
		{
			PlaceLoot loot = PlacePlan.Loot(place, 10 + i);
			float along = (i - (count - 1) * 0.5f) * lockerWidth;
			GameObject locker = frame.Spawn("locker", along, side * back, 0f, new Vector3(0f, 0f, -side));
			if (locker == null)
			{
				continue;
			}
			Transform door = PlaceArt.Find(locker, "door");
			Interactable open = Once(locker, frame, 10 + i, "open", Verb.Use, loot.Food, 0f, 0f, girl => Got(girl, loot), delegate
			{
				if (door != null)
				{
					door.localRotation = Quaternion.Euler(0f, -95f, 0f);
				}
			});
			open.PromptHeight = 1.3f;
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

	// a small old-world thing on a steel shelf, and what they make of it. once. facing is the way the shelf looks
	private static void Relic(Frame frame, float u, float v, Vector3 facing)
	{
		int index = (int)(WorldRandom.Derive(frame.Place.Seed, Gen2.StreamPlace, 5uL) % (ulong)PlaceLines.Relics.Length);
		GameObject shelf = frame.Spawn("shelf", u, v, 0f, facing);
		if (shelf == null)
		{
			return;
		}
		GameObject thing = PlaceArt.Spawn("relics", shelf.transform, shelf.transform.TransformPoint(new Vector3(0f, relicBoard, 0.02f)), shelf.transform.rotation, frame.Layer, "relic_" + index);
		Interactable look = Once(shelf, frame, relicIndex, "look", Verb.Use, 0, 0f, 0f, delegate
		{
			foreach (PlaceLines.Line line in PlaceLines.Relics[index])
			{
				Speaker.SayToBoth(line.Who, line.Text);
			}
		}, delegate
		{
			// seen: put back a little askew. nothing more to say
			if (thing != null)
			{
				thing.transform.Rotate(0f, 30f, 0f, Space.Self);
			}
		});
		look.Range = 2.2f;
		look.PromptHeight = 1.3f;
	}

	// the shelf's board the relic sits on (place_models.py: shelf)
	private const float relicBoard = 0.9625f;

	// a pump house's walls and roof, its door, windows here and there, the stack, a fat pipe over the roof and down
	// the end wall into the ground
	private static void Shell(Frame frame)
	{
		Place place = frame.Place;
		float hu = (float)place.RoomU;
		float hv = (float)place.RoomV;
		List<Vector4> windows = new List<Vector4>();
		PlaceArt.PumpShell(place, frame.Parent, frame.Centre, frame.Turn, frame.Layer, windows);
		Vector3 n = PlaceArt.Out(place.Door);
		float reach = (place.Door < 2) ? hu : hv;
		frame.Spawn("ph_door", n.x * reach, n.z * reach, 0f, n);
		foreach (Vector4 w in windows)
		{
			frame.Spawn("ph_window", w.x, w.z, 0f, PlaceArt.Out((int)w.y));
		}
		float roof = PlaceArt.PumpHeight + 0.2f;
		frame.Spawn("ph_stack", new Vector3(hu * 0.5f, roof, 0f), Quaternion.identity);
		// the pipe: along the roof on the side away from the door, over the parapet at the far end, down into the snow
		float back = (place.Door == 2) ? -1f : 1f;
		float end = (place.Door == 1) ? 1f : -1f;
		float z = back * (hv - 0.8f);
		float y = roof + 0.3f + pipeRadius;
		float x0 = -end * (hu - 1f);
		float corner = end * (hu + 0.5f);
		float x1 = corner - end * elbowArm;
		GameObject run = frame.Spawn("pipe_run", new Vector3((x0 + x1) * 0.5f, y, z), Quaternion.identity);
		if (run != null)
		{
			run.transform.localScale = new Vector3(Mathf.Abs(x1 - x0) * 0.5f, 1f, 1f);
		}
		for (float x = x0 + end * 0.6f; Mathf.Abs(x) < hu - 0.4f; x += end * 2.5f)
		{
			frame.Spawn("pipe_saddle", new Vector3(x, roof, z), Quaternion.identity);
		}
		frame.Spawn("pipe_elbow", new Vector3(corner, y, z), (end > 0f) ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f));
		float down = y - elbowArm;
		GameObject drop = frame.Spawn("pipe_run", new Vector3(corner, (down - 0.3f) * 0.5f, z), Quaternion.Euler(0f, 0f, 90f));
		if (drop != null)
		{
			drop.transform.localScale = new Vector3((down + 0.3f) * 0.5f, 1f, 1f);
			BoxCollider stop = drop.AddComponent<BoxCollider>();
			stop.size = new Vector3(2f, pipeRadius * 2f, pipeRadius * 2f);
		}
	}

	// the pipe kit (place_models.py): its radius, the elbow's arms
	private const float pipeRadius = 0.3f;

	private const float elbowArm = 0.9f;

	// the pump against the wall away from the door, a wheel either side of it, a tub at one end that fills once the
	// water's hot
	private static void Pump(Frame frame)
	{
		Place place = frame.Place;
		float back = (place.Door == 2) ? -1f : 1f;
		float across = back * ((float)place.RoomV - 1.0f);
		Vector3 toRoom = new Vector3(0f, 0f, -back);
		ulong a = PlacePlan.Id(frame.LayerIndex, place.CellX, place.CellZ, wheelA);
		ulong b = PlacePlan.Id(frame.LayerIndex, place.CellX, place.CellZ, wheelB);
		ulong done = PlacePlan.Id(frame.LayerIndex, place.CellX, place.CellZ, hotWater);
		// each stub points at the pump: which hand the model has depends on which wall it's on
		GameObject one = Wheel(frame, wheelA, -2.2f, across, toRoom, (back > 0f) ? "valve_r" : "valve_l");
		GameObject two = Wheel(frame, wheelB, 2.2f, across, toRoom, (back > 0f) ? "valve_l" : "valve_r");
		GameObject works = frame.Spawn("pump", 0f, across, 0f, toRoom);
		float tubEnd = (place.Door == 0) ? -1f : 1f;
		GameObject tub = frame.Spawn("tub", tubEnd * ((float)place.RoomU - PlaceArt.Wall - 0.65f), 0f, 0f, new Vector3(-tubEnd, 0f, 0f));
		Transform water = PlaceArt.Find(tub, "water");
		if (works == null || one == null || two == null)
		{
			return;
		}
		PumpHouse pump = works.AddComponent<PumpHouse>();
		pump.Init(a, b, done, one.transform, two.transform, hotWaterAmount, place.RoomU, frame.At(0f, 0f, 0f), (water != null) ? water.gameObject : null);
	}

	private static GameObject Wheel(Frame frame, int index, float u, float v, Vector3 facing, string model)
	{
		GameObject post = frame.Spawn(model, u, v, 0f, facing);
		if (post == null)
		{
			return null;
		}
		Transform wheel = PlaceArt.Find(post, "wheel");
		Interactable turn = Once(post, frame, index, "turn", Verb.Use, 0, 0f, 0f, null, delegate
		{
			if (wheel != null)
			{
				wheel.localRotation = Quaternion.Euler(0f, 0f, 100f);
			}
		});
		turn.Press = Press.Hold;
		turn.HoldTime = wheelHold;
		turn.PromptHeight = 1.5f;
		return post;
	}

	// a fuel depot: the game's own fuel tower (the kit's landmark) at the end away from the door, a bund wall along
	// the far side, the valve on its pipe and the drums on the near side
	private static void Depot(Frame frame, TileStreamer tiles)
	{
		Place place = frame.Place;
		float hu = (float)place.RoomU;
		float hv = (float)place.RoomV;
		float away = (place.Door == 0) ? -1f : 1f;
		if (tiles != null && tiles.LandmarkSize(PlaceArt.DepotTower, out Vector3 size))
		{
			float scale = PlaceArt.DepotFit(place, size, out float towerU, out bool turned);
			Quaternion turn = frame.Turn * Quaternion.Euler(0f, turned ? 90f : 0f, 0f);
			tiles.SpawnLandmark(PlaceArt.DepotTower, frame.Parent, frame.At(towerU, 0f, 0f), turn, scale);
		}
		int far = place.Door ^ 1;
		Vector3 n = PlaceArt.Out(far);
		Vector3 along = Vector3.Cross(Vector3.up, n);
		float reach = (far < 2) ? hu : hv;
		float length = (far < 2) ? hv : hu;
		int pieces = Mathf.Max(1, Mathf.RoundToInt(length));
		float piece = length * 2f / pieces;
		for (int i = 0; i < pieces; i++)
		{
			Vector3 at = n * (reach - 0.25f) + along * (-length + (i + 0.5f) * piece);
			GameObject wall = frame.Spawn("bund", at.x, at.z, 0f, n);
			if (wall != null)
			{
				wall.transform.localScale = new Vector3(piece * 0.5f, 1f, 1f);
			}
		}
		float valveU = -away * hu * 0.55f;
		GameObject pipe = frame.Spawn("depot_valve", valveU, 0f, 0f, new Vector3(-away, 0f, 0f));
		for (int i = 0; i < drums.Length; i++)
		{
			Vector3 d = drums[i];
			if (Gen2.Unit(WorldRandom.Derive(place.Seed, Gen2.StreamPlace, 30uL + (ulong)i)) < 0.25)
			{
				continue;
			}
			GameObject drum = frame.Spawn("drum", valveU - away * d.x, d.z * (hv - 1.2f), d.y, Vector3.forward);
			if (drum != null && d.y > 0f)
			{
				// one on its side
				drum.transform.rotation = drum.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
			}
		}
		if (pipe == null)
		{
			return;
		}
		Transform wheel = PlaceArt.Find(pipe, "wheel");
		Interactable open = Once(pipe, frame, valve, "open the valve", Verb.Use, 0, 0f, depotShare, null, delegate
		{
			if (wheel != null)
			{
				wheel.localRotation = Quaternion.Euler(0f, 90f, 0f);
			}
		});
		open.Press = Press.Hold;
		open.HoldTime = valveHold;
		open.PromptHeight = 1.7f;
		// the ket's tank's what it fills. no point with the ket left miles off
		Transform valveAt = pipe.transform;
		open.Can = girl => KetNear(valveAt.position);
	}

	// drums by the valve: x further from it, y lying (raised to its middle), z out along the side as a share
	private static readonly Vector3[] drums =
	{
		new Vector3(-0.4f, 0f, 0.8f),
		new Vector3(0.4f, 0f, 0.85f),
		new Vector3(0f, 0f, 0.62f),
		new Vector3(-0.6f, 0f, -0.8f),
		new Vector3(0.9f, 0.3f, -0.6f)
	};

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

	// the room's middle in the pump's own space: the world moves under the floating origin
	private Vector3 middle;

	private Vector3 Middle => transform.TransformPoint(middle);

	private float nextCheck;

	private float aiFor;

	// the tub's water, there once it's running
	private GameObject bath;

	public void Init(ulong wheelA, ulong wheelB, ulong hotWater, Transform first, Transform second, float amount, double room, Vector3 centre, GameObject tubWater)
	{
		bath = tubWater;
		a = wheelA;
		b = wheelB;
		done = hotWater;
		one = first;
		two = second;
		water = amount;
		reach = (float)room + 1f;
		middle = transform.InverseTransformPoint(centre);
	}

	public bool Open => WorldFlags.Has(a) && WorldFlags.Has(b);

	private void Update()
	{
		bool open = Open;
		if (open)
		{
			Warm();
		}
		// every peer from its own flags, a friend coming back too
		if (bath != null && bath.activeSelf != open)
		{
			bath.SetActive(open);
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
		if (other == null || other.Ai == null || firstDone == WorldFlags.Has(b) || Vector3.Distance(Girls.Local.transform.position, Middle) > reach * 2f)
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
			if (girl.Control != GirlControl.Remote && girl.Items != null && Vector3.Distance(girl.transform.position, Middle) < reach)
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
