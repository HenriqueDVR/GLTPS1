using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

// the hand lamp (17 section 2): L or the pad's stick click, aimed where you look, on foot and on the back of the ket. the
// headlight only points ahead, this finds what's off the road at night and shows the way in dark rooms. each player's
// own, the other's only hears where it's pointing. in solo the AI girl keeps hers on when it's dark, sweeps it toward
// things and says what she's seen, which is the rider's job when there's no second player
public class GirlLamp : MonoBehaviour
{
	private const float range = 45f;

	private const float angle = 42f;

	private const float intensity = 40f;

	private const float sendEvery = 0.1f;

	private const float turnRate = 6f;

	private const float spotEvery = 1f;

	private const double spotReach = 220.0;

	private const float sweepFor = 4f;

	private Girl girl;

	private Light lamp;

	private Quaternion aim = Quaternion.identity;

	private Quaternion wanted = Quaternion.identity;

	private bool on;

	private float nextSend;

	private float nextSpot;

	private float sweepUntil;

	private Vector3 sweepAt;

	private static readonly HashSet<long> spoken = new HashSet<long>();

	private static bool toldOfLamp;

	private static readonly GirlLamp[] byId = new GirlLamp[2];

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		NetRegistry.Register(NetRegistry.Kind.Lamp, OnMessage);
	}

	public void Init(Girl owner)
	{
		girl = owner;
		byId[(int)owner.Id] = this;
		spoken.Clear();
		toldOfLamp = false;
		GameObject holder = new GameObject("hand lamp");
		holder.transform.SetParent(transform, false);
		holder.transform.localPosition = new Vector3(0.15f, 1.15f, 0.25f);
		lamp = holder.AddComponent<Light>();
		lamp.type = LightType.Spot;
		lamp.range = range;
		lamp.spotAngle = angle;
		lamp.intensity = intensity;
		lamp.color = new Color(1f, 0.93f, 0.78f);
		lamp.shadows = LightShadows.None;
		lamp.renderMode = LightRenderMode.ForcePixel;
		lamp.enabled = false;
	}

	private static void OnMessage(ulong netId, NetDataReader reader)
	{
		bool isOn = reader.GetBool();
		float yaw = reader.GetFloat();
		float pitch = reader.GetFloat();
		GirlLamp who = (netId < 2uL) ? byId[(int)netId] : null;
		if (who != null && who.girl != null && who.girl.Control == GirlControl.Remote)
		{
			who.on = isOn;
			who.lamp.enabled = isOn;
			who.wanted = Quaternion.Euler(pitch, yaw, 0f);
		}
	}

	private void Update()
	{
		if (girl == null)
		{
			return;
		}
		switch (girl.Control)
		{
		case GirlControl.Player:
			Played();
			break;
		case GirlControl.AI:
			AiLamp();
			break;
		case GirlControl.Down:
			Set(false);
			break;
		}
		aim = Quaternion.Slerp(aim, wanted, 1f - Mathf.Exp(-turnRate * Time.deltaTime));
		lamp.transform.rotation = aim;
	}

	private void Set(bool value)
	{
		on = value;
		lamp.enabled = value;
	}

	private static bool Dark
	{
		get
		{
			AtmosphereStack stack = GameSession.Get<AtmosphereStack>();
			return WorldClock.Phase == DayPhase.Night || (stack != null && stack.Dark > 0.5f);
		}
	}

	private void Played()
	{
		bool pressed = false;
		Keyboard keys = Keyboard.current;
		if (keys != null && keys.lKey.wasPressedThisFrame)
		{
			pressed = true;
		}
		Gamepad pad = Gamepad.current;
		if (pad != null && pad.leftStickButton.wasPressedThisFrame)
		{
			pressed = true;
		}
		if (pressed && Time.timeScale > 0f)
		{
			Set(!on);
			Send(true);
		}
		// where she's looking
		Camera view = Camera.main;
		if (view != null)
		{
			wanted = view.transform.rotation;
			if (on && Time.unscaledTime >= nextSend)
			{
				nextSend = Time.unscaledTime + sendEvery;
				Send(false);
			}
		}
		if (!toldOfLamp && !on && Dark && CoopSession.InGame == false)
		{
			toldOfLamp = true;
			Girl other = Girls.Other(girl);
			if (other != null)
			{
				Speaker.Say(GirlNeeds.NameOf(other), (other.Id == GirlId.Yuuri) ? "Chi-chan, the lamp. [L]" : "Yuu, the lamp. [L]");
			}
		}
	}

	private void Send(bool reliable)
	{
		if (!CoopSession.Active)
		{
			return;
		}
		Vector3 angles = wanted.eulerAngles;
		NetRegistry.Send(NetRegistry.Kind.Lamp, (ulong)(int)girl.Id, delegate(NetDataWriter writer)
		{
			writer.Put(on);
			writer.Put(angles.y);
			writer.Put(angles.x);
		}, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
	}

	private void AiLamp()
	{
		if (Time.time >= nextSpot)
		{
			nextSpot = Time.time + spotEvery;
			Set(Dark);
			Spot();
		}
		Vector3 facing = transform.forward;
		if (Time.time < sweepUntil)
		{
			facing = sweepAt - lamp.transform.position;
		}
		else if (Girls.Local != null)
		{
			// ahead and a little to the side, slowly back and forth
			facing = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.6f) * 35f, 0f) * Girls.Local.transform.forward;
		}
		facing.y = -0.08f * facing.magnitude;
		if (facing.sqrMagnitude > 0.001f)
		{
			wanted = Quaternion.LookRotation(facing);
		}
	}

	// something worth stopping for: she says so, once a place, and points the lamp at it
	private void Spot()
	{
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		Girl player = Girls.Local;
		if (world == null || player == null || CoopSession.InGame || WakeFlow.Busy)
		{
			return;
		}
		Vector3 layout = player.transform.position + SaveScene.Offset();
		XZ here = new XZ(layout.x, layout.z);
		if (!world.Plan.Places.Nearest(p => true, here, spotReach, out Place place) || place.Center.DistanceTo(here) > spotReach)
		{
			return;
		}
		long key = ((long)world.Layer << 48) ^ ((long)place.CellX << 24) ^ (uint)place.CellZ;
		if (!spoken.Add(key))
		{
			return;
		}
		Vector3 where = new Vector3((float)place.Center.X, layout.y, (float)place.Center.Z) - SaveScene.Offset();
		sweepAt = where + Vector3.up * 2f;
		sweepUntil = Time.time + sweepFor;
		Vector3 to = where - player.transform.position;
		to.y = 0f;
		Transform front = player.Movement != null && player.Movement.Body != null ? player.Movement.Body : player.transform;
		float side = Vector3.SignedAngle(front.forward, to, Vector3.up);
		string way = (Mathf.Abs(side) < 35f) ? "Ahead." : (Mathf.Abs(side) > 135f) ? "Behind us." : (side > 0f) ? "On the right." : "On the left.";
		Speaker.Say(GirlNeeds.NameOf(girl), What(place.Kind) + " " + way);
	}

	private string What(PlaceKind kind)
	{
		bool yuuri = girl.Id == GirlId.Yuuri;
		switch (kind)
		{
		case PlaceKind.FuelDepot:
			return yuuri ? "Chi-chan, a big drum." : "A fuel tank.";
		case PlaceKind.PumpHouse:
			return yuuri ? "Something's steaming." : "Steam, there's a pump house.";
		case PlaceKind.TankHull:
			return yuuri ? "One of those tanks." : "An old tank.";
		default:
			return yuuri ? "A door, a dark one." : "There's a doorway.";
		}
	}
}

