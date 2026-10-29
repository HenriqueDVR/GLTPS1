using System.Collections.Generic;
using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// coop death: girl goes down instead and the partner revives her (hold interact).
// costs a ration / water / a campfire depending on what ran out. both down = game over
public class CoopDowned : MonoBehaviour
{
	private enum Need
	{
		Food,
		Water,
		Warmth
	}

	private const float reviveReach = 2.5f;

	private const float reviveDuration = 3f;

	private const float waterCost = 20f;

	private const float campfireReach = 8f;

	private const float revivedLevel = 25f;

	private const float gameOverDelay = 6f;

	private static readonly NetDataWriter writer = new NetDataWriter();

	private CoopSession session;

	private CharacterStatus status;

	private bool localDown;

	private bool remoteDown;

	private float reviveProgress;

	private float gameOverAt = -1f;

	private TextMeshProUGUI waypoint;

	private TextMeshProUGUI message;

	private Image progressBar;

	private RectTransform mapMarker;

	private Image mapMarkerIcon;

	private TextMeshProUGUI mapMarkerLabel;

	public static CoopDowned Instance { get; private set; }

	public bool LocalDown => localDown;

	public bool RemoteDown => remoteDown;

	private void Awake()
	{
		Instance = this;
		session = CoopSession.Instance;
	}

