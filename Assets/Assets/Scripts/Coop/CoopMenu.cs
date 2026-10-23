using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// coop page, made from copies of the menu buttons. invite goes through the clipboard
public class CoopMenu : MonoBehaviour
{
	private const int titleScene = 0;

	private const float buttonTop = -111f;

	private const float buttonSpacing = 100f;

	private const float backButtonY = -469f;

	private CoopSession session;

	private GameObject titleMenu;

	private GameObject page;

	private TextMeshProUGUI statusText;

	private Button startButton;

	private Button hostButton;

	private Button joinButton;

	private Button copyButton;

	private Button startGameButton;

	private Button leaveButton;

	private Button backButton;

	private TextMeshProUGUI copyLabel;

	private Button connectionButton;

	private Button loadGameButton;

	private TextMeshProUGUI connectionLabel;

	private float copiedUntil;

	private GUIStyle overlayStyle;

	private void Awake()
	{
		session = GetComponent<CoopSession>();
	}

	public void BuildTitlePage()
	{
		if (page != null)
		{
			return;
		}
		titleMenu = GameObject.Find("TitleMenu");
		if (titleMenu == null)
		{
			Debug.LogWarning("Coop: title menu not found");
			return;
		}
		Transform transform = titleMenu.transform.Find("Settings");
		startButton = titleMenu.transform.Find("Start").GetComponent<Button>();
		Transform transform2 = titleMenu.transform.Find("Text (TMP)");
		CloneButton(transform, titleMenu.transform, "co-op", -311f, OpenPage);
		page = new GameObject("CoopPage", typeof(RectTransform));
		page.transform.SetParent(titleMenu.transform.parent, worldPositionStays: false);
		RectTransform obj = (RectTransform)page.transform;
		obj.anchorMin = Vector2.zero;
		obj.anchorMax = Vector2.one;
		obj.offsetMin = Vector2.zero;
		obj.offsetMax = Vector2.zero;
		TextMeshProUGUI textMeshProUGUI = CloneText(transform2, "co-op", new Vector2(0f, 330f), new Vector2(819f, 200f));
		textMeshProUGUI.fontSize *= 0.6f;
		statusText = CloneText(transform2, "", new Vector2(0f, 90f), new Vector2(1500f, 260f));
		statusText.fontSize = 34f;
		statusText.enableWordWrapping = true;
		statusText.alignment = TextAlignmentOptions.Center;
		hostButton = CloneButton(transform, page.transform, "host a game", 0f, session.Host);
		joinButton = CloneButton(transform, page.transform, "join (paste invite)", 0f, delegate
		{
			session.Join(GUIUtility.systemCopyBuffer);
		});
		connectionButton = CloneButton(transform, page.transform, "connection: auto", 0f, NextConnectionChoice);
		connectionLabel = connectionButton.GetComponentInChildren<TextMeshProUGUI>();
		copyButton = CloneButton(transform, page.transform, "copy invite", 0f, CopyInvite);
		copyLabel = copyButton.GetComponentInChildren<TextMeshProUGUI>();
		startGameButton = CloneButton(transform, page.transform, "start game", 0f, StartGame);
		loadGameButton = CloneButton(transform, page.transform, "load game", 0f, delegate
		{
			GameSaveMenu.OpenLoadFromCoop(page);
		});
		leaveButton = CloneButton(transform, page.transform, "leave", 0f, session.Stop);
		backButton = CloneButton(transform, page.transform, "back", backButtonY, ClosePage);
		page.SetActive(value: false);
		if (session.Mode != CoopSession.SessionMode.Off)
		{
			OpenPage();
		}
	}

	private Button CloneButton(Transform template, Transform parent, string text, float y, UnityAction onClick)
	{
		GameObject obj = Object.Instantiate(template.gameObject, parent, worldPositionStays: false);
		obj.name = "Coop " + text;
		((RectTransform)obj.transform).anchoredPosition = new Vector2(((RectTransform)template).anchoredPosition.x, y);
		TextMeshProUGUI componentInChildren = obj.GetComponentInChildren<TextMeshProUGUI>();
		componentInChildren.text = text;
		// template text box only fits "settings"
		((RectTransform)obj.transform).sizeDelta = new Vector2(700f, ((RectTransform)template).sizeDelta.y);
		componentInChildren.enableWordWrapping = false;
		Button component = obj.GetComponent<Button>();
		// can't RemoveAllListeners on scene listeners, so replace
		component.onClick = new Button.ButtonClickedEvent();
		component.onClick.AddListener(onClick);
		return component;
	}

	private TextMeshProUGUI CloneText(Transform template, string text, Vector2 position, Vector2 size)
	{
		GameObject obj = Object.Instantiate(template.gameObject, page.transform, worldPositionStays: false);
		RectTransform obj2 = (RectTransform)obj.transform;
		obj2.anchoredPosition = position;
		obj2.sizeDelta = size;
		TextMeshProUGUI component = obj.GetComponent<TextMeshProUGUI>();
		component.text = text;
		return component;
	}

