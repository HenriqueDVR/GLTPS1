using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// food/water/fuel are shared, host's copy is the real one. friend sends running totals
// and the host acks how much it counted so nothing in flight gets undone
public class CoopSupplies : MonoBehaviour
{
	private const float sendInterval = 0.1f;

	private readonly NetDataWriter writer = new NetDataWriter();

	private CoopSession session;

	private CharacterStatus status;

	private VehicleFuelManager fuel;

	private float nextSend;

	// so the host knows when the totals restart
	private int friendRun;

	private int foodTotal;

	private double waterTotal;

	private double fuelTotal;

	private int seenFood;

	private float seenWater;

	private float seenFuel;

	private bool hasHostState;

	private int hostFood;

	private float hostWater;

	private float hostFuel;

	private int ackedFood;

	private double ackedWater;

	private double ackedFuel;

	private int countedRun;

	private int countedFood;

	private double countedWater;

	private double countedFuel;

	private void Awake()
	{
		session = CoopSession.Instance;
		session.SuppliesReceived += OnSupplies;
		friendRun = Random.Range(1, int.MaxValue);
	}

	private void Start()
	{
		status = Object.FindObjectOfType<CharacterStatus>();
		fuel = Object.FindObjectOfType<VehicleFuelManager>();
		seenFood = status.index;
		seenWater = status.waterLevel;
		seenFuel = fuel.currentFuel;
	}

	private void OnDestroy()
	{
		if (session != null)
		{
			session.SuppliesReceived -= OnSupplies;
		}
	}

	private void Update()
	{
		CharacterItemInteraction local = Girl(CoopPlayers.Instance?.Local);
		if (local == null)
		{
			return;
		}
		if (!session.IsHost)
		{
			foodTotal += status.index - seenFood;
			waterTotal += status.waterLevel - seenWater;
			fuelTotal += fuel.currentFuel - seenFuel;
			if (hasHostState)
			{
				hasHostState = false;
				local.SetFood(hostFood + foodTotal - ackedFood);
				status.waterLevel = hostWater + (float)(waterTotal - ackedWater);
				fuel.currentFuel = hostFuel + (float)(fuelTotal - ackedFuel);
			}
			seenFood = status.index;
			seenWater = status.waterLevel;
			seenFuel = fuel.currentFuel;
		}
		if (Time.unscaledTime >= nextSend && !CoopGirlState.Pending)
		{
			nextSend = Time.unscaledTime + sendInterval;
			Send(local);
		}
	}

	private void Send(CharacterItemInteraction local)
	{
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Supplies);
		if (session.IsHost)
		{
			writer.Put(countedRun);
			writer.Put(status.index);
			writer.Put(status.waterLevel);
			writer.Put(fuel.currentFuel);
			writer.Put(countedFood);
			writer.Put(countedWater);
			writer.Put(countedFuel);
		}
		else
		{
			writer.Put(friendRun);
			writer.Put(foodTotal);
			writer.Put(waterTotal);
			writer.Put(fuelTotal);
		}
		writer.Put(local.hungerLevel);
		writer.Put(local.thirstLevel);
		writer.Put(local.temperatureLevel);
		session.Send(writer, DeliveryMethod.Sequenced);
	}

	private void OnSupplies(NetDataReader reader)
	{
		if (status == null)
		{
			return;
		}
		CharacterItemInteraction local = Girl(CoopPlayers.Instance?.Local);
		int run = reader.GetInt();
		if (session.IsHost)
		{
			int @int = reader.GetInt();
			double @double = reader.GetDouble();
			double double3 = reader.GetDouble();
			if (run != countedRun)
			{
				countedRun = run;
				countedFood = 0;
				countedWater = 0.0;
				countedFuel = 0.0;
			}
			if (local != null)
			{
				local.SetFood(status.index + @int - countedFood);
			}
			status.waterLevel += (float)(@double - countedWater);
			fuel.currentFuel += (float)(double3 - countedFuel);
			countedFood = @int;
			countedWater = @double;
			countedFuel = double3;
		}
		else
		{
			int int2 = reader.GetInt();
			float @float = reader.GetFloat();
			float float2 = reader.GetFloat();
			int int3 = reader.GetInt();
			double double2 = reader.GetDouble();
			double double4 = reader.GetDouble();
			// counts are from an older run until the host sees this one
			if (run == friendRun)
			{
				hostFood = int2;
				hostWater = @float;
				hostFuel = float2;
				ackedFood = int3;
				ackedWater = double2;
				ackedFuel = double4;
				hasHostState = true;
			}
		}
		CharacterItemInteraction remote = Girl(CoopPlayers.Instance?.Remote);
		if (remote != null)
		{
			remote.hungerLevel = reader.GetFloat();
			remote.thirstLevel = reader.GetFloat();
			remote.temperatureLevel = reader.GetFloat();
		}
	}

	private static CharacterItemInteraction Girl(Transform girl)
	{
		if (!(girl != null))
		{
			return null;
		}
		return girl.GetComponentInChildren<CharacterItemInteraction>(includeInactive: true);
	}
}
