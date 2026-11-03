using System;
using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

// who decides whether a verb happens
public enum Authority
{
	// the girl who pressed, on the spot
	Self,
	// the host: alone or hosting that's on the spot too, a friend asks and waits
	Host
}

// a thing in the world that offers a verb itself, nothing on the girl needs to know about it. listed while enabled,
// so a tile going back in the pool takes its things out and coming back puts them in, checked against the flags again.
// add it in code (rule 7), then set the fields
public class Interactable : MonoBehaviour
{
	private static readonly List<Interactable> live = new List<Interactable>();

	private static readonly Dictionary<ulong, Interactable> byFlag = new Dictionary<ulong, Interactable>();

	public Verb Verb = Verb.Use;

	public Press Press = Press.Tap;

	public float HoldTime = 0.4f;

	// from the girl's feet, flat. height gets a bit more: a thing on a shelf
	public float Range = 1.5f;

	public int Priority = 1;

	// what the prompt says, null for none
	public string Label;

	public float PromptHeight = 0.6f;

	// null: always. the girl's null in tests
	public Func<Girl, bool> Can;

	public Authority Authority;

	// Authority.Host: who decides, and what they're told. Flag's own decider just claims NetId
	public NetRegistry.Kind Kind = NetRegistry.Kind.Flag;

	public ulong NetId;

	public Action<NetDataWriter> AskBody;

	// gone from the asker's view until the answer comes
	public bool HideWhileAsking;

	// on the side that pressed, once it's allowed
	public Action<Girl> Run;

	// its flag got set: a thing that stays (an opened container) changes itself here. null: it's destroyed
	public Action Gone;

	private readonly Interaction offer = new Interaction();

	private Action fire;

	private Girl presser;

	private bool flagged;

	private bool listed;

	private bool asking;

	private bool gone;

	public static bool InRange(Vector3 from, Vector3 to, float range)
	{
		Vector3 flat = to - from;
		float height = Mathf.Abs(flat.y);
		flat.y = 0f;
		return flat.sqrMagnitude <= range * range && height <= range + 1f;
	}

	// what's in reach of a girl at that spot right now. nothing allocated, this runs every frame
	public static void Collect(Girl girl, Vector3 at, List<Interaction> into)
	{
		foreach (Interactable thing in live)
		{
			if (!thing.asking && !thing.gone && InRange(at, thing.transform.position, thing.Range) && (thing.Can == null || thing.Can(girl)))
			{
				into.Add(thing.Offer(girl));
			}
		}
	}

	public static void OnFlagSet(ulong id)
	{
		if (byFlag.TryGetValue(id, out Interactable thing) && thing != null)
		{
			thing.Finish();
		}
	}

	// once it's done for good it's gone for both, saved. something already set finishes it now
	public void UseFlag(ulong id)
	{
		NetId = id;
		flagged = true;
		if (WorldFlags.Has(id))
		{
			Finish();
		}
		else if (listed)
		{
			byFlag[id] = this;
		}
	}

	private void OnEnable()
	{
		List();
	}

	private void OnDisable()
	{
		Unlist();
	}

	public void List()
	{
		if (listed)
		{
			return;
		}
		listed = true;
		live.Add(this);
		if (flagged)
		{
			// set while it was in the pool?
			if (WorldFlags.Has(NetId))
			{
				Finish();
			}
			else
			{
				byFlag[NetId] = this;
			}
		}
	}

	public void Unlist()
	{
		if (!listed)
		{
			return;
		}
		listed = false;
		live.Remove(this);
		if (flagged && byFlag.TryGetValue(NetId, out Interactable thing) && thing == this)
		{
			byFlag.Remove(NetId);
		}
	}

	private Interaction Offer(Girl girl)
	{
		presser = girl;
		if (fire == null)
		{
			fire = Fire;
		}
		offer.Verb = Verb;
		offer.Press = Press;
		offer.HoldTime = HoldTime;
		offer.Target = transform;
		offer.Priority = Priority;
		offer.Label = Label;
		offer.PromptHeight = PromptHeight;
		offer.Run = fire;
		offer.Source = Label;
		return offer;
	}

	private void Fire()
	{
		Girl girl = presser;
		InteractPrompt.Used(Label);
		if (Authority == Authority.Self)
		{
			Run?.Invoke(girl);
			return;
		}
		asking = true;
		if (HideWhileAsking)
		{
			gameObject.SetActive(false);
		}
		if (!NetRegistry.Ask(Kind, NetId, AskBody, granted => Answered(girl, granted)))
		{
			// the host wouldn't hear: it stays lying there
			Answered(girl, false);
		}
	}

	private void Answered(Girl girl, bool granted)
	{
		if (this == null)
		{
			return;
		}
		asking = false;
		if (granted)
		{
			Run?.Invoke(girl);
		}
		else if (flagged && WorldFlags.Has(NetId))
		{
			Finish();
		}
		if (HideWhileAsking && !gone && !gameObject.activeSelf)
		{
			gameObject.SetActive(true);
		}
	}

	private void Finish()
	{
		if (gone)
		{
			return;
		}
		gone = true;
		if (Gone != null)
		{
			Gone();
		}
		else
		{
			Destroy(gameObject);
		}
	}

}
