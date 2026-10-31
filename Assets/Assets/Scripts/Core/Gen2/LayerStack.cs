using System.Collections.Generic;

// the layers reached so far this game, each one's plan made the first time it's reached: its origin is the
// pillar first arrived through (01, concepts). layer 0's plan comes from the start
public sealed class LayerStack
{
	private readonly List<LayerPlan> plans = new List<LayerPlan>();

	public LayerStack(LayerPlan first)
	{
		plans.Add(first);
	}

	// how many layers have a plan, 0 up to Count - 1
	public int Count => plans.Count;

	public LayerPlan Get(int layer)
	{
		return (layer >= 0 && layer < plans.Count) ? plans[layer] : null;
	}

	// which way a pillar's elevator goes from this layer: 1 up, -1 down, 0 none. up wins where it does both
	public static int Way(PillarState state)
	{
		if ((state & PillarState.Up) != 0)
		{
			return 1;
		}
		return ((state & PillarState.Down) != 0) ? -1 : 0;
	}

	// the layer riding this pillar's elevator from `from` leads to, null if it doesn't go that way
	public LayerPlan Ride(int from, int cellX, int cellZ, int way)
	{
		LayerPlan plan = Get(from);
		if (plan == null)
		{
			return null;
		}
		PillarInfo pillar = plan.Pillar(cellX, cellZ);
		if (way > 0)
		{
			if ((pillar.State & PillarState.Up) == 0)
			{
				return null;
			}
			if (from + 1 < plans.Count)
			{
				return plans[from + 1];
			}
			LayerPlan next = new LayerPlan(plan.WorldSeed, from + 1, pillar.Position, plan);
			plans.Add(next);
			return next;
		}
		if (way < 0 && (pillar.State & PillarState.Down) != 0)
		{
			return plans[from - 1];
		}
		return null;
	}

	// the next layer up as it was first reached, from its origin: a co-op friend's world from the host's (Start)
	public LayerPlan Add(XZ origin)
	{
		LayerPlan top = plans[plans.Count - 1];
		LayerPlan next = new LayerPlan(top.WorldSeed, top.Layer + 1, origin, top);
		plans.Add(next);
		return next;
	}

	// layer k's deck over layer 0's
	public static double Rise(int layer)
	{
		return layer * Gen2.LayerHeight;
	}
}
