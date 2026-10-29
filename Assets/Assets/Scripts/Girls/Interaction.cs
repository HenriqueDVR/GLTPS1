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

	// for logs
	public string Source;

	public override string ToString()
	{
		return Verb + " (" + Source + (Target != null ? " " + Target.name : "") + ")";
	}
}

// anything that offers interactions: the girl's own scripts for now, objects later
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
