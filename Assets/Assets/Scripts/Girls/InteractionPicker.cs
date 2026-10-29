using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// the only thing listening to Interact. asks the girl's scripts (and session-wide sources like revive)
// what she could do, picks one by priority then by where the camera looks
public class InteractionPicker : MonoBehaviour
{
	private static readonly List<IInteractionSource> global = new List<IInteractionSource>();

	private readonly List<IInteractionSource> sources = new List<IInteractionSource>();

	private readonly List<Interaction> candidates = new List<Interaction>();

	private readonly InteractionResolver resolver = new InteractionResolver();

	private PlayerInputInitialize input;

	// for systems not on a girl (co-op revive)
	public static void AddGlobal(IInteractionSource source)
	{
		if (!global.Contains(source))
		{
			global.Add(source);
		}
	}

	public static void RemoveGlobal(IInteractionSource source)
	{
		global.Remove(source);
	}

	private void Awake()
	{
		input = GetComponent<PlayerInputInitialize>();
		GetComponentsInChildren(true, sources);
	}

	private void OnEnable()
	{
		input.actions.Player.Interact.performed += OnInteract;
	}

	private void OnDisable()
	{
		input.actions.Player.Interact.performed -= OnInteract;
	}

	private void OnInteract(InputAction.CallbackContext context)
	{
		if (Time.timeScale != 1f)
		{
			return;
		}
		Camera view = Camera.main;
		resolver.Press(Collect(), Eye(view), Forward(view));
		Log("press");
	}

	private void Update()
	{
		if (!resolver.Pressed)
		{
			return;
		}
		Camera view = Camera.main;
		// a menu coming up counts as letting go, but nothing fires then
		if ((input.actions.Player.Interact.IsPressed() || debugHeld) && Time.timeScale == 1f)
		{
			resolver.Hold(Collect(), Eye(view), Forward(view), Time.deltaTime);
			Log("hold");
		}
		else
		{
			resolver.Release(Time.timeScale == 1f ? Collect() : new List<Interaction>(), Eye(view), Forward(view));
			Log("release");
		}
	}

	private List<Interaction> Collect()
	{
		candidates.Clear();
		foreach (IInteractionSource source in sources)
		{
			if (source is Behaviour behaviour && !behaviour.isActiveAndEnabled)
			{
				continue;
			}
			source.Collect(candidates);
		}
		// only the played girl takes session-wide ones
		if (Girls.Local != null && Girls.Local.gameObject == gameObject)
		{
			foreach (IInteractionSource source in global)
			{
				source.Collect(candidates);
			}
		}
		return candidates;
	}

	private Vector3 Eye(Camera view)
	{
		return (view != null) ? view.transform.position : transform.position;
	}

	private Vector3 Forward(Camera view)
	{
		return (view != null) ? view.transform.forward : transform.forward;
	}

#if !DUMP_WORLDGEN
	private const bool debugHeld = false;
#endif

#if DUMP_WORLDGEN
	private Interaction lastLogged;

	private bool debugHeld;

	// test runs press Interact without a keyboard
	public void DebugPress()
	{
		debugHeld = true;
		Camera view = Camera.main;
		resolver.Press(Collect(), Eye(view), Forward(view));
		Log("press");
	}

	public void DebugRelease()
	{
		debugHeld = false;
	}

	public string DebugOffered()
	{
		Collect();
		return string.Join(", ", candidates);
	}
#endif

	private void Log(string when)
	{
#if DUMP_WORLDGEN
		if (resolver.Fired != null && resolver.Fired != lastLogged)
		{
			lastLogged = resolver.Fired;
			Debug.Log("Interact: " + name + " " + resolver.Fired + " on " + when + ", " + candidates.Count + " offered");
		}
#endif
	}
}
