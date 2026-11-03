using System.Collections.Generic;
using LiteNetLib.Utils;
using NUnit.Framework;
using UnityEngine;

public class StateTests
{
	private class Counter : IStateful
	{
		public string key;

		public int version = 1;

		public int value;

		public int readVersion = -1;

		public string Key => key;

		public int Version => version;

		public void Write(StateWriter writer)
		{
			writer.Put(value);
		}

		public void Read(StateReader reader, int version)
		{
			value = reader.GetInt();
			readVersion = version;
		}
	}

	private readonly List<Counter> made = new List<Counter>();

	private Counter Make(string key, int value = 0)
	{
		Counter counter = new Counter { key = "test." + key, value = value };
		made.Add(counter);
		return counter;
	}

	[TearDown]
	public void TearDown()
	{
		foreach (Counter counter in made)
		{
			StateRegistry.Unregister(counter);
		}
		made.Clear();
		StateRegistry.ClearPending();
	}

	private static StateSection Section(string key, int version, int value)
	{
		StateWriter writer = new StateWriter();
		writer.Put(value);
		return new StateSection { Key = "test." + key, Version = version, Data = writer.ToArray() };
	}

	[Test]
	public void WriterReaderRoundTrip()
	{
		StateWriter writer = new StateWriter();
		writer.Put(true);
		writer.Put((byte)7);
		writer.Put(-12345);
		writer.Put(1.5f);
		writer.Put(0xFEDCBA9876543210uL);
		writer.Put("chito");
		writer.Put(new Vector3(1f, 2f, 3f));
		writer.Put(new Quaternion(0.1f, 0.2f, 0.3f, 0.4f));
		writer.Put(new byte[] { 1, 2, 3 });
		StateReader reader = new StateReader(writer.ToArray());
		Assert.IsTrue(reader.GetBool());
		Assert.AreEqual(7, reader.GetByte());
		Assert.AreEqual(-12345, reader.GetInt());
		Assert.AreEqual(1.5f, reader.GetFloat());
		Assert.AreEqual(0xFEDCBA9876543210uL, reader.GetULong());
		Assert.AreEqual("chito", reader.GetString());
		Assert.AreEqual(new Vector3(1f, 2f, 3f), reader.GetVector3());
		Assert.AreEqual(new Quaternion(0.1f, 0.2f, 0.3f, 0.4f), reader.GetQuaternion());
		CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, reader.GetBytes());
		Assert.IsTrue(reader.AtEnd);
	}

	[Test]
	public void RegisteredSystemReadsAtOnce()
	{
		Counter counter = Make("now");
		StateRegistry.Register(counter);
		StateRegistry.SetPending(new[] { Section("now", 1, 42) });
		Assert.AreEqual(42, counter.value);
		Assert.AreEqual(1, counter.readVersion);
		Assert.IsFalse(StateRegistry.HasPending(counter.Key));
	}

	// the world's sections come up a few frames after a save is loaded
	[Test]
	public void PendingWaitsForRegister()
	{
		StateRegistry.SetPending(new[] { Section("later", 1, 7) });
		Counter counter = Make("later");
		Assert.AreEqual(0, counter.value);
		Assert.IsTrue(StateRegistry.HasPending(counter.Key));
		StateRegistry.Register(counter);
		Assert.AreEqual(7, counter.value);
		Assert.IsFalse(StateRegistry.HasPending(counter.Key));
	}

	[Test]
	public void OlderSectionGetsItsVersion()
	{
		Counter counter = Make("old");
		counter.version = 3;
		StateRegistry.Register(counter);
		StateRegistry.SetPending(new[] { Section("old", 1, 5) });
		Assert.AreEqual(1, counter.readVersion);
	}

	[Test]
	public void NewerSectionIsSkipped()
	{
		Counter counter = Make("newer", 9);
		StateRegistry.Register(counter);
		UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("newer version"));
		StateRegistry.SetPending(new[] { Section("newer", 2, 5) });
		Assert.AreEqual(9, counter.value);
	}

	[Test]
	public void CaptureFiltersByUse()
	{
		StateRegistry.Register(Make("save", 1), StateUse.Save);
		StateRegistry.Register(Make("join", 2), StateUse.Join);
		StateRegistry.Register(Make("both", 3), StateUse.Both);
		List<string> saved = Keys(StateRegistry.Capture(StateUse.Save));
		List<string> joined = Keys(StateRegistry.Capture(StateUse.Join));
		CollectionAssert.Contains(saved, "test.save");
		CollectionAssert.Contains(saved, "test.both");
		CollectionAssert.DoesNotContain(saved, "test.join");
		CollectionAssert.Contains(joined, "test.join");
		CollectionAssert.Contains(joined, "test.both");
		CollectionAssert.DoesNotContain(joined, "test.save");
	}

	// a section nobody read this run still goes back in the save, not in the join snapshot
	[Test]
	public void UnreadSectionKeptInSave()
	{
		StateRegistry.SetPending(new[] { Section("unread", 1, 3) });
		CollectionAssert.Contains(Keys(StateRegistry.Capture(StateUse.Save)), "test.unread");
		CollectionAssert.DoesNotContain(Keys(StateRegistry.Capture(StateUse.Join)), "test.unread");
	}

	private class Broken : IStateful
	{
		public string Key => "test.broken";

		public int Version => 1;

		public void Write(StateWriter writer)
		{
			throw new System.InvalidOperationException("not ready");
		}

		public void Read(StateReader reader, int version)
		{
		}
	}

	// the rejoin section threw on a fresh game and took the whole join down
	[Test]
	public void BrokenSectionIsLeftOut()
	{
		Broken broken = new Broken();
		StateRegistry.Register(broken, StateUse.Join);
		StateRegistry.Register(Make("fine", 4), StateUse.Join);
		UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("couldn't write test.broken"));
		List<string> keys = Keys(StateRegistry.Capture(StateUse.Join));
		StateRegistry.Unregister(broken);
		CollectionAssert.Contains(keys, "test.fine");
		CollectionAssert.DoesNotContain(keys, "test.broken");
	}

	[Test]
	public void PackUnpackRoundTrip()
	{
		List<StateSection> sections = new List<StateSection> { Section("a", 1, 10), Section("b", 4, -3) };
		List<StateSection> back = StateRegistry.Unpack(StateRegistry.Pack(sections));
		Assert.AreEqual(2, back.Count);
		Assert.AreEqual("test.b", back[1].Key);
		Assert.AreEqual(4, back[1].Version);
		Assert.AreEqual(-3, new StateReader(back[1].Data).GetInt());
	}

	private static List<string> Keys(List<StateSection> sections)
	{
		return sections.ConvertAll(s => s.Key);
	}

	// a slot written by 0.1.x (version 1), cut down
	private const string v1Save = "{\"version\":1,\"savedAt\":\"29 Oct 12:00\",\"playSeconds\":600.5,\"coop\":true,\"seed\":\"12345\",\"difficulty\":\"easy\","
		+ "\"chito\":{\"x\":1.0,\"y\":2.0,\"z\":3.0,\"yaw\":90.0,\"hunger\":80.0,\"thirst\":70.0,\"temperature\":60.0,\"hasGun\":false,\"magazine\":0,\"spare\":0},"
		+ "\"yuuri\":{\"x\":4.0,\"y\":5.0,\"z\":6.0,\"yaw\":180.0,\"hunger\":50.0,\"thirst\":40.0,\"temperature\":30.0,\"hasGun\":true,\"magazine\":3,\"spare\":12},"
		+ "\"food\":4,\"water\":120.0,\"fuel\":33.5,\"hasMap\":true,\"hasCamera\":false,"
		+ "\"vehicle\":[10.0,0.5,-20.0,0.0,0.7071068,0.0,0.7071068],\"barrelMounted\":false,\"hasLooseBarrel\":true,\"barrel\":[11.0,0.0,-21.0],"
		+ "\"takenItems\":[\"18446744073709551615\",\"42\"],\"pipeKeys\":[\"99\"],\"pipeLevels\":[0.25]}";

	[Test]
	public void ImportsVersion1Save()
	{
		GameSave.SaveData data = GameSave.Parse(v1Save);
		Assert.AreEqual(GameSave.CurrentVersion, data.version);
		Assert.AreEqual(1, data.genVersion);
		Assert.AreEqual("12345", data.seed);
		Assert.AreEqual("easy", data.difficulty);
		Assert.IsTrue(data.coop);
		Assert.AreEqual(600.5f, data.playSeconds);
		Dictionary<string, StateSection> sections = new Dictionary<string, StateSection>();
		foreach (StateSection section in GameSave.ToStateSections(data.sections))
		{
			sections[section.Key] = section;
		}

		GirlsSection.ReadData(new StateReader(sections["girls"].Data), out SavedGirl chito, out SavedGirl yuuri);
		Assert.AreEqual(new Vector3(1f, 2f, 3f), chito.Position);
		Assert.AreEqual(90f, chito.yaw);
		Assert.AreEqual(80f, chito.hunger);
		Assert.AreEqual(new Vector3(4f, 5f, 6f), yuuri.Position);
		Assert.IsTrue(yuuri.hasGun);
		Assert.AreEqual(3, yuuri.magazine);
		Assert.AreEqual(12, yuuri.spare);
		Assert.AreEqual(30f, yuuri.temperature);

		SuppliesSection supplies = new SuppliesSection();
		supplies.ReadData(new StateReader(sections["supplies"].Data));
		Assert.AreEqual(4, supplies.Food);
		Assert.AreEqual(120f, supplies.Water);
		Assert.AreEqual(33.5f, supplies.Fuel);
		Assert.IsTrue(supplies.HasMap);
		Assert.IsFalse(supplies.HasCamera);

		KetSection ket = new KetSection();
		ket.ReadData(new StateReader(sections["ket"].Data));
		Assert.IsTrue(ket.HasVehicle);
		Assert.AreEqual(new Vector3(10f, 0.5f, -20f), ket.VehiclePosition);
		Assert.AreEqual(0.7071068f, ket.VehicleRotation.y, 1e-6f);
		Assert.IsFalse(ket.BarrelMounted);
		Assert.IsTrue(ket.HasLooseBarrel);
		Assert.AreEqual(new Vector3(11f, 0f, -21f), ket.Barrel);

		CollectionAssert.AreEquivalent(new[] { ulong.MaxValue, 42uL }, WorldFlags.State.ReadData(new StateReader(sections["items"].Data)));
		Dictionary<ulong, float> pipes = CoopPipes.State.ReadData(new StateReader(sections["pipes"].Data));
		Assert.AreEqual(1, pipes.Count);
		Assert.AreEqual(0.25f, pipes[99uL]);
	}

	// a slot from before the ket or barrel were saved
	[Test]
	public void ImportsVersion1WithoutKet()
	{
		GameSave.SaveData data = GameSave.Parse("{\"version\":1,\"seed\":\"1\",\"chito\":{\"x\":1.0},\"yuuri\":{\"x\":2.0}}");
		foreach (StateSection section in GameSave.ToStateSections(data.sections))
		{
			if (section.Key == "ket")
			{
				KetSection ket = new KetSection();
				ket.ReadData(new StateReader(section.Data));
				Assert.IsFalse(ket.HasVehicle);
				Assert.IsFalse(ket.HasLooseBarrel);
			}
		}
	}

	[Test]
	public void NewerSaveIsRefused()
	{
		UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("newer version"));
		Assert.IsNull(GameSave.Parse("{\"version\":99,\"seed\":\"1\"}"));
	}

	[Test]
	public void Version2RoundTrip()
	{
		GameSave.SaveData data = new GameSave.SaveData { version = GameSave.CurrentVersion, seed = "5", savedAt = "now" };
		data.sections.Add(new GameSave.SavedSection { key = "test.x", version = 2, data = System.Convert.ToBase64String(new byte[] { 9, 8 }) });
		GameSave.SaveData back = GameSave.Parse(JsonUtility.ToJson(data));
		Assert.AreEqual(1, back.sections.Count);
		CollectionAssert.AreEqual(new byte[] { 9, 8 }, GameSave.ToStateSections(back.sections)[0].Data);
	}

	// v2 files had no generator: all of them are the legacy world
	[Test]
	public void Version2SaveIsLegacyWorld()
	{
		GameSave.SaveData data = GameSave.Parse("{\"version\":2,\"seed\":\"7\",\"sections\":[]}");
		Assert.AreEqual(GameSave.CurrentVersion, data.version);
		Assert.AreEqual(1, data.genVersion);
	}

	[Test]
	public void Version3KeepsGenerator()
	{
		GameSave.SaveData data = new GameSave.SaveData { version = GameSave.CurrentVersion, seed = "5", genVersion = 2 };
		Assert.AreEqual(2, GameSave.Parse(JsonUtility.ToJson(data)).genVersion);
	}

	[Test]
	public void LayersRoundTrip()
	{
		XZ[] origins = { new XZ(1234.5, -987.25), new XZ(-0.125, 4096.0) };
		StateWriter writer = new StateWriter();
		LayersSection.WriteData(writer, 2, origins);
		LayersSection.ReadData(new StateReader(writer.ToArray()), out int layer, out XZ[] back);
		Assert.AreEqual(2, layer);
		CollectionAssert.AreEqual(origins, back);
	}

	// a loaded save's layers are what the world gets built from, like a co-op friend's Start
	[Test]
	public void LayersReadRequestsTheWorld()
	{
		try
		{
			StateWriter writer = new StateWriter();
			LayersSection.WriteData(writer, 1, new[] { new XZ(10.0, 20.0) });
			new LayersSection().Read(new StateReader(writer.ToArray()), 1);
			Assert.AreEqual(1, WorldGenerator.RequestedLayer);
			CollectionAssert.AreEqual(new[] { new XZ(10.0, 20.0) }, WorldGenerator.RequestedOrigins);
			writer = new StateWriter();
			LayersSection.WriteData(writer, 0, new XZ[0]);
			new LayersSection().Read(new StateReader(writer.ToArray()), 1);
			Assert.AreEqual(0, WorldGenerator.RequestedLayer);
			Assert.IsNull(WorldGenerator.RequestedOrigins);
		}
		finally
		{
			WorldGenerator.ClearRequested();
		}
	}

	[Test]
	public void NetRegistryRoutesByKind()
	{
		ulong gotId = 0uL;
		int gotValue = 0;
		NetRegistry.Register(NetRegistry.Kind.Nuko, delegate(ulong netId, NetDataReader reader)
		{
			gotId = netId;
			gotValue = reader.GetInt();
		});
		NetDataWriter writer = new NetDataWriter();
		NetRegistry.Write(writer, NetRegistry.Kind.Nuko, 0x4000000000000007uL, w => w.Put(77));
		NetDataReader message = new NetDataReader(writer.CopyData());
		Assert.AreEqual((byte)CoopSession.MessageType.Entity, message.GetByte());
		NetRegistry.Dispatch(message);
		Assert.AreEqual(0x4000000000000007uL, gotId);
		Assert.AreEqual(77, gotValue);
	}

	private class Failing : IStateful
	{
		public string Key => "broken";

		public int Version => 1;

		public bool failWrite;

		public bool failRead;

		public int value;

		public void Write(StateWriter writer)
		{
			if (failWrite)
			{
				throw new System.Exception("can't write");
			}
			writer.Put(value);
		}

		public void Read(StateReader reader, int version)
		{
			if (failRead)
			{
				throw new System.Exception("can't read");
			}
			value = reader.GetInt();
		}
	}

	private static StateSection BrokenSection(int value, int version = 1)
	{
		StateWriter writer = new StateWriter();
		writer.Put(value);
		return new StateSection { Key = "broken", Version = version, Data = writer.ToArray() };
	}

	private static int SavedValue()
	{
		StateSection saved = StateRegistry.Capture(StateUse.Save).Find(section => section.Key == "broken");
		Assert.IsNotNull(saved, "the section was left out");
		return new StateReader(saved.Data).GetInt();
	}

	// a section that can't write this time keeps what it had, the save replaces the only good copy
	[Test]
	public void FailedWriteKeepsTheOldData()
	{
		StateRegistry.ClearPending();
		Failing broken = new Failing();
		StateRegistry.Register(broken);
		try
		{
			StateRegistry.SetPending(new[] { BrokenSection(5) });
			Assert.AreEqual(5, broken.value);
			broken.value = 6;
			Assert.AreEqual(6, SavedValue());
			broken.failWrite = true;
			broken.value = 7;
			Assert.AreEqual(6, SavedValue());
			// a snapshot just goes without
			Assert.IsNull(StateRegistry.Capture(StateUse.Join).Find(section => section.Key == "broken"));
		}
		finally
		{
			StateRegistry.Unregister(broken);
			StateRegistry.ClearPending();
		}
	}

	// one that couldn't be read goes back in the file as it was, not as whatever the system holds
	[Test]
	public void FailedReadKeepsTheSection()
	{
		StateRegistry.ClearPending();
		Failing broken = new Failing { failRead = true, value = 99 };
		StateRegistry.Register(broken);
		try
		{
			StateRegistry.SetPending(new[] { BrokenSection(5) });
			Assert.AreEqual(5, SavedValue());
			broken.failRead = false;
			StateRegistry.SetPending(new[] { BrokenSection(8, 2) });
			Assert.AreEqual(8, SavedValue(), "from a newer version");
			StateRegistry.SetPending(new[] { BrokenSection(9) });
			Assert.AreEqual(9, broken.value);
			Assert.AreEqual(9, SavedValue(), "read fine now");
		}
		finally
		{
			StateRegistry.Unregister(broken);
			StateRegistry.ClearPending();
		}
	}

	private static string SaveJson(string seed)
	{
		return JsonUtility.ToJson(new GameSave.SaveData { version = GameSave.CurrentVersion, genVersion = 2, seed = seed });
	}

	private static string TempSlot()
	{
		string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gltps1-save-test-" + System.Guid.NewGuid().ToString("N"));
		System.IO.Directory.CreateDirectory(folder);
		return System.IO.Path.Combine(folder, "slot.json");
	}

	[Test]
	public void SavingKeepsABackup()
	{
		string path = TempSlot();
		try
		{
			Assert.AreEqual(GameSave.SlotState.Empty, GameSave.Inspect(path, out _));
			GameSave.WriteAtomic(path, SaveJson("1"));
			Assert.IsFalse(System.IO.File.Exists(path + ".bak"), "nothing to back up the first time");
			GameSave.WriteAtomic(path, SaveJson("2"));
			Assert.AreEqual(GameSave.SlotState.Ok, GameSave.Inspect(path, out GameSave.SaveData data));
			Assert.AreEqual("2", data.seed);
			Assert.IsFalse(System.IO.File.Exists(path + ".tmp"));
			Assert.AreEqual("1", JsonUtility.FromJson<GameSave.SaveData>(System.IO.File.ReadAllText(path + ".bak")).seed);
		}
		finally
		{
			System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path), true);
		}
	}

	[Test]
	public void DamagedSlots()
	{
		string path = TempSlot();
		try
		{
			// nothing else to fall back on
			System.IO.File.WriteAllText(path, "{\"version\":3,\"seed\":\"12");
			Assert.AreEqual(GameSave.SlotState.Corrupt, GameSave.Inspect(path, out GameSave.SaveData none));
			Assert.IsNull(none);
			System.IO.File.WriteAllText(path, "{}");
			Assert.AreEqual(GameSave.SlotState.Corrupt, GameSave.Inspect(path, out _), "parses, no seed");
			// a good backup stands in, and the next save doesn't push it out
			System.IO.File.WriteAllText(path + ".bak", SaveJson("1"));
			Assert.AreEqual(GameSave.SlotState.Backup, GameSave.Inspect(path, out GameSave.SaveData backup));
			Assert.AreEqual("1", backup.seed);
			GameSave.WriteAtomic(path, SaveJson("2"));
			Assert.AreEqual("1", JsonUtility.FromJson<GameSave.SaveData>(System.IO.File.ReadAllText(path + ".bak")).seed);
			Assert.IsTrue(System.IO.File.Exists(path + ".corrupt"));
			Assert.AreEqual(GameSave.SlotState.Ok, GameSave.Inspect(path, out _));
			// what a crash left under the old way of saving: the temp file, no slot
			System.IO.File.Delete(path);
			System.IO.File.Delete(path + ".bak");
			System.IO.File.WriteAllText(path + ".tmp", SaveJson("3"));
			Assert.AreEqual(GameSave.SlotState.Backup, GameSave.Inspect(path, out GameSave.SaveData temp));
			Assert.AreEqual("3", temp.seed);
			// not ours to read
			System.IO.File.WriteAllText(path, JsonUtility.ToJson(new GameSave.SaveData { version = GameSave.CurrentVersion + 1, seed = "4" }));
			Assert.AreEqual(GameSave.SlotState.Newer, GameSave.Inspect(path, out GameSave.SaveData newer));
			Assert.IsNull(newer);
		}
		finally
		{
			System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path), true);
		}
	}
}
