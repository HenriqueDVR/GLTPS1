using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

// world gen v2 roads: tiles ask for their own pieces, so the answers can't depend on order
public class RoadPlanTests
{
	private const ulong seed = 12345uL;

	private static LayerPlan Layer0()
	{
		return new LayerPlan(seed, 0, new XZ(0.0, 0.0), null);
	}

	private static LayerPlan Layer1()
	{
		return new LayerPlan(seed, 1, new XZ(7300.0, -2100.0), Layer0());
	}

	private static string F(double v)
	{
		return v.ToString("R", CultureInfo.InvariantCulture);
	}

	private static string F(XZ p)
	{
		return F(p.X) + "," + F(p.Z);
	}

	private static string Describe(List<RoadPiece> pieces, List<RoadPatch> patches)
	{
		StringBuilder text = new StringBuilder();
		foreach (RoadPiece p in pieces)
		{
			text.Append(p.Kind).Append(' ').Append(F(p.A)).Append(' ').Append(F(p.B)).Append(' ').Append(F(p.TangentA)).Append(' ').Append(F(p.TangentB)).Append(' ').Append(F(p.HeightA)).Append(' ').Append(F(p.HeightB)).Append(' ').Append(F(p.Width)).Append(' ').Append(p.BrokenA).Append(p.BrokenB).Append(p.Index).Append('\n');
		}
		foreach (RoadPatch p in patches)
		{
			text.Append(p.Kind).Append(' ').Append(F(p.Center)).Append(' ').Append(F(p.Size)).Append(' ').Append(F(p.Axis)).Append(p.Round).Append('\n');
		}
		return text.ToString();
	}

	private static List<RoadPiece> Area(LayerPlan plan, int x0, int z0, int size, List<RoadPatch> allPatches = null)
	{
		List<RoadPiece> all = new List<RoadPiece>();
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		for (int x = x0; x < x0 + size; x++)
		{
			for (int z = z0; z < z0 + size; z++)
			{
				plan.Roads.Collect(x, z, pieces, patches);
				all.AddRange(pieces);
				allPatches?.AddRange(patches);
			}
		}
		return all;
	}

	private static XZ Mid(RoadPiece p)
	{
		return new XZ((p.A.X + p.B.X) * 0.5, (p.A.Z + p.B.Z) * 0.5);
	}

	private static double NearestJunction(LayerPlan plan, XZ p)
	{
		int cx = Gen2.Cell(p.X, RoadPlan.HighwayCell);
		int cz = Gen2.Cell(p.Z, RoadPlan.HighwayCell);
		double best = double.MaxValue;
		for (int x = cx - 1; x <= cx + 1; x++)
		{
			for (int z = cz - 1; z <= cz + 1; z++)
			{
				best = Math.Min(best, plan.Roads.Junction(x, z).DistanceTo(p));
			}
		}
		return best;
	}

	[Test]
	public void SameTileSameRoadsInAnyOrder()
	{
		LayerPlan a = Layer1();
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		Dictionary<string, string> forward = new Dictionary<string, string>();
		for (int x = 50; x < 60; x++)
		{
			for (int z = -20; z < -10; z++)
			{
				a.Roads.Collect(x, z, pieces, patches);
				forward[x + "," + z] = Describe(pieces, patches);
			}
		}
		LayerPlan b = Layer1();
		for (int x = 59; x >= 50; x--)
		{
			for (int z = -11; z >= -20; z--)
			{
				b.Roads.Collect(x, z, pieces, patches);
				Assert.AreEqual(forward[x + "," + z], Describe(pieces, patches), "tile " + x + "," + z);
			}
		}
	}

