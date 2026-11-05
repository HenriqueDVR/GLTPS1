using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// buildings and rubble for one tile's lots, in the district's style (13-content-pipeline, look of the source):
// monoliths with raised panels and few small windows, office slabs with window bands, ruins and skeleton frames,
// industrial tanks with pipes that reach the ground. one mesh, four submeshes (office walls, concrete, dark, metal).
// tile-local like RoadMesh
public class LotMesh
{
	public const int SubmeshCount = 5;

	private const int office = 0;

	private const int concrete = 1;

	private const int dark = 2;

	private const int metal = 3;

	// the deck's own, for what lies on it (drifts)
	private const int snow = 4;

	public const float FloorHeight = 3.5f;

	private const float module = 4f;

	// buildings sit this deep so they don't float on uneven ground later
	private const float sink = 0.5f;

	private const int tankSides = 12;

	private readonly List<Vector3> vertices = new List<Vector3>();

	private readonly List<Vector2> uvs = new List<Vector2>();

	private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

	private WorldRandom random;

	// pieces harvested from the old node models (WorldKit), set by the streamer
	public Mesh[] Buildings;

	public Mesh[] Tanks;

	public Mesh[] Props;

	// a harvested mesh's geometry, read once
	private struct Piece
	{
		public Vector3[] Vertices;

		public Vector2[] Uvs;

		public int[] Triangles;

		// filled window openings (uv -1,-1 from Art/Scripts/fill_holes.py), drawn dark
		public int[] Windows;

		public Vector3 Size;
	}

	private static readonly Dictionary<Mesh, Piece> pieces = new Dictionary<Mesh, Piece>();

	public int Triangles { get; private set; }

	// a far tile's: nothing thinner than this (traces, panels, rails, steps, thin beams, small windows). they were most of
	// the triangles, a pixel or less from 700 m out, and flickered. the same random draws, so the buildings are the same
	public bool Far;

	private const float thin = 0.6f;

	// dark faces at least this big each way stay far
	private const float darkKept = 1f;

	// what far left out, where it would have started in vertices and how high it reached: roofs and raises still count
	// it, so what's stacked on a building stands where it does near
	private readonly List<Vector2> ghosts = new List<Vector2>();

	private bool Skipped(float smallest, float top)
	{
		if (!Far || smallest >= thin)
		{
			return false;
		}
		ghosts.Add(new Vector2(vertices.Count, top));
		return true;
	}

	public enum Debris
	{
		// a few small slabs and chunks
		Pile,
		// a big slab tipped into the snow
		Slab,
		// a bare dead tree
		Tree,
		// what's left of a frame: a column or two and a beam
		Frame,
		// a power pole leaning over
		Pole
	}

	public void Begin()
	{
		roofs.Clear();
		ghosts.Clear();
		vertices.Clear();
		uvs.Clear();
		foreach (List<int> list in triangles)
		{
			list.Clear();
		}
	}

	// lots from..to as they'll be drawn: a landmark lot that got no landmark comes in as Open. the sky-bridges go in
	// with the last lot, so a tile can be added over several calls
	public void AddLots(List<Lot> lots, int from, int to, DistrictDef[] districts, double ruin, double originX, double originZ)
	{
		for (int n = from; n < to; n++)
		{
			Lot lot = lots[n];
			if (lot.Hollow)
			{
				continue;
			}
			random = new WorldRandom(lot.Seed);
			Vector3 centre = new Vector3((float)(lot.Center.X - originX), 0f, (float)(lot.Center.Z - originZ));
			Vector3 u = new Vector3((float)lot.Axis.X, 0f, (float)lot.Axis.Z);
			float hu = (float)lot.HalfU;
			float hv = (float)lot.HalfV;
			if (lot.Kind == LotKind.Open)
			{
				// a big site in the fields can hold a whole ruined block, smaller open lots a harvested prop
				if (hu >= 25f && Unit() < 0.4f && Harvested(Buildings, centre, u, hu, hv, concrete, 0.6f))
				{
					continue;
				}
				if (Unit() < (float)TileOdds.HarvestedProp && Harvested(Props, centre, u, Mathf.Min(hu, 6f), Mathf.Min(hv, 6f), concrete, 0.8f))
				{
					continue;
				}
				Rubble(centre, u, hu, hv);
				continue;
			}
			if (lot.Kind != LotKind.Building)
			{
				continue;
			}
			BuildingStyle style = districts[lot.District].Style;
			float share = (float)TileOdds.Harvested(style);
			int first = vertices.Count;
			if (lot.Door != 0)
			{
				roofs.Add(new Vector4(centre.x, Entered(lot, style, centre, u, hu, hv, districts[lot.District], ruin), centre.z, (float)districts[lot.District].Stacking));
				continue;
			}
			// on a podium the building stands on the plate, clear of the strip its stairs go up
			float side = (Unit() < 0.5f) ? 1f : -1f;
			Vector3 at = centre;
			float bu = hu;
			float bv = hv;
			if (lot.Base > 0.0)
			{
				Vector3 across = Left((hu >= hv) ? u : Left(u)) * side;
				at = centre - across * 1.3f;
				if (hu >= hv)
				{
					bv -= 1.3f;
				}
				else
				{
					bu -= 1.3f;
				}
			}
			if (!(Unit() < share && Harvested(Buildings, at, u, bu, bv, concrete, 0.6f)))
			{
				Building(style, at, u, bu, bv, lot.Floors, ruin);
			}
			if (lot.Base > 0.0)
			{
				// the building goes up onto its plate
				Raise(first, (float)lot.Base);
			}
			float roof = Top(first);
			if (lot.Base > 0.0)
			{
				Podium(centre, u, hu, hv, (float)lot.Base, side);
			}
			double stacking = districts[lot.District].Stacking;
			if (style != BuildingStyle.Industrial && Unit() < stacking)
			{
				roof = Stack(centre, u, hu, hv, roof, 1 + random.Next(3));
			}
			roofs.Add(new Vector4(centre.x, roof, centre.z, (float)stacking));
		}
		if (to == lots.Count)
		{
			Bridges();
		}
	}

	// a building entered from a raised street (LotPlan.Door): it starts at the deck, on a plinth that's its lower
	// storeys, set back from the street's side by the entry. gives its roof
	private float Entered(Lot lot, BuildingStyle style, Vector3 centre, Vector3 u, float hu, float hv, DistrictDef def, double ruin)
	{
		float floor = (float)lot.Deck + deckLift;
		float depth = (float)LotPlan.PorchDepth;
		Vector3 toStreet = Left(u) * lot.Door;
		int first = vertices.Count;
		Building(style, centre - toStreet * (depth * 0.5f), u, hu, hv - depth * 0.5f, lot.Floors, ruin);
		Raise(first, floor);
		float roof = Top(first);
		Plinth(centre, u, hu, hv, floor, toStreet);
		Entry(centre + toStreet * hv + u * (float)lot.DoorAt, toStreet, floor, (float)def.Sidewalk + 0.45f);
		if (style != BuildingStyle.Industrial && Unit() < def.Stacking)
		{
			roof = Stack(centre - toStreet * (depth * 0.5f), u, hu, hv - depth * 0.5f, roof, 1 + random.Next(3));
		}
		return roof;
	}

	// streets sit this much over their deck (RoadMesh)
	private const float deckLift = 0.06f;

	// and the deck's top this much under the tile's (TileStreamer.NewTile)
	private const float deckDrop = 0.02f;

	// a flight of stairs up the side of a raised street (LotPlan.Stair) from foot along dir, out pointing away from
	// the street: solid concrete steps, a landing half way, a top landing beside the deck's opened parapet, a wall
	// on the outer side and across the top end
	// a place's shell, tile-local centre (PlaceDresser puts what's inside). walls one thick, a doorway in one, a roof
	private const float placeWall = 0.3f;

	private const int wallMaterial = 0;

	private const int metalMaterial = 3;

	private const int darkMaterial = 2;

	public void AddPlace(Place place, Vector3 centre)
	{
		Vector3 u = new Vector3((float)place.Axis.X, 0f, (float)place.Axis.Z);
		float hu = (float)place.RoomU;
		float hv = (float)place.RoomV;
		float h = (float)place.Height;
		random = new WorldRandom(place.Seed);
		switch (place.Kind)
		{
		case PlaceKind.TankHull:
			Room(centre, u, hu, hv, h, place.Door, 1.4f, 2f, darkMaterial, darkMaterial);
			// tracks down its long sides, a sloped-looking nose in steps, a turret off centre, its gun, burnt out and rusting
			Box(centre + Left(u) * (hv + 0.45f), u, hu * 1.08f, 0.45f, 0f, 1.15f, metalMaterial, darkMaterial, false);
			Box(centre - Left(u) * (hv + 0.45f), u, hu * 1.08f, 0.45f, 0f, 1.15f, metalMaterial, darkMaterial, false);
			Box(centre, u, hu * 0.42f, hv * 0.72f, h, h + 0.95f, metalMaterial, darkMaterial, false);
			Box(centre, u, hu * 0.28f, hv * 0.5f, h + 0.95f, h + 1.25f, darkMaterial, darkMaterial, false);
			Box(centre + u * (hu * 0.42f + 2.4f), u, 2.4f, 0.11f, h + 0.42f, h + 0.66f, darkMaterial, darkMaterial, true);
			break;
		case PlaceKind.PumpHouse:
			Room(centre, u, hu, hv, h, place.Door, 2f, 2.4f, concrete, concrete);
			// a stack that steams, a fat pipe along the roof and down
			Box(centre + u * (hu * 0.5f), u, 0.7f, 0.7f, h, h + 4.5f, metalMaterial, metalMaterial, false);
			Box(centre - Left(u) * (hv - 0.6f), u, hu, 0.45f, h, h + 0.9f, metalMaterial, metalMaterial, false);
			break;
		case PlaceKind.FuelDepot:
			// a low bunker wall, a tall drum on legs, a crane over it: seen from a long way off
			Box(centre + u * (hu * 0.45f), u, 2.6f, 2.6f, 0f, 11f, metalMaterial, metalMaterial, false);
			Box(centre + u * (hu * 0.45f), u, 3f, 3f, 11f, 11.5f, metalMaterial, metalMaterial, false);
			Box(centre - Left(u) * hv, u, 0.35f, 0.35f, 0f, 16f, metalMaterial, metalMaterial, false);
			Box(centre - Left(u) * hv + u * 3f, u, 6f, 0.28f, 16f, 16.6f, metalMaterial, metalMaterial, false);
			Box(centre - u * hu, u, 0.25f, hv, 0f, 1.2f, concrete, concrete, false);
			break;
		default:
			Room(centre, u, hu, hv, h, place.Door, 1.6f, 2.2f, wallMaterial, concrete);
			// a floor or two over it, so from outside it's one more block
			Box(centre, u, hu, hv, h + 0.2f, h + 0.2f + LotMesh.FloorHeight * (1f + (float)((place.Seed >> 8) % 2uL)), wallMaterial, concrete, false);
			break;
		}
	}

	private void Room(Vector3 centre, Vector3 u, float hu, float hv, float height, int door, float doorWidth, float doorHeight, int walls, int roof)
	{
		Vector3 v = Left(u);
		WallSide(centre + u * (hu - placeWall * 0.5f), v, hv, height, door == 0, doorWidth, doorHeight, walls);
		WallSide(centre - u * (hu - placeWall * 0.5f), v, hv, height, door == 1, doorWidth, doorHeight, walls);
		WallSide(centre + v * (hv - placeWall * 0.5f), u, hu - placeWall, height, door == 2, doorWidth, doorHeight, walls);
		WallSide(centre - v * (hv - placeWall * 0.5f), u, hu - placeWall, height, door == 3, doorWidth, doorHeight, walls);
		Box(centre, u, hu, hv, height, height + 0.2f, walls, roof, true);
	}

