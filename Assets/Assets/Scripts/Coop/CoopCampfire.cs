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
