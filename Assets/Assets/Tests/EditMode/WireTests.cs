using System;
using System.Reflection;
using NUnit.Framework;

// hard rule 2 with a guard: the message lists can't change without the connection key changing with them. when this
// fails, bump connectionKey in CoopSession, then copy the new values here
public class WireTests
{
	private const string key = "GLTPS1-coop-30";

	private const string kinds = "Nuko=1,Item=2,Clock=3,Weather=4,Flag=5,Line=6,Wake=7,Place=8,Camp=9,Lamp=10";

	private static string Names(Type type)
	{
		string text = "";
		foreach (object value in Enum.GetValues(type))
		{
			text += (text.Length > 0 ? "," : "") + value + "=" + Convert.ToInt32(value);
		}
		return text;
	}

	[Test]
	public void KindsNeedAKeyBump()
	{
		FieldInfo field = typeof(CoopSession).GetField("connectionKey", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.AreEqual(key, field.GetRawConstantValue(), "the key moved: update this test's lists with it");
		Assert.AreEqual(kinds, Names(typeof(NetRegistry.Kind)), "a net kind changed: bump connectionKey");
	}

	[Test]
	public void MessageTypesNeedAKeyBump()
	{
		ulong hash = 14695981039346656037uL;
		foreach (char c in Names(typeof(CoopSession.MessageType)))
		{
			hash = (hash ^ c) * 1099511628211uL;
		}
		Assert.AreEqual(MessageTypesHash, hash, "a message type changed: bump connectionKey");
	}

	private const ulong MessageTypesHash = 17007545304208184192uL;
}