	[Test]
	public void PiecesStayInTheirTile()
	{
		LayerPlan plan = Layer1();
		List<RoadPiece> pieces = new List<RoadPiece>();
		List<RoadPatch> patches = new List<RoadPatch>();
		int total = 0;
		double slack = RoadPlan.PieceLength;
		for (int x = 40; x < 70; x++)
		{
			for (int z = -30; z < 0; z++)
			{
				plan.Roads.Collect(x, z, pieces, patches);
				total += pieces.Count;
				foreach (RoadPiece p in pieces)
				{
					// owned by the midpoint before clipping at a junction, so a clipped one can sit a little outside
					XZ mid = Mid(p);
					Assert.That(mid.X, Is.InRange(x * Gen2.TileSize - slack, (x + 1) * Gen2.TileSize + slack));
					Assert.That(mid.Z, Is.InRange(z * Gen2.TileSize - slack, (z + 1) * Gen2.TileSize + slack));
					Assert.LessOrEqual(p.A.DistanceTo(p.B), RoadPlan.PieceLength * 1.4);
				}
				foreach (RoadPatch p in patches)
				{
					Assert.AreEqual(x, Gen2.Cell(p.Center.X, Gen2.TileSize));
					Assert.AreEqual(z, Gen2.Cell(p.Center.Z, Gen2.TileSize));
				}
			}
		}
		Assert.Greater(total, 1000, "a city layer should have plenty of road");
	}

	// a highway piece's end is the next piece's start, across tile borders too
	[Test]
	public void HighwaysJoinUp()
	{
		LayerPlan plan = Layer0();
		List<RoadPiece> all = Area(plan, -40, -40, 80);
		HashSet<XZ> starts = new HashSet<XZ>();
		foreach (RoadPiece p in all)
		{
			if (p.Kind == RoadKind.Highway)
			{
				starts.Add(p.A);
			}
		}
		int joined = 0;
		foreach (RoadPiece p in all)
		{
			if (p.Kind != RoadKind.Highway || p.BrokenB)
			{
				continue;
			}
			// the last piece before a junction, the start room, or the edge of the area
			bool edge = Math.Abs(p.B.X) > 39.0 * Gen2.TileSize || Math.Abs(p.B.Z) > 39.0 * Gen2.TileSize;
			if (edge || NearestJunction(plan, p.B) < RoadPlan.HighwayWidth + RoadPlan.PieceLength * 2.0 || plan.Origin.DistanceTo(p.B) < RoadPlan.StartClearance + RoadPlan.PieceLength * 2.0)
			{
				continue;
			}
			Assert.IsTrue(starts.Contains(p.B), "nothing continues from " + p.B);
			joined++;
		}
		Assert.Greater(joined, 500);
	}

	[Test]
	public void ElevatedStretchesAndBrokenSpans()
	{
		LayerPlan plan = Layer0();
		int elevated = 0;
		int broken = 0;
		for (int x = -10; x <= 10; x++)
		{
			for (int z = -10; z <= 10; z++)
			{
				for (int d = 0; d < 2; d++)
				{
					if (plan.Roads.Highway(x, z, d == 1, out HighwayLink link) && link.Elevated)
					{
						elevated++;
						broken += link.Broken ? 1 : 0;
						Assert.Greater(link.ElevatedFrom, 0.1);
						Assert.Less(link.ElevatedTo, 0.9);
					}
				}
			}
		}
		Assert.Greater(elevated, 40);
		Assert.Greater(broken, 10);
		Assert.Less(broken, elevated);
		int brokenEnds = 0;
		foreach (RoadPiece p in Area(plan, -30, -30, 60))
		{
			Assert.GreaterOrEqual(p.HeightA, 0.0);
			Assert.LessOrEqual(p.HeightA, RoadPlan.DeckHeight);
			// ramps no steeper than their steady middle part
			Assert.LessOrEqual(Math.Abs(p.HeightB - p.HeightA), p.A.DistanceTo(p.B) * RoadPlan.DeckHeight / (RoadPlan.RampLength * (1.0 - RoadPlan.RampEase)) + 1e-9);
			if (p.BrokenA || p.BrokenB)
			{
				Assert.AreEqual(RoadKind.Highway, p.Kind);
				Assert.AreEqual(RoadPlan.DeckHeight, p.BrokenA ? p.HeightA : p.HeightB, "breaks happen up on the deck");
				brokenEnds++;
			}
		}
		Assert.Greater(brokenEnds, 0);
	}

