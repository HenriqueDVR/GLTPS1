using System.Collections.Generic;
using LiteNetLib.Utils;
using NUnit.Framework;
using UnityEngine;

public class WorldFlagsTests
{
	private readonly List<GameObject> made = new List<GameObject>();

	[SetUp]
	public void SetUp()
	{
		GameSave.RestoreTaken = null;
		WorldFlags.Init();
		CoopItem.Init();
		WorldFlags.ResetLevel();
	}

	[TearDown]
	public void TearDown()
	{
		foreach (GameObject go in made)
		{
			go.GetComponent<Interactable>()?.Unlist();
			Object.DestroyImmediate(go);
		}
		made.Clear();
		GameSave.RestoreTaken = null;
		WorldFlags.ResetLevel();
	}

	private Interactable Thing(Vector3 at, float range = 1.5f)
	{
		GameObject go = new GameObject("thing");
		go.transform.position = at;
		made.Add(go);
		Interactable thing = go.AddComponent<Interactable>();
		thing.Range = range;
		thing.Label = "use";
		thing.Gone = () => { };
		thing.List();
		return thing;
	}

	private static List<Interaction> Near(Vector3 at)
	{
		List<Interaction> into = new List<Interaction>();
		Interactable.Collect(null, at, into);
		return into;
	}

	[Test]
	public void SetOnlyOnce()
	{
		List<ulong> heard = new List<ulong>();
		System.Action<ulong> listen = heard.Add;
		WorldFlags.Changed += listen;
		Assert.IsTrue(WorldFlags.Set(7uL));
		Assert.IsFalse(WorldFlags.Set(7uL));
		WorldFlags.Changed -= listen;
		Assert.IsTrue(WorldFlags.Has(7uL));
		CollectionAssert.AreEqual(new[] { 7uL }, heard);
	}

	[Test]
	public void ClaimAloneFirstAskerWins()
	{
		bool? first = null;
		bool? second = null;
		Assert.IsTrue(WorldFlags.Claim(9uL, granted => first = granted));
		Assert.IsTrue(WorldFlags.Claim(9uL, granted => second = granted));
		Assert.AreEqual(true, first);
		Assert.AreEqual(false, second);
	}

	[Test]
	public void StateRoundTripsAndKeepsTheItemsSection()
	{
		WorldFlags.Set(ulong.MaxValue);
		WorldFlags.Set(42uL);
		StateWriter writer = new StateWriter();
		new WorldFlags.State().Write(writer);
		Assert.AreEqual("items", new WorldFlags.State().Key);
		Assert.AreEqual(1, new WorldFlags.State().Version);
		CollectionAssert.AreEquivalent(new[] { ulong.MaxValue, 42uL }, WorldFlags.State.ReadData(new StateReader(writer.ToArray())));
	}

	// a friend resuming: into a running world, only what's new is news
	[Test]
	public void SnapshotAnnouncesOnlyNewFlags()
	{
		WorldFlags.Set(1uL);
		StateWriter writer = new StateWriter();
		WorldFlags.State.WriteData(writer, new[] { 1uL, 2uL });
		List<ulong> heard = new List<ulong>();
		System.Action<ulong> listen = heard.Add;
		WorldFlags.Changed += listen;
		new WorldFlags.State().Read(new StateReader(writer.ToArray()), 1);
		WorldFlags.Changed -= listen;
		CollectionAssert.AreEqual(new[] { 2uL }, heard);
	}

	[Test]
	public void ResetKeepsWhatASaveLoaded()
	{
		WorldFlags.Set(1uL);
		GameSave.RestoreTaken = new HashSet<ulong> { 2uL };
		WorldFlags.ResetLevel();
		Assert.IsFalse(WorldFlags.Has(1uL));
		Assert.IsTrue(WorldFlags.Has(2uL));
	}

	// a friend's whole round trip, minus the socket
	[Test]
	public void RequestAnswersTheOneWhoAsked()
	{
		bool? got = null;
		NetDataWriter request = new NetDataWriter();
		NetRegistry.WriteRequest(request, NetRegistry.Kind.Flag, 5uL, granted => got = granted, null);
		NetDataReader atHost = new NetDataReader(request.CopyData());
		Assert.AreEqual((byte)CoopSession.MessageType.Request, atHost.GetByte());
		NetDataWriter reply = new NetDataWriter();
		NetRegistry.AnswerRequest(atHost, reply);
		Assert.IsTrue(WorldFlags.Has(5uL));
		NetDataReader atFriend = new NetDataReader(reply.CopyData());
		Assert.AreEqual((byte)CoopSession.MessageType.Answer, atFriend.GetByte());
		NetRegistry.OnAnswer(atFriend);
		Assert.AreEqual(true, got);
	}

	[Test]
	public void SecondRequestIsRefused()
	{
		WorldFlags.Set(5uL);
		bool? got = null;
		NetDataWriter request = new NetDataWriter();
		NetRegistry.WriteRequest(request, NetRegistry.Kind.Flag, 5uL, granted => got = granted, null);
		NetDataReader atHost = new NetDataReader(request.CopyData());
		atHost.GetByte();
		NetDataWriter reply = new NetDataWriter();
		NetRegistry.AnswerRequest(atHost, reply);
		NetDataReader atFriend = new NetDataReader(reply.CopyData());
		atFriend.GetByte();
		NetRegistry.OnAnswer(atFriend);
		Assert.AreEqual(false, got);
	}

