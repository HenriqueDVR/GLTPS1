using System.Collections;
using LiteNetLib;
using LiteNetLib.Utils;
using TMPro;
using UnityEngine;

// whoever talks to him, both get the map/camera and both hear the lines.
// friend's kanazawas just follow the host's
public class CoopKanazawa : MonoBehaviour
{
	private enum Op : byte
	{
		UnlockMap,
		UnlockCamera,
		Open,
		Line,
		Close
	}

	private static readonly NetDataWriter writer = new NetDataWriter();

	private const float poseInterval = 0.15f;

	private const float walkSpeed = 7f;

	private float nextPoses;

	private class Follower
	{
		public Transform Body;

		public Animator Animator;

		public Vector3 TargetLayout;

		public float TargetYaw;

		public Vector3 Velocity;

		public float ReceivedAt;

		public float Speed;

		public bool HasTarget;
	}

	private static readonly System.Collections.Generic.Dictionary<ulong, Follower> followers = new System.Collections.Generic.Dictionary<ulong, Follower>();

	private const float sendRange = 150f;

	private PlayerMapController map;

	private PlayerCameraController photoCamera;

	private bool mapWasOn;

	private bool cameraWasOn;

	private TextMeshProUGUI dialogueText;

	private Animator dialogueBox;

	public static CoopKanazawa Instance { get; private set; }

	private void Awake()
	{
		Instance = this;
		followers.Clear();
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
		if (CoopSession.Instance.IsHost)
		{
			SendPoses();
		}
		else
		{
			FollowHost();
		}
		if (map == null)
		{
			if (CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
			{
				return;
			}
			map = CoopPlayers.Instance.Local.GetComponent<PlayerMapController>();
			photoCamera = CoopPlayers.Instance.Local.GetComponent<PlayerCameraController>();
			mapWasOn = map.enabled;
			cameraWasOn = photoCamera.enabled;
		}
		// UnlockDialogueItem turns these on mid dialogue
		if (map.enabled && !mapWasOn)
		{
			mapWasOn = true;
			Send(Op.UnlockMap);
		}
		if (photoCamera.enabled && !cameraWasOn)
		{
			cameraWasOn = true;
			Send(Op.UnlockCamera);
		}
	}

	// same seed, so the path from his piece identifies him on both sides
	private static ulong Key(Transform kanazawa)
	{
		string path = kanazawa.name;
		for (Transform t = kanazawa.parent; t != null; t = t.parent)
		{
			path = t.name + "/" + path;
		}
		return WorldLayout.Hash(path);
	}

	private static bool NearAGirl(Transform kanazawa)
	{
		CoopPlayers players = CoopPlayers.Instance;
		return Vector3.Distance(kanazawa.position, players.Local.position) < sendRange || (players.Remote != null && Vector3.Distance(kanazawa.position, players.Remote.position) < sendRange);
	}

	private void SendPoses()
	{
		if (Time.unscaledTime < nextPoses || !CoopSession.Together || CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null)
		{
			return;
		}
		nextPoses = Time.unscaledTime + poseInterval;
		GameObject[] all = GameObject.FindGameObjectsWithTag("Kanazawa");
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.KanazawaPoses);
		System.Collections.Generic.List<GameObject> near = new System.Collections.Generic.List<GameObject>();
		foreach (GameObject kanazawa in all)
		{
			if (kanazawa.GetComponent<NavMeshAgentPatrol>() != null && NearAGirl(kanazawa.transform) && near.Count < 40)
			{
				near.Add(kanazawa);
			}
		}
		if (near.Count == 0)
		{
			return;
		}
		writer.Put((byte)near.Count);
		foreach (GameObject kanazawa in near)
		{
			Vector3 layout = CoopPlayers.Instance.ToLayout(kanazawa.transform.position);
			writer.Put(Key(kanazawa.transform));
			writer.Put(layout.x);
			writer.Put(layout.y);
			writer.Put(layout.z);
			writer.Put(kanazawa.transform.eulerAngles.y);
			NavMeshAgentPatrol patrol = kanazawa.GetComponent<NavMeshAgentPatrol>();
			Vector3 velocity = (patrol.agent != null && patrol.agent.enabled) ? patrol.agent.velocity : Vector3.zero;
			writer.Put(velocity.x);
			writer.Put(velocity.z);
		}
		CoopSession.Instance.Send(writer, DeliveryMethod.Sequenced);
	}

	public static void OnPoses(NetDataReader reader)
	{
		int count = reader.GetByte();
		for (int i = 0; i < count; i++)
		{
			ulong key = reader.GetULong();
			Vector3 layout = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
			float yaw = reader.GetFloat();
			Vector3 velocity = new Vector3(reader.GetFloat(), 0f, reader.GetFloat());
			Follower follower = Find(key);
			if (follower != null)
			{
				follower.TargetLayout = layout;
				follower.TargetYaw = yaw;
				follower.Velocity = velocity;
				follower.ReceivedAt = Time.time;
				follower.HasTarget = true;
			}
		}
	}

	private static Follower Find(ulong key)
	{
		if (followers.TryGetValue(key, out Follower found) && found.Body != null)
		{
			return found;
		}
		foreach (GameObject kanazawa in GameObject.FindGameObjectsWithTag("Kanazawa"))
		{
			if (Key(kanazawa.transform) != key)
			{
				continue;
			}
			NavMeshAgentPatrol patrol = kanazawa.GetComponent<NavMeshAgentPatrol>();
			Animator animator = (patrol != null && patrol.AgentAnimator != null) ? patrol.AgentAnimator : kanazawa.GetComponentInChildren<Animator>();
			if (patrol != null)
			{
				patrol.enabled = false;
				if (patrol.agent != null)
				{
					patrol.agent.enabled = false;
				}
			}
			Follower follower = new Follower
			{
				Body = kanazawa.transform,
				Animator = animator
			};
			followers[key] = follower;
#if DUMP_WORLDGEN
			Debug.Log("CoopDebug following the host's Kanazawa " + key);
#endif
			return follower;
		}
		return null;
	}

#if DUMP_WORLDGEN
	private static float nextFollowLog;
#endif

