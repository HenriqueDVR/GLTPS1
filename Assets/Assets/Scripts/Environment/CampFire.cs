using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// what a fire's for (17 section 6). it burns a couple of minutes, warms slowly, melts snow while it's lit
// (CharacterStatus), and at it you can cook and, under a roof once it's getting dark, lie down for the night. added to
// every fire as it's lit, on both peers
public class CampFire : MonoBehaviour
{
	public const float BurnSeconds = 120f;

	// warmth a second by a lit fire: all of it in about a minute
	public const float WarmPerSecond = 1.5f;

	private const float reach = 3.5f;

	private const float cookHold = 2.5f;

	private const float cupOfWater = 20f;

	private const float mealFills = 35f;

	private const float mealWarms = 10f;

	// a meal costs daylight (17 section 1)
	private const double cookSeconds = 30.0;

	private CampfireFuelLevel level;

	private void Awake()
	{
		level = GetComponentInChildren<CampfireFuelLevel>();
		if (level == null)
		{
			Destroy(this);
			return;
		}
		level.SetBurnSeconds(BurnSeconds);
		Interactable cook = gameObject.AddComponent<Interactable>();
		cook.Verb = Verb.Use;
		cook.Press = Press.Hold;
		cook.HoldTime = cookHold;
		cook.Range = reach;
		cook.Label = "cook";
		cook.Priority = 2;
		cook.Can = CanCook;
		cook.Run = Cook;
		Interactable sleep = gameObject.AddComponent<Interactable>();
		sleep.Verb = Verb.Use;
		sleep.Press = Press.Tap;
		sleep.Priority = 2;
		sleep.Range = reach;
		sleep.Label = "lie down";
		sleep.Can = CanSleep;
		sleep.Run = girl => Camp.LieDown(girl, this);
	}

	public bool Lit => level != null && level.currentFuelLevel > 0f;

	private bool CanCook(Girl girl)
	{
		if (!Lit || girl == null)
		{
			return false;
		}
		CharacterStatus status = Camp.Status;
		return status != null && status.index > 0 && status.waterLevel >= cupOfWater;
	}

	private bool CanSleep(Girl girl)
	{
		return Lit && girl != null && girl.Items.isUnderCeiling && Camp.NightComing && !Camp.IsLying(girl) && !WakeFlow.Busy;
	}

	private void Cook(Girl girl)
	{
		CharacterStatus status = Camp.Status;
		girl.Items.RemoveFood(1);
		status.waterLevel -= cupOfWater;
		Camp.Ate(transform.position, mealFills, mealWarms, cookSeconds);
		Speaker.SayToBoth(Speaker.Yuuri, "It's the same ration.");
		Speaker.SayToBoth(Speaker.Chito, "But hot.");
	}
}

// the camp's shared part: who's lying down, the night passing, meals reaching whoever's by the fire. the host or solo
// decides when the night passes, each player's girl is their own
public static class Camp
{
	private enum Op : byte
	{
		Lying,
		Sleep,
		Meal,
		Advance
	}

	// in co-op the other player's a while coming, then she gets up again
	private const float waitForOther = 45f;

	private const float aiComes = 10f;

	private const float mealReach = 6f;

	private static float lyingSince = -1f;

	private static bool remoteLying;

	private static bool aiLying;

	private static InputContextStack.Token token;

	private static CampFire fire;

	private static CharacterStatus status;

	public static CharacterStatus Status
	{
		get
		{
			if (status == null)
			{
				status = Object.FindObjectOfType<CharacterStatus>();
			}
			return status;
		}
	}

