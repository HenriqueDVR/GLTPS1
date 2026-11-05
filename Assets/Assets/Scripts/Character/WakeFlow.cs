using System.Collections;
using LiteNetLib;
using LiteNetLib.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// both down, or nobody got to her: they fall asleep in the snow and wake at the last shelter next morning, the ket beside
// it, food and fuel down by about a day's worth (09). a setback, never a loss. the host or solo decides and pays, each
// player moves their own girl
public class WakeFlow : MonoBehaviour
{
	private const float fadeOut = 2f;

	private const float holdBlack = 2.5f;

	private const float fadeIn = 2f;

	private const float noteEvery = 1f;

	// the other girl wakes this far from the spot
	private const float besideBy = 1.5f;

	private const float navMeshWait = 10f;

	private const string line = "They fell asleep in the snow...";

	public static bool Busy { get; private set; }

	private static WakeFlow instance;

	private Image black;

	private TextMeshProUGUI words;

	private GameObject overlay;

	private float nextNote;

	private VehicleMovement ket;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		NetRegistry.Register(NetRegistry.Kind.Wake, OnMessage);
	}

	private void Awake()
	{
		instance = this;
		Busy = false;
		Camp.Reset();
	}

	private void OnDestroy()
	{
		if (instance == this)
		{
			instance = null;
			Busy = false;
		}
		if (overlay != null)
		{
			Destroy(overlay);
		}
	}

	private void Start()
	{
		ket = FindObjectOfType<VehicleMovement>();
		Canvas canvas = new GameObject("WakeFlow fade", typeof(Canvas)).GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 1001;
		black = new GameObject("black", typeof(Image)).GetComponent<Image>();
		black.transform.SetParent(canvas.transform, false);
		black.rectTransform.anchorMin = Vector2.zero;
		black.rectTransform.anchorMax = Vector2.one;
		black.rectTransform.offsetMin = Vector2.zero;
		black.rectTransform.offsetMax = Vector2.zero;
		black.raycastTarget = false;
		GameObject text = new GameObject("words", typeof(RectTransform));
		text.transform.SetParent(canvas.transform, false);
		words = text.AddComponent<TextMeshProUGUI>();
		words.font = CoopDowned.FindGameFont();
		words.fontSize = 34f;
		words.alignment = TextAlignmentOptions.Center;
		words.raycastTarget = false;
		words.rectTransform.anchorMin = Vector2.zero;
		words.rectTransform.anchorMax = Vector2.one;
		words.rectTransform.offsetMin = Vector2.zero;
		words.rectTransform.offsetMax = Vector2.zero;
		overlay = canvas.gameObject;
		overlay.SetActive(false);
		// the tunnel they set out from
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		if (world != null && world.Layer == 0 && Girls.Chito != null && ket != null && !StateRegistry.HasPending(GirlsSection.SectionKey))
		{
			LastShelter.SetIfNone(0, new LastShelter.Spot { Girl = Girls.Chito.transform.position + SaveScene.Offset(), Ket = ket.transform.position + SaveScene.Offset(), KetTurn = ket.transform.rotation });
		}
	}

	private void Update()
	{
		Camp.Tick();
		DuskHint();
		if (Time.time < nextNote || Busy || !NetRegistry.HostOrSolo || LayerTransition.Busy)
		{
			return;
		}
		nextNote = Time.time + noteEvery;
		foreach (Girl girl in Girls.All)
		{
			NoteIfCamped(girl);
		}
	}

	private DayPhase lastPhase = DayPhase.Day;

	private const double shelterHintReach = 1600.0;

	// as it starts getting dark, whoever's up says where the nearest roof is. no marker, you just know which way (17)
	private void DuskHint()
	{
		DayPhase phase = WorldClock.Phase;
		if (phase == lastPhase)
		{
			return;
		}
		bool dusk = phase == DayPhase.Dusk && lastPhase == DayPhase.Day;
		lastPhase = phase;
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		if (!dusk || !NetRegistry.HostOrSolo || world == null || Girls.Chito == null)
		{
			return;
		}
		Vector3 at = Girls.Chito.transform.position + SaveScene.Offset();
		XZ here = new XZ(at.x, at.z);
		if (!world.Plan.Places.Nearest(p => p.Shelter, here, shelterHintReach, out Place shelter))
		{
			Speaker.SayToBoth(Speaker.Chito, "It's getting dark, and there's nothing round here.");
			return;
		}
		Vector3 to = new Vector3((float)(shelter.Center.X - here.X), 0f, (float)(shelter.Center.Z - here.Z));
		int metres = Mathf.RoundToInt(to.magnitude / 50f) * 50;
		string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
		int way = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 360f) / 45f) % 8;
		Speaker.SayToBoth(Speaker.Chito, "It's getting dark. There's a roof about " + metres + " metres " + names[way] + ".");
	}

	// any roof with a lit fire
	private void NoteIfCamped(Girl girl)
	{
		if (girl.Items == null || !girl.Items.isUnderCeiling || girl.Items.FireHere() == null || ket == null)
		{
			return;
		}
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		LastShelter.Set(world.Layer, Here(girl));
	}

	public static LastShelter.Spot Here(Girl girl)
	{
		VehicleMovement vehicle = (instance != null) ? instance.ket : null;
		Vector3 offset = SaveScene.Offset();
		Vector3 ketAt = (vehicle != null) ? vehicle.transform.position : girl.transform.position;
		return new LastShelter.Spot { Girl = girl.transform.position + offset, Ket = ketAt + offset, KetTurn = (vehicle != null) ? vehicle.transform.rotation : Quaternion.identity };
	}

	// the host's or solo's call
	public static void Begin()
	{
		if (instance == null || Busy || !NetRegistry.HostOrSolo || LayerTransition.Busy)
		{
			return;
		}
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		bool known = LastShelter.Get(world.Layer, out LastShelter.Spot spot);
		// a layer reached and no camp on it yet: where they lie
		if (!known)
		{
			spot = Here(Girls.Local);
		}
		NetRegistry.Send(NetRegistry.Kind.Wake, NetRegistry.SessionId, delegate(NetDataWriter writer)
		{
			writer.Put(spot.Girl.x);
			writer.Put(spot.Girl.y);
			writer.Put(spot.Girl.z);
		}, DeliveryMethod.ReliableOrdered);
		instance.StartCoroutine(instance.Run(spot, true, known));
	}

	private static void OnMessage(ulong netId, NetDataReader reader)
	{
		Vector3 at = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
		if (instance != null && !NetRegistry.HostOrSolo && !Busy)
		{
			instance.StartCoroutine(instance.Run(new LastShelter.Spot { Girl = at }, false, true));
		}
	}

	private IEnumerator Run(LastShelter.Spot spot, bool deciding, bool moveKet)
	{
		Busy = true;
		overlay.SetActive(true);
		for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
		{
			Shade(t / fadeOut);
			yield return null;
		}
		Shade(1f);
		float until = Time.unscaledTime + holdBlack;
		Girl me = Girls.Local;
		Girl other = Girls.Other(me);
		bool solo = other != null && other.Control == GirlControl.AI;
		CharacterDeath death = FindObjectOfType<CharacterDeath>();
		if (death != null)
		{
			death.AllUp();
		}
		if (CoopDowned.Instance != null)
		{
			CoopDowned.Instance.Woke();
		}
		// the friend wakes beside the host's spot, not on it
		Vector3 mine = deciding ? spot.Girl : spot.Girl + Vector3.right * besideBy;
		yield return SaveScene.PlaceOnGround(me.transform, mine, me.transform.eulerAngles.y);
		if (deciding && moveKet)
		{
			SaveScene.PlaceVehicle(SaveScene.FromLayout(spot.Ket), spot.KetTurn);
			if (CoopKettenkrad.Instance != null)
			{
				CoopKettenkrad.Instance.SendCurrentPlace();
			}
		}
		if (solo)
		{
			yield return WakeAi(other, spot.Girl + Vector3.right * besideBy);
		}
		GirlNeeds.Raise(me.Items, NeedsRules.WokenLevel);
		if (solo)
		{
			GirlNeeds.Raise(other.Items, NeedsRules.WokenLevel);
		}
		if (deciding)
		{
			Pay(me);
		}
		while (Time.unscaledTime < until)
		{
			yield return null;
		}
		words.text = "";
		for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
		{
			black.color = new Color(0f, 0f, 0f, 1f - t / fadeIn);
			yield return null;
		}
		overlay.SetActive(false);
		Busy = false;
		if (deciding)
		{
			GameSave.SaveIfPossible();
		}
	}

	// both lying down by a fire under a roof: morning, warm, a bit hungry, the fire's burned out, autosaved. host or solo
	public static void Sleep()
	{
		if (instance == null || Busy || !NetRegistry.HostOrSolo || LayerTransition.Busy)
		{
			return;
		}
		Camp.SleepWord();
		instance.StartCoroutine(instance.NightPasses(true));
	}

	public static void SleptOnWord()
	{
		if (instance != null && !Busy)
		{
			instance.StartCoroutine(instance.NightPasses(false));
		}
	}

	private const float nightHungers = 12f;

	private const float nightFadeOut = 1.2f;

	private const float nightBlack = 2.6f;

	private const float nightFadeIn = 1.2f;

	private IEnumerator NightPasses(bool deciding)
	{
		Busy = true;
		overlay.SetActive(true);
		shown = "";
		for (float t = 0f; t < nightFadeOut; t += Time.unscaledDeltaTime)
		{
			Shade(t / nightFadeOut);
			yield return null;
		}
		Shade(1f);
		Girl me = Girls.Local;
		Girl other = Girls.Other(me);
		bool solo = other != null && other.Control == GirlControl.AI;
		Rested(me);
		if (solo)
		{
			Rested(other);
		}
		if (deciding)
		{
			WorldClock.Advance((NeedsRules.NextMorning(WorldClock.Total) - WorldClock.Total) * DayClock.DayLength);
		}
		Camp.PutFiresOut();
		yield return new WaitForSecondsRealtime(nightBlack);
		Camp.Up();
		for (float t = 0f; t < nightFadeIn; t += Time.unscaledDeltaTime)
		{
			black.color = new Color(0f, 0f, 0f, 1f - t / nightFadeIn);
			yield return null;
		}
		overlay.SetActive(false);
		shown = line;
		Busy = false;
		if (deciding)
		{
			GameSave.SaveIfPossible();
		}
	}

	private static void Rested(Girl girl)
	{
		girl.Items.temperatureLevel = 100f;
		girl.Items.hungerLevel = Mathf.Max(5f, girl.Items.hungerLevel - nightHungers);
		girl.Items.thirstLevel = Mathf.Max(5f, girl.Items.thirstLevel - nightHungers);
	}

	private string shown = line;

	private void Shade(float amount)
	{
		black.color = new Color(0f, 0f, 0f, amount);
		words.text = shown;
		words.color = new Color(1f, 1f, 1f, amount);
	}

	private static void Pay(Girl girl)
	{
		CharacterStatus status = FindObjectOfType<CharacterStatus>();
		girl.Items.SetFood(NeedsRules.FoodAfterNight(status.index));
		VehicleFuelManager fuel = FindObjectOfType<VehicleFuelManager>();
		fuel.currentFuel = NeedsRules.FuelAfterNight(fuel.currentFuel, fuel.maxFuel);
		WorldClock.Advance((NeedsRules.NextMorning(WorldClock.Total) - WorldClock.Total) * DayClock.DayLength);
	}

	// held by the spot till the navmesh has come in round it, her agent can't stand on nothing
	private static IEnumerator WakeAi(Girl girl, Vector3 layout)
	{
		bool agentOn = girl.Agent.enabled;
		girl.Agent.enabled = false;
		float giveUp = Time.unscaledTime + navMeshWait;
		NavMeshHit hit;
		while (true)
		{
			Vector3 at = SaveScene.FromLayout(layout);
			girl.transform.position = at;
			if (NavMesh.SamplePosition(at, out hit, 3f, NavMesh.AllAreas) || Time.unscaledTime > giveUp)
			{
				break;
			}
			yield return null;
		}
		girl.Agent.enabled = agentOn;
		if (agentOn && hit.hit)
		{
			girl.Agent.Warp(hit.position);
		}
	}
}
