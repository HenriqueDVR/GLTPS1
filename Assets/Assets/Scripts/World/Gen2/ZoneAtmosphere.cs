using UnityEngine;

// world gen v2: the district the local girl is in thickens or thins the fog (DistrictDef.Fog) and pulls its colour toward
// its hue (DistrictDef.Tint). eased, so crossing into the next district there's no step. AtmosphereStack puts it together
// with the rest. local only, each peer its own
public class ZoneAtmosphere : MonoBehaviour
{
	// a share a second
	private const float rate = 0.15f;

	private LayerPlan plan;

	private FloatingOrigin floatingOrigin;

	private float scale = -1f;

	private Color tint = Color.clear;

	public void Init(LayerPlan plan, FloatingOrigin origin)
	{
		this.plan = plan;
		floatingOrigin = origin;
	}

	private void LateUpdate()
	{
		Girl girl = Girls.Local;
		AtmosphereStack stack = GameSession.Get<AtmosphereStack>();
		if (plan == null || girl == null || stack == null)
		{
			return;
		}
		Vector3 at = girl.transform.position + ((floatingOrigin != null) ? floatingOrigin.TotalOffset : Vector3.zero);
		DistrictDef district = plan.Def.Districts[plan.DistrictAt(at.x, at.z)];
		float target = (float)district.Fog;
		scale = (scale < 0f) ? target : Mathf.MoveTowards(scale, target, rate * Time.deltaTime);
		Color hue = new Color(((district.Tint >> 16) & 0xFF) / 255f, ((district.Tint >> 8) & 0xFF) / 255f, (district.Tint & 0xFF) / 255f, 1f);
		tint = (tint.a <= 0f) ? hue : new Color(Mathf.MoveTowards(tint.r, hue.r, rate * Time.deltaTime), Mathf.MoveTowards(tint.g, hue.g, rate * Time.deltaTime), Mathf.MoveTowards(tint.b, hue.b, rate * Time.deltaTime), 1f);
		stack.ZoneDensity = scale;
		stack.ZoneTint = tint;
	}
}
