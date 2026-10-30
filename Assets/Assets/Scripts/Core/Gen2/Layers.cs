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
			Ruin = 0.6,
			Districts = new[]
			{
				new DistrictDef { Id = "fields", Name = "Fields", From = 0.0, Roads = DistrictRoads.Open, LotLandmark = 0.35, LotOpen = 0.3, Cables = 0.3 },
				new DistrictDef { Id = "war", Name = "War ruins", From = 1200.0, Roads = DistrictRoads.Streets, StreetKeep = 0.4, LotBuilding = 0.45, LotOpen = 0.45, LotLandmark = 0.1, FloorsMin = 1, FloorsMax = 3, Style = BuildingStyle.Ruin, Cables = 0.2 },
				new DistrictDef { Id = "pipes", Name = "Pipes", From = 2600.0, Roads = DistrictRoads.Streets, StreetKeep = 0.5, LotBuilding = 0.6, LotOpen = 0.3, LotLandmark = 0.1, FloorsMin = 2, FloorsMax = 5, Lamps = 0.03, Style = BuildingStyle.Industrial },
				new DistrictDef { Id = "laundry", Name = "Laundry", From = 3800.0, Roads = DistrictRoads.Streets, StreetKeep = 0.6, LotBuilding = 0.65, LotOpen = 0.25, LotLandmark = 0.1, FloorsMin = 4, FloorsMax = 10, Lamps = 0.05, Cables = 0.7 },
				new DistrictDef { Id = "kanazawa", Name = "Kanazawa", From = 5000.0, Roads = DistrictRoads.Streets, StreetKeep = 0.65, LotBuilding = 0.7, LotOpen = 0.2, LotLandmark = 0.1, FloorsMin = 5, FloorsMax = 14, Lamps = 0.08 }
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
			Ruin = 0.25,
			Districts = new[]
			{
				new DistrictDef { Id = "city", Name = "City", From = 0.0, Roads = DistrictRoads.Streets, StreetKeep = 0.9, LotBuilding = 0.85, LotOpen = 0.1, LotLandmark = 0.05, FloorsMin = 4, FloorsMax = 12, Lamps = 0.15, Style = BuildingStyle.Slab },
				new DistrictDef { Id = "streetlights", Name = "Streetlights", From = 1200.0, Roads = DistrictRoads.Streets, StreetKeep = 0.9, LotBuilding = 0.85, LotOpen = 0.1, LotLandmark = 0.05, FloorsMin = 4, FloorsMax = 12, Lamps = 0.4 },
				new DistrictDef { Id = "photo", Name = "Photo", From = 2400.0, Roads = DistrictRoads.Streets, StreetKeep = 0.85, LotBuilding = 0.8, LotOpen = 0.15, LotLandmark = 0.05, FloorsMin = 4, FloorsMax = 10, Lamps = 0.15 },
				new DistrictDef { Id = "temple", Name = "Temple", From = 3600.0, Roads = DistrictRoads.Streets, StreetKeep = 0.6, LotBuilding = 0.5, LotOpen = 0.4, LotLandmark = 0.1, FloorsMin = 1, FloorsMax = 4, Lamps = 0.1 },
				new DistrictDef { Id = "dwelling", Name = "Dwelling", From = 4800.0, Roads = DistrictRoads.Streets, StreetKeep = 0.85, LotBuilding = 0.8, LotOpen = 0.15, LotLandmark = 0.05, FloorsMin = 3, FloorsMax = 8, Lamps = 0.15, Style = BuildingStyle.Slab, Cables = 0.7 }
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
			Ruin = 0.4,
			Districts = new[]
			{
				new DistrictDef { Id = "upper", Name = "Upper", From = 0.0, Roads = DistrictRoads.Open, LotLandmark = 0.3, LotOpen = 0.3, Cables = 0.2 },
				new DistrictDef { Id = "rain", Name = "Rain", From = 1200.0, Roads = DistrictRoads.Streets, StreetKeep = 0.5, LotBuilding = 0.6, LotOpen = 0.3, LotLandmark = 0.1, FloorsMin = 3, FloorsMax = 8, Lamps = 0.05 },
				new DistrictDef { Id = "breakdown", Name = "Breakdown", From = 2400.0, Roads = DistrictRoads.Open, LotLandmark = 0.25, LotOpen = 0.35, Cables = 0.2 },
				new DistrictDef { Id = "technology", Name = "Technology", From = 3600.0, Roads = DistrictRoads.Streets, StreetKeep = 0.5, LotBuilding = 0.6, LotOpen = 0.3, LotLandmark = 0.1, FloorsMin = 1, FloorsMax = 3, Lamps = 0.05, Style = BuildingStyle.Industrial },
				new DistrictDef { Id = "takeoff", Name = "Takeoff", From = 4800.0, Roads = DistrictRoads.Open, LotLandmark = 0.2, LotOpen = 0.3, Cables = 0.2 }
			}
		}
	};

	public static int Count => All.Length;

	public static LayerDef Get(int layer)
	{
		return All[layer];
	}
}
