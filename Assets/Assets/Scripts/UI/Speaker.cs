using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using TMPro;
using UnityEngine;

// lines waiting their turn, one at a time. no scene in here, so it can be tested
public class LineQueue
{
	public struct Line
	{
		public string Who;

		public string Text;
	}

	private readonly Queue<Line> waiting = new Queue<Line>();

	private float left;

	public bool Showing { get; private set; }

	public Line Current { get; private set; }

	// how far into the current one
	public float Shown { get; private set; }

	public static float SecondsFor(string text)
	{
		return 1.2f + 0.055f * text.Length;
	}

	public void Add(string who, string text)
	{
		waiting.Enqueue(new Line { Who = who, Text = text });
		if (!Showing)
		{
			Next();
		}
	}

	public void Tick(float seconds)
	{
		if (!Showing)
		{
			return;
		}
		left -= seconds;
		Shown += seconds;
		if (left <= 0f)
		{
			Next();
		}
	}

	private void Next()
	{
		Showing = waiting.Count > 0;
		if (Showing)
		{
			Current = waiting.Dequeue();
			left = SecondsFor(Current.Text);
			Shown = 0f;
		}
	}
}

// one-off lines as subtitles, in order, typed out like the dialogue box. who's a name, an NPC's as well as a girl's.
// no voice and no pool of lines to choose from yet (STORY-2). each player shows their own: Say for this player alone
// (a sign only Chito can read), SayToBoth for what both should see, sent by whoever decides it. gone with the scene,
// and a friend coming back later doesn't get old lines
public class Speaker : MonoBehaviour
{
	public const string Chito = "Chito";

	public const string Yuuri = "Yuuri";

	private const float charsPerSecond = 45f;

	private static Speaker instance;

	private readonly LineQueue lines = new LineQueue();

	private TextMeshProUGUI text;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		NetRegistry.Register(NetRegistry.Kind.Line, OnMessage);
	}

	public static void Say(string who, string line)
	{
		if (instance == null)
		{
			instance = new GameObject("Speaker").AddComponent<Speaker>();
		}
		instance.lines.Add(who, line);
		instance.Refresh();
	}

	public static void SayToBoth(string who, string line)
	{
		NetRegistry.Send(NetRegistry.Kind.Line, NetRegistry.SessionId, delegate(NetDataWriter writer)
		{
			writer.Put(who);
			writer.Put(line);
		}, DeliveryMethod.ReliableOrdered);
		Say(who, line);
	}

	private static void OnMessage(ulong netId, NetDataReader reader)
	{
		string who = reader.GetString();
		Say(who, reader.GetString());
	}

	private static string NameColour(string who)
	{
		switch (who)
		{
		case Chito:
			return "#E8B48C";
		case Yuuri:
			return "#A8C8E8";
		default:
			return "#D8D8C8";
		}
	}

	private void Awake()
	{
		Canvas canvas = gameObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 50;
		UnityEngine.UI.CanvasScaler scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
		scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		GameObject child = new GameObject("Text", typeof(RectTransform));
		child.transform.SetParent(transform, worldPositionStays: false);
		text = child.AddComponent<TextMeshProUGUI>();
		text.font = CoopDowned.FindGameFont();
		text.fontSize = 26f;
		text.alignment = TextAlignmentOptions.Center;
		text.outlineWidth = 0.2f;
		text.outlineColor = Color.black;
		text.raycastTarget = false;
		RectTransform rect = text.rectTransform;
		rect.anchorMin = new Vector2(0.5f, 0f);
		rect.anchorMax = new Vector2(0.5f, 0f);
		rect.anchoredPosition = new Vector2(0f, 70f);
		rect.sizeDelta = new Vector2(1000f, 80f);
	}

	private string shownText;

	private void Update()
	{
		lines.Tick(Time.deltaTime);
		Refresh();
	}

	private void Refresh()
	{
		if (!lines.Showing)
		{
			if (shownText != null)
			{
				shownText = null;
				text.text = "";
			}
			return;
		}
		if (shownText != lines.Current.Text)
		{
			shownText = lines.Current.Text;
			text.text = "<color=" + NameColour(lines.Current.Who) + ">" + lines.Current.Who + "</color>  " + shownText;
		}
		// the name's there at once, the line types out after it
		text.maxVisibleCharacters = lines.Current.Who.Length + 2 + Mathf.FloorToInt(lines.Shown * charsPerSecond);
	}
}