// something a shot breaks, once for both (the first of GUN-1). shooter asks the host, the flag does the rest
public class Breakable : MonoBehaviour
{
	private ulong id;

	private System.Action broken;

	private bool done;

	public void Init(ulong flag, System.Action onBroken)
	{
		id = flag;
		broken = onBroken;
		if (WorldFlags.Has(id))
		{
			Break();
		}
		else
		{
			WorldFlags.Changed += OnFlag;
		}
	}

	private void OnDestroy()
	{
		WorldFlags.Changed -= OnFlag;
	}

	private void OnFlag(ulong flag)
	{
		if (flag == id)
		{
			Break();
		}
	}

	private void Break()
	{
		if (!done)
		{
			done = true;
			WorldFlags.Changed -= OnFlag;
			broken?.Invoke();
		}
	}

	// from the shooter's own raycast
	public static void Shot(Collider what)
	{
		Breakable thing = what.GetComponent<Breakable>();
		if (thing != null && !thing.done)
		{
			WorldFlags.Claim(thing.id, null);
		}
	}
}

// falls when its chain's shot, a place's crate you can only reach that way
public class HangingCrate : MonoBehaviour
{
	private const float gravity = 12f;

	private float floor;

	private float speed;

	private bool falling;

	public bool Down { get; private set; }

	public void Drop(float groundY, bool now)
	{
		floor = groundY;
		falling = !now;
		Down = now;
		if (now)
		{
			Vector3 p = transform.position;
			transform.position = new Vector3(p.x, floor, p.z);
		}
	}

	private void Update()
	{
		if (!falling)
		{
			return;
		}
		speed += gravity * Time.deltaTime;
		Vector3 p = transform.position;
		p.y -= speed * Time.deltaTime;
		if (p.y <= floor)
		{
			p.y = floor;
			falling = false;
			Down = true;
		}
		transform.position = p;
	}
}
