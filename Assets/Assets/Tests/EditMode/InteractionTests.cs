using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class InteractionTests
{
	private readonly List<string> ran = new List<string>();

	private readonly List<GameObject> made = new List<GameObject>();

	private InteractionResolver resolver;

	[SetUp]
	public void SetUp()
	{
		ran.Clear();
		resolver = new InteractionResolver();
	}

	[TearDown]
	public void TearDown()
	{
		foreach (GameObject go in made)
		{
			Object.DestroyImmediate(go);
		}
		made.Clear();
	}

	private Interaction Make(string name, Press press, int priority = 1, Vector3? at = null, float holdTime = 0.4f)
	{
		Transform target = null;
		if (at.HasValue)
		{
			GameObject go = new GameObject(name);
			go.transform.position = at.Value;
			made.Add(go);
			target = go.transform;
		}
		return new Interaction { Verb = Verb.Use, Press = press, Priority = priority, Target = target, HoldTime = holdTime, Run = () => ran.Add(name), Source = name };
	}

	private void Down(params Interaction[] now)
	{
		resolver.Press(new List<Interaction>(now), Vector3.zero, Vector3.forward);
	}

	private void Keep(float seconds, params Interaction[] now)
	{
		resolver.Hold(new List<Interaction>(now), Vector3.zero, Vector3.forward, seconds);
	}

	private void Up(params Interaction[] now)
	{
		resolver.Release(new List<Interaction>(now), Vector3.zero, Vector3.forward);
	}

	[Test]
	public void LoneTapFiresOnPress()
	{
		Interaction food = Make("food", Press.Tap);
		Down(food);
		CollectionAssert.AreEqual(new[] { "food" }, ran);
		Up(food);
		CollectionAssert.AreEqual(new[] { "food" }, ran);
	}

	// one press used to fire every script whose trigger she stood in
	[Test]
	public void OnlyOneFires()
	{
		Down(Make("food", Press.Tap, at: Vector3.forward), Make("gate", Press.Tap, at: Vector3.right));
		Assert.AreEqual(1, ran.Count);
	}

	[Test]
	public void LookingDecides()
	{
		Interaction left = Make("left", Press.Tap, at: new Vector3(-3f, 0f, 1f));
		Interaction ahead = Make("ahead", Press.Tap, at: new Vector3(0.2f, 0f, 5f));
		Down(left, ahead);
		CollectionAssert.AreEqual(new[] { "ahead" }, ran);
	}

	[Test]
	public void PriorityBeforeLooking()
	{
		Interaction ahead = Make("ahead", Press.Tap, at: Vector3.forward * 2f);
		Interaction drop = Make("drop", Press.Tap, priority: 5);
		Down(ahead, drop);
		CollectionAssert.AreEqual(new[] { "drop" }, ran);
	}

	// at the ket: a tap takes the barrel, a hold gets in
	[Test]
	public void ShortPressNearKetIsTheTap()
	{
		Interaction barrel = Make("barrel", Press.Tap);
		Interaction board = Make("board", Press.Hold);
		Down(barrel, board);
		Assert.AreEqual(0, ran.Count);
		Keep(0.1f, barrel, board);
		Up(barrel, board);
		CollectionAssert.AreEqual(new[] { "barrel" }, ran);
	}

	[Test]
	public void LongPressNearKetIsTheHold()
	{
		Interaction barrel = Make("barrel", Press.Tap);
		Interaction board = Make("board", Press.Hold);
		Down(barrel, board);
		Keep(0.25f, barrel, board);
		Keep(0.25f, barrel, board);
		CollectionAssert.AreEqual(new[] { "board" }, ran);
		Up(barrel, board);
		CollectionAssert.AreEqual(new[] { "board" }, ran);
	}

	// walked away from the ket mid hold
	[Test]
	public void HoldGoneNothingFires()
	{
		Interaction board = Make("board", Press.Hold);
		Down(board);
		Keep(0.5f);
		Up();
		Assert.AreEqual(0, ran.Count);
	}

	[Test]
	public void ContinuousRunsEveryFrameAndWins()
	{
		Interaction revive = Make("revive", Press.Continuous, priority: 2);
		Interaction food = Make("food", Press.Tap);
		Down(revive, food);
		Keep(0.02f, revive, food);
		Keep(0.02f, revive, food);
		CollectionAssert.AreEqual(new[] { "revive", "revive", "revive" }, ran);
		Up(revive, food);
		Assert.AreEqual(3, ran.Count);
	}

	[Test]
	public void NothingOfferedNothingHappens()
	{
		Down();
		Keep(1f);
		Up();
		Assert.AreEqual(0, ran.Count);
		Assert.IsNull(resolver.Fired);
	}
}