	[Test]
	public void LinkLostAnswersNo()
	{
		bool? got = null;
		NetRegistry.WriteRequest(new NetDataWriter(), NetRegistry.Kind.Flag, 5uL, granted => got = granted, null);
		NetRegistry.LinkLost();
		Assert.AreEqual(false, got);
		Assert.IsFalse(WorldFlags.Has(5uL));
	}

	[Test]
	public void ItemRequestCarriesItsBody()
	{
		bool? got = null;
		Assert.IsTrue(NetRegistry.Ask(NetRegistry.Kind.Item, 11uL, writer => writer.Put(false), granted => got = granted));
		Assert.AreEqual(true, got);
		Assert.IsTrue(WorldFlags.Has(11uL));
		Assert.IsTrue(NetRegistry.Ask(NetRegistry.Kind.Item, 11uL, writer => writer.Put(false), granted => got = granted));
		Assert.AreEqual(false, got);
	}

	[Test]
	public void CatchUpRunsItsListeners()
	{
		bool? heard = null;
		System.Action<bool> listen = arrived => heard = arrived;
		NetRegistry.CatchUp += listen;
		NetRegistry.RunCatchUp(true);
		NetRegistry.CatchUp -= listen;
		Assert.AreEqual(true, heard);
	}

	// the bug in 18, G8: kinds behind one Entity count
	[Test]
	public void SequenceCountsEachKindApart()
	{
		NetDataWriter weather = new NetDataWriter();
		NetRegistry.Write(weather, NetRegistry.Kind.Weather, 0uL, null);
		NetDataWriter clock = new NetDataWriter();
		NetRegistry.Write(clock, NetRegistry.Kind.Clock, 0uL, null);
		ushort weatherKey = NetSequence.KeyOf(weather.Data, 0);
		ushort clockKey = NetSequence.KeyOf(clock.Data, 0);
		Assert.AreNotEqual(weatherKey, clockKey);
		Assert.AreEqual((ushort)CoopSession.MessageType.PlayerState, NetSequence.KeyOf(new[] { (byte)CoopSession.MessageType.PlayerState, (byte)9 }, 0));
		NetSequence sequence = new NetSequence();
		for (int i = 0; i < 100; i++)
		{
			Assert.IsTrue(sequence.Fresh(weatherKey, sequence.Next(weatherKey)));
		}
		// silent till now: it isn't stale next to the busy one
		Assert.IsTrue(sequence.Fresh(clockKey, sequence.Next(clockKey)));
		Assert.IsFalse(sequence.Fresh(weatherKey, 50));
		sequence.ClearSeen();
		Assert.IsTrue(sequence.Fresh(weatherKey, 0));
	}

	[Test]
	public void RangeIsFlatWithSomeHeight()
	{
		Assert.IsTrue(Interactable.InRange(Vector3.zero, new Vector3(1f, 0f, 1f), 1.5f));
		Assert.IsFalse(Interactable.InRange(Vector3.zero, new Vector3(1.2f, 0f, 1.2f), 1.5f));
		Assert.IsTrue(Interactable.InRange(Vector3.zero, new Vector3(0f, 2f, 0f), 1.5f));
		Assert.IsFalse(Interactable.InRange(Vector3.zero, new Vector3(0f, 3f, 0f), 1.5f));
	}

	[Test]
	public void OffersOnlyInRangeAndWhenItCan()
	{
		Interactable near = Thing(new Vector3(1f, 0f, 0f));
		Thing(new Vector3(10f, 0f, 0f));
		Assert.AreEqual(1, Near(Vector3.zero).Count);
		Assert.AreEqual(near.transform, Near(Vector3.zero)[0].Target);
		Assert.AreEqual("use", Near(Vector3.zero)[0].Label);
		near.Can = girl => false;
		Assert.AreEqual(0, Near(Vector3.zero).Count);
	}

	// a tile going back in the pool and coming out again
	[Test]
	public void PooledThingLeavesAndComesBack()
	{
		Interactable thing = Thing(Vector3.zero);
		thing.Unlist();
		Assert.AreEqual(0, Near(Vector3.zero).Count);
		thing.List();
		Assert.AreEqual(1, Near(Vector3.zero).Count);
		thing.List();
		Assert.AreEqual(1, Near(Vector3.zero).Count);
	}

	[Test]
	public void FlagSetWhilePooledFinishesItOnReturn()
	{
		Interactable thing = Thing(Vector3.zero);
		int finished = 0;
		thing.Gone = () => finished++;
		thing.UseFlag(21uL);
		thing.Unlist();
		WorldFlags.Set(21uL);
		Assert.AreEqual(0, finished);
		thing.List();
		Assert.AreEqual(1, finished);
		Assert.AreEqual(0, Near(Vector3.zero).Count);
	}

