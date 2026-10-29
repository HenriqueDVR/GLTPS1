using System.IO;
using System.Text;
using UnityEngine;

public class StateReader
{
	private readonly MemoryStream stream;

	private readonly BinaryReader reader;

	public StateReader(byte[] data)
	{
		stream = new MemoryStream(data ?? new byte[0]);
		reader = new BinaryReader(stream, Encoding.UTF8);
	}

	// fields added later are read only when there are bytes left, so an older section still reads
	public bool AtEnd => stream.Position >= stream.Length;

	public bool GetBool()
	{
		return reader.ReadBoolean();
	}

	public byte GetByte()
	{
		return reader.ReadByte();
	}

	public int GetInt()
	{
		return reader.ReadInt32();
	}

	public float GetFloat()
	{
		return reader.ReadSingle();
	}

	public ulong GetULong()
	{
		return reader.ReadUInt64();
	}

	public string GetString()
	{
		return reader.ReadString();
	}

	public byte[] GetBytes()
	{
		return reader.ReadBytes(reader.ReadInt32());
	}

	public Vector3 GetVector3()
	{
		return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
	}

	public Quaternion GetQuaternion()
	{
		return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
	}
}
