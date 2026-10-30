using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// world gen v2: the voids (ChasmPlan) go a long way down. whoever falls in is put back where they last stood on solid
// deck, behind a short fade: a setback, not a punishment (00, pillars). each peer looks after its own girl, and the
// ket where its body is simulated (the driver's side in co-op, so the driver and whoever rides go back with it).
// an AI girl who falls is put by the girl she follows
public class SafeGround : MonoBehaviour
{
	// this far under the deck and she's gone
	private const float fallDepth = 20f;

	private const float keepInterval = 0.5f;

	// safe spots stay this far from a void's edge
	private const double margin = 6.0;

	private const float fadeOut = 0.35f;

	private const float fadeIn = 0.6f;

	private LayerPlan plan;

	private TileStreamer tiles;

	private FloatingOrigin floatingOrigin;

	private VehicleMovement ket;

	private Rigidbody ketBody;

	// layout coords
	private Vector3 girlSafe;

	private bool haveGirl;

	private Vector3 ketSafe;

	private Quaternion ketTurn;

	private bool haveKet;

	private float nextKeep;

	private bool busy;

	private Image black;

	private GameObject overlay;

	public void Init(LayerPlan plan, TileStreamer tiles, FloatingOrigin origin)
	{
		this.plan = plan;
		this.tiles = tiles;
		floatingOrigin = origin;
	}

	private void Start()
	{
		ket = FindObjectOfType<VehicleMovement>();
		ketBody = (ket != null) ? ket.GetComponent<Rigidbody>() : null;
		Canvas canvas = new GameObject("SafeGround fade", typeof(Canvas)).GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 1000;
		black = new GameObject("black", typeof(Image)).GetComponent<Image>();
		black.transform.SetParent(canvas.transform, false);
		black.rectTransform.anchorMin = Vector2.zero;
		black.rectTransform.anchorMax = Vector2.one;
		black.rectTransform.offsetMin = Vector2.zero;
		black.rectTransform.offsetMax = Vector2.zero;
		black.color = Color.clear;
		black.raycastTarget = false;
		overlay = canvas.gameObject;
		overlay.SetActive(false);
	}

	private Vector3 Offset()
	{
		return (floatingOrigin != null) ? floatingOrigin.TotalOffset : Vector3.zero;
	}

	private bool Simulated => ketBody != null && !ketBody.isKinematic;

	private void Update()
	{
		Girl girl = Girls.Local;
		if (plan == null || girl == null || busy)
		{
			return;
		}
		float deck = tiles.GroundY;
		bool driving = Driving(girl);
		Vector3 at = girl.transform.position + Offset();
		if (Time.time >= nextKeep)
		{
			nextKeep = Time.time + keepInterval;
			Keep(girl, at, driving, deck);
		}
		if (at.y < deck - fallDepth)
		{
			StartCoroutine(PutBack(girl, driving));
			return;
		}
		// the ket on its own, nobody here driving it
		if (Simulated && !driving && haveKet && ket.transform.position.y + Offset().y < deck - fallDepth)
		{
			PlaceKet();
		}
		Girl other = Girls.Other(girl);
		if (other != null && other.Control == GirlControl.AI && other.Agent != null && other.transform.position.y + Offset().y < deck - fallDepth)
		{
			other.Agent.Warp(girl.transform.position);
		}
	}

	private static bool Driving(Girl girl)
	{
		if (CoopKettenkrad.Instance != null)
		{
			return CoopKettenkrad.Instance.LocalSeat != CoopKettenkrad.SeatState.Off;
		}
		CharacterVehicleInteraction vehicle = girl.GetComponentInChildren<CharacterVehicleInteraction>();
		return vehicle != null && vehicle.IsDriving;
	}

	// remembers where she and the ket stand while they're on solid deck, well away from a void
	private void Keep(Girl girl, Vector3 at, bool driving, float deck)
	{
		if (!driving && Mathf.Abs(at.y - deck) < 30f && Grounded(girl.transform.position) && plan.Chasms.Inside(at.x, at.z) < -margin)
		{
			girlSafe = at;
			haveGirl = true;
		}
		if (Simulated && ket.isGrounded)
		{
			Vector3 k = ket.transform.position + Offset();
			if (Mathf.Abs(k.y - deck) < 30f && plan.Chasms.Inside(k.x, k.z) < -(margin + 2.0))
			{
				ketSafe = k;
				ketTurn = Quaternion.Euler(0f, ket.transform.eulerAngles.y, 0f);
				haveKet = true;
			}
		}
	}

	private static bool Grounded(Vector3 position)
	{
		return Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
	}

	private IEnumerator PutBack(Girl girl, bool driving)
	{
		busy = true;
		overlay.SetActive(true);
		for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
		{
			black.color = new Color(0f, 0f, 0f, t / fadeOut);
			yield return null;
		}
		black.color = Color.black;
#if DUMP_WORLDGEN
		Debug.Log("SafeGround: fell at " + (girl.transform.position + Offset()) + (driving ? ", ket back to " + ketSafe : ", back to " + girlSafe));
#endif
		if (driving && Simulated && haveKet)
		{
			PlaceKet();
		}
		else if (!driving && haveGirl)
		{
			CharacterController controller = girl.GetComponentInChildren<CharacterController>();
			if (controller != null)
			{
				controller.enabled = false;
			}
			girl.transform.position = girlSafe - Offset() + Vector3.up * 0.3f;
			Physics.SyncTransforms();
			if (controller != null)
			{
				controller.enabled = true;
			}
		}
		// riding on the other player's ket: theirs is put back on their side
		yield return new WaitForSecondsRealtime(0.2f);
		for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
		{
			black.color = new Color(0f, 0f, 0f, 1f - t / fadeIn);
			yield return null;
		}
		black.color = Color.clear;
		overlay.SetActive(false);
		busy = false;
	}

	private void PlaceKet()
	{
#if DUMP_WORLDGEN
		Debug.Log("SafeGround: ket back to " + ketSafe);
#endif
		ketBody.velocity = Vector3.zero;
		ketBody.angularVelocity = Vector3.zero;
		ketBody.position = ketSafe - Offset() + Vector3.up * 0.5f;
		ketBody.rotation = ketTurn;
		ket.transform.SetPositionAndRotation(ketBody.position, ketBody.rotation);
		Physics.SyncTransforms();
		ketBody.WakeUp();
	}
}
