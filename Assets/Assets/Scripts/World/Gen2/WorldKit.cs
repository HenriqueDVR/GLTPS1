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

	public static WorldKit Load()
	{
		return Resources.Load<WorldKit>("WorldKit");
	}
}