	private static void FollowHost()
	{
		if (CoopPlayers.Instance == null)
		{
			return;
		}
#if DUMP_WORLDGEN
		if (Time.unscaledTime >= nextFollowLog && followers.Count > 0)
		{
			nextFollowLog = Time.unscaledTime + 1f;
			string speeds = "";
			foreach (Follower f in followers.Values)
			{
				if (f.Body != null && f.Animator != null)
				{
					speeds += f.Animator.GetFloat("velocityXZ").ToString("F2") + " ";
				}
			}
			Debug.Log("CoopDebug kanazawa walk " + speeds);
		}
#endif
		foreach (Follower follower in followers.Values)
		{
			if (follower.Body == null || !follower.HasTarget)
			{
				continue;
			}
			Vector3 before = follower.Body.position;
			// extrapolate so he doesn't stop and start
			float age = Mathf.Min(Time.time - follower.ReceivedAt, 0.3f);
			Vector3 target = CoopPlayers.Instance.FromLayout(follower.TargetLayout) + follower.Velocity * age;
			// way off, just snap
			Vector3 now = (Vector3.Distance(before, target) > 10f) ? target : Vector3.Lerp(before, target, Mathf.Clamp01(Time.deltaTime * 10f));
			follower.Body.position = now;
			follower.Body.rotation = Quaternion.Slerp(follower.Body.rotation, Quaternion.Euler(0f, follower.TargetYaw, 0f), Mathf.Clamp01(Time.deltaTime * 10f));
			if (follower.Animator != null)
			{
				// use the host's speed, per frame distance jitters and broke the walk cycle
				follower.Speed = Mathf.MoveTowards(follower.Speed, follower.Velocity.magnitude, walkSpeed * 2f * Time.deltaTime);
				CharacterMovementAnimation.Movement(follower.Animator, Vector3.forward * follower.Speed, walkSpeed);
			}
		}
	}

	public static void SendUnlocks()
	{
		if (Instance == null || Instance.map == null)
		{
			return;
		}
		if (Instance.map.enabled)
		{
			Send(Op.UnlockMap);
		}
		if (Instance.photoCamera.enabled)
		{
			Send(Op.UnlockCamera);
		}
	}

	// original always gave them to chito
	public static GameObject Recipient(GameObject chito)
	{
		if (CoopSession.InGame && CoopPlayers.Instance != null && CoopPlayers.Instance.Local != null)
		{
			return CoopPlayers.Instance.Local.gameObject;
		}
		return chito;
	}

	public static PlayerMapController LocalMap(PlayerMapController chitoMap)
	{
		if (CoopSession.InGame && CoopPlayers.Instance != null && CoopPlayers.Instance.Local != null)
		{
			return CoopPlayers.Instance.Local.GetComponent<PlayerMapController>();
		}
		return chitoMap;
	}

	public static void Opened()
	{
		Send(Op.Open);
	}

	public static void LineShown(int index)
	{
		Send(Op.Line, index);
	}

	public static void Closed()
	{
		Send(Op.Close);
	}

	private static void Send(Op op, int index = 0)
	{
		if (!CoopSession.InGame)
		{
			return;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Kanazawa);
		writer.Put((byte)op);
		writer.Put(index);
		CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	public void OnMessage(NetDataReader reader)
	{
		Op op = (Op)reader.GetByte();
		int @int = reader.GetInt();
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug kanazawa from the other player: " + op + " " + @int);
#endif
		switch (op)
		{
		case Op.UnlockMap:
			if (map != null)
			{
				map.enabled = true;
				mapWasOn = true;
			}
			CoopNotice.Show("Kanazawa gave you a map.");
			break;
		case Op.UnlockCamera:
			if (photoCamera != null)
			{
				photoCamera.enabled = true;
				cameraWasOn = true;
			}
			CoopNotice.Show("Kanazawa gave you a camera.");
			break;
		case Op.Open:
			FindDialogueBox();
			dialogueBox.SetTrigger("open");
			break;
		case Op.Line:
		{
			FindDialogueBox();
			DialogueManager dialogueManager = Object.FindObjectOfType<DialogueManager>(includeInactive: true);
			if (dialogueManager != null && @int < dialogueManager.lines.Length)
			{
				StopAllCoroutines();
				StartCoroutine(TypeLine(dialogueManager, @int));
			}
			break;
		}
		case Op.Close:
			FindDialogueBox();
			StopAllCoroutines();
			dialogueBox.SetTrigger("close");
			break;
		}
	}

	private void FindDialogueBox()
	{
		if (dialogueBox == null)
		{
			dialogueBox = GameObject.FindWithTag("DialogueBox").GetComponent<Animator>();
			dialogueText = GameObject.FindWithTag("Text").GetComponent<TextMeshProUGUI>();
		}
	}

	// like DialogueManager.TypeLine but the voice plays here, he could be far away
	private IEnumerator TypeLine(DialogueManager dialogue, int index)
	{
		if (index < dialogue.voicelines.Length && dialogue.voicelines[index] != null && Camera.main != null)
		{
			AudioSource.PlayClipAtPoint(dialogue.voicelines[index], Camera.main.transform.position);
		}
		dialogueText.text = string.Empty;
		string text = dialogue.lines[index];
		foreach (char c in text)
		{
			dialogueText.text += c;
			yield return new WaitForSeconds(dialogue.textSpeed);
		}
	}
}
