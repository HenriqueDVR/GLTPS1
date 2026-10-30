// the layer table. 3 layers for now (episodes 1-16), layer 2 is the last one.
// band distances are first guesses, tuned for pacing in WP-SURV-4.
// roads: the lower deck is the oldest and most fought over, so its streets are sparse and its overpasses broken
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
			ElevatedChance = 0.3,
			BrokenChance = 0.4,
			Districts = new[]
			{
				new DistrictDef { Id = "fields", Name = "Fields", From = 0.0, Roads = DistrictRoads.Open },
				new DistrictDef { Id = "war", Name = "War ruins", From = 1200.0, Roads = DistrictRoads.Streets, StreetKeep = 0.4 },
				new DistrictDef { Id = "pipes", Name = "Pipes", From = 2600.0, Roads = DistrictRoads.Streets, StreetKeep = 0.5 },
				new DistrictDef { Id = "laundry", Name = "Laundry", From = 3800.0, Roads = DistrictRoads.Streets, StreetKeep = 0.6 },
				new DistrictDef { Id = "kanazawa", Name = "Kanazawa", From = 5000.0, Roads = DistrictRoads.Streets, StreetKeep = 0.65 }
			}
		},
		new LayerDef
		{
			Index = 1,
			Name = "City",
			ExitMinDistance = 5000.0,
			UpChance = 0.4,
			ElevatedChance = 0.5,
			BrokenChance = 0.1,
			Districts = new[]
			{
				new DistrictDef { Id = "city", Name = "City", From = 0.0, Roads = DistrictRoads.Streets, StreetKeep = 0.9 },
				new DistrictDef { Id = "streetlights", Name = "Streetlights", From = 1200.0, Roads = DistrictRoads.Streets, StreetKeep = 0.9 },
				new DistrictDef { Id = "photo", Name = "Photo", From = 2400.0, Roads = DistrictRoads.Streets, StreetKeep = 0.85 },
				new DistrictDef { Id = "temple", Name = "Temple", From = 3600.0, Roads = DistrictRoads.Streets, StreetKeep = 0.6 },
				new DistrictDef { Id = "dwelling", Name = "Dwelling", From = 4800.0, Roads = DistrictRoads.Streets, StreetKeep = 0.85 }
			}
		},
		new LayerDef
		{
			Index = 2,
			Name = "Upper",
			ExitMinDistance = 5000.0,
			UpChance = 0.0,
			IsLast = true,
			ElevatedChance = 0.3,
			BrokenChance = 0.2,
			Districts = new[]
			{
				new DistrictDef { Id = "upper", Name = "Upper", From = 0.0, Roads = DistrictRoads.Open },
				new DistrictDef { Id = "rain", Name = "Rain", From = 1200.0, Roads = DistrictRoads.Streets, StreetKeep = 0.5 },
				new DistrictDef { Id = "breakdown", Name = "Breakdown", From = 2400.0, Roads = DistrictRoads.Open },
				new DistrictDef { Id = "technology", Name = "Technology", From = 3600.0, Roads = DistrictRoads.Streets, StreetKeep = 0.5 },
				new DistrictDef { Id = "takeoff", Name = "Takeoff", From = 4800.0, Roads = DistrictRoads.Open }
			}
		}
	};

	public static int Count => All.Length;

	public static LayerDef Get(int layer)
	{
		return All[layer];
	}
}
