using System;
using System.Collections.Generic;
using UnityEngine;

public enum Verb
{
	Take,
	Drop,
	Board,
	Refuel,
	Talk,
	Use,
	PutOut,
	Revive
}

public enum Press
{
	// on press, or on release when there's also a hold verb
	Tap,
	// once, after holding HoldTime
	Hold,
	// every frame while held
	Continuous
}

// one thing the girl could do right now
public class Interaction
{
	public Verb Verb;

	public Press Press;

	public float HoldTime;

	// null: nothing to look at (dropping what she carries, the next dialogue line)
	public Transform Target;

	// higher wins before looking direction counts
	public int Priority;

	public Action Run;

	// what the prompt says (InteractPrompt), and how far over Target. null: no prompt
	public string Label;

	public float PromptHeight;

	// for logs
	public string Source;

	public override string ToString()
	{
		return Verb + " (" + Source + (Target != null ? " " + Target.name : "") + ")";
	}
}

// anything on the girl or session-wide that offers interactions. things in the world are Interactables
public interface IInteractionSource
{
	void Collect(List<Interaction> into);
}

// picks one interaction per press and works out tap against hold. no input or scene in here, so it can be tested
public class InteractionResolver
{
	private Interaction holding;

	private float held;

	private bool continuing;

	private bool pressed;

	public Interaction Fired { get; private set; }

	public static Interaction Best(List<Interaction> candidates, Press press, Vector3 eye, Vector3 forward)
	{
		Interaction best = null;
		float bestAngle = 0f;
		forward.y = 0f;
		foreach (Interaction candidate in candidates)
		{
			if (candidate.Press != press)
			{
				continue;
			}
			float angle = 0f;
			if (candidate.Target != null)
			{
				Vector3 to = candidate.Target.position - eye;
				to.y = 0f;
				angle = (to.sqrMagnitude > 0.0001f && forward.sqrMagnitude > 0.0001f) ? Vector3.Angle(forward, to) : 0f;
			}
			if (best == null || candidate.Priority > best.Priority || (candidate.Priority == best.Priority && angle < bestAngle))
			{
				best = candidate;
				bestAngle = angle;
			}
		}
		return best;
	}

	// what a press now would do, without doing it. a tap shows over a hold: it needs nothing more than the press. unless
	// only the hold has something to say (the barrel's tap by the ket's get in)
	public static Interaction Preview(List<Interaction> now, Vector3 eye, Vector3 forward)
	{
		Interaction continuous = Best(now, global::Press.Continuous, eye, forward);
		if (continuous != null)
		{
			return continuous;
		}
		Interaction tap = Best(now, global::Press.Tap, eye, forward);
		Interaction hold = Best(now, global::Press.Hold, eye, forward);
		return (tap == null || (tap.Label == null && hold != null && hold.Label != null)) ? hold : tap;
	}

	// being held, not fired yet
	public Interaction Holding => holding;

	// 0-1, -1 when nothing's being held
	public float HeldShare => (holding == null) ? -1f : ((holding.HoldTime > 0f) ? Mathf.Clamp01(held / holding.HoldTime) : 1f);

	public void Press(List<Interaction> now, Vector3 eye, Vector3 forward)
	{
		pressed = true;
		holding = null;
		held = 0f;
		continuing = false;
		Interaction continuous = Best(now, global::Press.Continuous, eye, forward);
		if (continuous != null)
		{
			continuing = true;
			Fire(continuous);
			return;
		}
		Interaction hold = Best(now, global::Press.Hold, eye, forward);
		if (hold == null)
		{
			Fire(Best(now, global::Press.Tap, eye, forward));
			return;
		}
		// the tap waits for the release
		holding = hold;
	}

	public void Hold(List<Interaction> now, Vector3 eye, Vector3 forward, float deltaTime)
	{
		if (!pressed)
		{
			return;
		}
		if (continuing)
		{
			Fire(Best(now, global::Press.Continuous, eye, forward));
			return;
		}
		if (holding == null)
		{
			return;
		}
		held += deltaTime;
		if (held >= holding.HoldTime)
		{
			holding = null;
			// still there?
			Fire(Best(now, global::Press.Hold, eye, forward));
		}
	}

	public void Release(List<Interaction> now, Vector3 eye, Vector3 forward)
	{
		if (pressed && holding != null)
		{
			Fire(Best(now, global::Press.Tap, eye, forward));
		}
		pressed = false;
		holding = null;
		continuing = false;
	}

	public bool Pressed => pressed;

	private void Fire(Interaction interaction)
	{
		Fired = interaction;
		interaction?.Run?.Invoke();
	}
}
