using LiteNetLib.Utils;
using UnityEngine;

// food and ammo lying around, an Interactable with a WorldFlags id. ids come from the seeded spawner (or name+pos for
// scene items) so they match. host decides: alone or hosting it's taken right away, a friend asks first
public class CoopItem : MonoBehaviour
{
	// about what the old 1 m trigger reached, with the girl's own width
	private const float reach = 1.2f;

	public ulong Id { get; private set; }

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	public static void Init()
	{
		NetRegistry.RegisterDecider(NetRegistry.Kind.Item, OnAsked);
	}

	public static void ResetLevel()
	{
		WorldFlags.ResetLevel();
	}

	public static bool IsTaken(ulong id)
	{
		return WorldFlags.Has(id);
	}

	// id 0: a loose one nobody else knows about, each player has their own and saves don't remember it
	public static void Track(GameObject item, ulong id)
	{
		item.AddComponent<CoopItem>().Id = id;
		bool food = !item.CompareTag("Ammo");
		Interactable thing = item.AddComponent<Interactable>();
		thing.Verb = Verb.Take;
		thing.Range = reach;
		thing.Label = "take";
		if (food)
		{
			thing.Can = girl => girl == null || girl.Items.FoodHasRoom;
		}
		if (id == 0)
		{
			thing.Run = delegate(Girl girl)
			{
				if (food)
				{
					AddToBar(girl);
				}
				Give(girl, food);
				Destroy(item);
			};
			return;
		}
		thing.Authority = Authority.Host;
		thing.Kind = NetRegistry.Kind.Item;
		thing.AskBody = writer => writer.Put(food);
		thing.HideWhileAsking = true;
		thing.Run = girl => Give(girl, food);
		thing.UseFlag(id);
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
					ulong id = 0x4000000000000000uL | WorldRandom.HashName(gameObject.name + "@" + Mathf.RoundToInt(position.x * 100f) + "," + Mathf.RoundToInt(position.y * 100f) + "," + Mathf.RoundToInt(position.z * 100f));
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

	// host or alone: who gets it. food goes in the shared bar here, which the friend's follows
	private static bool OnAsked(ulong id, NetDataReader reader)
	{
		bool food = reader.GetBool();
		bool first = WorldFlags.Set(id);
		if (first && food)
		{
			AddToBar(Girls.Local);
		}
		return first;
	}

	private static void AddToBar(Girl girl)
	{
		if (girl != null)
		{
			girl.Items.AddFood(girl.Items.RollFood());
		}
	}

	// to whoever picked it up: ammo in the gun, food's already in the bar
	private static void Give(Girl girl, bool food)
	{
		if (girl == null)
		{
			return;
		}
		if (food)
		{
			girl.Items.PlayPickupSound();
		}
		else
		{
			girl.Items.GiveAmmo();
		}
	}

#if DUMP_WORLDGEN
	public static void DebugTakeNearest(Transform girl)
	{
		Interactable nearest = null;
		float num = float.MaxValue;
		ulong num2 = 0uL;
		int count = 0;
		foreach (Interactable thing in Interactable.DebugLive)
		{
			CoopItem item = thing.GetComponent<CoopItem>();
			if (item != null)
			{
				count++;
				num2 ^= item.Id * 0x9E3779B97F4A7C15uL;
				float num3 = Vector3.Distance(girl.position, thing.transform.position);
				if (num3 < num)
				{
					num = num3;
					nearest = thing;
				}
			}
		}
		Debug.Log("CoopDebug items spawned " + count + " taken " + WorldFlags.Count + " set " + num2.ToString("x16"));
		// also ask for one already taken, to test the refusal
		if (!CoopSession.Instance.IsHost && WorldFlags.Count > 0)
		{
			ulong gone = System.Linq.Enumerable.First(WorldFlags.All);
			NetRegistry.Ask(NetRegistry.Kind.Item, gone, writer => writer.Put(true), granted => Debug.Log("CoopDebug host answered " + gone + (granted ? ": got it" : ": host had it")));
		}
		if (nearest != null)
		{
			Debug.Log("CoopDebug taking " + nearest.NetId + " (" + nearest.tag + ") " + num.ToString("F0") + "m away");
			nearest.DebugFire(girl.GetComponent<Girl>());
		}
	}
#endif
}
