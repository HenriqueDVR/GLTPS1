using System.Collections.Generic;
using UnityEngine;

// world gen v2, layer 0's origin (episode 01): the girls start in a dark tunnel and drive out of it into the snow.
// a long concrete tunnel through a snowed over embankment, its portal in a retaining wall facing the fields.
// it replaces the legacy start node's walkway and lamps; the node itself stays for the scripts on it.
// built around where the girls stand in the scene, so it's the same on every peer
public static class OriginTunnel
{
	// tunnel inside, metres
	private const float width = 14f;

	private const float height = 9f;

	// from the girls to the portal, and from the girls back to the rubble at the dead end
	private const float ahead = 33f;

	private const float behind = 90f;

	// embankment: base and top half widths, top height, how far its slopes run past the tunnel's back
	private const float baseHalf = 60f;

	private const float topHalf = 26f;

	private const float rise = 20f;

	private const float backSlope = 40f;

	private const int concrete = 0;

	private const int snow = 1;

	private const int dark = 2;

	// the middle of the embankment on the ground, layout coords. v2's origin, everything else keeps clear of it
	public static Vector3 Centre(Vector3 girls, Quaternion facing)
	{
		return girls + facing * new Vector3(0f, 0f, ahead - Length() * 0.5f);
	}

	public static float Length()
	{
		return ahead + behind + backSlope;
	}

