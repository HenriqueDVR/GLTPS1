using System;
using UnityEngine;

// what v2 tiles place besides roads and buildings (13-content-pipeline): small landmarks and props, from the game's
// own models. one asset, Resources/WorldKit.asset
public class WorldKit : ScriptableObject
{
	[Serializable]
	public class Landmark
	{
		public string Id;

		// a prefab, or a part of a model (the fuel station out of its node)
		public GameObject Model;

		// district ids it shows up in
		public string[] Districts;

		public float Weight = 1f;

		// optional, placed with it in the model's space: the fuel station's refuel trigger
		public GameObject Extra;

		public Vector3 ExtraPosition;

		public Quaternion ExtraRotation = Quaternion.identity;

		// optional, its renderers' slots in order: what its node prefab gave a part of a model, which comes bare
		public Material[] Materials;

		public bool Allows(string district)
		{
			return Districts != null && Array.IndexOf(Districts, district) >= 0;
		}
	}

	public Landmark[] Landmarks;

	// along streets
	public GameObject[] Lamps;

	public GameObject ItemSpawner;

	// pieces harvested out of the old node models (Models/Kit/Harvest, from Art/Harvest in Blender), merged into a
	// tile's lot mesh. pivot at the middle of the base, y up, metres
	public Mesh[] Buildings;

	public Mesh[] Tanks;

	public Mesh[] Props;

	public Mesh[] Pipes;

	public Mesh[] Panels;

	public Mesh[] Railings;

	public static WorldKit Load()
	{
		return Resources.Load<WorldKit>("WorldKit");
	}

	[NonSerialized]
	private ulong hash;

	// what tiles take from the kit (which landmark fits a lot, which piece a building is), for the co-op world check
	public ulong Hash()
	{
		if (hash != 0uL)
		{
			return hash;
		}
		ulong h = 14695981039346656037uL;
		foreach (Landmark landmark in Landmarks ?? Array.Empty<Landmark>())
		{
			h = LayerPlan.Mix(h, landmark.Id ?? "");
			h = LayerPlan.Mix(h, LayerPlan.Centimetres(landmark.Weight));
			foreach (string district in landmark.Districts ?? Array.Empty<string>())
			{
				h = LayerPlan.Mix(h, district);
			}
			if (landmark.Model != null)
			{
				Vector3 scale = landmark.Model.transform.lossyScale;
				h = LayerPlan.Mix(h, LayerPlan.Centimetres(scale.x + scale.y * 7f + scale.z * 13f));
				foreach (MeshFilter filter in landmark.Model.GetComponentsInChildren<MeshFilter>(true))
				{
					h = Mix(h, filter.sharedMesh);
				}
			}
		}
		foreach (Mesh[] meshes in new[] { Buildings, Tanks, Props })
		{
			foreach (Mesh mesh in meshes ?? Array.Empty<Mesh>())
			{
				h = Mix(h, mesh);
			}
		}
		h = LayerPlan.Mix(h, (Lamps != null) ? Lamps.Length : 0);
		h = LayerPlan.Mix(h, (ItemSpawner != null) ? ItemSpawner.name : "");
		hash = h;
		return h;
	}

	private static ulong Mix(ulong h, Mesh mesh)
	{
		if (mesh == null)
		{
			return LayerPlan.Mix(h, 0);
		}
		Vector3 size = mesh.bounds.size;
		h = LayerPlan.Mix(h, mesh.name);
		h = LayerPlan.Mix(h, mesh.vertexCount);
		h = LayerPlan.Mix(h, LayerPlan.Centimetres(size.x));
		h = LayerPlan.Mix(h, LayerPlan.Centimetres(size.y));
		return LayerPlan.Mix(h, LayerPlan.Centimetres(size.z));
	}
}
