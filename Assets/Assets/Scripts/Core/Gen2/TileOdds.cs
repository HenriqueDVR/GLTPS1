// the odds of what a tile places on top of its plan. changing one changes tiles, so they're in the co-op hash:
// tune them before a v2 release, a genVersion bump after
public static class TileOdds
{
	// share of lots with an item spawner, about one per city tile like the legacy nodes (tuned with SURV-4)
	public const double ItemOnLandmark = 0.5;

	public const double ItemOnOpen = 0.1;

	public const double ItemOnBuilding = 0.03;

	// in the entry of a building entered from a raised street
	public const double ItemInEntry = 0.3;

	// debris on a tile: open districts FewestOpen and up to MoreOpen - 1 more, others up to MoreTown - 1
	public const int FewestOpen = 2;

	public const int MoreOpen = 6;

	public const int MoreTown = 3;

	// which debris in open districts, as running shares: slab, pile, tree, frame, the rest a pole
	public const double DebrisSlab = 0.25;

	public const double DebrisPile = 0.5;

	public const double DebrisTree = 0.75;

	public const double DebrisFrame = 0.93;

	// share of open lots with a harvested prop, not rubble
	public const double HarvestedProp = 0.3;

	// share of buildings that try a harvested piece first
	public const double HarvestedRuin = 0.25;

	public const double HarvestedIndustrial = 0.0;

	public const double HarvestedOther = 0.35;

	public static double Harvested(BuildingStyle style)
	{
		return (style == BuildingStyle.Ruin) ? HarvestedRuin : (style == BuildingStyle.Industrial) ? HarvestedIndustrial : HarvestedOther;
	}

	public static ulong Hash()
	{
		ulong hash = 14695981039346656037uL;
		foreach (double odds in new[] { ItemOnLandmark, ItemOnOpen, ItemOnBuilding, ItemInEntry, FewestOpen, MoreOpen, MoreTown, DebrisSlab, DebrisPile, DebrisTree, DebrisFrame, HarvestedProp, HarvestedRuin, HarvestedIndustrial, HarvestedOther })
		{
			hash = LayerPlan.Mix(hash, LayerPlan.Centimetres(odds * 100.0));
		}
		return hash;
	}
}
