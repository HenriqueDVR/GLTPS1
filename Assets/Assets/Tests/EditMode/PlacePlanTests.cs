using System.Collections.Generic;
using NUnit.Framework;

// 17 section 12: what the plan promises, on many seeds and on every layer
public class PlacePlanTests
{
	private static readonly ulong[] seeds = { 1uL, 2uL, 3uL, 7uL, 12345uL, 99uL, 2024uL, 555uL, 31337uL, 8uL, 13uL, 21uL, 34uL, 55uL, 89uL, 144uL, 233uL, 377uL, 610uL, 987uL };

	private static LayerPlan Plan(ulong seed, int layer)
	{
		LayerPlan plan = new LayerPlan(seed, 0, new XZ(0.0, 0.0), null, new XZ(0.0, 1.0));
		for (int i = 1; i <= layer; i++)
		{
			plan = new LayerPlan(seed, i, new XZ(i * 700.0, i * -300.0), plan);
		}
		return plan;
	}

	[Test]
	public void EveryCellMostlyHasOne()
	{
		foreach (ulong seed in seeds)
		{
			LayerPlan plan = Plan(seed, 0);
			int cells = 0;
			int have = 0;
			for (int x = -6; x <= 6; x++)
			{
				for (int z = -6; z <= 6; z++)
				{
					cells++;
					have += plan.Places.At(x, z, out _) ? 1 : 0;
				}
			}
			Assert.Greater(have, cells * 3 / 4, "seed " + seed);
		}
	}

	// straight lines out from the origin: never more than 1.5 cells between places, past voids
	[Test]
	public void NoLongGapsAlongALine()
	{
		foreach (ulong seed in seeds)
		{
			for (int layer = 0; layer < Layers.All.Length; layer++)
			{
				LayerPlan plan = Plan(seed, layer);
				for (int dir = 0; dir < 8; dir++)
				{
					double angle = dir * System.Math.PI / 4.0;
					double dx = Gen2.Cos(angle);
					double dz = Gen2.Sin(angle);
					double gap = 0.0;
					for (double s = 0.0; s < 6000.0; s += PlacePlan.Cell * 0.25)
					{
						XZ p = new XZ(plan.Origin.X + dx * s, plan.Origin.Z + dz * s);
						gap = plan.Places.Nearest(_ => true, p, PlacePlan.Cell, out Place near) && near.Center.DistanceTo(p) < PlacePlan.Cell * 1.5 ? 0.0 : gap + PlacePlan.Cell * 0.25;
						Assert.Less(gap, PlacePlan.Cell * 1.5, "seed " + seed + " layer " + layer + " dir " + dir + " at " + s);
					}
				}
			}
		}
	}

	// fuel, water and shelter within 1.6 km of any point within 6 km of the origin
	[Test]
	public void SuppliesAreAlwaysInReach()
	{
		foreach (ulong seed in seeds)
		{
			LayerPlan plan = Plan(seed, 0);
			for (double x = -6000.0; x <= 6000.0; x += 800.0)
			{
				for (double z = -6000.0; z <= 6000.0; z += 800.0)
				{
					XZ p = new XZ(plan.Origin.X + x, plan.Origin.Z + z);
					Assert.IsTrue(plan.Places.Nearest(a => a.Kind == PlaceKind.FuelDepot, p, 2400.0, out Place fuel) && fuel.Center.DistanceTo(p) < 2400.0, "fuel, seed " + seed + " at " + p);
					Assert.IsTrue(plan.Places.Nearest(a => a.Kind == PlaceKind.PumpHouse, p, 2400.0, out Place water) && water.Center.DistanceTo(p) < 2400.0, "water, seed " + seed + " at " + p);
					Assert.IsTrue(plan.Places.Nearest(a => a.Shelter, p, 2400.0, out Place shelter) && shelter.Center.DistanceTo(p) < 2400.0, "shelter, seed " + seed + " at " + p);
				}
			}
		}
	}

	// a buried road under a room comes up through its floor
	[Test]
	public void NoRoadThroughARoom()
	{
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		int rooms = 0;
		foreach (ulong seed in seeds)
		{
			LayerPlan plan = Plan(seed, 0);
			for (int x = -6; x <= 6; x++)
			{
				for (int z = -6; z <= 6; z++)
				{
					if (!plan.Places.At(x, z, out Place place) || !place.OnLot)
					{
						continue;
					}
					rooms++;
					int tileX = Gen2.Cell(place.Center.X, Gen2.TileSize);
					int tileZ = Gen2.Cell(place.Center.Z, Gen2.TileSize);
					for (int i = tileX - 1; i <= tileX + 1; i++)
					{
						for (int j = tileZ - 1; j <= tileZ + 1; j++)
						{
							plan.Roads.Collect(i, j, pieces, patches);
							foreach (RoadPiece piece in pieces)
							{
								for (int a = -4; a <= 4; a++)
								{
									for (int b = -4; b <= 4; b++)
									{
										double u = place.RoomU * a * 0.25;
										double v = place.RoomV * b * 0.25;
										XZ p = new XZ(place.Center.X + place.Axis.X * u - place.Axis.Z * v, place.Center.Z + place.Axis.Z * u + place.Axis.X * v);
										Assert.Greater(RoadPlan.SegmentDistance(p, piece.A, piece.B), piece.Width * 0.5, "seed " + seed + " " + place.Kind + " cell " + x + "," + z);
									}
								}
							}
						}
					}
				}
			}
		}
		Assert.Greater(rooms, 20);
	}

