using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// placing / putting out is synced, burning down runs on each side
public class CoopCampfire : MonoBehaviour
{
	private static readonly Dictionary<int, CoopCampfire> placed = new Dictionary<int, CoopCampfire>();

	private static readonly NetDataWriter writer = new NetDataWriter();

	private static int nextId;

	public int Id { get; private set; }

	// lit ones for a friend joining or coming back, so a cold host can be revived beside one. not saved: they burn
	// out in seconds. registered once the girls are there (CoopPlayers)
	public class State : IStateful
	{
		public const string SectionKey = "campfires";

		public string Key => SectionKey;

		public int Version => 1;

		public void Write(StateWriter stateWriter)
		{
			List<CoopCampfire> lit = new List<CoopCampfire>();
			foreach (CoopCampfire campfire in placed.Values)
			{
				if (campfire != null && Fuel(campfire) > 0f)
				{
					lit.Add(campfire);
				}
			}
			stateWriter.Put(lit.Count);
			foreach (CoopCampfire campfire in lit)
			{
				stateWriter.Put(campfire.Id);
				stateWriter.Put(CoopPlayers.Instance.ToLayout(campfire.transform.position));
				stateWriter.Put(campfire.transform.eulerAngles.y);
				stateWriter.Put(Fuel(campfire));
			}
		}

		public void Read(StateReader reader, int version)
		{
			int count = reader.GetInt();
			for (int i = 0; i < count; i++)
			{
				int id = reader.GetInt();
				Vector3 layout = reader.GetVector3();
				float y = reader.GetFloat();
				float fuel = reader.GetFloat();
				// ours continue from past any of the host's
				nextId = Mathf.Max(nextId, id / 2 + 1);
				if (!placed.TryGetValue(id, out CoopCampfire campfire) || campfire == null)
				{
					GameObject spawned = CoopPlayers.Instance.Local.GetComponentInChildren<CharacterItemInteraction>().SpawnCampfire(CoopPlayers.Instance.FromLayout(layout), Quaternion.Euler(0f, y, 0f));
					Track(spawned, id);
					campfire = placed[id];
				}
				campfire.StartCoroutine(SetFuel(campfire, fuel));
			}
		}

		private static float Fuel(CoopCampfire campfire)
		{
			CampfireFuelLevel level = campfire.GetComponentInChildren<CampfireFuelLevel>();
			return (level != null) ? level.currentFuelLevel : 0f;
		}

		// after its Start, which fills it
		private static System.Collections.IEnumerator SetFuel(CoopCampfire campfire, float fuel)
		{
			yield return null;
			foreach (CampfireFuelLevel level in campfire.GetComponentsInChildren<CampfireFuelLevel>())
			{
				level.currentFuelLevel = fuel;
			}
		}
	}

	public static void Placed(GameObject campfire)
	{
		if (!CoopSession.InGame || CoopPlayers.Instance == null)
		{
			return;
		}
		// host ids even, friend odd
		int id = nextId++ * 2 + ((!CoopSession.Instance.IsHost) ? 1 : 0);
		Track(campfire, id);
		Vector3 vector = CoopPlayers.Instance.ToLayout(campfire.transform.position);
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.CampfirePlaced);
		writer.Put(id);
		writer.Put(vector.x);
		writer.Put(vector.y);
		writer.Put(vector.z);
		writer.Put(campfire.transform.eulerAngles.y);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	public static void Extinguished(CampfireFuelLevel campfire)
	{
		CoopCampfire coopCampfire = campfire.GetComponentInParent<CoopCampfire>();
		if (coopCampfire == null)
		{
			return;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.CampfireOut);
		writer.Put(coopCampfire.Id);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	public static void OnPlaced(NetDataReader reader)
	{
		int id = reader.GetInt();
		Vector3 layout = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
		float y = reader.GetFloat();
		if (CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
		{
			return;
		}
		GameObject campfire = CoopPlayers.Instance.Local.GetComponentInChildren<CharacterItemInteraction>().SpawnCampfire(CoopPlayers.Instance.FromLayout(layout), Quaternion.Euler(0f, y, 0f));
		Track(campfire, id);
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug campfire " + id + " placed by the other player at " + layout.ToString("F1"));
#endif
	}

	public static void OnOut(NetDataReader reader)
	{
		int @int = reader.GetInt();
		if (placed.TryGetValue(@int, out var value) && value != null)
		{
			CampfireFuelLevel[] componentsInChildren = value.GetComponentsInChildren<CampfireFuelLevel>();
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				componentsInChildren[i].currentFuelLevel = 0f;
			}
#if DUMP_WORLDGEN
			Debug.Log("CoopDebug campfire " + @int + " put out by the other player");
#endif
		}
	}

	private static void Track(GameObject campfire, int id)
	{
		CoopCampfire coopCampfire = campfire.AddComponent<CoopCampfire>();
		coopCampfire.Id = id;
		placed[id] = coopCampfire;
	}

	private void OnDestroy()
	{
		if (placed.TryGetValue(Id, out var value) && value == this)
		{
			placed.Remove(Id);
		}
	}
}