	[Test]
	public void StreetsOnlyInStreetDistricts()
	{
		foreach (LayerPlan plan in new[] { Layer0(), Layer1() })
		{
			int streets = 0;
			int outside = 0;
			int tracks = 0;
			foreach (RoadPiece p in Area(plan, (int)(plan.Origin.X / Gen2.TileSize) - 40, (int)(plan.Origin.Z / Gen2.TileSize) - 40, 80))
			{
				XZ mid = Mid(p);
				if (p.Kind == RoadKind.Street)
				{
					streets++;
					// streets stop at their district's edge
					outside += plan.Def.Districts[plan.DistrictAt(p.A.X, p.A.Z)].Roads == DistrictRoads.Streets && plan.Def.Districts[plan.DistrictAt(p.B.X, p.B.Z)].Roads == DistrictRoads.Streets ? 0 : 1;
				}
				if (p.Kind == RoadKind.Track)
				{
					tracks++;
				}
			}
			Assert.Greater(streets, 0, "layer " + plan.Layer);
			Assert.AreEqual(0, outside, "layer " + plan.Layer);
			if (plan.Layer == 0)
			{
				Assert.Greater(tracks, 0, "the fields have buried roads");
			}
			else
			{
				Assert.AreEqual(0, tracks, "the city has no open districts");
			}
		}
		// the fields right around the start have no streets
		foreach (RoadPiece p in Area(Layer0(), -4, -4, 8))
		{
			Assert.AreNotEqual(RoadKind.Street, p.Kind);
		}
	}

	// roads bend around the pillars instead of going to them
	[Test]
	public void ClearOfTheStartAndThePillars()
	{
		LayerPlan plan = Layer0();
		List<RoadPatch> patches = new List<RoadPatch>();
		int near = 0;
		foreach (RoadPiece p in Area(plan, -40, -40, 80, patches))
		{
			XZ mid = Mid(p);
			Assert.GreaterOrEqual(plan.Origin.DistanceTo(mid), RoadPlan.StartClearance);
			double pillar = plan.PillarNear(mid.X, mid.Z).Position.DistanceTo(mid);
			Assert.GreaterOrEqual(pillar, RoadPlan.PillarClearance, p.Kind + " at " + mid);
			near += pillar < RoadPlan.PillarClearance + 40.0 ? 1 : 0;
		}
		Assert.Greater(near, 0, "some roads pass right by a pillar");
		int junctions = 0;
		foreach (RoadPatch p in patches)
		{
			if (p.Round)
			{
				junctions++;
				Assert.Less(NearestJunction(plan, p.Center), 1e-6);
				Assert.GreaterOrEqual(plan.PillarNear(p.Center.X, p.Center.Z).Position.DistanceTo(p.Center), RoadPlan.PillarGap - 1e-6);
			}
		}
		Assert.Greater(junctions, 0);
	}

	[Test]
	public void HashChangesWithInputs()
	{
		ulong h = Layer0().Roads.Hash(new XZ(100.0, 100.0), 1);
		Assert.AreEqual(h, Layer0().Roads.Hash(new XZ(100.0, 100.0), 1));
		Assert.AreNotEqual(h, Layer0().Roads.Hash(new XZ(1700.0, 100.0), 1));
		Assert.AreNotEqual(h, Layer1().Roads.Hash(new XZ(100.0, 100.0), 1));
		Assert.AreNotEqual(h, new LayerPlan(seed + 1, 0, new XZ(0.0, 0.0), null).Roads.Hash(new XZ(100.0, 100.0), 1));
	}

	// frozen genVersion 2 output. a failure here means v2 saves or mixed-version co-op would break: bump genVersion instead
	[Test]
	public void GoldenValues()
	{
		Assert.AreEqual(0x9a01d62f1c4261aauL, Fold(Layer0(), -12, -12, 24), "layer 0 roads");
		Assert.AreEqual(0xd994467ccdaec45buL, Fold(Layer1(), 50, -20, 12), "layer 1 roads");
		Assert.AreEqual(0x843116a825c27decuL, Layer0().Roads.Hash(new XZ(6000.0, 2500.0), 2));
	}

	private static ulong Fold(LayerPlan plan, int x0, int z0, int size)
	{
		List<RoadPatch> patches = new List<RoadPatch>();
		List<RoadPiece> pieces = Area(plan, x0, z0, size, patches);
		ulong hash = 14695981039346656037uL;
		foreach (char c in Describe(pieces, patches))
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		return hash;
	}
}
