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
}
