using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// every model's faces that could be seen from behind get a back (Core/MeshSides), so nothing shows its inside from one
// side while hiding the outside from the other. skinned models and meshes with blend shapes are left as they are (the
// girls), and so are submeshes whose material isn't opaque or already draws both sides
public class MeshSidesImport : AssetPostprocessor
{
	// a new number reimports every model
	public override uint GetVersion()
	{
		return 4;
	}

	private void OnPostprocessModel(GameObject root)
	{
		if (!assetPath.StartsWith("Assets/") || assetPath.Contains("/Plugins/") || root.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
		{
			return;
		}
		HashSet<Mesh> done = new HashSet<Mesh>();
		int added = 0;
		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
		foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
		{
			Mesh mesh = filter.sharedMesh;
			if (mesh == null || mesh.blendShapeCount > 0 || !done.Add(mesh))
			{
				continue;
			}
			MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
			added += Seal(mesh, (renderer != null) ? renderer.sharedMaterials : null);
		}
		if (added > 0)
		{
			Debug.Log("MeshSides: " + assetPath + " got " + added + " back faces in " + clock.ElapsedMilliseconds + " ms");
		}
	}

	private static bool Skipped(Material material)
	{
		return material != null && (material.renderQueue >= (int)RenderQueue.AlphaTest || (material.HasProperty("_Cull") && material.GetFloat("_Cull") < 0.5f));
	}

	// the backs as new triangles on copies of their corners, normals turned round; returns how many
	public static int Seal(Mesh mesh, Material[] materials)
	{
		Vector3[] vertices = mesh.vertices;
		float[] positions = new float[vertices.Length * 3];
		for (int i = 0; i < vertices.Length; i++)
		{
			positions[i * 3] = vertices[i].x;
			positions[i * 3 + 1] = vertices[i].y;
			positions[i * 3 + 2] = vertices[i].z;
		}
		List<int> all = new List<int>();
		List<int> from = new List<int>();
		int[][] submeshes = new int[mesh.subMeshCount][];
		for (int s = 0; s < mesh.subMeshCount; s++)
		{
			if (mesh.GetTopology(s) != MeshTopology.Triangles || (materials != null && s < materials.Length && Skipped(materials[s])))
			{
				continue;
			}
			submeshes[s] = mesh.GetTriangles(s);
			all.AddRange(submeshes[s]);
			for (int t = 0; t < submeshes[s].Length / 3; t++)
			{
				from.Add(s);
			}
		}
		bool[] backs = MeshSides.Backs(positions, all.ToArray());
		int added = 0;
		foreach (bool b in backs)
		{
			added += b ? 1 : 0;
		}
		if (added == 0)
		{
			return 0;
		}
		// a back's corner per front corner it uses
		Dictionary<int, int> twin = new Dictionary<int, int>();
		List<int> copied = new List<int>();
		List<int>[] lists = new List<int>[mesh.subMeshCount];
		for (int s = 0; s < lists.Length; s++)
		{
			lists[s] = (submeshes[s] != null) ? new List<int>(submeshes[s]) : null;
		}
		bool[] changed = new bool[lists.Length];
		for (int t = 0; t < backs.Length; t++)
		{
			if (!backs[t])
			{
				continue;
			}
			int[] corners = new int[3];
			for (int k = 0; k < 3; k++)
			{
				int v = all[t * 3 + k];
				if (!twin.TryGetValue(v, out int c))
				{
					c = vertices.Length + copied.Count;
					twin[v] = c;
					copied.Add(v);
				}
				corners[k] = c;
			}
			List<int> list = lists[from[t]];
			changed[from[t]] = true;
			list.Add(corners[0]);
			list.Add(corners[2]);
			list.Add(corners[1]);
		}
		int total = vertices.Length + copied.Count;
		if (total > 65535)
		{
			mesh.indexFormat = IndexFormat.UInt32;
		}
		// every channel read before the vertex count changes: after it, unity hands them back padded
		Vector3[] normals = mesh.normals;
		Vector4[] tangents = mesh.tangents;
		Color[] colors = mesh.colors;
		List<Vector4>[] uvs = new List<Vector4>[8];
		int[] dimensions = new int[8];
		for (int channel = 0; channel < 8; channel++)
		{
			VertexAttribute attribute = VertexAttribute.TexCoord0 + channel;
			if (mesh.HasVertexAttribute(attribute))
			{
				uvs[channel] = new List<Vector4>();
				mesh.GetUVs(channel, uvs[channel]);
				dimensions[channel] = mesh.GetVertexAttributeDimension(attribute);
			}
		}
		List<Vector3> v3 = new List<Vector3>(vertices);
		foreach (int v in copied)
		{
			v3.Add(vertices[v]);
		}
		mesh.SetVertices(v3);
		if (normals.Length == vertices.Length)
		{
			List<Vector3> n3 = new List<Vector3>(normals);
			foreach (int v in copied)
			{
				n3.Add(-normals[v]);
			}
			mesh.SetNormals(n3);
		}
		if (tangents.Length == vertices.Length)
		{
			List<Vector4> t4 = new List<Vector4>(tangents);
			foreach (int v in copied)
			{
				Vector4 t = tangents[v];
				t4.Add(new Vector4(t.x, t.y, t.z, -t.w));
			}
			mesh.SetTangents(t4);
		}
		if (colors.Length == vertices.Length)
		{
			List<Color> c4 = new List<Color>(colors);
			foreach (int v in copied)
			{
				c4.Add(colors[v]);
			}
			mesh.SetColors(c4);
		}
		for (int channel = 0; channel < 8; channel++)
		{
			List<Vector4> list = uvs[channel];
			if (list == null || list.Count != vertices.Length)
			{
				continue;
			}
			foreach (int v in copied)
			{
				list.Add(list[v]);
			}
			if (dimensions[channel] == 2)
			{
				List<Vector2> uv2 = new List<Vector2>(list.Count);
				foreach (Vector4 uv in list)
				{
					uv2.Add(uv);
				}
				mesh.SetUVs(channel, uv2);
			}
			else if (dimensions[channel] == 3)
			{
				List<Vector3> uv3 = new List<Vector3>(list.Count);
				foreach (Vector4 uv in list)
				{
					uv3.Add(uv);
				}
				mesh.SetUVs(channel, uv3);
			}
			else
			{
				mesh.SetUVs(channel, list);
			}
		}
		for (int s = 0; s < lists.Length; s++)
		{
			if (changed[s])
			{
				mesh.SetTriangles(lists[s], s, false);
			}
		}
		mesh.RecalculateBounds();
		return added;
	}
}
