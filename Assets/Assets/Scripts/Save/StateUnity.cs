using UnityEngine;

// Unity's types in the state format, so Core needn't know them. same bytes as ever
public static class StateUnity
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void Init()
	{
		StateRegistry.Warn = Debug.LogWarning;
	}

	public static void Put(this StateWriter writer, Vector3 value)
	{
		writer.Put(value.x);
		writer.Put(value.y);
		writer.Put(value.z);
	}

	public static void Put(this StateWriter writer, Quaternion value)
	{
		writer.Put(value.x);
		writer.Put(value.y);
		writer.Put(value.z);
		writer.Put(value.w);
	}

	public static Vector3 GetVector3(this StateReader reader)
	{
		float x = reader.GetFloat();
		float y = reader.GetFloat();
		return new Vector3(x, y, reader.GetFloat());
	}

	public static Quaternion GetQuaternion(this StateReader reader)
	{
		float x = reader.GetFloat();
		float y = reader.GetFloat();
		float z = reader.GetFloat();
		return new Quaternion(x, y, z, reader.GetFloat());
	}
}
