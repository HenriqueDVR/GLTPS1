using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// fuel barrel sync. whoever touched it last owns it and sends updates,
// other side keeps it kinematic
public class CoopBarrel : MonoBehaviour
{
	private enum Op : byte
	{
		TookFromVehicle,
		Took,
		Dropped,
		Mounted,
		Pose,
		Request
	}

	private const float poseInterval = 0.1f;

	private static readonly NetDataWriter writer = new NetDataWriter();

	private static bool remoteOwned;

	private static GameObject barrel;

	private float nextPose;

	private static Vector3 sentPosition;

	private static Quaternion sentRotation;

	public static bool RemoteCarrying
	{
		get
		{
			if (RemoteOwned && barrel != null)
			{
				return barrel.GetComponent<UnityEngine.Animations.ParentConstraint>().constraintActive;
			}
			return false;
		}
	}

	public static bool RemoteOwned
	{
		get
		{
			if (CoopSession.InGame)
			{
				return remoteOwned;
			}
			return false;
		}
	}

	private void Awake()
	{
		remoteOwned = false;
		barrel = null;
	}

	private System.Collections.IEnumerator Start()
	{
		if (CoopSession.Instance.IsHost)
		{
			yield break;
		}
		while (CoopPlayers.Instance == null || CoopPlayers.Instance.Remote == null)
		{
			yield return null;
		}
		Send(Op.Request);
	}

	// if they had it, drop it where it was
	public static void OnPartnerLeft()
	{
		if (!remoteOwned)
		{
			return;
		}
		remoteOwned = false;
		if (barrel != null)
		{
			barrel.GetComponent<UnityEngine.Animations.ParentConstraint>().constraintActive = false;
			Rigidbody body = barrel.GetComponent<Rigidbody>();
			body.isKinematic = false;
			body.velocity = Vector3.zero;
		}
	}

	public static void LocalTook(GameObject taken, bool fromVehicle)
	{
		if (!CoopSession.InGame)
		{
			return;
		}
		barrel = taken;
		remoteOwned = false;
		taken.GetComponent<Rigidbody>().isKinematic = false;
		Send(fromVehicle ? Op.TookFromVehicle : Op.Took);
	}

	public static void LocalDropped(CharacterBarrelInteraction carrier, GameObject dropped)
	{
		if (!CoopSession.InGame || CoopPlayers.IsPuppet(carrier) || remoteOwned)
		{
			return;
		}
		barrel = dropped;
		Send(Op.Dropped);
	}

	public static void LocalMounted()
	{
		// trigger can fire a few more times before it's gone
		if (CoopSession.InGame && barrel != null)
		{
			barrel = null;
			Send(Op.Mounted);
		}
	}

	private static void SendCurrent()
	{
		if (barrel == null)
		{
			barrel = GameObject.FindWithTag("Barrel");
		}
		if (barrel == null)
		{
			Send(Op.Mounted);
			return;
		}
		CharacterBarrelInteraction characterBarrelInteraction = barrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript;
		if (!remoteOwned && characterBarrelInteraction != null && characterBarrelInteraction.isCarrying)
		{
			Send(Op.Took);
		}
		else if (!remoteOwned)
		{
			Send(Op.Dropped);
		}
	}

	private static void Send(Op op)
	{
		if (CoopPlayers.Instance == null)
		{
			return;
		}
		writer.Reset();
		writer.Put((byte)CoopSession.MessageType.Barrel);
		writer.Put((byte)op);
		if (op == Op.Dropped || op == Op.Pose)
		{
			Vector3 vector = CoopPlayers.Instance.ToLayout(barrel.transform.position);
			Quaternion rotation = barrel.transform.rotation;
			sentPosition = vector;
			sentRotation = rotation;
			writer.Put(vector.x);
			writer.Put(vector.y);
			writer.Put(vector.z);
			writer.Put(rotation.x);
			writer.Put(rotation.y);
			writer.Put(rotation.z);
			writer.Put(rotation.w);
		}
		CoopSession.Instance.Send(writer, (op == Op.Pose) ? DeliveryMethod.Sequenced : DeliveryMethod.ReliableOrdered);
	}

	public static void OnMessage(NetDataReader reader)
	{
		CoopPlayers instance = CoopPlayers.Instance;
		if (instance == null || instance.Remote == null)
		{
			return;
		}
		Op op = (Op)reader.GetByte();
		if (op == Op.Request)
		{
			if (CoopSession.Instance.IsHost)
			{
				SendCurrent();
			}
			return;
		}
		CharacterBarrelInteraction componentInChildren = instance.Remote.GetComponentInChildren<CharacterBarrelInteraction>();
		switch (op)
		{
		case Op.TookFromVehicle:
		case Op.Took:
			if (barrel == null)
			{
				barrel = GameObject.FindWithTag("Barrel");
			}
			if (barrel == null)
			{
				// spawning it hides the mounted one (VehicleBarrel.Start)
				barrel = componentInChildren.SpawnBarrel();
			}
			remoteOwned = true;
			barrel.GetComponent<Rigidbody>().isKinematic = true;
			componentInChildren.ShowCarrying(barrel);
			break;
		case Op.Dropped:
		case Op.Pose:
		{
			Vector3 layout = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
			Quaternion rotation = new Quaternion(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
			if (op == Op.Dropped)
			{
				if (barrel == null)
				{
					barrel = GameObject.FindWithTag("Barrel") ?? componentInChildren.SpawnBarrel();
				}
				remoteOwned = true;
				barrel.GetComponent<Rigidbody>().isKinematic = true;
				barrel.GetComponent<UnityEngine.Animations.ParentConstraint>().constraintActive = false;
			}
			if (remoteOwned && barrel != null)
			{
				barrel.transform.SetPositionAndRotation(instance.FromLayout(layout), rotation);
			}
			break;
		}
		case Op.Mounted:
			if (barrel == null)
			{
				barrel = GameObject.FindWithTag("Barrel");
			}
			if (barrel != null)
			{
				Object.Destroy(barrel);
			}
			barrel = null;
			remoteOwned = false;
			GameObject.FindWithTag("BarrelDropArea").GetComponent<SkinnedMeshRenderer>().enabled = true;
			break;
		}
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug barrel from the other player: " + op);
#endif
	}

	private void Update()
	{
		if (barrel == null || remoteOwned || !CoopSession.InGame || Time.unscaledTime < nextPose)
		{
			return;
		}
		CharacterBarrelInteraction characterBarrelInteractionScript = barrel.GetComponent<VehicleBarrel>().characterBarrelInteractionScript;
		if ((characterBarrelInteractionScript == null || !characterBarrelInteractionScript.isCarrying) && (Vector3.Distance(CoopPlayers.Instance.ToLayout(barrel.transform.position), sentPosition) > 0.01f || Quaternion.Angle(barrel.transform.rotation, sentRotation) > 1f))
		{
			nextPose = Time.unscaledTime + poseInterval;
			Send(Op.Pose);
		}
	}
}
