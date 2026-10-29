using System.IO;
using System.Text;
using UnityEngine;

// binary, little endian. the same bytes go in the save file and to a joining friend
public class StateWriter
{
	private readonly MemoryStream stream = new MemoryStream();

	private readonly BinaryWriter writer;

	public StateWriter()
	{
		writer = new BinaryWriter(stream, Encoding.UTF8);
	}

	public void Put(bool value)
	{
		writer.Write(value);
	}

	public void Put(byte value)
	{
		writer.Write(value);
	}

	public void Put(int value)
	{
		writer.Write(value);
	}

	public void Put(float value)
	{
		writer.Write(value);
	}

	public void Put(ulong value)
	{
		writer.Write(value);
	}

	public void Put(string value)
	{
		writer.Write(value ?? "");
	}

	public void Put(Vector3 value)
	{
		writer.Write(value.x);
		writer.Write(value.y);
		writer.Write(value.z);
	}

	public void Put(Quaternion value)
	{
		writer.Write(value.x);
		writer.Write(value.y);
		writer.Write(value.z);
		writer.Write(value.w);
	}

	// length first
	public void Put(byte[] value)
	{
		value = value ?? new byte[0];
		writer.Write(value.Length);
		writer.Write(value);
	}

	public byte[] ToArray()
	{
		writer.Flush();
		return stream.ToArray();
	}
}
