using System.IO;
using System.Text;

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

	public void Put(double value)
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
