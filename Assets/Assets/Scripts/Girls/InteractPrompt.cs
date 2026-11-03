using System.Collections.Generic;
using TMPro;
using UnityEngine;

// the verb and its button floating by the thing, not on the hud, and only the first few times (14). one in the scene,
// the played girl's picker points it at what a press would do. hold progress shows however often it's been used
public class InteractPrompt : MonoBehaviour
{
	private const int shownUses = 5;

	private const float fadeSpeed = 8f;

	private static InteractPrompt instance;

	private static readonly Dictionary<string, int> uses = new Dictionary<string, int>();

	private TextMeshPro text;

	private Transform target;

	private float height;

	private float alpha;

	private bool wanted;

	private string shownLabel;

	private string shownButton;

	private int shownSteps = -1;

	private static string UseKey(string label)
	{
		return "promptUses_" + label;
	}

	private static int UsesOf(string label)
	{
		if (!uses.TryGetValue(label, out int count))
		{
			count = PlayerPrefs.GetInt(UseKey(label), 0);
			uses[label] = count;
		}
		return count;
	}

	public static void Used(string label)
	{
		if (string.IsNullOrEmpty(label))
		{
			return;
		}
		int count = UsesOf(label) + 1;
		uses[label] = count;
		PlayerPrefs.SetInt(UseKey(label), count);
	}

	// held: 0-1 while it's being held, negative when it isn't
	public static void Show(Interaction interaction, string button, float held)
	{
		bool holding = held >= 0f;
		if (interaction.Target == null || string.IsNullOrEmpty(interaction.Label) || (!holding && UsesOf(interaction.Label) >= shownUses))
		{
			Hide();
			return;
		}
		if (instance == null)
		{
			instance = new GameObject("InteractPrompt", typeof(RectTransform)).AddComponent<InteractPrompt>();
		}
		instance.Point(interaction, button, held);
	}

	public static void Hide()
	{
		if (instance != null)
		{
			instance.wanted = false;
		}
	}

	private void Awake()
	{
		text = gameObject.AddComponent<TextMeshPro>();
		text.font = CoopDowned.FindGameFont();
		text.fontSize = 1.6f;
		text.alignment = TextAlignmentOptions.Center;
		text.rectTransform.sizeDelta = new Vector2(3f, 1f);
		text.enableWordWrapping = false;
		text.outlineWidth = 0.2f;
		text.outlineColor = Color.black;
		text.color = new Color(1f, 1f, 1f, 0f);
	}

	private void Point(Interaction interaction, string button, float held)
	{
		target = interaction.Target;
		height = interaction.PromptHeight;
		wanted = true;
		int steps = (held >= 0f) ? Mathf.RoundToInt(held * 10f) : -1;
		if (interaction.Label != shownLabel || button != shownButton || steps != shownSteps)
		{
			shownLabel = interaction.Label;
			shownButton = button;
			shownSteps = steps;
			string line = ((interaction.Press == Press.Hold) ? "hold [" : "[") + button + "] " + interaction.Label;
			text.text = (steps >= 0) ? line + "\n" + new string('|', steps) + "<alpha=#40>" + new string('|', 10 - steps) : line;
		}
	}

	private void LateUpdate()
	{
		if (target == null)
		{
			wanted = false;
		}
		alpha = Mathf.MoveTowards(alpha, wanted ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
		text.enabled = alpha > 0f;
		if (!text.enabled || target == null)
		{
			return;
		}
		Color color = text.color;
		color.a = alpha;
		text.color = color;
		transform.position = target.position + Vector3.up * height;
		Camera view = Camera.main;
		if (view != null)
		{
			transform.rotation = view.transform.rotation;
		}
	}
}
