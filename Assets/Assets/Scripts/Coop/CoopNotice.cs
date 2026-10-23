using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CoopNotice : MonoBehaviour
{
	private const float shownFor = 4f;

	private static CoopNotice instance;

	private TextMeshProUGUI text;

	private float hideAt;

	public static void Show(string message)
	{
		if (instance == null)
		{
			instance = new GameObject("CoopNotice").AddComponent<CoopNotice>();
		}
		instance.text.text = message;
#if DUMP_WORLDGEN
		Debug.Log("CoopNotice " + message);
#endif
		instance.hideAt = Time.unscaledTime + shownFor;
	}

	private void Awake()
	{
		Canvas canvas = base.gameObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 51;
		CanvasScaler canvasScaler = base.gameObject.AddComponent<CanvasScaler>();
		canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		canvasScaler.referenceResolution = new Vector2(1280f, 720f);
		GameObject gameObject = new GameObject("Text", typeof(RectTransform));
		gameObject.transform.SetParent(base.transform, worldPositionStays: false);
		text = gameObject.AddComponent<TextMeshProUGUI>();
		text.font = CoopDowned.FindGameFont();
		text.fontSize = 26f;
		text.alignment = TextAlignmentOptions.Center;
		text.outlineWidth = 0.2f;
		text.outlineColor = Color.black;
		text.raycastTarget = false;
		RectTransform rectTransform = text.rectTransform;
		rectTransform.anchorMin = new Vector2(0.5f, 1f);
		rectTransform.anchorMax = new Vector2(0.5f, 1f);
		rectTransform.anchoredPosition = new Vector2(0f, -120f);
		rectTransform.sizeDelta = new Vector2(1000f, 60f);
	}

	private void Update()
	{
		if (text.text != "" && Time.unscaledTime >= hideAt)
		{
			text.text = "";
		}
	}
}