	// hides the node's walkway and its compound, builds the tunnel in their place. world coords
	public static GameObject Build(Node start, Vector3 girls, Quaternion facing)
	{
		int layer = 0;
		for (int i = 0; i < start.transform.childCount; i++)
		{
			Transform child = start.transform.GetChild(i);
			if (child.GetComponent<GenerateEdge>() != null)
			{
				continue;
			}
			Collider floor = child.GetComponentInChildren<Collider>(true);
			if (floor != null && !floor.isTrigger)
			{
				layer = floor.gameObject.layer;
			}
			child.gameObject.SetActive(false);
		}
		GameObject tunnel = new GameObject("origin tunnel", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
		tunnel.layer = layer;
		tunnel.transform.SetPositionAndRotation(girls, facing);
		Builder b = new Builder();
		b.Make();
		Mesh mesh = b.ToMesh();
		tunnel.GetComponent<MeshFilter>().sharedMesh = mesh;
		tunnel.GetComponent<MeshRenderer>().sharedMaterials = new[] { Load("Roads/Concrete"), Load("Ground/Snow"), Load("Buildings/Dark") };
		tunnel.GetComponent<MeshCollider>().sharedMesh = mesh;
		// one light still working over the girls, the rest of it is dark
		Light light = new GameObject("tunnel light").AddComponent<Light>();
		light.transform.SetParent(tunnel.transform, false);
		light.transform.localPosition = new Vector3(0f, height - 1.5f, -4f);
		light.type = LightType.Point;
		light.range = 22f;
		light.intensity = 1.2f;
		light.color = new Color(1f, 0.85f, 0.6f);
		light.shadows = LightShadows.None;
		Physics.SyncTransforms();
		return tunnel;
	}

	private static Material Load(string name)
	{
		return Resources.Load<Material>(name);
	}

	// tunnel-local: x across, y up, z out of the portal. the girls at the origin
	private sealed class Builder
	{
		private readonly List<Vector3> vertices = new List<Vector3>();

		private readonly List<Vector2> uvs = new List<Vector2>();

		private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };

		public void Make()
		{
			float w = width * 0.5f;
			float front = ahead;
			float back = -behind;
			float baseBack = back - backSlope;
			Vector3 axis = new Vector3(0f, height * 0.5f, 0f);
			// floor, walls, roof of the tunnel, facing in
			Quad(concrete, new Vector3(-w, 0.02f, back), new Vector3(-w, 0.02f, front), new Vector3(w, 0.02f, front), new Vector3(w, 0.02f, back), Vector3.up);
			Quad(concrete, new Vector3(-w, 0f, front), new Vector3(-w, 0f, back), new Vector3(-w, height, back), new Vector3(-w, height, front), Vector3.right);
			Quad(concrete, new Vector3(w, 0f, back), new Vector3(w, 0f, front), new Vector3(w, height, front), new Vector3(w, height, back), Vector3.left);
			Quad(dark, new Vector3(-w, height, back), new Vector3(w, height, back), new Vector3(w, height, front), new Vector3(-w, height, front), Vector3.down);
			// ribs across the roof and down the walls every 8 m
			for (float z = front - 4f; z > back + 2f; z -= 8f)
			{
				Box(new Vector3(0f, height - 0.6f, z), new Vector3(width, 1.2f, 1f), concrete);
				Box(new Vector3(-w + 0.4f, height * 0.5f, z), new Vector3(0.8f, height, 1f), concrete);
				Box(new Vector3(w - 0.4f, height * 0.5f, z), new Vector3(0.8f, height, 1f), concrete);
			}
			// a pipe run along one wall, a cable tray along the other
			Box(new Vector3(-w + 1.2f, height - 2.2f, (front + back) * 0.5f), new Vector3(0.8f, 0.8f, front - back - 2f), dark);
			Box(new Vector3(-w + 1.2f, height - 3.4f, (front + back) * 0.5f), new Vector3(0.6f, 0.6f, front - back - 2f), dark);
			Box(new Vector3(w - 0.9f, height - 2.6f, (front + back) * 0.5f), new Vector3(0.6f, 0.25f, front - back - 2f), dark);
			// the dead end: the roof came down
			Quad(dark, new Vector3(-w, 0f, back), new Vector3(w, 0f, back), new Vector3(w, height, back), new Vector3(-w, height, back), Vector3.forward);
			WorldRandom random = new WorldRandom(0x54554E4E454CuL);
			for (int i = 0; i < 14; i++)
			{
				float x = Range(random, -w + 1.5f, w - 1.5f);
				float z = back + Range(random, 1f, 9f);
				float s = Range(random, 1.5f, 4f);
				Box(new Vector3(x, s * 0.35f, z), new Vector3(s, s * 0.7f, s * 0.8f), concrete);
			}
			// the retaining wall the portal is in: the embankment's end, cut straight, the portal a hole in it
			float hole = baseHalf - (baseHalf - topHalf) * height / rise;
			Quad(concrete, new Vector3(-baseHalf, 0f, front), new Vector3(-w, 0f, front), new Vector3(-w, height, front), new Vector3(-hole, height, front), Vector3.forward);
			Quad(concrete, new Vector3(w, 0f, front), new Vector3(baseHalf, 0f, front), new Vector3(hole, height, front), new Vector3(w, height, front), Vector3.forward);
			Quad(concrete, new Vector3(-hole, height, front), new Vector3(hole, height, front), new Vector3(topHalf, rise, front), new Vector3(-topHalf, rise, front), Vector3.forward);
			// the portal's frame standing out of it
			Box(new Vector3(-w - 1.2f, height * 0.5f + 0.6f, front + 0.8f), new Vector3(2.4f, height + 1.2f, 1.6f), concrete);
			Box(new Vector3(w + 1.2f, height * 0.5f + 0.6f, front + 0.8f), new Vector3(2.4f, height + 1.2f, 1.6f), concrete);
			Box(new Vector3(0f, height + 1.2f, front + 0.8f), new Vector3(width + 4.8f, 2.4f, 1.6f), concrete);
			// raised panels on the wall, the source's monolith look
			for (int i = 0; i < 10; i++)
			{
				float side = (i % 2 == 0) ? 1f : -1f;
				bool low = i < 6;
				float x = side * (low ? Range(random, w + 6f, hole - 6f) : Range(random, 2f, topHalf - 6f));
				float y = low ? Range(random, 2f, height - 2f) : Range(random, height + 4f, rise - 3f);
				Box(new Vector3(x, y, front + 0.7f), new Vector3(Range(random, 3f, 9f), Range(random, 2f, 5f), 1.4f), concrete);
			}
			// a parapet along the wall's top
			Box(new Vector3(0f, rise + 0.5f, front - 0.4f), new Vector3(topHalf * 2f, 1f, 0.8f), concrete);
			// the embankment: snow over the top and down to the ground on three sides
			Vector3 t0 = new Vector3(-topHalf, rise, front);
			Vector3 t1 = new Vector3(topHalf, rise, front);
			Vector3 t2 = new Vector3(topHalf, rise, back);
			Vector3 t3 = new Vector3(-topHalf, rise, back);
			Vector3 b0 = new Vector3(-baseHalf, 0f, front);
			Vector3 b1 = new Vector3(baseHalf, 0f, front);
			Vector3 b2 = new Vector3(baseHalf, 0f, baseBack);
			Vector3 b3 = new Vector3(-baseHalf, 0f, baseBack);
			Vector3 inside = new Vector3(0f, 0f, (front + baseBack) * 0.5f);
			Quad(snow, t0, t1, t2, t3, Vector3.up);
			Tri(snow, b1, t1, t2, Vector3.right);
			Tri(snow, b1, t2, b2, Vector3.right);
			Tri(snow, b0, t0, t3, Vector3.left);
			Tri(snow, b0, t3, b3, Vector3.left);
			Quad(snow, t2, t3, b3, b2, Vector3.back + Vector3.up);
		}

		private static float Range(WorldRandom random, float a, float b)
		{
			return a + (float)Gen2.Unit(random.NextULong()) * (b - a);
		}

		// a closed box, axis aligned in tunnel space
		private void Box(Vector3 c, Vector3 s, int submesh)
		{
			Vector3 h = s * 0.5f;
			for (int axis = 0; axis < 3; axis++)
			{
				for (int sign = -1; sign <= 1; sign += 2)
				{
					Vector3 n = Vector3.zero;
					n[axis] = sign;
					Vector3 u = Vector3.zero;
					Vector3 v = Vector3.zero;
					u[(axis + 1) % 3] = h[(axis + 1) % 3];
					v[(axis + 2) % 3] = h[(axis + 2) % 3];
					Vector3 m = c + Vector3.Scale(n, h);
					Quad(submesh, m - u - v, m + u - v, m + u + v, m - u + v, n);
				}
			}
		}

		private void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
		{
			Tri(submesh, a, b, c, facing);
			Tri(submesh, a, c, d, facing);
		}

		// wound so it faces the way asked
		private void Tri(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
		{
			Vector3 n = Vector3.Cross(b - a, c - a);
			if (n.sqrMagnitude < 1e-6f)
			{
				return;
			}
			if (Vector3.Dot(n, facing) < 0f)
			{
				Vector3 swap = b;
				b = c;
				c = swap;
				n = -n;
			}
			n.Normalize();
			int first = vertices.Count;
			foreach (Vector3 p in new[] { a, b, c })
			{
				vertices.Add(p);
				// planar by the face's main axis, 4 m a repeat
				Vector2 uv = (Mathf.Abs(n.y) > 0.6f) ? new Vector2(p.x, p.z) : (Mathf.Abs(n.x) > Mathf.Abs(n.z)) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
				uvs.Add(uv * 0.25f);
			}
			triangles[submesh].Add(first);
			triangles[submesh].Add(first + 1);
			triangles[submesh].Add(first + 2);
		}

		public Mesh ToMesh()
		{
			Mesh mesh = new Mesh { name = "origin tunnel" };
			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.subMeshCount = triangles.Length;
			for (int i = 0; i < triangles.Length; i++)
			{
				mesh.SetTriangles(triangles[i], i);
			}
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