	private void Start()
	{
		status = Object.FindObjectOfType<CharacterStatus>();
		BuildHud();
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void Update()
	{
		CoopPlayers instance = CoopPlayers.Instance;
		if (instance == null || instance.Local == null || instance.Remote == null)
		{
			return;
		}
		CharacterItemInteraction local = Girl(instance.Local);
		if (!localDown && OutOfSomething(local) && CanGoDownHere())
		{
			GoDown();
		}
		UpdateRevive(instance, local);
		UpdateHud(instance);
		if (session.IsHost && localDown && remoteDown)
		{
			if (gameOverAt < 0f)
			{
				gameOverAt = Time.unscaledTime + gameOverDelay;
			}
			else if (Time.unscaledTime >= gameOverAt)
			{
				gameOverAt = float.MaxValue;
				SceneManager.LoadScene(Scenes.Title);
			}
		}
	}

	private static bool OutOfSomething(CharacterItemInteraction girl)
	{
		if (!(girl.thirstLevel <= 0f) && !(girl.hungerLevel <= 0f))
		{
			if (girl.temperatureLevel <= 0f)
			{
				return !girl.anim.GetCurrentAnimatorStateInfo(1).IsTag("Ket");
			}
			return false;
		}
		return true;
	}

	// not while driving, she goes down once she gets out
	private static bool CanGoDownHere()
	{
		CoopKettenkrad instance = CoopKettenkrad.Instance;
		if (instance != null)
		{
			CoopKettenkrad.SeatState localSeat = instance.LocalSeat;
			if (localSeat == CoopKettenkrad.SeatState.Boarding || localSeat == CoopKettenkrad.SeatState.Driver)
			{
				return false;
			}
		}
		return true;
	}

	private void GoDown()
	{
		CoopPlayers instance = CoopPlayers.Instance;
		Transform local = instance.Local;
		localDown = true;
		if (CoopKettenkrad.Instance != null)
		{
			CoopKettenkrad.Instance.GetOff();
		}
		CharacterBarrelInteraction component = local.GetComponent<CharacterBarrelInteraction>();
		if (component != null && component.isCarrying)
		{
			component.DropBarrel();
		}
		// disabling PlayerMovement kills the controls too
		local.GetComponent<PlayerMovement>().enabled = false;
		local.GetComponentInChildren<Animator>().SetTrigger("die");
		FollowWithCamera(instance.Remote);
		Send(CoopSession.MessageType.Downed);
	}

	private void GetUp()
	{
		CoopPlayers instance = CoopPlayers.Instance;
		Transform local = instance.Local;
		CharacterItemInteraction characterItemInteraction = Girl(local);
		characterItemInteraction.hungerLevel = Mathf.Max(characterItemInteraction.hungerLevel, revivedLevel);
		characterItemInteraction.thirstLevel = Mathf.Max(characterItemInteraction.thirstLevel, revivedLevel);
		characterItemInteraction.temperatureLevel = Mathf.Max(characterItemInteraction.temperatureLevel, revivedLevel);
		Animator componentInChildren = local.GetComponentInChildren<Animator>();
		// death anim has no exit so force it back
		componentInChildren.Rebind();
		componentInChildren.Update(0f);
		local.GetComponent<PlayerMovement>().enabled = true;
		FollowWithCamera(local);
		localDown = false;
	}

	private static void FollowWithCamera(Transform girl)
	{
		CameraRig rig = GameSession.Get<CameraRig>();
		if (rig != null)
		{
			rig.Watch(girl);
		}
	}

	private List<Need> Needs(CharacterItemInteraction girl)
	{
		List<Need> list = new List<Need>();
		if (girl.hungerLevel <= 0f)
		{
			list.Add(Need.Food);
		}
		if (girl.thirstLevel <= 0f)
		{
			list.Add(Need.Water);
		}
		if (girl.temperatureLevel <= 0f)
		{
			list.Add(Need.Warmth);
		}
		return list;
	}

	private bool Has(Need need, Vector3 body)
	{
		switch (need)
		{
		case Need.Food:
			return status.index > 0;
		case Need.Water:
			return status.waterLevel >= waterCost;
		default:
		{
			CampfireFuelLevel[] array = Object.FindObjectsOfType<CampfireFuelLevel>();
			foreach (CampfireFuelLevel campfireFuelLevel in array)
			{
				if (campfireFuelLevel.currentFuelLevel > 0f && Vector3.Distance(campfireFuelLevel.transform.position, body) < campfireReach)
				{
					return true;
				}
			}
			return false;
		}
		}
	}

	private static string Describe(Need need)
	{
		switch (need)
		{
		case Need.Food:
			return "a ration";
		case Need.Water:
			return "water";
		default:
			return "a lit campfire beside her";
		}
	}

	private void UpdateRevive(CoopPlayers players, CharacterItemInteraction local)
	{
		if (!remoteDown || localDown || !CanReach(players))
		{
			reviveProgress = 0f;
			return;
		}
		Vector3 position = players.Remote.position;
		foreach (Need item in Needs(Girl(players.Remote)))
		{
			if (!Has(item, position))
			{
				reviveProgress = 0f;
				return;
			}
		}
		if (!InteractHeld(players))
		{
			reviveProgress = Mathf.Max(0f, reviveProgress - Time.deltaTime * 2f);
			return;
		}
		reviveProgress += Time.deltaTime;
		if (reviveProgress >= reviveDuration)
		{
			reviveProgress = 0f;
			foreach (Need item2 in Needs(Girl(players.Remote)))
			{
				if (item2 == Need.Food)
				{
					local.RemoveFood(1);
				}
				else if (item2 == Need.Water)
				{
					status.waterLevel -= waterCost;
				}
			}
			remoteDown = false;
			Send(CoopSession.MessageType.Revived);
		}
	}

	private static bool CanReach(CoopPlayers players)
	{
		return Vector3.Distance(players.Local.position, players.Remote.position) < reviveReach;
	}

	private static bool InteractHeld(CoopPlayers players)
	{
#if DUMP_WORLDGEN
		if (DebugHoldInteract)
		{
			return true;
		}
#endif
		PlayerInputInitialize component = players.Local.GetComponent<PlayerInputInitialize>();
		if (component.actions.Player.enabled)
		{
			return component.actions.Player.Interact.IsPressed();
		}
		return false;
	}

	private static void Send(CoopSession.MessageType type)
	{
		writer.Reset();
		writer.Put((byte)type);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	public void OnMessage(CoopSession.MessageType type)
	{
		if (type == CoopSession.MessageType.Downed)
		{
			remoteDown = true;
		}
		else if (localDown)
		{
			GetUp();
		}
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug downed: the other player sent " + type);
#endif
	}

	private static CharacterItemInteraction Girl(Transform girl)
	{
		return girl.GetComponentInChildren<CharacterItemInteraction>(includeInactive: true);
	}

	private static string NameOf(Transform girl)
	{
		return girl.name.Replace("Parent", "");
	}

	private string NeedsText(List<Need> needs)
	{
		if (needs.Count == 0)
		{
			return "nothing more";
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < needs.Count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append((i == needs.Count - 1) ? " and " : ", ");
			}
			stringBuilder.Append(Describe(needs[i]));
		}
		return stringBuilder.ToString();
	}

	private void BuildHud()
	{
		GameObject gameObject = new GameObject("CoopDownedHud");
		gameObject.transform.SetParent(base.transform, worldPositionStays: false);
		Canvas canvas = gameObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 50;
		CanvasScaler canvasScaler = gameObject.AddComponent<CanvasScaler>();
		canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		canvasScaler.referenceResolution = new Vector2(1280f, 720f);
		TMP_FontAsset font = FindGameFont();
		waypoint = MakeText(gameObject.transform, "Waypoint", font, 22f);
		waypoint.rectTransform.sizeDelta = new Vector2(420f, 120f);
		message = MakeText(gameObject.transform, "Message", font, 26f);
		RectTransform rectTransform = message.rectTransform;
		rectTransform.anchorMin = new Vector2(0.5f, 0f);
		rectTransform.anchorMax = new Vector2(0.5f, 0f);
		rectTransform.anchoredPosition = new Vector2(0f, 150f);
		rectTransform.sizeDelta = new Vector2(900f, 80f);
		GameObject gameObject2 = new GameObject("ReviveProgress", typeof(RectTransform), typeof(Image));
		gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
		progressBar = gameObject2.GetComponent<Image>();
		progressBar.color = new Color(1f, 1f, 1f, 0.85f);
		RectTransform rectTransform2 = progressBar.rectTransform;
		rectTransform2.anchorMin = new Vector2(0.5f, 0f);
		rectTransform2.anchorMax = new Vector2(0.5f, 0f);
		rectTransform2.pivot = new Vector2(0f, 0.5f);
		rectTransform2.anchoredPosition = new Vector2(-150f, 110f);
		rectTransform2.sizeDelta = new Vector2(0f, 8f);
	}

	public static TMP_FontAsset FindGameFont()
	{
		TextMeshProUGUI[] array = Object.FindObjectsOfType<TextMeshProUGUI>(includeInactive: true);
		foreach (TextMeshProUGUI textMeshProUGUI in array)
		{
			if (textMeshProUGUI.font != null)
			{
				return textMeshProUGUI.font;
			}
		}
		return TMP_Settings.defaultFontAsset;
	}

	private static TextMeshProUGUI MakeText(Transform parent, string name, TMP_FontAsset font, float size)
	{
		GameObject gameObject = new GameObject(name, typeof(RectTransform));
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		TextMeshProUGUI textMeshProUGUI = gameObject.AddComponent<TextMeshProUGUI>();
		textMeshProUGUI.font = font;
		textMeshProUGUI.fontSize = size;
		textMeshProUGUI.alignment = TextAlignmentOptions.Center;
		textMeshProUGUI.color = Color.white;
		textMeshProUGUI.outlineWidth = 0.2f;
		textMeshProUGUI.outlineColor = Color.black;
		textMeshProUGUI.raycastTarget = false;
		return textMeshProUGUI;
	}

	private void UpdateHud(CoopPlayers players)
	{
		if (waypoint == null)
		{
			return;
		}
		string text = NameOf(players.Remote);
		string text2 = "";
		float x = 0f;
		if (localDown)
		{
			List<Need> needs = Needs(Girl(players.Local));
			text2 = "You're down. " + text + " can revive you with " + NeedsText(needs) + ".";
		}
		else if (remoteDown)
		{
			List<Need> needs2 = Needs(Girl(players.Remote));
			if (CanReach(players))
			{
				List<Need> list = needs2.FindAll((Need n) => !Has(n, players.Remote.position));
				text2 = ((list.Count > 0) ? ("To revive " + text + " you need " + NeedsText(list) + ".") : ("Hold interact to revive " + text + "."));
				x = 300f * (reviveProgress / reviveDuration);
			}
		}
		message.text = text2;
		progressBar.rectTransform.sizeDelta = new Vector2(x, 8f);
		UpdateWaypoint(players, text);
		UpdateMapMarker(players, text);
	}

	private void UpdateWaypoint(CoopPlayers players, string name)
	{
		Camera main = Camera.main;
		if (!remoteDown || localDown || main == null || CanReach(players))
		{
			waypoint.text = "";
			return;
		}
		Vector3 vector = players.Remote.position + Vector3.up * 2.2f;
		Vector3 vector2 = main.WorldToScreenPoint(vector);
		if (vector2.z < 0f)
		{
			vector2 = new Vector3(Screen.width - vector2.x, 0f, 0f);
		}
		float num = 60f;
		vector2.x = Mathf.Clamp(vector2.x, num * 2f, (float)Screen.width - num * 2f);
		vector2.y = Mathf.Clamp(vector2.y, num, (float)Screen.height - num);
		waypoint.transform.position = new Vector3(vector2.x, vector2.y, 0f);
		waypoint.text = name + " is down\nneeds " + NeedsText(Needs(Girl(players.Remote))) + "\n" + Mathf.RoundToInt(Vector3.Distance(players.Local.position, players.Remote.position)) + " m";
	}

	// map is a screenshot taken when it's opened
	private void UpdateMapMarker(CoopPlayers players, string name)
	{
		PlayerMapController playerMapController = null;
		PlayerMapController[] array = Object.FindObjectsOfType<PlayerMapController>(includeInactive: true);
		foreach (PlayerMapController playerMapController2 in array)
		{
			if (playerMapController2.isMapEnabled)
			{
				playerMapController = playerMapController2;
			}
		}
		bool flag = remoteDown && !localDown && playerMapController != null;
		if (mapMarker == null && flag)
		{
			BuildMapMarker(playerMapController.MapImage);
		}
		if (mapMarker == null)
		{
			return;
		}
		mapMarker.gameObject.SetActive(flag);
		if (flag)
		{
			RawImage mapImage = playerMapController.MapImage;
			Vector4 vector = playerMapController.RenderedViewProjection * new Vector4(players.Remote.position.x, players.Remote.position.y, players.Remote.position.z, 1f);
			Vector2 vector2 = ((vector.w != 0f) ? new Vector2(vector.x / vector.w, vector.y / vector.w) : Vector2.zero);
			// clamp to edge
			vector2.x = Mathf.Clamp(vector2.x, -0.95f, 0.95f);
			vector2.y = Mathf.Clamp(vector2.y, -0.95f, 0.95f);
			Rect rect = mapImage.rectTransform.rect;
			mapMarker.anchoredPosition = new Vector2(vector2.x * rect.width * 0.5f, vector2.y * rect.height * 0.5f);
			float a = mapImage.color.a;
			mapMarkerIcon.color = new Color(0.9f, 0.15f, 0.1f, a);
			mapMarkerLabel.color = new Color(1f, 1f, 1f, a);
			mapMarkerLabel.text = name;
		}
	}

	private void BuildMapMarker(RawImage mapImage)
	{
		GameObject gameObject = new GameObject("CoopDownedMarker", typeof(RectTransform));
		gameObject.transform.SetParent(mapImage.transform, worldPositionStays: false);
		mapMarker = gameObject.GetComponent<RectTransform>();
		mapMarker.sizeDelta = Vector2.zero;
		GameObject gameObject2 = new GameObject("Icon", typeof(RectTransform), typeof(Image));
		gameObject2.transform.SetParent(gameObject.transform, worldPositionStays: false);
		mapMarkerIcon = gameObject2.GetComponent<Image>();
		mapMarkerIcon.raycastTarget = false;
		mapMarkerIcon.rectTransform.sizeDelta = new Vector2(14f, 14f);
		mapMarkerIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
		mapMarkerLabel = MakeText(gameObject.transform, "Label", waypoint.font, 18f);
		mapMarkerLabel.rectTransform.anchoredPosition = new Vector2(0f, 18f);
		mapMarkerLabel.rectTransform.sizeDelta = new Vector2(200f, 30f);
	}

#if DUMP_WORLDGEN
	public static bool DebugHoldInteract;

	public bool DebugRemoteDown => remoteDown;

	public float DebugProgress => reviveProgress;
#endif
}