	// along's half length long, a gap in its middle if it has the door
	private void WallSide(Vector3 middle, Vector3 along, float half, float height, bool doorHere, float doorWidth, float doorHeight, int material)
	{
		float thick = placeWall * 0.5f;
		if (!doorHere)
		{
			Box(middle, along, half, thick, 0f, height, material, material, false);
			return;
		}
		float gap = doorWidth * 0.5f;
		float piece = (half - gap) * 0.5f;
		Box(middle + along * (gap + piece), along, piece, thick, 0f, height, material, material, false);
		Box(middle - along * (gap + piece), along, piece, thick, 0f, height, material, material, false);
		Box(middle, along, gap, thick, doorHeight, height, material, material, false);
	}

	public void AddStair(Vector3 foot, Vector3 dir, Vector3 out_, float deck)
	{
		float top = deck + deckLift;
		float rise = (float)LotPlan.StairRise;
		float run = (float)LotPlan.StairRun;
		float mid = (float)LotPlan.MidLanding;
		float landing = (float)LotPlan.TopLanding;
		float half = (float)LotPlan.StairWidth * 0.5f;
		const float wall = 0.3f;
		Vector3 walk = foot - out_ * (wall * 0.5f);
		float walkHalf = half - wall * 0.5f;
		Vector3 side = foot + out_ * (half - wall * 0.5f);
		int steps = Mathf.CeilToInt(deck / rise);
		int first = steps / 2;
		float s = 0f;
		for (int i = 0; i < steps; i++)
		{
			if (i == first)
			{
				float flat = i * rise;
				Box(walk + dir * (s + mid * 0.5f), dir, mid * 0.5f, walkHalf, -sink, flat, concrete, concrete, false);
				// the first flight's wall, rising with it, then flat along the landing
				Sloped(concrete, side, side + dir * s, wall * 0.5f, -sink, 1f, -sink, flat + 1f);
				Sloped(concrete, side + dir * s, side + dir * (s + mid), wall * 0.5f, -sink, flat + 1f, -sink, flat + 1f);
				s += mid;
			}
			Box(walk + dir * (s + run * 0.5f), dir, run * 0.5f, walkHalf, -sink, Mathf.Min(top, (i + 1) * rise), concrete, concrete, false);
			s += run;
		}
		float second = first * rise;
		float flightFrom = first * run + mid;
		Sloped(concrete, side + dir * flightFrom, side + dir * s, wall * 0.5f, -sink, second + 1f, -sink, top + 1f);
		Box(walk + dir * (s + landing * 0.5f), dir, landing * 0.5f, walkHalf, -sink, top, concrete, concrete, false);
		Sloped(concrete, side + dir * s, side + dir * (s + landing), wall * 0.5f, -sink, top + 1f, -sink, top + 1f);
		Box(walk + dir * (s + landing - wall * 0.5f), dir, wall * 0.5f, walkHalf, top, top + 1f, concrete, concrete, false);
	}

	// the lower storeys under an entered building, the whole lot up to the deck: panels and windows on its faces,
	// plain windows only on the street's side (nothing that could reach into the deck's space)
	private void Plinth(Vector3 centre, Vector3 u, float hu, float hv, float top, Vector3 toStreet)
	{
		Box(centre, u, hu, hv, -sink, top, concrete, concrete, false);
		Vector3 v = Left(u);
		for (int side = 0; side < 4; side++)
		{
			Vector3 normal = (side == 0) ? u : (side == 1) ? -u : (side == 2) ? v : -v;
			float half = (side < 2) ? hv : hu;
			Vector3 face = centre + normal * ((side < 2) ? hu : hv);
			if (Vector3.Dot(normal, toStreet) < 0.5f)
			{
				FaceDetail(face, normal, half, top);
				continue;
			}
			Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;
			for (float y = 1.2f; y + 1.6f < top - 0.5f; y += FloorHeight)
			{
				for (float x = -half + 2f; x + 1.2f < half - 1f; x += 3.2f)
				{
					if (Unit() < 0.35f)
					{
						continue;
					}
					Vector3 at = face + along * x + normal * 0.04f + Vector3.up * y;
					Quad(dark, at, at + along * 1.2f, at + along * 1.2f + Vector3.up * 1.4f, at + Vector3.up * 1.4f, normal);
				}
			}
		}
	}

	// the way in from a raised street, on the lot's edge at the deck's height: a concrete entry with a dark
	// doorway in its front and a dark room behind it, a steel walkway with railings across to the deck. edge is
	// the middle of the entry's front, out points at the street
	private void Entry(Vector3 edge, Vector3 out_, float floor, float walk)
	{
		float w = (float)LotPlan.PorchWidth * 0.5f;
		float d = (float)LotPlan.PorchDepth;
		float door = (float)LotPlan.DoorWidth * 0.5f;
		const float t = 0.3f;
		const float h = 3.0f;
		const float opening = 2.4f;
		Vector3 along = Left(out_);
		Vector3 middle = edge - out_ * (d * 0.5f);
		Box(middle + along * (w - t * 0.5f), out_, d * 0.5f, t * 0.5f, floor - 0.1f, floor + h, concrete, concrete, false);
		Box(middle - along * (w - t * 0.5f), out_, d * 0.5f, t * 0.5f, floor - 0.1f, floor + h, concrete, concrete, false);
		Box(edge - out_ * (d - t * 0.5f), along, w, t * 0.5f, floor - 0.1f, floor + h, concrete, concrete, false);
		float jamb = (w - t - door) * 0.5f;
		Box(edge - out_ * (t * 0.5f) + along * (door + jamb), along, jamb, t * 0.5f, floor - 0.1f, floor + h, concrete, concrete, false);
		Box(edge - out_ * (t * 0.5f) - along * (door + jamb), along, jamb, t * 0.5f, floor - 0.1f, floor + h, concrete, concrete, false);
		Box(edge - out_ * (t * 0.5f), along, door, t * 0.5f, floor + opening, floor + h, concrete, concrete, false);
		Box(middle + out_ * 0.15f, along, w + 0.15f, d * 0.5f + 0.15f, floor + h, floor + h + 0.3f, concrete, concrete, true);
		// inside, dark: the back, the sides, the ceiling, the floor
		Vector3 back = edge - out_ * (d - t - 0.02f);
		Vector3 front = edge - out_ * (t + 0.02f);
		float iw = w - t - 0.02f;
		Vector3 lo = Vector3.up * (floor + 0.01f);
		Vector3 hi = Vector3.up * (floor + h - 0.02f);
		Quad(dark, back - along * iw + lo, back + along * iw + lo, back + along * iw + hi, back - along * iw + hi, out_);
		Quad(dark, back + along * iw + lo, front + along * iw + lo, front + along * iw + hi, back + along * iw + hi, -along);
		Quad(dark, back - along * iw + lo, front - along * iw + lo, front - along * iw + hi, back - along * iw + hi, along);
		Quad(dark, back - along * iw + hi, back + along * iw + hi, front + along * iw + hi, front - along * iw + hi, Vector3.down);
		Quad(dark, back - along * iw + lo, back + along * iw + lo, front + along * iw + lo, front - along * iw + lo, Vector3.up);
		// the walkway, a grating a hair over the deck so it doesn't flicker where it laps onto it
		Vector3 bridge = edge + out_ * (walk * 0.5f);
		Box(bridge, out_, walk * 0.5f + 0.1f, door, floor - 0.12f, floor + 0.02f, metal, metal, true);
		for (int s = -1; s <= 1; s += 2)
		{
			Vector3 rail = bridge + along * (s * (door - 0.05f));
			Box(rail, out_, walk * 0.5f, 0.04f, floor + 1.0f, floor + 1.06f, metal, metal, true);
			Box(rail - out_ * (walk * 0.5f - 0.05f), out_, 0.04f, 0.04f, floor, floor + 1.0f, metal, metal, false);
			Box(rail + out_ * (walk * 0.5f - 0.05f), out_, 0.04f, 0.04f, floor, floor + 1.0f, metal, metal, false);
		}
	}

	// decks near the tile, tile-local (raised streets, viaducts): nothing of a building reaches into the space over
	// them. set by the streamer before AddLots
	public struct Lane
	{
		public Vector3 A;

		public Vector3 B;

		public float Half;

		// deck heights at A and B
		public float HA;

		public float HB;
	}

	public readonly List<Lane> Lanes = new List<Lane>();

	// whether a box (centre, axis, half sizes, y0..y1) gets into a lane's space: its deck and a storey over it
	private bool InLane(Vector3 centre, Vector3 u, float hu, float hv, float y0, float y1)
	{
		Vector3 v = Left(u);
		foreach (Lane lane in Lanes)
		{
			float length = Vector3.Distance(lane.A, lane.B);
			int steps = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
			for (int i = 0; i <= steps; i++)
			{
				float t = (float)i / steps;
				float deck = Mathf.Lerp(lane.HA, lane.HB, t);
				if (y1 < deck - 2f || y0 > deck + 6f)
				{
					continue;
				}
				Vector3 p = Vector3.Lerp(lane.A, lane.B, t) - centre;
				float du = Mathf.Abs(p.x * u.x + p.z * u.z) - hu;
				float dv = Mathf.Abs(p.x * v.x + p.z * v.z) - hv;
				float outside = new Vector2(Mathf.Max(du, 0f), Mathf.Max(dv, 0f)).magnitude;
				if (outside < lane.Half + 0.5f)
				{
					return true;
				}
			}
		}
		return false;
	}

	// building lots of the tile so far: middle (x, z), roof (y), the district's stacking (w)
	private readonly List<Vector4> roofs = new List<Vector4>();

	// the highest point added since first
	private float Top(int first)
	{
		float top = 0f;
		for (int i = first; i < vertices.Count; i++)
		{
			top = Mathf.Max(top, vertices[i].y);
		}
		foreach (Vector2 ghost in ghosts)
		{
			if (ghost.x >= first)
			{
				top = Mathf.Max(top, ghost.y);
			}
		}
		return top;
	}

	// blocks piled on the roof, each smaller or bigger than the one under it, shifted, sometimes turned a little or
	// hanging out over the street, cantilevered (no legs: they'd stand in the alley): the city builds on itself for
	// want of room (13, look of the source).
	// gives the new roof
	private float Stack(Vector3 centre, Vector3 u, float hu, float hv, float roof, int count)
	{
		for (int k = 0; k < count && roof < 160f; k++)
		{
			float turn = (Unit() < 0.3f) ? Range(-12f, 12f) : ((Unit() < 0.5f) ? 90f : 0f);
			Vector3 axis = Quaternion.Euler(0f, turn, 0f) * u;
			float su = Mathf.Clamp(hu * Range(0.45f, 1.15f), 4f, hu + 5f);
			float sv = Mathf.Clamp(hv * Range(0.45f, 1.15f), 4f, hv + 5f);
			Vector3 at = centre + u * (Range(-0.35f, 0.35f) * hu) + Left(u) * (Range(-0.35f, 0.35f) * hv);
			int floors = 2 + random.Next(6);
			if (InLane(at, axis, su + 1f, sv + 1f, roof - 1f, roof + floors * FloorHeight + 6f))
			{
				// over a deck: it stays on its own roof, square with the one under it
				axis = u;
				at = centre;
				su = Mathf.Min(su, hu * 0.9f);
				sv = Mathf.Min(sv, hv * 0.9f);
			}
			// a slab under it, so where it hangs out it has an underside
			Box(at, axis, su, sv, roof - 0.6f, roof + 0.3f, concrete, concrete, true);
			int first = vertices.Count;
			if (Unit() < 0.6f)
			{
				Monolith(at, axis, su, sv, floors, 0.0);
			}
			else
			{
				Slab(at, axis, su, sv, floors);
			}
			Raise(first, roof);
			roof = Mathf.Max(roof, Top(first));
		}
		return roof;
	}