	[Test]
	public void FlagSetLiveFinishesItOnce()
	{
		Interactable thing = Thing(Vector3.zero);
		int finished = 0;
		thing.Gone = () => finished++;
		thing.UseFlag(22uL);
		WorldFlags.Set(22uL);
		Assert.AreEqual(1, finished);
		Interactable.OnFlagSet(22uL);
		Assert.AreEqual(1, finished);
		Assert.AreEqual(0, Near(Vector3.zero).Count);
	}

	[Test]
	public void AlreadyFlaggedIsFinishedAtOnce()
	{
		WorldFlags.Set(23uL);
		Interactable thing = Thing(Vector3.zero);
		int finished = 0;
		thing.Gone = () => finished++;
		thing.UseFlag(23uL);
		Assert.AreEqual(1, finished);
	}

	[Test]
	public void HostAuthorityRunsOnGrantOnly()
	{
		Interactable thing = Thing(Vector3.zero);
		int ran = 0;
		thing.Authority = Authority.Host;
		thing.Run = girl => ran++;
		thing.UseFlag(24uL);
		Near(Vector3.zero)[0].Run();
		Assert.AreEqual(1, ran);
		Assert.IsTrue(WorldFlags.Has(24uL));
		Assert.AreEqual(0, Near(Vector3.zero).Count);
	}

	[Test]
	public void SelfAuthorityRunsWithoutAFlag()
	{
		Interactable thing = Thing(Vector3.zero);
		int ran = 0;
		thing.Run = girl => ran++;
		Near(Vector3.zero)[0].Run();
		Near(Vector3.zero)[0].Run();
		Assert.AreEqual(2, ran);
	}

	[Test]
	public void PreviewFiresNothingAndMatchesAPress()
	{
		int ran = 0;
		Interaction tap = new Interaction { Press = Press.Tap, Run = () => ran++ };
		Interaction hold = new Interaction { Press = Press.Hold, HoldTime = 1f, Run = () => ran++ };
		Interaction keep = new Interaction { Press = Press.Continuous, Run = () => ran++ };
		Assert.AreEqual(tap, InteractionResolver.Preview(new List<Interaction> { hold, tap }, Vector3.zero, Vector3.forward));
		Assert.AreEqual(hold, InteractionResolver.Preview(new List<Interaction> { hold }, Vector3.zero, Vector3.forward));
		Assert.AreEqual(keep, InteractionResolver.Preview(new List<Interaction> { tap, hold, keep }, Vector3.zero, Vector3.forward));
		Assert.IsNull(InteractionResolver.Preview(new List<Interaction>(), Vector3.zero, Vector3.forward));
		Assert.AreEqual(0, ran);
	}

	// the barrel's tap has no prompt, the ket's get in does
	[Test]
	public void PreviewShowsAHoldWithALabelOverASilentTap()
	{
		Interaction silent = new Interaction { Press = Press.Tap };
		Interaction said = new Interaction { Press = Press.Tap, Label = "take" };
		Interaction hold = new Interaction { Press = Press.Hold, Label = "get in" };
		Assert.AreEqual(hold, InteractionResolver.Preview(new List<Interaction> { silent, hold }, Vector3.zero, Vector3.forward));
		Assert.AreEqual(said, InteractionResolver.Preview(new List<Interaction> { said, hold }, Vector3.zero, Vector3.forward));
	}

	[Test]
	public void HoldProgressGoesUpAndStops()
	{
		InteractionResolver resolver = new InteractionResolver();
		Interaction hold = new Interaction { Press = Press.Hold, HoldTime = 2f };
		List<Interaction> now = new List<Interaction> { hold };
		Assert.AreEqual(-1f, resolver.HeldShare);
		resolver.Press(now, Vector3.zero, Vector3.forward);
		Assert.AreEqual(hold, resolver.Holding);
		resolver.Hold(now, Vector3.zero, Vector3.forward, 1f);
		Assert.AreEqual(0.5f, resolver.HeldShare, 0.001f);
		resolver.Hold(now, Vector3.zero, Vector3.forward, 1f);
		Assert.IsNull(resolver.Holding);
		Assert.AreEqual(-1f, resolver.HeldShare);
	}

	[Test]
	public void LinesComeInOrderForAsLongAsTheyTake()
	{
		LineQueue lines = new LineQueue();
		Assert.IsFalse(lines.Showing);
		lines.Add(Speaker.Yuuri, "It says ten past four.");
		lines.Add(Speaker.Chito, "It's said that for a long time.");
		Assert.AreEqual(Speaker.Yuuri, lines.Current.Who);
		lines.Tick(LineQueue.SecondsFor("It says ten past four.") - 0.1f);
		Assert.AreEqual(Speaker.Yuuri, lines.Current.Who);
		lines.Tick(0.2f);
		Assert.AreEqual(Speaker.Chito, lines.Current.Who);
		Assert.AreEqual(0f, lines.Shown);
		lines.Tick(100f);
		Assert.IsFalse(lines.Showing);
		lines.Add(Speaker.Chito, "Again.");
		Assert.IsTrue(lines.Showing);
	}
}
