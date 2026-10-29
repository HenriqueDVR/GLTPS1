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
}

public sealed class DistrictDef
{
	// stable id, saves and the director refer to districts by it
	public string Id;

	public string Name;

	// warped progress where the band starts, metres
	public double From;
}
