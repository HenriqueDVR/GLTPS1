using System.Collections.Generic;
using UnityEngine;

// where they last camped on each layer: any roof with a lit fire (17, the slice's shelter), and where the ket stood then.
// layout coords. the host's or solo's: a friend's only told when it matters (WakeFlow). saved, read before the scene's up
public static class LastShelter
{
	public struct Spot
	{
		public Vector3 Girl;

		public Vector3 Ket;

		public Quaternion KetTurn;
	}

	private static readonly Dictionary<int, Spot> spots = new Dictionary<int, Spot>();

	public static void ResetLevel()
	{
		spots.Clear();
	}

	public static void Set(int layer, Spot spot)
	{
		spots[layer] = spot;
	}

	// the tunnel they set out from, unless a save already knows better
	public static void SetIfNone(int layer, Spot spot)
	{
		if (!spots.ContainsKey(layer))
		{
			spots[layer] = spot;
		}
	}

	public static bool Get(int layer, out Spot spot)
	{
		return spots.TryGetValue(layer, out spot);
	}

	public class State : IStateful
	{
		public const string SectionKey = "shelter";

		public string Key => SectionKey;

		public int Version => 1;

		public void Write(StateWriter writer)
		{
			writer.Put(spots.Count);
			foreach (KeyValuePair<int, Spot> entry in spots)
			{
				writer.Put(entry.Key);
				writer.Put(entry.Value.Girl);
				writer.Put(entry.Value.Ket);
				writer.Put(entry.Value.KetTurn);
			}
		}

		public void Read(StateReader reader, int version)
		{
			spots.Clear();
			int count = reader.GetInt();
			for (int i = 0; i < count; i++)
			{
				int layer = reader.GetInt();
				spots[layer] = new Spot { Girl = reader.GetVector3(), Ket = reader.GetVector3(), KetTurn = reader.GetQuaternion() };
			}
		}
	}
}
