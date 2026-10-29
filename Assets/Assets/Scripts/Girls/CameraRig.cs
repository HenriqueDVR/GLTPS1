using Cinemachine;
using UnityEngine;
using UnityEngine.Animations;

// follows the played girl: the camera's follow and look-at targets, and the things that sit on the player
// (sky, weather particles, out-of-bounds box, map camera). the scene wires all of them on CharacterPossession
public class CameraRig : MonoBehaviour
{
	// seconds the look speed takes to catch up with the mouse. the scene had 10 s up and 0.4 s down:
	// a half second flick turned the camera only ~11% before the mouse stopped, then it drifted on
	private const float lookAccelTime = 0.05f;

	private const float lookDecelTime = 0.05f;

	private CharacterPossession wiring;

	private CharacterPossession Wiring
	{
		get
		{
			if (wiring == null && Girls.Chito != null)
			{
				wiring = Girls.Chito.Possession;
			}
			return wiring;
		}
	}

	private ConstraintSource source;

	private void Start()
	{
		CinemachineFreeLook freeLook = FindObjectOfType<CinemachineFreeLook>(true);
		if (freeLook != null)
		{
			Responsive(ref freeLook.m_XAxis);
			Responsive(ref freeLook.m_YAxis);
		}
	}

	private static void Responsive(ref AxisState axis)
	{
		axis.m_AccelTime = lookAccelTime;
		axis.m_DecelTime = lookDecelTime;
	}

	private void OnEnable()
	{
		Girls.LocalChanged += Follow;
	}

	private void OnDisable()
	{
		Girls.LocalChanged -= Follow;
	}

	public void Follow(Girl girl)
	{
		CharacterPossession scene = Wiring;
		if (scene == null || girl == null)
		{
			return;
		}
		scene.NodeData.player = girl.gameObject;
		source.sourceTransform = girl.transform;
		source.weight = 1f;
		Anchor(scene.OutOfBoundsBox);
		Anchor(scene.Skybox);
		Anchor(scene.WeatherParticles);
		Anchor(scene.MapCamera);
		Watch(girl.transform);
	}

	// only the camera, e.g. a downed player watching their partner
	public void Watch(Transform girl)
	{
		CharacterPossession scene = Wiring;
		if (scene == null)
		{
			return;
		}
		Transform follow = scene.FollowTarget.transform;
		Transform lookAt = scene.LookAtTarget.transform;
		follow.parent = girl;
		lookAt.parent = girl;
		follow.localPosition = scene.FollowTargetOffset;
		lookAt.localPosition = scene.LookAtTargetOffset;
		follow.rotation = girl.rotation;
		lookAt.rotation = girl.rotation;
	}

	private void Anchor(PositionConstraint constraint)
	{
		if (constraint != null)
		{
			constraint.SetSource(0, source);
			constraint.constraintActive = true;
		}
	}
}
