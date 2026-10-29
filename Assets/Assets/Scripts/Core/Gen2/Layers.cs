// the layer table. 3 layers for now (episodes 1-16), layer 2 is the last one.
// band distances are first guesses, tuned for pacing in WP-SURV-4
public static class Layers
{
	public static readonly LayerDef[] All =
	{
		new LayerDef
		{
			Index = 0,
			Name = "Lower deck",
			ExitMinDistance = 5000.0,
			UpChance = 0.4,
			Districts = new[]
			{
				new DistrictDef { Id = "fields", Name = "Fields", From = 0.0 },
				new DistrictDef { Id = "war", Name = "War ruins", From = 1200.0 },
				new DistrictDef { Id = "pipes", Name = "Pipes", From = 2600.0 },
				new DistrictDef { Id = "laundry", Name = "Laundry", From = 3800.0 },
				new DistrictDef { Id = "kanazawa", Name = "Kanazawa", From = 5000.0 }
			}
		},
		new LayerDef
		{
			Index = 1,
			Name = "City",
			ExitMinDistance = 5000.0,
			UpChance = 0.4,
			Districts = new[]
			{
				new DistrictDef { Id = "city", Name = "City", From = 0.0 },
				new DistrictDef { Id = "streetlights", Name = "Streetlights", From = 1200.0 },
				new DistrictDef { Id = "photo", Name = "Photo", From = 2400.0 },
				new DistrictDef { Id = "temple", Name = "Temple", From = 3600.0 },
				new DistrictDef { Id = "dwelling", Name = "Dwelling", From = 4800.0 }
			}
		},
		new LayerDef
		{
			Index = 2,
			Name = "Upper",
			ExitMinDistance = 5000.0,
			UpChance = 0.0,
			IsLast = true,
			Districts = new[]
			{
				new DistrictDef { Id = "upper", Name = "Upper", From = 0.0 },
				new DistrictDef { Id = "rain", Name = "Rain", From = 1200.0 },
				new DistrictDef { Id = "breakdown", Name = "Breakdown", From = 2400.0 },
				new DistrictDef { Id = "technology", Name = "Technology", From = 3600.0 },
				new DistrictDef { Id = "takeoff", Name = "Takeoff", From = 4800.0 }
			}
		}
	};

	public static int Count => All.Length;

	public static LayerDef Get(int layer)
	{
		return All[layer];
	}
}
