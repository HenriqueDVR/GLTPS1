using System;
using UnityEngine;

// the two girls, set up per gameplay scene by GameSession. ask here instead of Find("ChitoParent")
public static class Girls
{
	public static Girl Chito { get; private set; }

	public static Girl Yuuri { get; private set; }

	// the one this machine's player controls
	public static Girl Local { get; private set; }

	public static event Action<Girl> LocalChanged;

	// both, chito first. empty outside the gameplay scene
	public static Girl[] All { get; private set; } = new Girl[0];

	public static Girl Get(GirlId id)
	{
		return (id == GirlId.Chito) ? Chito : Yuuri;
	}

	public static Girl Other(Girl girl)
	{
		return (girl == Chito) ? Yuuri : Chito;
	}

	// the scene starts with chito played and yuuri on the AI
	public static void Begin()
	{
		Chito = Add("ChitoParent", GirlId.Chito, GirlControl.Player);
		Yuuri = Add("YuuriParent", GirlId.Yuuri, GirlControl.AI);
		Local = Chito;
		All = (Chito != null && Yuuri != null) ? new Girl[2] { Chito, Yuuri } : new Girl[0];
	}

	public static void End()
	{
		Chito = null;
		Yuuri = null;
		Local = null;
		All = new Girl[0];
	}

	private static Girl Add(string name, GirlId id, GirlControl control)
	{
		GameObject root = GameObject.Find(name);
		if (root == null)
		{
			return null;
		}
		Girl girl = root.GetComponent<Girl>();
		if (girl == null)
		{
			girl = root.AddComponent<Girl>();
		}
		girl.Init(id, control);
		return girl;
	}

	// solo: the player takes the other girl. the one left goes to the AI, or stays down if she died
	public static void SwitchTo(Girl girl, bool leftIsDown = false)
	{
		Girl left = Local;
		if (girl == null || girl == left)
		{
			return;
		}
		left.SetControl(leftIsDown ? GirlControl.Down : GirlControl.AI);
		if (!leftIsDown)
		{
			// the played girl's footsteps are the loud ones
			float volume = left.Footsteps.volume;
			left.Footsteps.volume = girl.Footsteps.volume;
			girl.Footsteps.volume = volume;
		}
		girl.SetControl(GirlControl.Player);
		SetLocal(girl);
	}

	// co-op: we play our girl, the other one is the other player's
	public static void SetupCoop(Girl local)
	{
		if (local != Local)
		{
			SwitchTo(local);
		}
		Other(local).SetControl(GirlControl.Remote);
		// no switching to the other player's girl
		local.Possession.enabled = false;
	}

	private static void SetLocal(Girl girl)
	{
		Local = girl;
		LocalChanged?.Invoke(girl);
	}
}