	// sky-bridges between neighbouring buildings, part way up the lower one, a band of windows along them
	private void Bridges()
	{
		for (int i = 0; i < roofs.Count; i++)
		{
			Vector4 a = roofs[i];
			ulong roll = WorldRandom.Derive((ulong)(uint)Mathf.RoundToInt(a.x * 10f), Gen2.StreamLot + 9, (ulong)(uint)Mathf.RoundToInt(a.z * 10f));
			if (Gen2.Unit(roll) >= a.w * 0.5)
			{
				continue;
			}
			int best = -1;
			float near = 60f;
			for (int j = 0; j < roofs.Count; j++)
			{
				float d = new Vector2(roofs[j].x - a.x, roofs[j].z - a.z).magnitude;
				if (j != i && d > 14f && d < near)
				{
					near = d;
					best = j;
				}
			}
			if (best < 0)
			{
				continue;
			}
			Vector4 b = roofs[best];
			float low = Mathf.Min(a.y, b.y);
			if (low < 12f)
			{
				continue;
			}
			float y = low * (0.35f + 0.4f * (float)Gen2.Unit(roll >> 7));
			Vector3 from = new Vector3(a.x, 0f, a.z);
			Vector3 to = new Vector3(b.x, 0f, b.z);
			Vector3 along = (to - from).normalized;
			Vector3 mid = (from + to) * 0.5f;
			float width = 1.4f + (float)Gen2.Unit(roll >> 15) * 1.4f;
			if (InLane(mid, along, near * 0.5f, width, y, y + 3.4f))
			{
				continue;
			}
			// about half are open: a steel walkway with railings, a girder under it
			if (Gen2.Unit(roll >> 23) < 0.5)
			{
				float walk = 0.9f;
				Vector3 side = Left(along) * (walk - 0.06f);
				Box(mid, along, near * 0.5f, walk, y - 0.15f, y, metal, metal, true);
				Box(mid, along, near * 0.5f, 0.2f, y - 0.8f, y - 0.15f, metal, metal, true);
				Railing(mid - along * (near * 0.5f) + side, mid + along * (near * 0.5f) + side, y);
				Railing(mid - along * (near * 0.5f) - side, mid + along * (near * 0.5f) - side, y);
				continue;
			}
			Box(mid, along, near * 0.5f, width, y, y + 3.4f, concrete, concrete, true);
			Box(mid, along, near * 0.5f - 0.2f, width + 0.05f, y + 1.3f, y + 2.3f, dark, dark, false);
		}
		roofs.Clear();
	}

	private void Building(BuildingStyle style, Vector3 centre, Vector3 u, float hu, float hv, int floors, double ruin)
	{
		{
			switch (style)
			{
				case BuildingStyle.Slab:
					Slab(centre, u, hu, hv, floors);
					break;
				case BuildingStyle.Ruin:
					if (Unit() < 0.5f)
					{
						Skeleton(centre, u, hu, hv, floors);
					}
					else
					{
						Monolith(centre, u, hu, hv, floors, 1.0);
					}
					break;
				case BuildingStyle.Industrial:
					Industrial(centre, u, hu, hv, floors);
					break;
				default:
					Monolith(centre, u, hu, hv, floors, ruin);
					break;
			}
		}
	}

	// everything added since first, moved up (or down)
	private void Raise(int first, float by)
	{
		for (int i = first; i < vertices.Count; i++)
		{
			vertices[i] += Vector3.up * by;
		}
		for (int i = 0; i < ghosts.Count; i++)
		{
			if (ghosts[i].x >= first)
			{
				ghosts[i] = new Vector2(ghosts[i].x, ghosts[i].y + by);
			}
		}
	}

	// the raised plate a podium lot stands on, its cornice, and stairs up one long side, on the sidewalk
	private void Podium(Vector3 centre, Vector3 u, float hu, float hv, float height, float side)
	{
		Vector3 along = (hu >= hv) ? u : Left(u);
		float length = Mathf.Max(hu, hv);
		float depth = Mathf.Min(hu, hv);
		// the plate leaves a strip of the lot for the stairs, so they never stand in the street
		Vector3 across = Left(along) * side;
		Vector3 plate = centre - across * 1.3f;
		Box(plate, along, length, depth - 1.3f, -sink, height, concrete, concrete, false);
		Box(plate, along, length + 0.3f, depth - 1.0f, height - 0.8f, height, concrete, concrete, false);
		Vector3 out_ = across * (depth - 1.2f);
		int steps = Mathf.CeilToInt(height / stairRise);
		for (int k = 0; k < steps; k++)
		{
			float top = Mathf.Min(height, (k + 1) * stairRise);
			Box(centre + out_ + along * (-length + (k + 0.5f) * stairRun), along, stairRun * 0.5f, 1.2f, -sink, top, concrete, concrete, false);
		}
	}

	// under the girls' step height (0.23 m), the same 1.5 m of run a metre up that LotPlan.Podium counts on
	private const float stairRise = 0.2f;

	private const float stairRun = 0.3f;

	// ---- voids (ChasmPlan)

	// a block on a stilt standing up out of a void: its body from top down, the stilt on down into the dark
	public void AddStiltTower(Vector3 centre, Vector3 u, float half, float top, float body, ulong seed)
	{
		if (InLane(centre, u, half, half, top - body - 4f, top + 6f))
		{
			return;
		}
		random = new WorldRandom(seed);
		int floors = Mathf.Max(4, Mathf.RoundToInt(body / FloorHeight));
		// not through the void's floor, the deck's underside: from the layer below it would hang out of the ceiling
		floors = Mathf.Max(1, Mathf.Min(floors, Mathf.FloorToInt((top + DeckMesh.Abyss - 4f) / FloorHeight)));
		int first = vertices.Count;
		Slab(centre, u, half, half * Range(0.7f, 1f), floors);
		float bottom = top - floors * FloorHeight;
		Raise(first, bottom);
		// down to the void's dark floor, past it the stilt hung out under the deck in the layer below's sky
		Box(centre, u, 2.2f, 2.2f, -DeckMesh.Abyss, bottom + 1f, concrete, concrete, false);
		// a strut or two from the stilt up under the block
		Box(centre, u, half * 0.9f, 0.8f, bottom - 1.5f, bottom + 0.5f, metal, metal, true);
		Box(centre, Left(u), half * 0.6f, 0.8f, bottom - 1.5f, bottom + 0.5f, metal, metal, true);
	}

	// a narrow steel walkway straight across a void, railings over the gap, the ends resting on the deck
	public void AddFootbridge(Vector3 middle, Vector3 across, float length, float gap)
	{
		float w = 1.2f;
		Vector3 v = Left(across);
		Box(middle, across, length * 0.5f, w, -0.45f, 0.12f, metal, metal, true);
		Box(middle + v * (w - 0.3f), across, length * 0.5f, 0.15f, -1.1f, -0.45f, metal, metal, true);
		Box(middle - v * (w - 0.3f), across, length * 0.5f, 0.15f, -1.1f, -0.45f, metal, metal, true);
		for (int s = -1; s <= 1; s += 2)
		{
			Vector3 rail = middle + v * (s * (w - 0.06f));
			Box(rail, across, gap * 0.5f, 0.05f, 1.0f, 1.08f, metal, metal, true);
			for (float t = -gap * 0.5f; t <= gap * 0.5f + 0.01f; t += 2.5f)
			{
				Box(rail + across * t, across, 0.04f, 0.04f, 0.12f, 1.0f, metal, metal, false);
			}
		}
	}

	// one piece of scatter at a tile-local point on the ground
	public void AddDebris(Debris kind, Vector3 at, ulong seed)
	{
		random = new WorldRandom(seed);
		Quaternion turn = Quaternion.Euler(0f, Range(0f, 360f), 0f);
		switch (kind)
		{
			case Debris.Pile:
				for (int i = 3 + random.Next(4); i > 0; i--)
				{
					Vector3 p = at + turn * new Vector3(Range(-3f, 3f), 0f, Range(-3f, 3f));
					OrientedBox(p, Quaternion.Euler(Range(-25f, 25f), Range(0f, 360f), Range(-25f, 25f)), new Vector3(Range(0.3f, 1.5f), Range(0.2f, 0.6f), Range(0.3f, 1.5f)), concrete);
				}
				break;
			case Debris.Slab:
				OrientedBox(at + Vector3.up * Range(-0.5f, 0.5f), turn * Quaternion.Euler(Range(10f, 35f), 0f, Range(-10f, 10f)), new Vector3(Range(2f, 5f), Range(0.25f, 0.5f), Range(1.5f, 4f)), concrete);
				break;
			case Debris.Tree:
				Tree(at);
				break;
			case Debris.Frame:
				Frame(at, turn);
				break;
			default:
				Vector3 lean = turn * Quaternion.Euler(Range(5f, 25f), 0f, 0f) * Vector3.up;
				float tall = Range(6f, 10f);
				Beam(at - Vector3.up * sink, at + lean * tall, 0.15f, dark);
				Vector3 arm = turn * Vector3.right;
				Beam(at + lean * (tall - 0.6f) - arm * 1.2f, at + lean * (tall - 0.6f) + arm * 1.2f, 0.08f, dark);
				break;
		}
	}

	// a utility pole with a crossbar along across, its wires hang from top
	public void AddPole(Vector3 at, float tall, Vector3 across)
	{
		Beam(at - Vector3.up * sink, at + Vector3.up * tall, 0.12f, dark);
		Beam(at + Vector3.up * (tall - 0.5f) - across * 1.1f, at + Vector3.up * (tall - 0.5f) + across * 1.1f, 0.07f, dark);
	}

	// a wire sagging between two points
	public void AddCable(Vector3 a, Vector3 b, float sag)
	{
		Vector3 previous = a;
		for (int i = 1; i <= 6; i++)
		{
			float t = i / 6f;
			Vector3 p = Vector3.Lerp(a, b, t) - Vector3.up * (sag * 4f * t * (1f - t));
			Beam(previous, p, 0.035f, dark);
			previous = p;
		}
	}