	public static bool NightComing
	{
		get
		{
			DayPhase phase = WorldClock.Phase;
			return phase == DayPhase.Dusk || phase == DayPhase.Night || phase == DayPhase.Dawn;
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		NetRegistry.Register(NetRegistry.Kind.Camp, OnMessage);
	}

	public static void Reset()
	{
		lyingSince = -1f;
		remoteLying = false;
		aiLying = false;
		token = null;
		fire = null;
		status = null;
	}

	public static bool IsLying(Girl girl)
	{
		return girl == Girls.Local && lyingSince >= 0f;
	}

	private static void Send(Op op, System.Action<NetDataWriter> more)
	{
		NetRegistry.Send(NetRegistry.Kind.Camp, NetRegistry.SessionId, delegate(NetDataWriter writer)
		{
			writer.Put((byte)op);
			more?.Invoke(writer);
		}, DeliveryMethod.ReliableOrdered);
	}

	private static void OnMessage(ulong netId, NetDataReader reader)
	{
		switch ((Op)reader.GetByte())
		{
		case Op.Lying:
			remoteLying = reader.GetBool();
			break;
		case Op.Sleep:
			if (!NetRegistry.HostOrSolo)
			{
				WakeFlow.SleptOnWord();
			}
			break;
		case Op.Meal:
		{
			Vector3 at = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
			float fills = reader.GetFloat();
			float warms = reader.GetFloat();
			Feed(SaveScene.FromLayout(at), fills, warms);
			break;
		}
		case Op.Advance:
		{
			double seconds = reader.GetDouble();
			if (NetRegistry.HostOrSolo)
			{
				WorldClock.Advance(seconds);
			}
			break;
		}
		}
	}

	// whoever's by the fire eats. each player feeds their own girl
	public static void Ate(Vector3 where, float fills, float warms, double seconds)
	{
		Vector3 layout = where + SaveScene.Offset();
		Send(Op.Meal, delegate(NetDataWriter writer)
		{
			writer.Put(layout.x);
			writer.Put(layout.y);
			writer.Put(layout.z);
			writer.Put(fills);
			writer.Put(warms);
		});
		Feed(where, fills, warms);
		if (NetRegistry.HostOrSolo)
		{
			WorldClock.Advance(seconds);
		}
		else
		{
			Send(Op.Advance, writer => writer.Put(seconds));
		}
	}

	private static void Feed(Vector3 where, float fills, float warms)
	{
		foreach (Girl girl in Girls.All)
		{
			if (girl.Control != GirlControl.Remote && girl.Items != null && Vector3.Distance(girl.transform.position, where) < mealReach)
			{
				girl.Items.hungerLevel = Mathf.Min(100f, girl.Items.hungerLevel + fills);
				girl.Items.temperatureLevel = Mathf.Min(100f, girl.Items.temperatureLevel + warms);
			}
		}
	}

	public static void LieDown(Girl girl, CampFire by)
	{
		if (lyingSince >= 0f)
		{
			return;
		}
		lyingSince = Time.time;
		fire = by;
		Lay(girl);
		token = girl.Input.Contexts.Push(InputContext.Downed, "sleep");
		Send(Op.Lying, writer => writer.Put(true));
		// the AI comes and lies down too
		Girl other = Girls.Other(girl);
		if (other != null && other.Control == GirlControl.AI && other.Ai != null)
		{
			other.Ai.Tend = by.transform;
		}
	}

	private static void Lay(Girl girl)
	{
		if (girl.Barrel != null && girl.Animator.GetCurrentAnimatorStateInfo(2).IsTag("Carry"))
		{
			girl.Barrel.DropBarrel();
		}
		girl.Animator.SetTrigger("die");
	}

	// WakeFlow's Update
	public static void Tick()
	{
		if (lyingSince < 0f || WakeFlow.Busy)
		{
			return;
		}
		Girl me = Girls.Local;
		Girl other = (me != null) ? Girls.Other(me) : null;
		if (other != null && other.Control == GirlControl.AI && other.Ai != null)
		{
			if (!aiLying && fire != null && (Vector3.Distance(other.transform.position, fire.transform.position) < 3f || Time.time >= lyingSince + aiComes))
			{
				aiLying = true;
				other.Ai.Tend = null;
				other.Agent.enabled = false;
				other.AgentVehicle.enabled = false;
				Lay(other);
			}
			if (aiLying)
			{
				WakeFlow.Sleep();
			}
			return;
		}
		if (remoteLying && NetRegistry.HostOrSolo)
		{
			WakeFlow.Sleep();
			return;
		}
		// alone by the fire with no one coming: a host with no friend sleeps, otherwise up again
		if (CoopSession.InGame && !CoopSession.Instance.Connected && NetRegistry.HostOrSolo)
		{
			WakeFlow.Sleep();
			return;
		}
		if (Time.time >= lyingSince + waitForOther)
		{
			Up();
			Send(Op.Lying, writer => writer.Put(false));
		}
	}

	public static void SleepWord()
	{
		Send(Op.Sleep, null);
	}

	// the night's over, or nobody came
	public static void Up()
	{
		if (lyingSince < 0f)
		{
			return;
		}
		lyingSince = -1f;
		remoteLying = false;
		fire = null;
		Girl me = Girls.Local;
		token?.Release();
		token = null;
		Stand(me);
		Girl other = Girls.Other(me);
		if (aiLying && other != null)
		{
			aiLying = false;
			other.Agent.enabled = true;
			other.AgentVehicle.enabled = true;
			Stand(other);
		}
		else if (other != null && other.Ai != null)
		{
			other.Ai.Tend = null;
		}
	}

	private static void Stand(Girl girl)
	{
		girl.Animator.Rebind();
		girl.Animator.Update(0f);
	}

	public static void PutFiresOut()
	{
		foreach (CampfireFuelLevel level in Object.FindObjectsOfType<CampfireFuelLevel>())
		{
			level.currentFuelLevel = 0f;
		}
	}
}
