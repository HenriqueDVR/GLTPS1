using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// item ids come from the seeded spawner (or name+pos for scene items) so they match.
// host decides: host takes right away, friend asks first
public class CoopItem : MonoBehaviour
{
	private enum Kind : byte
	{
		Food,
		Ammo
	}

	private static readonly Dictionary<ulong, CoopItem> spawned = new Dictionary<ulong, CoopItem>();

	private static readonly HashSet<ulong> taken = new HashSet<ulong>();

	private static readonly Dictionary<ulong, Kind> requested = new Dictionary<ulong, Kind>();

	private static readonly NetDataWriter writer = new NetDataWriter();

	public ulong Id { get; private set; }

	// keep what a loaded save already took
	public static void ResetLevel()
	{
		taken.Clear();
		requested.Clear();
		if (GameSave.RestoreTaken != null)
		{
			taken.UnionWith(GameSave.RestoreTaken);
		}
	}

	public static IEnumerable<ulong> TakenIds => taken;

	public static bool IsTaken(ulong id)
	{
		return taken.Contains(id);
	}

	public static void Track(GameObject item, ulong id)
	{
		item.AddComponent<CoopItem>().Id = id;
	}

	public static void TrackScenePickups()
	{
		string[] array = new string[2] { "Food", "Ammo" };
		foreach (string tag in array)
		{
			GameObject[] array2 = GameObject.FindGameObjectsWithTag(tag);
			foreach (GameObject gameObject in array2)
			{
				if (!(gameObject.GetComponentInParent<CoopItem>() != null) && !(gameObject.GetComponentInParent<ItemSpawnManager>() != null))
				{
					Vector3 position = gameObject.transform.position;
					// bit 62 so these don't clash with spawner ids
					ulong id = 0x4000000000000000uL | WorldLayout.Hash(gameObject.name + "@" + Mathf.RoundToInt(position.x * 100f) + "," + Mathf.RoundToInt(position.y * 100f) + "," + Mathf.RoundToInt(position.z * 100f));
					if (IsTaken(id))
					{
						Object.Destroy(gameObject);
					}
					else
					{
						Track(gameObject, id);
					}
				}
			}
		}
	}

	// single player too, for saves
	public static CoopItem Find(Collider collider)
	{
		return collider.GetComponentInParent<CoopItem>();
	}

	private void Start()
	{
		spawned[Id] = this;
	}

	private void OnDestroy()
	{
		if (spawned.TryGetValue(Id, out var value) && value == this)
		{
			spawned.Remove(Id);
		}
	}

	public void TakeAsHost()
	{
		taken.Add(Id);
		SendTaken(Id, toFriend: false);
	}

	public bool RequestAsFriend()
	{
		if (taken.Contains(Id) || requested.ContainsKey(Id))
		{
			return false;
		}
		requested[Id] = (base.gameObject.CompareTag("Ammo") ? Kind.Ammo : Kind.Food);
		base.gameObject.SetActive(false);
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.TakeItem);
		writer.Put(Id);
		writer.Put(requested[Id] == Kind.Food);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
		return true;
	}

	// food goes in the shared bar, ammo to the friend's gun
	public static void OnTakeRequest(ulong id, bool isFood)
	{
		bool flag = taken.Add(id);
		if (flag && isFood && CoopPlayers.Instance != null && CoopPlayers.Instance.Local != null)
		{
			CharacterItemInteraction componentInChildren = CoopPlayers.Instance.Local.GetComponentInChildren<CharacterItemInteraction>();
			componentInChildren.AddFood(componentInChildren.RollFood());
		}
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug friend asked for " + id + " (" + (spawned.TryGetValue(id, out var item) && item != null ? item.tag : "not spawned here") + ")" + (flag ? ", granted" : ", already gone"));
#endif
		Remove(id);
		SendTaken(id, flag);
	}

	public static void OnTaken(ulong id, bool toFriend)
	{
		taken.Add(id);
		Remove(id);
		if (requested.TryGetValue(id, out var value))
		{
			requested.Remove(id);
#if DUMP_WORLDGEN
			Debug.Log("CoopDebug host answered " + id + (toFriend ? ": got it" : ": host had it"));
#endif
			if (toFriend)
			{
				GiveToLocalGirl(value);
			}
		}
	}

	public static void OnTakenList(NetDataReader reader)
	{
		int @int = reader.GetInt();
		for (int i = 0; i < @int; i++)
		{
			ulong @ulong = reader.GetULong();
			taken.Add(@ulong);
			Remove(@ulong);
		}
	}

	public static void SendTakenList()
	{
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.TakenItems);
		writer.Put(taken.Count);
		foreach (ulong item in taken)
		{
			writer.Put(item);
		}
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	private static void SendTaken(ulong id, bool toFriend)
	{
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.ItemTaken);
		writer.Put(id);
		writer.Put(toFriend);
		if (CoopSession.Active)
		{
			CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
		}
	}

#if DUMP_WORLDGEN
	public static void DebugTakeNearest(Transform girl)
	{
		CoopItem coopItem = null;
		float num = float.MaxValue;
		ulong num2 = 0uL;
		foreach (CoopItem value in spawned.Values)
		{
			if (value != null && value.gameObject.activeInHierarchy)
			{
				num2 ^= value.Id * 0x9E3779B97F4A7C15uL;
				float num3 = Vector3.Distance(girl.position, value.transform.position);
				if (num3 < num)
				{
					num = num3;
					coopItem = value;
				}
			}
		}
		Debug.Log("CoopDebug items spawned " + spawned.Count + " taken " + taken.Count + " set " + num2.ToString("x16"));
		// also ask for one already taken, to test the refusal
		if (!CoopSession.Instance.IsHost && taken.Count > 0)
		{
			writer.Reset();
			writer.Put((byte)CoopSession.MessageType.TakeItem);
			writer.Put(System.Linq.Enumerable.First(taken));
			writer.Put(true);
			CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
		}
		if (coopItem != null)
		{
			Collider collider = coopItem.GetComponentInChildren<Collider>();
			Debug.Log("CoopDebug taking " + coopItem.Id + " (" + coopItem.tag + ") " + num.ToString("F0") + "m away");
			girl.GetComponentInChildren<CharacterItemInteraction>().DebugTake(collider);
		}
	}
#endif

	private static void Remove(ulong id)
	{
		if (spawned.TryGetValue(id, out var value))
		{
			spawned.Remove(id);
			if (value != null)
			{
				Object.Destroy(value.gameObject);
			}
		}
	}

	private static void GiveToLocalGirl(Kind kind)
	{
		if (CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
		{
			return;
		}
		CharacterItemInteraction componentInChildren = CoopPlayers.Instance.Local.GetComponentInChildren<CharacterItemInteraction>();
		if (kind == Kind.Food)
		{
			// food comes with the host's food bar
			componentInChildren.PlayPickupSound();
		}
		else
		{
			componentInChildren.GiveAmmo();
		}
	}
}