	public bool End(Mesh mesh)
	{
		mesh.Clear();
		Triangles = 0;
		if (vertices.Count == 0)
		{
			return false;
		}
		mesh.indexFormat = (vertices.Count > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16;
		mesh.SetVertices(vertices);
		mesh.SetUVs(0, uvs);
		mesh.subMeshCount = SubmeshCount;
		for (int i = 0; i < SubmeshCount; i++)
		{
			mesh.SetTriangles(triangles[i], i, false);
			Triangles += triangles[i].Count / 3;
		}
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return true;
	}

	private float Unit()
	{
		return (float)Gen2.Unit(random.NextULong());
	}

	private float Range(float min, float max)
	{
		return min + (max - min) * Unit();
	}

	private static Vector3 Left(Vector3 axis)
	{
		return new Vector3(-axis.z, 0f, axis.x);
	}

	// ---- styles

	// a concrete mass, maybe a set-back upper block, raised panels and a few small windows on its faces,
	// boxes jutting out higher up, something on the roof. a ruin loses part of its top
	private void Monolith(Vector3 centre, Vector3 u, float hu, float hv, int floors, double ruin)
	{
		float margin = Range(0.2f, 1.5f);
		hu = Mathf.Max(3f, hu - margin);
		hv = Mathf.Max(3f, hv - margin);
		Vector3 v = Left(u);
		float height = floors * FloorHeight + Range(0f, 2f);
		bool broken = floors >= 2 && Unit() < ruin;
		if (broken)
		{
			float at = Range(0.4f, 0.7f);
			float lost = Mathf.Max(1, Mathf.FloorToInt(Unit() * floors)) * FloorHeight + Range(0f, 1.2f);
			float a = hu * at;
			float b = hu - a;
			Box(centre - u * (hu - a), u, a, hv, -sink, height, concrete, concrete, false);
			Box(centre + u * (hu - b), u, b, hv, -sink, Mathf.Max(1.5f, height - lost), concrete, concrete, false);
		}
		else
		{
			Box(centre, u, hu, hv, -sink, height, concrete, concrete, false);
			if (Unit() < 0.6f)
			{
				// a narrower block on top, pushed towards one corner
				float su = hu * Range(0.45f, 0.85f);
				float sv = hv * Range(0.45f, 0.85f);
				Vector3 top = centre + u * ((hu - su) * Range(-1f, 1f)) + v * ((hv - sv) * Range(-1f, 1f));
				Box(top, u, su, sv, height, height + Mathf.FloorToInt(Range(1f, 5f)) * FloorHeight, concrete, concrete, false);
			}
		}
		for (int side = 0; side < 4; side++)
		{
			Vector3 normal = (side == 0) ? u : (side == 1) ? -u : (side == 2) ? v : -v;
			float half = (side < 2) ? hv : hu;
			float depth = (side < 2) ? hu : hv;
			Vector3 face = centre + normal * depth;
			FaceDetail(face, normal, half, broken ? height * 0.6f : height);
		}
		if (!broken && Unit() < 0.3f)
		{
			// a stair house or a tank on the roof
			Box(centre + u * (hu * Range(-0.5f, 0.5f)) + v * (hv * Range(-0.5f, 0.5f)), u, Range(1.5f, 3f), Range(1.5f, 3f), height, height + Range(2.5f, 4f), concrete, concrete, false);
		}
	}

	// panels standing out of a face, a few small square windows, sometimes a cantilevered box
	private void FaceDetail(Vector3 face, Vector3 normal, float half, float height)
	{
		Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;
		int panels = 2 + random.Next(4);
		for (int i = 0; i < panels; i++)
		{
			float w = Range(1.5f, Mathf.Min(8f, half));
			float h = Range(2f, Mathf.Max(2.1f, Mathf.Min(12f, height * 0.6f)));
			float x = Range(-half + w * 0.5f, half - w * 0.5f);
			float y = Range(0.5f, Mathf.Max(0.6f, height - h - 0.5f));
			float d = Range(0.2f, 0.5f);
			Box(face + along * x + normal * (d * 0.5f), along, w * 0.5f, d * 0.5f, y, y + h, concrete, concrete, true);
		}
		// dark traces cut into the face, straight or bent once, like the source's walls
		int traces = 2 + random.Next(5);
		for (int i = 0; i < traces; i++)
		{
			float width = Range(0.25f, 0.6f);
			float x = Range(-half + 0.5f, half - 0.5f);
			float y = Range(0.8f, Mathf.Max(0.9f, height - 1f));
			float run = Range(3f, Mathf.Max(3.1f, Mathf.Min(16f, height - y)));
			bool up = Unit() < 0.5f;
			Trace(face, along, normal, half, height, x, y, up ? 0f : run, up ? run : 0f, width);
			if (Unit() < 0.5f)
			{
				// the bend
				float turn = Range(2f, 8f) * (Unit() < 0.5f ? -1f : 1f);
				Trace(face, along, normal, half, height, up ? x : x + run, up ? y + run : y, up ? turn : 0f, up ? 0f : -Mathf.Abs(turn), width);
			}
		}
		int windows = random.Next(5);
		for (int i = 0; i < windows; i++)
		{
			float s = Range(0.7f, 1.3f);
			int row = 1 + random.Next(4);
			float x = Range(-half + 1f, Mathf.Max(-half + 1.1f, half - 1f - row * s * 1.6f));
			float y = Mathf.Floor(Range(1f, Mathf.Max(1.1f, height - 2f)) / FloorHeight) * FloorHeight + 1.2f;
			for (int j = 0; j < row; j++)
			{
				Vector3 at = face + along * (x + j * s * 1.6f) + normal * 0.04f + Vector3.up * y;
				Quad(dark, at, at + along * s, at + along * s + Vector3.up * s, at + Vector3.up * s, normal);
			}
		}
		if (height > 10f && Unit() < 0.35f)
		{
			float w = Range(3f, Mathf.Max(3.1f, Mathf.Min(9f, half * 1.6f)));
			float d = Range(2f, 5f);
			float y = Range(height * 0.35f, height - 4f);
			float x = Range(-half + w * 0.5f, half - w * 0.5f);
			float top = y + Range(3f, 7f);
			if (!InLane(face + along * x + normal * (d * 0.5f), along, w * 0.5f, d * 0.5f, y, top))
			{
				Box(face + along * x + normal * (d * 0.5f), along, w * 0.5f, d * 0.5f, y, top, concrete, concrete, true);
			}
		}
	}

	// a low drift of snow on the deck, its rim under the deck's top and its slope easing to nothing there: the ket's
	// thin wheels stop dead at a 2 cm step, so there's none to meet. uvs like the deck's, the snow runs on over it
	public void AddDrift(Vector3 at, float a, float b, float height, float turn, ulong seed)
	{
		const int rings = 5;
		const int sectors = 14;
		const float under = 0.06f;
		random = new WorldRandom(seed);
		float p1 = Range(0f, 6.28f);
		float p2 = Range(0f, 6.28f);
		float cos = Mathf.Cos(turn);
		float sin = Mathf.Sin(turn);
		int first = vertices.Count;
		float size = (float)Gen2.TileSize;
		vertices.Add(at + Vector3.up * (height - under));
		uvs.Add(new Vector2(at.x, at.z) / size);
		for (int ring = 1; ring <= rings; ring++)
		{
			float r = (float)ring / rings;
			float y = height * (1f - r * r) * (1f - r * r) - under;
			for (int s = 0; s < sectors; s++)
			{
				float angle = s * 6.2831855f / sectors;
				float wobble = r * (1f + 0.16f * Mathf.Sin(2f * angle + p1) + 0.08f * Mathf.Sin(3f * angle + p2));
				float dx = Mathf.Cos(angle) * a * wobble;
				float dz = Mathf.Sin(angle) * b * wobble;
				Vector3 p = at + new Vector3(dx * cos - dz * sin, y, dx * sin + dz * cos);
				vertices.Add(p);
				uvs.Add(new Vector2(p.x, p.z) / size);
			}
		}
		List<int> list = triangles[snow];
		for (int s = 0; s < sectors; s++)
		{
			int next = (s + 1) % sectors;
			Upward(list, first, first + 1 + s, first + 1 + next);
			for (int ring = 0; ring < rings - 1; ring++)
			{
				int inner = first + 1 + ring * sectors;
				int outer = inner + sectors;
				Upward(list, inner + s, outer + s, outer + next);
				Upward(list, inner + s, outer + next, inner + next);
			}
		}
	}

	private void Upward(List<int> list, int a, int b, int c)
	{
		bool up = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y > 0f;
		list.Add(a);
		list.Add(up ? b : c);
		list.Add(up ? c : b);
	}

	// a dark strip from (x, y) running dx along the face and dy up, clipped to the face
	private void Trace(Vector3 face, Vector3 along, Vector3 normal, float half, float height, float x, float y, float dx, float dy, float width)
	{
		float x0 = Mathf.Clamp(Mathf.Min(x, x + dx) - width * 0.5f, -half, half);
		float x1 = Mathf.Clamp(Mathf.Max(x, x + dx) + width * 0.5f, -half, half);
		float y0 = Mathf.Clamp(Mathf.Min(y, y + dy) - width * 0.5f, 0f, height);
		float y1 = Mathf.Clamp(Mathf.Max(y, y + dy) + width * 0.5f, 0f, height);
		if (x1 - x0 < 0.1f || y1 - y0 < 0.1f)
		{
			return;
		}
		Vector3 o = face + normal * 0.03f;
		Quad(dark, o + along * x0 + Vector3.up * y0, o + along * x1 + Vector3.up * y0, o + along * x1 + Vector3.up * y1, o + along * x0 + Vector3.up * y1, normal);
	}

	// a tall office slab along the lot's long side, a dark window band on every floor of its long faces
	private void Slab(Vector3 centre, Vector3 u, float hu, float hv, int floors)
	{
		if (hv > hu)
		{
			u = Left(u);
			float h = hu;
			hu = hv;
			hv = h;
		}
		hu = Mathf.Max(4f, hu - Range(1f, 2.5f));
		hv = Mathf.Max(4f, Mathf.Min(hv - 1.5f, Range(5f, 8f)));
		Vector3 v = Left(u);
		int storeys = floors + 3;
		float height = storeys * FloorHeight;
		Box(centre, u, hu, hv, -sink, height, office, concrete, false);
		for (int side = -1; side <= 1; side += 2)
		{
			Vector3 normal = v * side;
			Vector3 face = centre + normal * (hv + 0.04f);
			for (int f = 0; f < storeys; f++)
			{
				Vector3 a = face - u * (hu - 0.6f) + Vector3.up * (f * FloorHeight + 1.1f);
				Vector3 b = face + u * (hu - 0.6f) + Vector3.up * (f * FloorHeight + 1.1f);
				Quad(dark, a, b, b + Vector3.up * 1.3f, a + Vector3.up * 1.3f, normal);
			}
		}
		// a parapet along the long edges of the roof
		Box(centre + v * (hv - 0.15f), u, hu, 0.15f, height, height + 1f, concrete, concrete, false);
		Box(centre - v * (hv - 0.15f), u, hu, 0.15f, height, height + 1f, concrete, concrete, false);
	}

	// bare columns and beams, broken off at different heights, a slab or two left
	private void Skeleton(Vector3 centre, Vector3 u, float hu, float hv, int floors)
	{
		hu = Mathf.Max(3f, hu - Range(1f, 3f));
		hv = Mathf.Max(3f, hv - Range(1f, 3f));
		Vector3 v = Left(u);
		int nu = Mathf.Max(2, Mathf.RoundToInt(hu * 2f / Range(4f, 6f)) + 1);
		int nv = Mathf.Max(2, Mathf.RoundToInt(hv * 2f / Range(4f, 6f)) + 1);
		int storeys = Mathf.Max(2, floors + 1);
		int[,] tops = new int[nu, nv];
		for (int i = 0; i < nu; i++)
		{
			for (int j = 0; j < nv; j++)
			{
				tops[i, j] = Unit() < 0.15f ? 0 : 1 + random.Next(storeys);
			}
		}
		for (int i = 0; i < nu; i++)
		{
			for (int j = 0; j < nv; j++)
			{
				Vector3 at = Grid(centre, u, v, hu, hv, nu, nv, i, j);
				if (tops[i, j] > 0)
				{
					Box(at, u, 0.3f, 0.3f, -sink, tops[i, j] * FloorHeight, concrete, concrete, false);
				}
				// beams to the next column along each axis, on the floors both still reach
				for (int axis = 0; axis < 2; axis++)
				{
					int ni = i + (axis == 0 ? 1 : 0);
					int nj = j + (axis == 1 ? 1 : 0);
					if (ni >= nu || nj >= nv)
					{
						continue;
					}
					Vector3 to = Grid(centre, u, v, hu, hv, nu, nv, ni, nj);
					int reach = Mathf.Min(tops[i, j], tops[ni, nj]);
					for (int f = 1; f <= reach; f++)
					{
						if (Unit() < 0.8f)
						{
							float y = f * FloorHeight - 0.25f;
							Beam(at + Vector3.up * y, to + Vector3.up * y, 0.25f, concrete);
						}
					}
				}
			}
		}
		if (Unit() < 0.6f)
		{
			// what's left of a floor slab
			Box(centre + u * (hu * Range(-0.4f, 0.4f)), u, hu * Range(0.3f, 0.6f), hv * Range(0.4f, 0.9f), FloorHeight - 0.3f, FloorHeight, concrete, concrete, true);
		}
	}

	private static Vector3 Grid(Vector3 centre, Vector3 u, Vector3 v, float hu, float hv, int nu, int nv, int i, int j)
	{
		return centre + u * (-hu + hu * 2f * i / (nu - 1)) + v * (-hv + hv * 2f * j / (nv - 1));
	}

	// one to three tanks on a concrete pad, banded, a pipe from each down to the ground and between neighbours
	private void Industrial(Vector3 centre, Vector3 u, float hu, float hv, int floors)
	{
		// a harvested silo now and then
		if (Unit() < 0.4f && Harvested(Tanks, centre, u, hu, hv, metal, 0.5f))
		{
			return;
		}
		// not every lot is a tank farm: sheds and blocks between them
		if (Unit() < 0.4f)
		{
			Monolith(centre, u, hu, hv, Mathf.Max(1, floors - 1), 0.2);
			return;
		}
		Vector3 v = Left(u);
		if (hv > hu)
		{
			u = Left(u);
			v = Left(u);
			float h = hu;
			hu = hv;
			hv = h;
		}
		hu = Mathf.Max(4f, hu - 1.5f);
		hv = Mathf.Max(4f, hv - 1.5f);
		Box(centre, u, hu, hv, -sink, 0.3f, concrete, concrete, false);
		int count = 1 + random.Next(Mathf.Clamp(Mathf.FloorToInt(hu * hv / 120f), 1, 3));
		Vector3[] at = new Vector3[count];
		float[] radius = new float[count];
		float[] height = new float[count];
		float slot = hu * 2f / count;
		for (int i = 0; i < count; i++)
		{
			// side by side along the long axis
			radius[i] = Mathf.Min(Range(2.5f, 7f), slot * 0.4f, hv * 0.8f);
			height[i] = Range(6f, 10f + floors * 3f);
			at[i] = centre + u * (-hu + slot * (i + 0.5f)) + v * (Range(-1f, 1f) * (hv - radius[i]) * 0.5f);
			Cylinder(at[i], radius[i], 0.3f, height[i], metal, true);
			for (float y = 3f; y < height[i] - 1f; y += Range(3f, 5f))
			{
				Cylinder(at[i], radius[i] + 0.12f, y, y + 0.3f, metal, false);
			}
			// out of the side and down into the ground
			Vector3 dir = (i % 2 == 0) ? v : -v;
			float py = height[i] * Range(0.4f, 0.8f);
			float pipe = Range(0.25f, 0.45f);
			Vector3 start = at[i] + dir * radius[i] + Vector3.up * py;
			Vector3 elbow = start + dir * Range(1.5f, 3f);
			Beam(start, elbow + dir * pipe, pipe, metal);
			Beam(elbow + Vector3.up * pipe, elbow - Vector3.up * (py + sink), pipe, metal);
		}
		for (int i = 0; i + 1 < count; i++)
		{
			float y = Mathf.Min(height[i], height[i + 1]) * Range(0.5f, 0.85f);
			Vector3 dir = (at[i + 1] - at[i]).normalized;
			Beam(at[i] + dir * radius[i] + Vector3.up * y, at[i + 1] - dir * radius[i + 1] + Vector3.up * y, Range(0.3f, 0.6f), metal);
		}
	}

	// a trunk leaning a little, bare branches reaching up and out, a few twigs off those
	private void Tree(Vector3 at)
	{
		float tall = Range(3f, 7f);
		float girth = Range(0.12f, 0.25f);
		Vector3 top = at + new Vector3(Range(-0.6f, 0.6f), tall, Range(-0.6f, 0.6f));
		Beam(at - Vector3.up * sink, top, girth, dark);
		int branches = 3 + random.Next(4);
		for (int i = 0; i < branches; i++)
		{
			Vector3 from = Vector3.Lerp(at, top, Range(0.45f, 0.95f));
			Vector3 dir = Quaternion.Euler(Range(-55f, -15f), Range(0f, 360f), 0f) * Vector3.forward;
			Vector3 to = from + dir * Range(1f, 3f);
			Beam(from, to, girth * 0.45f, dark);
			if (Unit() < 0.5f)
			{
				Beam(to, to + Quaternion.Euler(Range(-60f, -10f), Range(0f, 360f), 0f) * Vector3.forward * Range(0.5f, 1.4f), girth * 0.25f, dark);
			}
		}
	}

	// one or two columns with a beam across, snapped somewhere, sometimes leaning
	private void Frame(Vector3 at, Quaternion turn)
	{
		Vector3 across = turn * Vector3.right;
		float span = Range(4f, 8f);
		float tall = Range(4f, 10f);
		Vector3 up = Quaternion.AngleAxis(Range(-8f, 8f), turn * Vector3.forward) * Vector3.up;
		Beam(at - Vector3.up * sink, at + up * tall, 0.35f, concrete);
		bool second = Unit() < 0.6f;
		if (second)
		{
			Beam(at + across * span - Vector3.up * sink, at + across * span + up * (tall * Range(0.3f, 1f)), 0.35f, concrete);
		}
		// the beam, broken off past the column or carrying on to the second
		float reach = second ? span : span * Range(0.4f, 0.9f);
		Beam(at + up * (tall - 0.3f) - across * Range(0.5f, 2f), at + up * (tall - 0.3f) + across * reach, 0.3f, concrete);
	}

	// a few low slabs and heaps, turned every which way
	private void Rubble(Vector3 centre, Vector3 u, float hu, float hv)
	{
		Vector3 v = Left(u);
		int count = 3 + random.Next(5);
		for (int i = 0; i < count; i++)
		{
			Vector3 at = centre + u * (Range(-1f, 1f) * hu * 0.7f) + v * (Range(-1f, 1f) * hv * 0.7f);
			Vector3 axis = new Vector3(1f, 0f, Range(-1f, 1f)).normalized;
			Box(at, axis, Range(1f, 4f), Range(1f, 4f), -sink, Range(0.4f, 1.8f), concrete, concrete, false);
		}
	}

	// ---- viaducts (ViaductPlan) and raised streets (RoadPlan)

	private const float deckThickness = 1.5f;

	// RoadMesh's deck slab
	private const float roadDeck = 1.2f;

	private const int archSteps = 8;

	// ---- the underground (UndergroundPlan), heights from the deck

	private static float Level => -(float)UndergroundPlan.Level;

	private static float Ceiling => -(float)UndergroundPlan.Slab;

	// a hall: the slab over it, walls down into its pit with the galleries' mouths at the level (gaps: per side +x, -x,
	// +z, -z, the middle of an opening along the wall from the hall's middle, NaN for none), the ledge round the walls,
	// a railing at its edge, the bridge across; pipes, lattice towers, maybe a fan in the wall fanOut points at.
	// a small hall is all floor
	public void AddHall(Vector3 centre, float halfX, float halfZ, float[] gaps, ulong seed, Vector3 fanOut)
	{
		random = new WorldRandom(seed);
		float wall = (float)UndergroundPlan.Wall;
		float mouth = (float)UndergroundPlan.GalleryWidth * 0.5f;
		float ledge = (float)UndergroundPlan.Ledge;
		float bottom = -(float)UndergroundPlan.Bottom;
		float pitX = halfX - ledge;
		float pitZ = halfZ - ledge;
		bool pit = pitX >= (float)UndergroundPlan.MinPit && pitZ >= (float)UndergroundPlan.MinPit;
		float low = pit ? bottom : Level;
		Box(centre, Vector3.right, halfX + wall, halfZ + wall, Ceiling, -0.1f, concrete, concrete, true);
		Box(centre, Vector3.right, halfX + wall, halfZ + wall, low - 1f, low, pit ? dark : concrete, concrete, false);
		for (int side = 0; side < 4; side++)
		{
			Vector3 out_ = Side(side);
			Vector3 along = Left(out_);
			float half = (side < 2) ? halfZ + wall : halfX + wall;
			Vector3 middle = centre + out_ * (((side < 2) ? halfX : halfZ) + wall * 0.5f);
			openings.Clear();
			if (!float.IsNaN(gaps[side]))
			{
				openings.Add(new Vector2(gaps[side] - mouth, gaps[side] + mouth));
			}
			UnderWall(middle, along, half, low - 1f, Ceiling, Level, Ceiling);
		}
		if (!pit)
		{
			return;
		}
		// the ledge, its edge railed but where the bridge meets it, girders under it
		bool alongX = halfX >= halfZ;
		float bridge = (float)UndergroundPlan.BridgeWidth * 0.5f;
		Box(centre + Vector3.right * (pitX + ledge * 0.5f), Vector3.right, ledge * 0.5f, halfZ, Level - 1f, Level, concrete, concrete, true);
		Box(centre - Vector3.right * (pitX + ledge * 0.5f), Vector3.right, ledge * 0.5f, halfZ, Level - 1f, Level, concrete, concrete, true);
		Box(centre + Vector3.forward * (pitZ + ledge * 0.5f), Vector3.right, pitX, ledge * 0.5f, Level - 1f, Level, concrete, concrete, true);
		Box(centre - Vector3.forward * (pitZ + ledge * 0.5f), Vector3.right, pitX, ledge * 0.5f, Level - 1f, Level, concrete, concrete, true);
		for (int side = 0; side < 4; side++)
		{
			Vector3 out_ = Side(side);
			Vector3 along = Left(out_);
			float half = (side < 2) ? pitZ : pitX;
			Vector3 edge = centre + out_ * (((side < 2) ? pitX : pitZ) - 0.1f);
			// the bridge lands on the long sides' ledges
			bool landing = alongX ? side >= 2 : side < 2;
			if (landing)
			{
				Railing(edge - along * half, edge - along * bridge, Level);
				Railing(edge + along * bridge, edge + along * half, Level);
			}
			else
			{
				Railing(edge - along * half, edge + along * half, Level);
			}
			for (float t = -half + 4f; t < half; t += 8f)
			{
				Beam(centre + out_ * (((side < 2) ? halfX : halfZ) - 0.2f) + along * t + Vector3.up * (Level - 5f), edge + along * t + Vector3.up * (Level - 1f), 0.25f, metal);
			}
		}
		Vector3 across = alongX ? Vector3.forward : Vector3.right;
		float span = alongX ? pitZ : pitX;
		Box(centre, across, span, bridge, Level - 0.6f, Level, metal, metal, true);
		Box(centre, across, span, bridge * 0.4f, Level - 2.2f, Level - 0.6f, metal, metal, true);
		Vector3 v = Left(across);
		Railing(centre - across * span + v * (bridge - 0.1f), centre + across * span + v * (bridge - 0.1f), Level);
		Railing(centre - across * span - v * (bridge - 0.1f), centre + across * span - v * (bridge - 0.1f), Level);
		// pipes up out of the pit in its corners, others along walls under the slab
		for (int c = 0; c < 4; c++)
		{
			float sx = ((c & 1) == 0) ? -1f : 1f;
			float sz = ((c & 2) == 0) ? -1f : 1f;
			Vector3 corner = centre + new Vector3(sx * (pitX - 2f), 0f, sz * (pitZ - 2f));
			float r = Range(0.5f, 1.3f);
			Tube(corner + Vector3.up * bottom, corner + Vector3.up * Ceiling, r, metal);
			if (Unit() < 0.5f)
			{
				Vector3 beside = corner - new Vector3(sx, 0f, 0f) * (r + 1.2f);
				Tube(beside + Vector3.up * bottom, beside + Vector3.up * Ceiling, r * 0.5f, metal);
			}
		}
		for (int side = 0; side < 4; side++)
		{
			if (Unit() < 0.5f)
			{
				continue;
			}
			Vector3 out_ = Side(side);
			Vector3 along = Left(out_);
			float half = (side < 2) ? halfZ : halfX;
			Vector3 run = centre + out_ * (((side < 2) ? halfX : halfZ) - 1.4f);
			for (int p = 1 + random.Next(3); p > 0; p--)
			{
				float y = Ceiling - 1f - p * 1.6f;
				Tube(run - along * half + Vector3.up * y - out_ * (p * 0.4f), run + along * half + Vector3.up * y - out_ * (p * 0.4f), Range(0.25f, 0.6f), metal);
			}
		}
		// lattice towers standing in the pit, clear of the bridge
		for (int i = random.Next(3); i > 0; i--)
		{
			Vector3 at = centre + new Vector3(Range(-pitX + 3f, pitX - 3f), 0f, Range(-pitZ + 3f, pitZ - 3f));
			if (Mathf.Abs(alongX ? at.x - centre.x : at.z - centre.z) < bridge + 3f)
			{
				continue;
			}
			Lattice(at, 1.2f, bottom, Ceiling);
		}
		// a giant fan in an end wall with nothing coming through it, behind the ledge
		if (fanOut != Vector3.zero)
		{
			float wallHalf = (Mathf.Abs(fanOut.x) > 0.5f) ? halfZ : halfX;
			float radius = Mathf.Min(wallHalf - 2f, (Ceiling - bottom) * 0.5f - 1f);
			Vector3 hub = centre + fanOut * (((Mathf.Abs(fanOut.x) > 0.5f) ? halfX : halfZ) - 1.5f) + Vector3.up * ((Ceiling + bottom) * 0.5f);
			Fan(hub, -fanOut, radius);
		}
	}

	private static Vector3 Side(int side)
	{
		return (side == 0) ? Vector3.right : (side == 1) ? Vector3.left : (side == 2) ? Vector3.forward : Vector3.back;
	}

	private readonly List<Vector2> openings = new List<Vector2>();

	// a wall wall-thick on its middle line, half long either way along it, from y0 to y1, with openings (along it from
	// middle) from gapBottom up to gapTop
	private void UnderWall(Vector3 middle, Vector3 along, float half, float y0, float y1, float gapBottom, float gapTop)
	{
		float w = (float)UndergroundPlan.Wall * 0.5f;
		openings.Sort((a, b) => a.x.CompareTo(b.x));
		float at = -half;
		foreach (Vector2 gap in openings)
		{
			if (gap.x > at)
			{
				Box(middle + along * ((at + gap.x) * 0.5f), along, (gap.x - at) * 0.5f, w, y0, y1, concrete, concrete, false);
			}
			Vector3 under = middle + along * ((gap.x + gap.y) * 0.5f);
			if (gapBottom > y0 + 0.01f)
			{
				Box(under, along, (gap.y - gap.x) * 0.5f, w, y0, gapBottom, concrete, concrete, false);
			}
			if (gapTop < y1 - 0.01f)
			{
				Box(under, along, (gap.y - gap.x) * 0.5f, w, gapTop, y1, concrete, concrete, true);
			}
			at = Mathf.Max(at, gap.y);
		}
		if (half > at)
		{
			Box(middle + along * ((at + half) * 0.5f), along, (half - at) * 0.5f, w, y0, y1, concrete, concrete, false);
		}
	}

	// a steel railing at height y from a to b: a kick plate, a middle rail and a top rail on posts, solid enough to
	// stop the ket
	private void Railing(Vector3 a, Vector3 b, float y)
	{
		Vector3 d = b - a;
		float length = d.magnitude;
		if (length < 0.1f)
		{
			return;
		}
		Vector3 along = d / length;
		Vector3 middle = (a + b) * 0.5f;
		Box(middle, along, length * 0.5f, 0.05f, y, y + 0.25f, metal, metal, false);
		Box(middle, along, length * 0.5f, 0.05f, y + 0.5f, y + 0.58f, metal, metal, false);
		Box(middle, along, length * 0.5f, 0.06f, y + 1.0f, y + 1.08f, metal, metal, false);
		for (float t = 0f; t <= length + 0.01f; t += 2f)
		{
			Box(a + along * Mathf.Min(t, length), along, 0.05f, 0.05f, y, y + 1.0f, metal, metal, false);
		}
	}

	// a stretch of gallery from a to b (along x or z): floor, walls, the slab over it, a pipe or two along a wall
	public void AddGallery(Vector3 a, Vector3 b, ulong seed)
	{
		random = new WorldRandom(seed);
		float wall = (float)UndergroundPlan.Wall;
		float half = (float)UndergroundPlan.GalleryWidth * 0.5f;
		Vector3 along = (b - a).normalized;
		Vector3 v = Left(along);
		Vector3 middle = (a + b) * 0.5f;
		float length = (b - a).magnitude * 0.5f;
		Box(middle, along, length, half + wall, Level - 1f, Level, concrete, concrete, false);
		Box(middle, along, length, half + wall, Ceiling, -0.1f, concrete, concrete, true);
		Box(middle + v * (half + wall * 0.5f), along, length, wall * 0.5f, Level, Ceiling, concrete, concrete, false);
		Box(middle - v * (half + wall * 0.5f), along, length, wall * 0.5f, Level, Ceiling, concrete, concrete, false);
		float side = ((seed & 1uL) == 0uL) ? 1f : -1f;
		Tube(a + v * (side * (half - 0.7f)) + Vector3.up * (Ceiling - 0.9f), b + v * (side * (half - 0.7f)) + Vector3.up * (Ceiling - 0.9f), 0.45f, metal);
		if (Unit() < 0.5f)
		{
			Tube(a + v * (side * (half - 0.5f)) + Vector3.up * (Level + 0.5f), b + v * (side * (half - 0.5f)) + Vector3.up * (Level + 0.5f), 0.3f, metal);
		}
	}

	// a way in (UndergroundPlan): the passage out of the hall's wall, flat then up the ramp, walled and under the slab
	// as far as the hole; then the ramp house over the hole: walls, a roof, the doorway at the deck end.
	// point(s, across) is the passage's frame, s from the hall's wall
	public void AddWayIn(System.Func<float, float, Vector3> point, Vector3 out_)
	{
		float wall = (float)UndergroundPlan.Wall;
		float half = (float)UndergroundPlan.GalleryWidth * 0.5f;
		float hole = (float)UndergroundPlan.HoleFrom;
		float house = (float)UndergroundPlan.HouseFrom;
		float length = (float)UndergroundPlan.PassageLength;
		float tall = (float)UndergroundPlan.HouseHeight;
		Vector3 along = Left(out_);
		const float step = 8f;
		float s = 0f;
		while (s < length - 0.01f)
		{
			float e = Mathf.Min(s + step, length);
			// split where the slab stops
			if (s < hole && e > hole)
			{
				e = hole;
			}
			// flush with the deck's top at the doorway, which sits a little under the tile (TileStreamer), and with the
			// hall's ledge at the bottom, or the ket's thin wheels catch on the step
			float fa = Floor(s);
			float fb = Floor(e);
			Sloped(concrete, point(s, 0f), point(e, 0f), half + wall, fa - 1f, fa, fb - 1f, fb);
			// under the deck its roof, in the house up to the house's roof
			float ra = (s < hole) ? (float)UndergroundPlan.PassageRoof(s) : tall;
			float rb = (s < hole) ? (float)UndergroundPlan.PassageRoof(e) : tall;
			Sloped(concrete, point(s, half + wall * 0.5f), point(e, half + wall * 0.5f), wall * 0.5f, fa - 1f, ra, fb - 1f, rb);
			Sloped(concrete, point(s, -half - wall * 0.5f), point(e, -half - wall * 0.5f), wall * 0.5f, fa - 1f, ra, fb - 1f, rb);
			if (s < hole)
			{
				Sloped(concrete, point(s, 0f), point(e, 0f), half + wall, ra, -0.1f, rb, -0.1f);
				Tube(point(s, half - 0.6f) + Vector3.up * (ra - 0.6f), point(e, half - 0.6f) + Vector3.up * (rb - 0.6f), 0.3f, metal);
			}
			s = e;
		}
		// the house: its back end over the deck, the roof, the doorway's wall
		Sloped(concrete, point(house, 0f), point(hole, 0f), half + wall, -0.1f, 0f, -0.1f, 0f);
		Box(point(house + 0.5f, 0f), along, half + wall, 0.5f, 0f, tall, concrete, concrete, false);
		Sloped(concrete, point(house, half + wall * 0.5f), point(hole, half + wall * 0.5f), wall * 0.5f, -0.5f, tall, -0.5f, tall);
		Sloped(concrete, point(house, -half - wall * 0.5f), point(hole, -half - wall * 0.5f), wall * 0.5f, -0.5f, tall, -0.5f, tall);
		Box(point((house + length) * 0.5f, 0f), out_, (length - house) * 0.5f + 0.6f, half + wall + 0.6f, tall, tall + 0.8f, concrete, concrete, true);
		const float door = 6f;
		Box(point(length - wall * 0.5f, 0f), along, half + wall, wall * 0.5f, door, tall, concrete, concrete, true);
		// a vent and a pipe on the roof
		Box(point(house + 6f, half * 0.4f), out_, 1.6f, 1.6f, tall + 0.8f, tall + 2.6f, metal, metal, false);
		Tube(point(house + 2f, -half * 0.5f) + Vector3.up * (tall + 1.4f), point(length - 3f, -half * 0.5f) + Vector3.up * (tall + 1.4f), 0.4f, metal);
	}

	private static float Floor(float s)
	{
		float floor = (float)UndergroundPlan.PassageFloor(s);
		return floor - deckDrop * (floor - Level) / -Level;
	}

	// the round chamber at the foot of the start tunnel's way down (UndergroundPlan.Start): the tunnel comes in from
	// back, the gallery leaves along out_. a round room so any angle meets the grid. floor, slab, walls, pipes
	public void AddChamber(Vector3 centre, Vector3 back, Vector3 out_)
	{
		float r = (float)UndergroundPlan.ChamberRadius;
		float wall = (float)UndergroundPlan.Wall;
		float mouth = (float)UndergroundPlan.GalleryWidth * 0.5f + 0.6f;
		Box(centre, Vector3.right, r + wall, r + wall, Level - 1f, Level, concrete, concrete, false);
		Box(centre, Vector3.right, r + wall, r + wall, Ceiling, -0.1f, concrete, concrete, true);
		const int segments = 24;
		for (int i = 0; i < segments; i++)
		{
			float angle = (i + 0.5f) * Mathf.PI * 2f / segments;
			Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
			// left open where the tunnel and the gallery come in
			if (Opening(dir, back, r, mouth) || Opening(dir, out_, r, mouth))
			{
				continue;
			}
			Box(centre + dir * (r + wall * 0.5f), Left(dir), r * Mathf.PI / segments + 0.05f, wall * 0.5f, Level, Ceiling, concrete, concrete, false);
		}
		for (int i = 0; i < 3; i++)
		{
			float angle = i * 2.1f + 0.4f;
			Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
			if (Opening(dir, back, r, mouth + 1.5f) || Opening(dir, out_, r, mouth + 1.5f))
			{
				continue;
			}
			Vector3 at = centre + dir * (r - 1.2f);
			Tube(at + Vector3.up * Level, at + Vector3.up * Ceiling, 0.5f, metal);
		}
	}

	// a light fitting on an underground wall at at, facing out of it: a bracket, a steel housing with a guard over its
	// lamp (the light itself is the streamer's)
	public void AddFixture(Vector3 at, Vector3 facing)
	{
		Vector3 housing = at + facing * 0.6f - Vector3.up * 0.2f;
		Beam(at, housing + Vector3.up * 0.1f, 0.05f, metal);
		Box(at + facing * 0.05f, Left(facing), 0.2f, 0.05f, -0.25f, 0.25f, metal, metal, true);
		Box(housing, facing, 0.25f, 0.32f, -0.12f, 0.12f, metal, metal, true);
		Box(housing - Vector3.up * 0.2f, facing, 0.2f, 0.26f, -0.1f, 0f, dark, dark, true);
		for (int i = -1; i <= 1; i++)
		{
			Vector3 bar = housing + Left(facing) * (i * 0.15f);
			Beam(bar - facing * 0.22f - Vector3.up * 0.12f, bar + facing * 0.22f - Vector3.up * 0.12f, 0.015f, metal);
			Beam(bar - facing * 0.22f - Vector3.up * 0.12f, bar - facing * 0.22f - Vector3.up * 0.32f, 0.015f, metal);
			Beam(bar + facing * 0.22f - Vector3.up * 0.12f, bar + facing * 0.22f - Vector3.up * 0.32f, 0.015f, metal);
			Beam(bar - facing * 0.22f - Vector3.up * 0.32f, bar + facing * 0.22f - Vector3.up * 0.32f, 0.015f, metal);
		}
	}

	// whether the wall towards dir is in the mouth of an opening towards towards
	private static bool Opening(Vector3 dir, Vector3 towards, float radius, float mouth)
	{
		return Vector3.Dot(dir, towards) > 0f && Vector3.Cross(towards, dir).magnitude * radius < mouth;
	}

	// an open steel tower, square, legs half apart either way, x-braced
	private void Lattice(Vector3 at, float half, float y0, float y1)
	{
		Vector3[] legs = { new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(half, 0f, half), new Vector3(-half, 0f, half) };
		foreach (Vector3 leg in legs)
		{
			Beam(at + leg + Vector3.up * y0, at + leg + Vector3.up * y1, 0.12f, metal);
		}
		for (float y = y0; y + 4f <= y1; y += 4f)
		{
			for (int i = 0; i < 4; i++)
			{
				Vector3 a = at + legs[i];
				Vector3 b = at + legs[(i + 1) % 4];
				Beam(a + Vector3.up * y, b + Vector3.up * (y + 4f), 0.06f, metal);
				Beam(a + Vector3.up * (y + 4f), b + Vector3.up * y, 0.06f, metal);
				Beam(a + Vector3.up * (y + 4f), b + Vector3.up * (y + 4f), 0.06f, metal);
			}
		}
	}

	// a still ventilation fan in a wall, facing along facing (the reference shot of the ket on a bridge before one): a
	// deep ring it turns in, struts from the ring to the bearing behind, a hub, five broad blades that twist from steep
	// at the root to flat at the tip and sweep back the way they turn
	private void Fan(Vector3 hub, Vector3 facing, float radius)
	{
		Vector3 right = Left(facing);
		const int segments = 32;
		const float depth = 2.6f;
		const float rim = 0.9f;
		for (int i = 0; i < segments; i++)
		{
			float a0 = i * Mathf.PI * 2f / segments;
			float a1 = (i + 1) * Mathf.PI * 2f / segments;
			Vector3 d0 = right * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0);
			Vector3 d1 = right * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1);
			Vector3 front = facing * (depth * 0.5f);
			Vector3 i0 = hub + d0 * radius;
			Vector3 i1 = hub + d1 * radius;
			Vector3 o0 = hub + d0 * (radius + rim);
			Vector3 o1 = hub + d1 * (radius + rim);
			Quad(metal, i0 - front, i1 - front, i1 + front, i0 + front, -(d0 + d1).normalized);
			Quad(metal, o0 - front, o1 - front, o1 + front, o0 + front, (d0 + d1).normalized);
			Quad(metal, i0 + front, i1 + front, o1 + front, o0 + front, facing);
		}
		// the bearing behind, held by struts to the ring
		Vector3 bearing = hub - facing * 1.6f;
		Tube(bearing - facing * 0.8f, hub + facing * 0.9f, radius * 0.13f, metal);
		Tube(hub + facing * 0.9f, hub + facing * 1.2f, radius * 0.08f, metal);
		for (int i = 0; i < 3; i++)
		{
			float angle = (i * 120f + 30f) * Mathf.Deg2Rad;
			Vector3 d = right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
			Beam(bearing + d * (radius * 0.12f), bearing + d * radius - facing * 0.4f, 0.25f, metal);
		}
		float turn = Range(0f, 72f);
		const int sections = 5;
		for (int b = 0; b < 5; b++)
		{
			float theta = (turn + b * 72f) * Mathf.Deg2Rad;
			Vector3[] lead = new Vector3[sections + 1];
			Vector3[] trail = new Vector3[sections + 1];
			for (int k = 0; k <= sections; k++)
			{
				float t = (float)k / sections;
				float r = radius * Mathf.Lerp(0.12f, 0.93f, t);
				// swept back as it goes out
				float a = theta + t * 0.35f;
				Vector3 d = right * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
				Vector3 tangent = -right * Mathf.Sin(a) + Vector3.up * Mathf.Cos(a);
				float chord = radius * Mathf.Lerp(0.22f, 0.46f, t);
				float pitch = Mathf.Lerp(48f, 16f, t) * Mathf.Deg2Rad;
				Vector3 across = (tangent * Mathf.Cos(pitch) + facing * Mathf.Sin(pitch)) * (chord * 0.5f);
				Vector3 centre = hub + d * r;
				lead[k] = centre + across;
				trail[k] = centre - across;
			}
			for (int k = 0; k < sections; k++)
			{
				Vector3 normal = Vector3.Cross(lead[k + 1] - trail[k], trail[k + 1] - lead[k]).normalized;
				Quad(metal, trail[k], lead[k], lead[k + 1], trail[k + 1], normal);
				Quad(metal, trail[k], lead[k], lead[k + 1], trail[k + 1], -normal);
			}
		}
	}

	// a pipe from a to b, octagonal
	private void Tube(Vector3 a, Vector3 b, float radius, int submesh)
	{
		Vector3 d = b - a;
		if (d.sqrMagnitude < 1e-6f)
		{
			return;
		}
		d.Normalize();
		Vector3 s = Vector3.Cross(d, (Mathf.Abs(d.y) > 0.9f) ? Vector3.right : Vector3.up).normalized;
		Vector3 t = Vector3.Cross(d, s).normalized;
		const int sides = 8;
		for (int i = 0; i < sides; i++)
		{
			float a0 = i * Mathf.PI * 2f / sides;
			float a1 = (i + 1) * Mathf.PI * 2f / sides;
			Vector3 o0 = (s * Mathf.Cos(a0) + t * Mathf.Sin(a0)) * radius;
			Vector3 o1 = (s * Mathf.Cos(a1) + t * Mathf.Sin(a1)) * radius;
			Quad(submesh, a + o0, a + o1, b + o1, b + o0, (o0 + o1).normalized);
		}
	}

	// a viaduct pier standing on the ground under the deck, centred on at
	public void AddPier(Vector3 at, Vector3 along, float height)
	{
		float half = (float)ViaductPlan.PierWidth * 0.5f;
		float w = (float)ViaductPlan.Width * 0.5f;
		Box(at, along, half, w, -sink, height - deckThickness, concrete, concrete, false);
		// a plinth
		Box(at, along, half + 0.25f, w + 0.25f, -sink, 1f, concrete, concrete, false);
	}

	// one bay of viaduct deck from start along it, rising from hA to hB (its ramps), cornice and parapets, and under
	// it an arch between the piers, a girder where a pier's missing, or a wall where the ramp is still low
	public void AddBay(Vector3 start, Vector3 along, float hA, float hB, bool arch)
	{
		float bay = (float)ViaductPlan.Bay;
		float w = (float)ViaductPlan.Width * 0.5f;
		Vector3 v = Left(along);
		Vector3 end = start + along * bay;
		Sloped(concrete, start, end, w, hA - deckThickness, hA, hB - deckThickness, hB);
		Sloped(concrete, start, end, w + 0.35f, hA - deckThickness - 0.5f, hA - deckThickness, hB - deckThickness - 0.5f, hB - deckThickness);
		Sloped(concrete, start + v * (w - 0.25f), end + v * (w - 0.25f), 0.25f, hA, hA + 1.1f, hB, hB + 1.1f);
		Sloped(concrete, start - v * (w - 0.25f), end - v * (w - 0.25f), 0.25f, hA, hA + 1.1f, hB, hB + 1.1f);
		float underA = hA - deckThickness - 0.5f;
		float underB = hB - deckThickness - 0.5f;
		float pier = (float)ViaductPlan.PierWidth * 0.5f;
		int mode = (Mathf.Max(hA, hB) < 0.3f) ? RoadPlan.ArchNone : (Mathf.Min(hA, hB) < (float)RoadPlan.ArchFrom) ? RoadPlan.ArchWall : arch ? RoadPlan.ArchPier : RoadPlan.ArchGirder;
		AddUnder(start, end, underA, underB, w, mode, pier, pier);
	}

	// a raised street's pier at a, up to under the deck, half as wide as the street
	public void AddStreetPier(Vector3 at, Vector3 along, float deck, float half)
	{
		Box(at, along, 1f, half - 0.2f, -sink, deck - roadDeck, concrete, concrete, false);
	}

	// what stands under a raised street's piece from a to b, its deck at hA and hB (RoadPlan.Arch*)
	public void AddStreetUnder(Vector3 a, Vector3 b, float hA, float hB, float half, int mode)
	{
		AddUnder(a, b, hA - roadDeck, hB - roadDeck, half - 0.2f, mode, 1f, 1f);
	}

	// under a deck from a to b whose underside is at underA and underB: a wall down to the ground, a girder, or an
	// arch between piers inset from the ends
	private void AddUnder(Vector3 a, Vector3 b, float underA, float underB, float half, int mode, float insetA, float insetB)
	{
		Vector3 along = b - a;
		along.y = 0f;
		float length = along.magnitude;
		along /= Mathf.Max(length, 0.001f);
		Vector3 v = Left(along);
		switch (mode)
		{
			case RoadPlan.ArchWall:
				if (Mathf.Max(underA, underB) > 0.2f)
				{
					Sloped(concrete, a, b, half, -sink, Mathf.Max(underA, 0.1f), -sink, Mathf.Max(underB, 0.1f));
				}
				return;
			case RoadPlan.ArchGirder:
				Sloped(concrete, a, b, half * 0.8f, underA - 1.4f, underA, underB - 1.4f, underB);
				return;
			case RoadPlan.ArchPier:
			case RoadPlan.ArchPiers:
				break;
			default:
				return;
		}
		float from = insetA;
		float to = length - insetB;
		float radius = (to - from) * 0.5f;
		float low = Mathf.Min(underA, underB) - 0.8f;
		float spring = Mathf.Max(1f, low - radius);
		// a flatter arch where there isn't room for a round one
		float rise = Mathf.Max(0.5f, Mathf.Min(radius, low - spring));
		for (int i = 0; i < archSteps; i++)
		{
			float a0 = Mathf.PI * i / archSteps;
			float a1 = Mathf.PI * (i + 1) / archSteps;
			float s0 = from + radius * (1f - Mathf.Cos(a0));
			float s1 = from + radius * (1f - Mathf.Cos(a1));
			Vector3 p0 = a + along * s0 + Vector3.up * (spring + rise * Mathf.Sin(a0));
			Vector3 p1 = a + along * s1 + Vector3.up * (spring + rise * Mathf.Sin(a1));
			Vector3 q0 = new Vector3(p0.x, Mathf.Lerp(underA, underB, s0 / length), p0.z);
			Vector3 q1 = new Vector3(p1.x, Mathf.Lerp(underA, underB, s1 / length), p1.z);
			Quad(concrete, p0 + v * half, p1 + v * half, q1 + v * half, q0 + v * half, v);
			Quad(concrete, p0 - v * half, p1 - v * half, q1 - v * half, q0 - v * half, -v);
			Vector3 middle = a + along * (from + radius) + Vector3.up * spring;
			Quad(concrete, p0 + v * half, p1 + v * half, p1 - v * half, p0 - v * half, (middle - (p0 + p1) * 0.5f).normalized);
		}
	}

	// a slab from a to b (their y ignored), half wide, underside and top sloping from the a end to the b end
	private void Sloped(int submesh, Vector3 a, Vector3 b, float half, float bottomA, float topA, float bottomB, float topB)
	{
		if (Skipped(half * 2f, Mathf.Max(topA, topB)))
		{
			return;
		}
		Vector3 along = b - a;
		along.y = 0f;
		along.Normalize();
		Vector3 v = Left(along) * half;
		Vector3 a0 = new Vector3(a.x, 0f, a.z);
		Vector3 b0 = new Vector3(b.x, 0f, b.z);
		Vector3 aLB = a0 - v + Vector3.up * bottomA;
		Vector3 aRB = a0 + v + Vector3.up * bottomA;
		Vector3 aLT = a0 - v + Vector3.up * topA;
		Vector3 aRT = a0 + v + Vector3.up * topA;
		Vector3 bLB = b0 - v + Vector3.up * bottomB;
		Vector3 bRB = b0 + v + Vector3.up * bottomB;
		Vector3 bLT = b0 - v + Vector3.up * topB;
		Vector3 bRT = b0 + v + Vector3.up * topB;
		Vector3 side = v.normalized;
		Quad(submesh, aRB, bRB, bRT, aRT, side);
		Quad(submesh, aLB, bLB, bLT, aLT, -side);
		Quad(submesh, aLT, aRT, bRT, bLT, Vector3.up);
		Quad(submesh, aLB, aRB, bRB, bLB, Vector3.down);
		Quad(submesh, aLB, aRB, aRT, aLT, -along);
		Quad(submesh, bLB, bRB, bRT, bLT, along);
	}

	// ---- harvested pieces

	// a piece from the list that fits the lot either way round (scaled down to no less than minScale), turned to
	// the lot's axis, merged into the mesh. false if none of a few tries fits
	private bool Harvested(Mesh[] from, Vector3 centre, Vector3 u, float hu, float hv, int submesh, float minScale)
	{
		if (from == null || from.Length == 0)
		{
			return false;
		}
		int start = random.Next(from.Length);
		float lu = hu * 2f - 2f;
		float lv = hv * 2f - 2f;
		for (int k = 0; k < Mathf.Min(4, from.Length); k++)
		{
			Mesh mesh = from[(start + k) % from.Length];
			if (mesh == null)
			{
				continue;
			}
			Piece piece = Read(mesh);
			float straight = Mathf.Min(1f, Mathf.Min(lu / piece.Size.x, lv / piece.Size.z));
			float turned = Mathf.Min(1f, Mathf.Min(lu / piece.Size.z, lv / piece.Size.x));
			float scale = Mathf.Max(straight, turned);
			if (scale < minScale)
			{
				continue;
			}
			float yaw = Mathf.Atan2(u.z, u.x) * -Mathf.Rad2Deg + ((turned > straight) ? 90f : 0f) + ((Unit() < 0.5f) ? 180f : 0f);
			AddPiece(piece, Matrix4x4.TRS(centre, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale), submesh);
			return true;
		}
		return false;
	}

	private static Piece Read(Mesh mesh)
	{
		if (!pieces.TryGetValue(mesh, out Piece piece))
		{
			piece.Vertices = mesh.vertices;
			piece.Uvs = mesh.uv;
			piece.Size = mesh.bounds.size;
			int[] all = mesh.triangles;
			List<int> solid = new List<int>(all.Length);
			List<int> windows = new List<int>();
			bool uv = piece.Uvs != null && piece.Uvs.Length == piece.Vertices.Length;
			for (int i = 0; i < all.Length; i += 3)
			{
				bool window = uv && Marked(piece.Uvs[all[i]]) && Marked(piece.Uvs[all[i + 1]]) && Marked(piece.Uvs[all[i + 2]]);
				List<int> to = window ? windows : solid;
				to.Add(all[i]);
				to.Add(all[i + 1]);
				to.Add(all[i + 2]);
			}
			piece.Triangles = solid.ToArray();
			piece.Windows = windows.ToArray();
			pieces[mesh] = piece;
		}
		return piece;
	}

	private void AddPiece(Piece piece, Matrix4x4 place, int submesh)
	{
		int first = vertices.Count;
		bool uv = piece.Uvs != null && piece.Uvs.Length == piece.Vertices.Length;
		for (int i = 0; i < piece.Vertices.Length; i++)
		{
			vertices.Add(place.MultiplyPoint3x4(piece.Vertices[i]));
			uvs.Add(uv ? piece.Uvs[i] : Vector2.zero);
		}
		List<int> list = triangles[submesh];
		foreach (int index in piece.Triangles)
		{
			list.Add(first + index);
		}
		foreach (int index in piece.Windows)
		{
			triangles[dark].Add(first + index);
		}
	}

	private static bool Marked(Vector2 uv)
	{
		return uv.x < -0.99f && uv.x > -1.01f && uv.y < -0.99f && uv.y > -1.01f;
	}

	// ---- shapes

	// four walls and a top, a bottom too if it hangs in the air
	private void Box(Vector3 centre, Vector3 u, float hu, float hv, float y0, float y1, int sides, int top, bool bottom)
	{
		if (Skipped(Mathf.Min(Mathf.Min(hu, hv) * 2f, y1 - y0), y1))
		{
			return;
		}
		Vector3 v = Left(u);
		Vector3 lo = Vector3.up * y0;
		Vector3 hi = Vector3.up * y1;
		Vector3 c00 = centre - u * hu - v * hv;
		Vector3 c10 = centre + u * hu - v * hv;
		Vector3 c11 = centre + u * hu + v * hv;
		Vector3 c01 = centre - u * hu + v * hv;
		Quad(sides, c10 + lo, c11 + lo, c11 + hi, c10 + hi, u);
		Quad(sides, c01 + lo, c00 + lo, c00 + hi, c01 + hi, -u);
		Quad(sides, c11 + lo, c01 + lo, c01 + hi, c11 + hi, v);
		Quad(sides, c00 + lo, c10 + lo, c10 + hi, c00 + hi, -v);
		Quad(top, c00 + hi, c10 + hi, c11 + hi, c01 + hi, Vector3.up);
		if (bottom)
		{
			Quad(top, c00 + lo, c01 + lo, c11 + lo, c10 + lo, Vector3.down);
		}
	}

	// a box turned any way, half its size each way
	private void OrientedBox(Vector3 centre, Quaternion turn, Vector3 half, int submesh)
	{
		if (Skipped(Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 2f, centre.y + half.magnitude))
		{
			return;
		}
		Vector3 x = turn * Vector3.right * half.x;
		Vector3 y = turn * Vector3.up * half.y;
		Vector3 z = turn * Vector3.forward * half.z;
		for (int axis = 0; axis < 3; axis++)
		{
			Vector3 n = (axis == 0) ? x : (axis == 1) ? y : z;
			Vector3 a = (axis == 0) ? y : (axis == 1) ? z : x;
			Vector3 b = (axis == 0) ? z : (axis == 1) ? x : y;
			for (int side = -1; side <= 1; side += 2)
			{
				Vector3 c = centre + n * side;
				Quad(submesh, c - a - b, c + a - b, c + a + b, c - a + b, n * side);
			}
		}
	}

	// a square-section bar from a to b
	private void Beam(Vector3 a, Vector3 b, float half, int submesh)
	{
		Vector3 d = b - a;
		if (d.sqrMagnitude < 1e-6f || Skipped(half * 2f, Mathf.Max(a.y, b.y) + half))
		{
			return;
		}
		d.Normalize();
		Vector3 s = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized * half;
		Vector3 t = Vector3.Cross(d, s).normalized * half;
		Vector3 c0 = s + t;
		Vector3 c1 = -s + t;
		Vector3 c2 = -s - t;
		Vector3 c3 = s - t;
		Quad(submesh, a + c0, a + c1, b + c1, b + c0, (c0 + c1).normalized);
		Quad(submesh, a + c1, a + c2, b + c2, b + c1, (c1 + c2).normalized);
		Quad(submesh, a + c2, a + c3, b + c3, b + c2, (c2 + c3).normalized);
		Quad(submesh, a + c3, a + c0, b + c0, b + c3, (c3 + c0).normalized);
		Quad(submesh, a + c0, a + c1, a + c2, a + c3, -d);
		Quad(submesh, b + c0, b + c1, b + c2, b + c3, d);
	}

	// an upright many-sided cylinder, capped on top if asked
	private void Cylinder(Vector3 centre, float radius, float y0, float y1, int submesh, bool cap)
	{
		// a tank's bands
		if (Skipped(y1 - y0, y1))
		{
			return;
		}
		for (int i = 0; i < tankSides; i++)
		{
			float a0 = i * Mathf.PI * 2f / tankSides;
			float a1 = (i + 1) * Mathf.PI * 2f / tankSides;
			Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
			Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
			Vector3 p0 = centre + d0 * radius;
			Vector3 p1 = centre + d1 * radius;
			Quad(submesh, p0 + Vector3.up * y0, p1 + Vector3.up * y0, p1 + Vector3.up * y1, p0 + Vector3.up * y1, (d0 + d1).normalized);
			if (cap)
			{
				Triangle(submesh, centre + Vector3.up * y1, p0 + Vector3.up * y1, p1 + Vector3.up * y1, Vector3.up);
			}
		}
	}

	// ---- triangles

	// four corners in order round the face, wound to face outward whichever way round they came.
	// tops get planar uvs, walls run along the wall and up a floor per repeat
	private void Quad(int submesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
	{
		// far, the dark is windows, traces and bands on faces: the bands and bigger windows stay, they're much of how
		// dark a building reads from afar (without them far ones stood out whiter), the thin traces go
		if (Far && submesh == dark && Mathf.Min((p1 - p0).magnitude, (p3 - p0).magnitude) < darkKept)
		{
			return;
		}
		int first = vertices.Count;
		Add(p0, outward);
		Add(p1, outward);
		Add(p2, outward);
		Add(p3, outward);
		List<int> list = triangles[submesh];
		bool facing = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) > 0f;
		list.Add(first);
		list.Add(facing ? first + 1 : first + 2);
		list.Add(facing ? first + 2 : first + 1);
		list.Add(first);
		list.Add(facing ? first + 2 : first + 3);
		list.Add(facing ? first + 3 : first + 2);
	}

	private void Triangle(int submesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 outward)
	{
		int first = vertices.Count;
		Add(p0, outward);
		Add(p1, outward);
		Add(p2, outward);
		List<int> list = triangles[submesh];
		bool facing = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) > 0f;
		list.Add(first);
		list.Add(facing ? first + 1 : first + 2);
		list.Add(facing ? first + 2 : first + 1);
	}

	private void Add(Vector3 p, Vector3 outward)
	{
		vertices.Add(p);
		if (Mathf.Abs(outward.y) > 0.7f)
		{
			uvs.Add(new Vector2(p.x, p.z) / module);
		}
		else
		{
			Vector3 along = Vector3.Cross(Vector3.up, outward).normalized;
			uvs.Add(new Vector2(Vector3.Dot(p, along) / module, p.y / FloorHeight));
		}
	}
}
