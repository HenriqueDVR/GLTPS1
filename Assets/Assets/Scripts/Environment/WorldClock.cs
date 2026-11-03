using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// the time of day. host or solo runs it, a friend runs its own copy toward the host's.
// state's static: a save or a snapshot is read before the scene's up, the component only ticks it
public partial class WorldClock : MonoBehaviour
{
	private const float sendEvery = 10f;

	private static double total = DayClock.NewGame;

	// what the host last said, moved on locally, a friend follows it
	private static double host = DayClock.NewGame;

	private static float rate = 1f;

	private float nextSend;

	public static int Day => DayClock.Day(total);

	public static double TimeOfDay => DayClock.TimeOfDay(total);

	public static DayPhase Phase => DayClock.PhaseOf(TimeOfDay);

	public static float Rate => rate;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		NetRegistry.Register(NetRegistry.Kind.Clock, OnMessage);
		NetRegistry.CatchUp += arrived => Send();
		StateRegistry.Register(new State(), StateUse.Both);
	}

	// a new game: dusk
	public static void ResetLevel()
	{
		Set(DayClock.NewGame);
	}

	// a save's about to be read, one from before the clock has no section
	public static void ResetForLoad()
	{
		Set(DayClock.OldSave);
	}

	private static void Set(double value)
	{
		total = value;
		host = value;
		rate = 1f;
	}

	// a ritual that pays in daylight. the host decides, false on a friend
	public static bool Advance(double seconds)
	{
		if (!NetRegistry.HostOrSolo || seconds <= 0.0)
		{
			return false;
		}
		total = DayClock.Tick(total, seconds, 1.0);
		host = total;
		Send();
		return true;
	}

	// sleep runs the night fast. the host decides, false on a friend
	public static bool SetRate(float newRate)
	{
		if (!NetRegistry.HostOrSolo || newRate < 0f)
		{
			return false;
		}
		rate = newRate;
		Send();
		return true;
	}

	// always reliable: one small message every 10 s, and an old one can't land after an Advance and pull a friend back
	private static void Send()
	{
		if (!CoopSession.Active || !CoopSession.Instance.IsHost)
		{
			return;
		}
		NetRegistry.Send(NetRegistry.Kind.Clock, NetRegistry.SessionId, delegate(NetDataWriter writer)
		{
			writer.Put(total);
			writer.Put(rate);
		}, DeliveryMethod.ReliableOrdered);
	}

	private static void OnMessage(ulong netId, NetDataReader reader)
	{
		double said = reader.GetDouble();
		float saidRate = reader.GetFloat();
		if (NetRegistry.HostOrSolo)
		{
			return;
		}
		host = said;
		rate = saidRate;
	}

	private void Update()
	{
		host = DayClock.Tick(host, Time.deltaTime, rate);
		if (NetRegistry.HostOrSolo)
		{
			total = host;
			if (Time.unscaledTime >= nextSend)
			{
				nextSend = Time.unscaledTime + sendEvery;
				Send();
			}
		}
		else
		{
			total = DayClock.Follow(total, host, Time.deltaTime, rate);
		}
		DebugTick();
	}

	private void Awake()
	{
		DebugArgs();
	}

	partial void DebugArgs();

	partial void DebugTick();

	public class State : IStateful
	{
		public const string SectionKey = "clock";

		public string Key => SectionKey;

		public int Version => 1;

		public void Write(StateWriter writer)
		{
			writer.Put(total);
		}

		public void Read(StateReader reader, int version)
		{
			double read = reader.GetDouble();
			if (!(read >= 0.0))
			{
				throw new System.IO.InvalidDataException("clock " + read);
			}
			Set(read);
		}
	}
}
