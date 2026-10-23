using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

// only goes with both girls in, closing the gate closes it for both
public static class CoopElevator
{
	private static readonly NetDataWriter writer = new NetDataWriter();

	public static bool ReadyToLeave(Animator elevator)
	{
		CoopPlayers instance = CoopPlayers.Instance;
		if (!CoopSession.InGame || instance == null || instance.Remote == null)
		{
			return true;
		}
		string text = instance.Remote.name.Replace("Parent", "");
		if (!CoopSession.Instance.Connected)
		{
			CoopNotice.Show(text + "'s player isn't here. Wait for them to come back.");
			return false;
		}
		if (CoopDowned.Instance != null && CoopDowned.Instance.RemoteDown)
		{
			CoopNotice.Show("Revive " + text + " first.");
			return false;
		}
		if (!Aboard(elevator, instance.Remote))
		{
			CoopNotice.Show("Wait for " + text + " to get in the elevator.");
			return false;
		}
		return true;
	}

	private static bool Aboard(Animator elevator, Transform girl)
	{
		Vector3 point = girl.position + Vector3.up * 0.5f;
		Collider[] componentsInChildren = elevator.GetComponentsInChildren<Collider>();
		foreach (Collider collider in componentsInChildren)
		{
			if (collider.isTrigger && collider.gameObject.CompareTag("Elevator") && collider.bounds.Contains(point))
			{
				return true;
			}
		}
		return false;
	}

	public static void GateClosed()
	{
		if (CoopSession.InGame)
		{
			writer.Reset();
			writer.Put((byte)CoopSession.MessageType.ElevatorClosed);
			CoopSession.Instance.Send(writer, DeliveryMethod.ReliableOrdered);
		}
	}

	// close the one their girl is in, other buildings have elevators too
	public static void OnGateClosed()
	{
		if (CoopPlayers.Instance == null || CoopPlayers.Instance.Remote == null)
		{
			return;
		}
		Vector3 position = CoopPlayers.Instance.Remote.position;
		Animator animator = null;
		GameObject[] array = GameObject.FindGameObjectsWithTag("Elevator");
		foreach (GameObject gameObject in array)
		{
			Animator component = gameObject.GetComponent<Animator>();
			if (component != null && (animator == null || Vector3.Distance(component.transform.position, position) < Vector3.Distance(animator.transform.position, position)))
			{
				animator = component;
			}
		}
		if (animator != null)
		{
			animator.SetBool("CloseGate", value: true);
			animator.SetBool("isGateOpen", value: false);
		}
#if DUMP_WORLDGEN
		Debug.Log("CoopDebug elevator gate closed by the other player");
#endif
	}
}
