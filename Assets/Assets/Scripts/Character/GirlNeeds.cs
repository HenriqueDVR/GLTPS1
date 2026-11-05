using System.Collections.Generic;
using UnityEngine;

public enum Need
{
	Food,
	Water,
	Warmth
}

// what a girl's run out of and what getting her up costs, for solo (CharacterDeath) and co-op (CoopDowned) alike. numbers
// in NeedsRules
public static class GirlNeeds
{
	public const float ReviveReach = 2.5f;

	public const float ReviveDuration = 3f;

	private const float waterCost = 20f;

	private const float fireReach = 8f;

	public static bool Out(CharacterItemInteraction girl)
	{
		return NeedsRules.Out(girl.hungerLevel, girl.thirstLevel, girl.temperatureLevel);
	}

	// been out of something long enough. the cold doesn't take a girl off the ket, hunger and thirst get her off it
	public static bool DueDown(CharacterItemInteraction girl)
	{
		if (!NeedsRules.DueDown(girl.OutFor))
		{
			return false;
		}
		return girl.hungerLevel <= 0f || girl.thirstLevel <= 0f || !girl.anim.GetCurrentAnimatorStateInfo(1).IsTag("Ket");
	}

	public static List<Need> Missing(CharacterItemInteraction girl)
	{
		List<Need> needs = new List<Need>();
		if (girl.hungerLevel <= 0f)
		{
			needs.Add(Need.Food);
		}
		if (girl.thirstLevel <= 0f)
		{
			needs.Add(Need.Water);
		}
		if (girl.temperatureLevel <= 0f)
		{
			needs.Add(Need.Warmth);
		}
		return needs;
	}

	public static bool Has(Need need, CharacterStatus status, Vector3 body)
	{
		switch (need)
		{
		case Need.Food:
			return status.index > 0;
		case Need.Water:
			return status.waterLevel >= waterCost;
		default:
			foreach (CampfireFuelLevel fire in Object.FindObjectsOfType<CampfireFuelLevel>())
			{
				if (fire.currentFuelLevel > 0f && Vector3.Distance(fire.transform.position, body) < fireReach)
				{
					return true;
				}
			}
			return false;
		}
	}

	public static bool HasAll(CharacterItemInteraction girl, CharacterStatus status)
	{
		foreach (Need need in Missing(girl))
		{
			if (!Has(need, status, girl.transform.position))
			{
				return false;
			}
		}
		return true;
	}

	// what the one getting her up uses. a fire isn't used up
	public static void Pay(CharacterItemInteraction girl, CharacterStatus status, CharacterItemInteraction payer)
	{
		foreach (Need need in Missing(girl))
		{
			if (need == Need.Food)
			{
				payer.RemoveFood(1);
			}
			else if (need == Need.Water)
			{
				status.waterLevel -= waterCost;
			}
		}
	}

	public static void Raise(CharacterItemInteraction girl, float level)
	{
		girl.hungerLevel = Mathf.Max(girl.hungerLevel, level);
		girl.thirstLevel = Mathf.Max(girl.thirstLevel, level);
		girl.temperatureLevel = Mathf.Max(girl.temperatureLevel, level);
		girl.OutFor = 0f;
	}

	// a fall into a void
	public static void Fell(CharacterItemInteraction girl)
	{
		girl.hungerLevel = NeedsRules.AfterFall(girl.hungerLevel);
		girl.thirstLevel = NeedsRules.AfterFall(girl.thirstLevel);
		girl.temperatureLevel = NeedsRules.AfterFall(girl.temperatureLevel);
	}

	// as she runs out, in her own words
	public static string Remark(Girl girl)
	{
		CharacterItemInteraction items = girl.Items;
		bool chito = girl.Id == GirlId.Chito;
		if (items.temperatureLevel <= 0f)
		{
			return chito ? "Yuu... my hands won't close." : "I've stopped shivering, Chi-chan. Is that good?";
		}
		if (items.thirstLevel <= 0f)
		{
			return chito ? "I need water. Soon." : "My tongue's gone big.";
		}
		return chito ? "I can't walk much further on nothing." : "Chi-chan. I can't feel my stomach any more.";
	}

	public static string NameOf(Girl girl)
	{
		return (girl.Id == GirlId.Chito) ? Speaker.Chito : Speaker.Yuuri;
	}
}
