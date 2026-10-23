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

	public void OnMessage(NetPacketReader reader)
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
