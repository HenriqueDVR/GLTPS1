using NUnit.Framework;

public class InputContextTests
{
	private InputActions actions;

	[SetUp]
	public void SetUp()
	{
		actions = new InputActions();
	}

	[TearDown]
	public void TearDown()
	{
		actions.Disable();
		UnityEngine.Object.DestroyImmediate(actions.asset);
	}

	private bool Walking => actions.Player.Move.enabled;

	private bool Driving => actions.Vehicle.Drive.enabled;

	[Test]
	public void BaseDecidesTheMap()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.OnFoot);
		Assert.IsTrue(Walking);
		Assert.IsFalse(Driving);
		stack.SetBase(InputContext.None);
		Assert.IsFalse(Walking);
		Assert.IsFalse(Driving);
	}

	// getting on the ket, the enter anim, driving, getting off
	[Test]
	public void KetRide()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.OnFoot);
		InputContextStack.Token boarding = stack.Push(InputContext.Boarding, "ket");
		Assert.IsFalse(Walking);
		Assert.IsFalse(Driving);
		InputContextStack.Token driving = stack.Push(InputContext.Driving, "ket");
		Assert.IsTrue(Driving);
		Assert.IsFalse(Walking);
		driving.Release();
		Assert.IsFalse(Driving);
		Assert.IsFalse(Walking);
		boarding.Release();
		Assert.IsTrue(Walking);
	}

	[Test]
	public void CarryingBlocksJumpAndSprintOnly()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.OnFoot);
		InputContextStack.Token carrying = stack.Push(InputContext.Carrying, "barrel");
		Assert.IsFalse(actions.Player.Jump.enabled);
		Assert.IsFalse(actions.Player.Sprint.enabled);
		Assert.IsTrue(actions.Player.Interact.enabled);
		Assert.IsTrue(Walking);
		carrying.Release();
		Assert.IsTrue(actions.Player.Jump.enabled);
		Assert.IsTrue(actions.Player.Sprint.enabled);
	}

	// the old code re-enabled the whole map when she was played again, jump came back with the barrel still held
	[Test]
	public void BlockSurvivesControlSwitch()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.OnFoot);
		stack.Push(InputContext.Carrying, "barrel");
		stack.SetBase(InputContext.None);
		stack.SetBase(InputContext.OnFoot);
		Assert.IsTrue(Walking);
		Assert.IsFalse(actions.Player.Jump.enabled);
	}

	// pause beats driving even if the ket's anim event comes in while paused
	[Test]
	public void MenuWinsWhateverTheOrder()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.OnFoot);
		InputContextStack.Token menu = stack.Push(InputContext.Menu, "pause");
		InputContextStack.Token driving = stack.Push(InputContext.Driving, "ket");
		Assert.IsFalse(Driving);
		Assert.IsFalse(Walking);
		menu.Release();
		Assert.IsTrue(Driving);
		driving.Release();
		Assert.IsTrue(Walking);
	}

	[Test]
	public void AirborneBlocksGettingOff()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.OnFoot);
		stack.Push(InputContext.Driving, "ket");
		InputContextStack.Token airborne = stack.Push(InputContext.KetAirborne, "ket");
		Assert.IsFalse(actions.Vehicle.Exit.enabled);
		Assert.IsTrue(Driving);
		airborne.Release();
		Assert.IsTrue(actions.Vehicle.Exit.enabled);
	}

	// a girl nobody here controls (AI, the other player's) has nothing on, even if something blocks or unblocks
	[Test]
	public void NoBaseNoInput()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.None);
		InputContextStack.Token closing = stack.Push(InputContext.ElevatorClosing, "elevator");
		closing.Release();
		Assert.IsFalse(actions.Player.Interact.enabled);
		Assert.IsFalse(Walking);
	}

	[Test]
	public void ReleaseTwiceIsFine()
	{
		InputContextStack stack = new InputContextStack(actions, InputContext.OnFoot);
		InputContextStack.Token downed = stack.Push(InputContext.Downed, "down");
		Assert.IsFalse(Walking);
		downed.Release();
		downed.Release();
		stack.Pop(null);
		Assert.IsTrue(Walking);
		Assert.AreEqual(InputContext.OnFoot, stack.Mode);
	}
}
