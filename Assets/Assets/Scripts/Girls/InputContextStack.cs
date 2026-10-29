using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// what a girl's input is doing. modes pick the action map (the highest priority one wins, so a pause
// beats driving whatever order they came in); blocks switch single actions off on top of it
public enum InputContext
{
	// modes
	None,
	OnFoot,
	Riding,
	Boarding,
	Driving,
	Downed,
	Cinematic,
	Menu,
	// blocks
	Carrying,
	HoldingMap,
	KetAirborne,
	ElevatorClosing
}

// one per girl (PlayerInputInitialize). push a context and keep the token, pop the token when done.
// nothing else turns her actions on or off
public class InputContextStack
{
	public class Token
	{
		public InputContext Context;

		public string Owner;

		public bool Live;

		public InputContextStack Stack;

		// pops it from the stack it came from
		public void Release()
		{
			Stack.Pop(this);
		}
	}

	private readonly InputActions actions;

	private readonly List<Token> stack = new List<Token>();

	private InputContext baseMode;

	public InputContextStack(InputActions actions, InputContext baseMode)
	{
		this.actions = actions;
		this.baseMode = baseMode;
		Apply();
	}

	public InputContext Mode { get; private set; }

	// who controls her: the local player's girl stands on foot, anyone else's has nothing on
	public void SetBase(InputContext mode)
	{
		baseMode = mode;
		Apply();
	}

	public Token Push(InputContext context, string owner)
	{
#if DUMP_WORLDGEN
		foreach (Token live in stack)
		{
			if (live.Owner == owner && live.Context == context)
			{
				Debug.LogWarning("InputContexts: " + owner + " pushed " + context + " again without popping it");
			}
		}
#endif
		Token token = new Token { Context = context, Owner = owner, Live = true, Stack = this };
		stack.Add(token);
		Apply();
		return token;
	}

	// fine to call twice or with null
	public void Pop(Token token)
	{
		if (token == null || !token.Live)
		{
			return;
		}
		token.Live = false;
		stack.Remove(token);
		Apply();
	}

	public bool Has(InputContext context)
	{
		return stack.Exists(t => t.Context == context);
	}

	public string Describe()
	{
		string text = "base " + baseMode + ", mode " + Mode;
		foreach (Token token in stack)
		{
			text += ", " + token.Context + " (" + token.Owner + ")";
		}
		return text;
	}

	private static bool IsMode(InputContext context)
	{
		return context < InputContext.Carrying;
	}

	// higher wins
	private static int Priority(InputContext context)
	{
		switch (context)
		{
		case InputContext.Menu:
			return 6;
		case InputContext.Cinematic:
			return 5;
		case InputContext.Downed:
			return 4;
		case InputContext.Driving:
			return 3;
		case InputContext.Boarding:
		case InputContext.Riding:
			return 2;
		default:
			return 0;
		}
	}

	private void Apply()
	{
		InputContext mode = baseMode;
		bool jump = true;
		bool sprint = true;
		bool possess = true;
		bool interact = true;
		bool exit = true;
		foreach (Token token in stack)
		{
			if (IsMode(token.Context))
			{
				// later ones win ties
				if (Priority(token.Context) >= Priority(mode))
				{
					mode = token.Context;
				}
				continue;
			}
			switch (token.Context)
			{
			case InputContext.Carrying:
				jump = false;
				sprint = false;
				break;
			case InputContext.HoldingMap:
				possess = false;
				break;
			case InputContext.KetAirborne:
				exit = false;
				break;
			case InputContext.ElevatorClosing:
				interact = false;
				break;
			}
		}
		Mode = mode;
		bool player = mode == InputContext.OnFoot || mode == InputContext.Riding;
		bool vehicle = mode == InputContext.Driving;
		SetMap(actions.Player.Get(), player);
		SetMap(actions.Vehicle.Get(), vehicle);
		if (player)
		{
			Set(actions.Player.Jump, jump);
			Set(actions.Player.Sprint, sprint);
			Set(actions.Player.Possess, possess);
			Set(actions.Player.Interact, interact);
		}
		if (vehicle)
		{
			Set(actions.Vehicle.Exit, exit);
		}
	}

	private static void SetMap(InputActionMap map, bool on)
	{
		if (on)
		{
			map.Enable();
		}
		else
		{
			map.Disable();
		}
	}

	private static void Set(InputAction action, bool on)
	{
		if (on)
		{
			action.Enable();
		}
		else
		{
			action.Disable();
		}
	}
}