	private void OpenPage()
	{
		titleMenu.SetActive(value: false);
		page.SetActive(value: true);
	}

	private void ClosePage()
	{
		page.SetActive(value: false);
		titleMenu.SetActive(value: true);
	}

	private void NextConnectionChoice()
	{
		session.Choice = (CoopSession.ConnectionChoice)(((int)session.Choice + 1) % 3);
	}

	private static string ConnectionName(CoopSession.ConnectionChoice choice)
	{
		switch (choice)
		{
		case CoopSession.ConnectionChoice.DirectOnly:
			return "connection: direct only";
		case CoopSession.ConnectionChoice.EpicOnly:
			return "connection: Epic only";
		default:
			return "connection: auto";
		}
	}

	private static string ConnectionHelp(CoopSession.ConnectionChoice choice)
	{
		switch (choice)
		{
		case CoopSession.ConnectionChoice.DirectOnly:
			return "Direct only: needs the host's router to let it through.";
		case CoopSession.ConnectionChoice.EpicOnly:
			return "Epic only: works through any router, relayed by Epic if it has to be.";
		default:
			return "Auto: direct first, then through Epic if that doesn't connect.";
		}
	}

	private void CopyInvite()
	{
		GUIUtility.systemCopyBuffer = session.Invite;
		copiedUntil = Time.unscaledTime + 2f;
	}

	private void StartGame()
	{
		ClosePage();
		startButton.onClick.Invoke();
	}

	private void Update()
	{
		if (page == null || !page.activeSelf)
		{
			return;
		}
		CoopSession.SessionMode mode = session.Mode;
		bool flag = mode == CoopSession.SessionMode.Off;
		bool flag2 = mode == CoopSession.SessionMode.Hosting;
		List<Button> list = new List<Button>();
		SetVisible(hostButton, flag, list);
		SetVisible(joinButton, flag, list);
		SetVisible(connectionButton, flag, list);
		SetVisible(copyButton, flag2, list);
		SetVisible(startGameButton, flag2 && session.Connected, list);
		SetVisible(loadGameButton, flag2 && session.Connected, list);
		SetVisible(leaveButton, !flag, list);
		for (int i = 0; i < list.Count; i++)
		{
			RectTransform obj = (RectTransform)list[i].transform;
			obj.anchoredPosition = new Vector2(obj.anchoredPosition.x, buttonTop - (float)i * buttonSpacing);
		}
		backButton.gameObject.SetActive(!(mode == CoopSession.SessionMode.Joining && session.Connected));
		leaveButton.GetComponentInChildren<TextMeshProUGUI>().text = (flag2 ? "stop hosting" : (session.Connected ? "leave" : "cancel"));
		// router + EOS add to the invite a few secs after hosting starts
		bool inviteReady = session.InviteReady;
		copyButton.interactable = inviteReady;
		connectionLabel.text = ConnectionName(session.Choice);
		copyLabel.text = (!inviteReady ? "getting invite..." : ((Time.unscaledTime < copiedUntil) ? "copied!" : "copy invite"));
		statusText.text = StatusText();
	}

	private static void SetVisible(Button button, bool visible, List<Button> visibleButtons)
	{
		button.gameObject.SetActive(visible);
		if (visible)
		{
			visibleButtons.Add(button);
		}
	}

	private string StatusText()
	{
		string text = session.Status;
		if (session.Mode == CoopSession.SessionMode.Off)
		{
			if (text == "")
			{
				return "Play together with a friend.\nHost: copy the invite and send it to them. Friend: copy the invite, then press join.\n" + ConnectionHelp(session.Choice);
			}
			return text;
		}
		if (session.IsHost)
		{
			if (session.PortOpener != null)
			{
				text = session.PortOpener.Detail + "\n" + text;
			}
			if (session.Connected)
			{
				text = text + "  (" + session.Ping + " ms)";
			}
			else
			{
				text += "\nIf Windows asks, allow GLTPS1 on your network.";
			}
		}
		if (session.Warning != "")
		{
			text = text + "\n" + session.Warning;
		}
		return text;
	}

	private void OnGUI()
	{
		if (SceneManager.GetActiveScene().buildIndex == titleScene || session.Mode == CoopSession.SessionMode.Off)
		{
			return;
		}
		if (overlayStyle == null)
		{
			overlayStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = Mathf.Max(12, Screen.height / 50),
				alignment = TextAnchor.UpperRight,
				wordWrap = true
			};
			overlayStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
		}
		string text = (session.Connected ? ("co-op: together, " + session.Ping + " ms" + ((session.LinkKind != "") ? (" (" + session.LinkKind + ")") : "")) : (session.IsHost ? "co-op: waiting for your friend" : "co-op: connecting"));
		if (session.Warning != "")
		{
			text = text + "\n" + session.Warning;
		}
		GUI.Label(new Rect((float)Screen.width * 0.5f, 8f, (float)Screen.width * 0.5f - 12f, (float)Screen.height * 0.2f), text, overlayStyle);
	}
}
