// one layer of the world (01-world-layers). plain data, the table is in Layers
public sealed class LayerDef
{
	public int Index;

	public string Name;

	// distance bands, sorted by From. the last one repeats forever
	public DistrictDef[] Districts;

	// working Up pillars only this far (warped progress) from the origin
	public double ExitMinDistance;

	// share of the pillars past ExitMinDistance that get an Up elevator
	public double UpChance;

	// last layer: no Up pillars, no ceiling, the ending plays at its top
	public bool IsLast;

	// share of highway links with an elevated stretch
	public double ElevatedChance;

	// share of elevated stretches with a collapsed span in the middle
	public double BrokenChance;

	// share of buildings with a broken, stepped top
	public double Ruin;
}

public enum DistrictRoads
{
	// buried roads between sites, no streets
	Open,
	// a street grid of its own
	Streets
}

// how a district's buildings look (13-content-pipeline, look of the source)
public enum BuildingStyle
{
	// concrete masses with raised panels, few small windows, setbacks, cantilevered boxes
	Monolith,
	// tall office slabs with window bands, lined up along the streets
	Slab,
	// broken monoliths and bare skeleton frames
	Ruin,
	// cylinder tanks with pipes that run somewhere
	Industrial
}

public sealed class DistrictDef
{
	// stable id, saves and the director refer to districts by it
	public string Id;

	public string Name;

	// warped progress where the band starts, metres
	public double From;

	public DistrictRoads Roads;

	// share of the grid's street segments that are still there
	public double StreetKeep;

	// lots (LotPlan): in street districts the shares of building, open and landmark lots (normalised);
	// in open districts the share of sites with a landmark or an open lot, the rest stay empty snow
	public double LotBuilding;

	public double LotOpen;

	public double LotLandmark;

	public int FloorsMin = 1;

	public int FloorsMax = 1;

	// share of street pieces with a lamp on the sidewalk
	public double Lamps;

	public BuildingStyle Style;
}