	[Test]
	public void DoorFacesTheNearestRoadIfThereIsOne()
	{
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		int checkedDoors = 0;
		int toRoad = 0;
		foreach (ulong seed in seeds)
		{
			LayerPlan plan = Plan(seed, 1);
			for (int x = -6; x <= 6; x++)
			{
				for (int z = -6; z <= 6; z++)
				{
					if (!plan.Places.At(x, z, out Place place) || !place.OnLot)
					{
						continue;
					}
					XZ door = place.DoorAt;
					int tileX = Gen2.Cell(door.X, Gen2.TileSize);
					int tileZ = Gen2.Cell(door.Z, Gen2.TileSize);
					double best = double.MaxValue;
					for (int i = tileX - 1; i <= tileX + 1; i++)
					{
						for (int j = tileZ - 1; j <= tileZ + 1; j++)
						{
							plan.Roads.Collect(i, j, pieces, patches);
							foreach (RoadPiece piece in pieces)
							{
								best = System.Math.Min(best, RoadPlan.SegmentDistance(door, piece.A, piece.B));
							}
						}
					}
					checkedDoors++;
					toRoad += (best < 40.0) ? 1 : 0;
				}
			}
		}
		Assert.Greater(checkedDoors, 500);
		Assert.Greater(toRoad, checkedDoors * 8 / 10);
	}

	[Test]
	public void SamePlacesWhateverOrderTheyreAskedIn()
	{
		LayerPlan a = Plan(12345uL, 0);
		LayerPlan b = Plan(12345uL, 0);
		for (int x = 8; x >= -8; x--)
		{
			for (int z = -8; z <= 8; z++)
			{
				a.Places.At(x, z, out _);
			}
		}
		for (int z = 8; z >= -8; z--)
		{
			for (int x = -8; x <= 8; x++)
			{
				bool hasB = b.Places.At(x, z, out Place other);
				Assert.AreEqual(a.Places.At(x, z, out Place first), hasB);
				if (hasB)
				{
					Assert.AreEqual(first.Kind, other.Kind);
					Assert.AreEqual(first.Center.X, other.Center.X);
					Assert.AreEqual(first.Center.Z, other.Center.Z);
					Assert.AreEqual(first.Door, other.Door);
				}
			}
		}
	}

	// the overlay never touches the plan's lots or their item ids
	[Test]
	public void NoLotOrItemMoves()
	{
		LayerPlan plain = Plan(777uL, 0);
		LayerPlan asked = Plan(777uL, 0);
		for (int x = -4; x <= 4; x++)
		{
			for (int z = -4; z <= 4; z++)
			{
				asked.Places.At(x, z, out _);
			}
		}
		for (int x = -8; x <= 8; x += 3)
		{
			for (int z = -8; z <= 8; z += 3)
			{
				Assert.AreEqual(plain.TileHash(x, z), asked.TileHash(x, z), "tile " + x + "," + z);
			}
		}
	}

	[Test]
	public void PlaceIdsDontClashWithItems()
	{
		ulong id = PlacePlan.Id(2, -5, 7, 3);
		Assert.AreEqual(0uL, id & Gen2.TileItemTag);
		Assert.AreNotEqual(id, PlacePlan.Id(2, -5, 7, 4));
		Assert.AreNotEqual(id, PlacePlan.Id(1, -5, 7, 3));
		Assert.AreNotEqual(id, PlacePlan.Id(2, 5, 7, 3));
	}

	[Test]
	public void LootIsTheSameEveryTime()
	{
		LayerPlan plan = Plan(5uL, 0);
		for (int x = 1; x < 5; x++)
		{
			if (plan.Places.At(x, 2, out Place place))
			{
				PlaceLoot a = PlacePlan.Loot(place, 3);
				PlaceLoot b = PlacePlan.Loot(place, 3);
				Assert.AreEqual(a.Food, b.Food);
				Assert.AreEqual(a.Ammo, b.Ammo);
			}
		}
	}
}
