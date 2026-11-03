using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// the only thing listening to Interact. asks the girl's scripts, session-wide sources like revive and the things in
// the world in her reach what she could do, picks one by priority then by where the camera looks
public class InteractionPicker : MonoBehaviour
{
	private static readonly List<IInteractionSource> global = new List<IInteractionSource>();

	private readonly List<IInteractionSource> sources = new List<IInteractionSource>();

	private readonly List<Interaction> candidates = new List<Interaction>();

	private readonly InteractionResolver resolver = new InteractionResolver();

	private PlayerInputInitialize input;

	private Girl girl;

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
		girl = GetComponent<Girl>();
		GetComponentsInChildren(true, sources);
	}

	private void OnEnable()
	{
		input.actions.Player.Interact.performed += OnInteract;
	}

	private void OnDisable()
	{
		input.actions.Player.Interact.performed -= OnInteract;
		if (IsPlayed)
		{
			InteractPrompt.Hide();
		}
	}

	private bool IsPlayed => Girls.Local != null && Girls.Local.gameObject == gameObject;

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
		Camera view = Camera.main;
		if (resolver.Pressed)
		{
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
		if (IsPlayed)
		{
			Prompt(view);
		}
	}

	// the girl's own scripts are only asked with a thing in reach or the ket to get on: this is every frame, and most
	// frames there's nothing
	private void Prompt(Camera view)
	{
		candidates.Clear();
		bool free = Time.timeScale == 1f && input.actions.Player.Interact.enabled && !girl.OnKet;
		if (free)
		{
			Interactable.Collect(girl, transform.position, candidates);
		}
		if (!free || (candidates.Count == 0 && !girl.CanBoard))
		{
			InteractPrompt.Hide();
			return;
		}
		Interaction shown = resolver.Holding ?? InteractionResolver.Preview(Collect(), Eye(view), Forward(view));
		if (shown == null)
		{
			InteractPrompt.Hide();
			return;
		}
		InteractPrompt.Show(shown, Button(), (shown == resolver.Holding) ? resolver.HeldShare : -1f);
	}

	private string keyButton;

	private string padButton;

	// the binding on what was touched last
	private string Button()
	{
		Gamepad pad = Gamepad.current;
		Keyboard keyboard = Keyboard.current;
		if (pad != null && (keyboard == null || pad.lastUpdateTime > keyboard.lastUpdateTime))
		{
			return padButton ?? (padButton = input.actions.Player.Interact.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions, "Gamepad"));
		}
		return keyButton ?? (keyButton = input.actions.Player.Interact.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions, "Keyboard&Mouse"));
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
		// only the played girl takes session-wide ones and things in the world
		if (IsPlayed)
		{
			foreach (IInteractionSource source in global)
			{
				source.Collect(candidates);
			}
			if (!girl.OnKet)
			{
				Interactable.Collect(girl, transform.position, candidates);
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
