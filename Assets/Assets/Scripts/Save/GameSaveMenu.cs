using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// save menus, same button copying trick as the coop page
public static class GameSaveMenu
{
	private enum Mode
	{
		NewGame,
		Load
	}

	private static GameObject titleMenu;

	private static GameObject slotPage;

	private static TextMeshProUGUI slotTitle;

	private static readonly List<TextMeshProUGUI> slotLabels = new List<TextMeshProUGUI>();

	private static Mode mode;

	private static GameObject returnTo;

	private static Button.ButtonClickedEvent originalStart;

	// after CoopMenu adds its button
	public static void BuildTitle()
	{
		GameSave.Runner.EnsureExists();
		titleMenu = GameObject.Find("TitleMenu");
		if (titleMenu == null)
		{
			return;
		}
		Transform settings = titleMenu.transform.Find("Settings");
		Button start = titleMenu.transform.Find("Start").GetComponent<Button>();
		Transform quit = titleMenu.transform.Find("Quit");
		Transform coop = titleMenu.transform.Find("Coop co-op");
		Transform heading = titleMenu.transform.Find("Text (TMP)");
		Button load = Clone(settings, titleMenu.transform, "load", delegate
		{
			OpenSlots(Mode.Load, titleMenu);
		});
		List<Transform> column = new List<Transform> { start.transform, load.transform, settings };
		if (coop != null)
		{
			column.Add(coop);
		}
		if (quit != null)
		{
			float top = ((RectTransform)start.transform).anchoredPosition.y;
			float bottom = ((RectTransform)quit).anchoredPosition.y;
			column.Add(quit);
			for (int i = 0; i < column.Count; i++)
			{
				RectTransform rect = (RectTransform)column[i];
				rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, Mathf.Lerp(top, bottom, (float)i / (column.Count - 1)));
			}
		}
		originalStart = start.onClick;
		start.onClick = new Button.ButtonClickedEvent();
		start.onClick.AddListener(delegate
		{
			OpenSlots(Mode.NewGame, titleMenu);
		});
		BuildSlotPage(settings, heading);
#if DUMP_WORLDGEN
		// -newgame N / -loadslot N
		string[] args = System.Environment.GetCommandLineArgs();
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (args[i] == "-newgame" && int.TryParse(args[i + 1], out int newSlot))
			{
				GameSave.StartNewGame(newSlot - 1);
				if (System.Array.IndexOf(args, "-coophost") < 0 && System.Array.IndexOf(args, "-coopjoin") < 0)
				{
					Object.FindObjectOfType<SetDifficultySetting>(true).SetEasyDifficulty();
				}
			}
			if (args[i] == "-loadslot" && int.TryParse(args[i + 1], out int loadSlot))
			{
				GameSave.Load(loadSlot - 1);
			}
		}
#endif
	}

	private static void BuildSlotPage(Transform buttonTemplate, Transform textTemplate)
	{
		slotPage = new GameObject("SavePage", typeof(RectTransform));
		slotPage.transform.SetParent(titleMenu.transform.parent, worldPositionStays: false);
		RectTransform rect = (RectTransform)slotPage.transform;
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		GameObject heading = Object.Instantiate(textTemplate.gameObject, slotPage.transform, worldPositionStays: false);
		((RectTransform)heading.transform).anchoredPosition = new Vector2(0f, 330f);
		((RectTransform)heading.transform).sizeDelta = new Vector2(1200f, 200f);
		slotTitle = heading.GetComponent<TextMeshProUGUI>();
		slotTitle.fontSize *= 0.6f;
		slotLabels.Clear();
		for (int i = 0; i < GameSave.SlotCount; i++)
		{
			int slot = i;
			Button button = Clone(buttonTemplate, slotPage.transform, "", delegate
			{
				PickSlot(slot);
			});
			RectTransform buttonRect = (RectTransform)button.transform;
			buttonRect.anchoredPosition = new Vector2(0f, 120f - (float)i * 110f);
			buttonRect.sizeDelta = new Vector2(1300f, buttonRect.sizeDelta.y);
			slotLabels.Add(button.GetComponentInChildren<TextMeshProUGUI>());
		}
		Button back = Clone(buttonTemplate, slotPage.transform, "back", CloseSlots);
		((RectTransform)back.transform).anchoredPosition = new Vector2(0f, -330f);
		slotPage.SetActive(false);
	}

	private static Button Clone(Transform template, Transform parent, string text, UnityAction onClick)
	{
		GameObject copy = Object.Instantiate(template.gameObject, parent, worldPositionStays: false);
		copy.name = "Save " + text;
		TextMeshProUGUI label = copy.GetComponentInChildren<TextMeshProUGUI>();
		label.text = text;
		label.enableWordWrapping = false;
		((RectTransform)copy.transform).sizeDelta = new Vector2(700f, ((RectTransform)template).sizeDelta.y);
		Button button = copy.GetComponent<Button>();
		// can't RemoveAllListeners on scene listeners, so replace
		button.onClick = new Button.ButtonClickedEvent();
		button.onClick.AddListener(onClick);
		return button;
	}

	private static void OpenSlots(Mode newMode, GameObject from)
	{
		mode = newMode;
		returnTo = from;
		slotTitle.text = (mode == Mode.NewGame) ? "new game: pick a slot" : "load";
		for (int i = 0; i < slotLabels.Count; i++)
		{
			string description = GameSave.Describe(i);
			bool empty = GameSave.Read(i) == null;
			slotLabels[i].text = (mode == Mode.NewGame && !empty) ? (description + " (replace)") : description;
		}
		from.SetActive(false);
		slotPage.SetActive(true);
	}

	public static void OpenLoadFromCoop(GameObject coopPage)
	{
		OpenSlots(Mode.Load, coopPage);
	}

	private static void CloseSlots()
	{
		slotPage.SetActive(false);
		returnTo.SetActive(true);
	}

	private static void PickSlot(int slot)
	{
		if (mode == Mode.Load)
		{
			if (GameSave.Read(slot) != null)
			{
				GameSave.Load(slot);
			}
			return;
		}
		GameSave.StartNewGame(slot);
		slotPage.SetActive(false);
		titleMenu.SetActive(true);
		originalStart.Invoke();
	}

	// friend doesn't get a save button
	public static void BuildPauseButton()
	{
		UIManager ui = Object.FindObjectOfType<UIManager>();
		if (ui == null || ui.MainMenu == null || (CoopSession.Active && !CoopSession.Instance.IsHost))
		{
			return;
		}
		Button settings = null;
		Button above = null;
		foreach (Button button in ui.MainMenu.GetComponentsInChildren<Button>(includeInactive: true))
		{
			TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
			if (label == null)
			{
				continue;
			}
			if (label.text.Trim() == "settings")
			{
				settings = button;
			}
			else if (label.text.Trim() == "photo album")
			{
				above = button;
			}
		}
		if (settings == null)
		{
			return;
		}
		RectTransform settingsRect = (RectTransform)settings.transform;
		float spacing = (above != null) ? (((RectTransform)above.transform).anchoredPosition.y - settingsRect.anchoredPosition.y) : 50f;
		Button save = Clone(settings.transform, settings.transform.parent, "save", delegate
		{
			string why = GameSave.WhyNotSave();
			CoopNotice.Show((why == null && GameSave.SaveNow()) ? ("Saved to slot " + (GameSave.CurrentSlot + 1) + ".") : (why ?? "Couldn't save."));
		});
		((RectTransform)save.transform).sizeDelta = settingsRect.sizeDelta;
		((RectTransform)save.transform).anchoredPosition = settingsRect.anchoredPosition - new Vector2(0f, spacing);
	}
}
