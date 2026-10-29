#if DUMP_WORLDGEN
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// debug only test runs
// -coopkanazawa  host gets the map + some of his lines
// -cooptravel    hop along the route to the pillar and try the elevator
// -coopelevator  hop into the elevator and wait there
// -coopnextsong  friend skips the song every 15s
public class CoopDebugTour : MonoBehaviour
{
	private CoopSession session;

	private IEnumerator Start()
	{
		session = CoopSession.Instance;
		while (CoopPlayers.Instance == null || CoopPlayers.Instance.Local == null || !session.Connected)
		{
			yield return null;
		}
		string[] commandLineArgs = System.Environment.GetCommandLineArgs();
		if (System.Array.IndexOf(commandLineArgs, "-coopkanazawa") >= 0 && session.IsHost)
		{
			StartCoroutine(Kanazawa());
		}
		if (System.Array.IndexOf(commandLineArgs, "-cooptravel") >= 0)
		{
			StartCoroutine(TravelToPillar());
		}
		if (System.Array.IndexOf(commandLineArgs, "-coopelevator") >= 0)
		{
			StartCoroutine(StandInElevator());
		}
		if (System.Array.IndexOf(commandLineArgs, "-coopnextsong") >= 0 && !session.IsHost)
		{
			StartCoroutine(SkipSongs());
		}
	}

	private IEnumerator SkipSongs()
	{
		while (true)
		{
			yield return new WaitForSeconds(15f);
			Debug.Log("CoopDebug friend skips the song");
			CoopMusic.Next();
		}
	}

	private IEnumerator Kanazawa()
	{
		yield return new WaitForSeconds(8f);
		Debug.Log("CoopTour unlocking the map on the host");
		CoopPlayers.Instance.Local.GetComponent<PlayerMapController>().enabled = true;
		yield return new WaitForSeconds(3f);
		Debug.Log("CoopTour dialogue opens");
		CoopKanazawa.Opened();
		for (int i = 0; i < 3; i++)
		{
			CoopKanazawa.LineShown(i);
			yield return new WaitForSeconds(4f);
		}
		CoopKanazawa.Closed();
	}

	private IEnumerator TravelToPillar()
	{
		WorldGenerator worldGenerator = GameSession.Get<WorldGenerator>();
		NodeData component = worldGenerator.GetComponent<NodeData>();
		WorldLayout.PlacedNode placedNode = null;
		foreach (WorldLayout.PlacedNode node in worldGenerator.Layout.Nodes)
		{
			if (node.Def.Prefab == component.pillarEdge && (placedNode == null || node.Id < placedNode.Id))
			{
				placedNode = node;
			}
		}
		if (placedNode == null)
		{
			Debug.Log("CoopTour no pillar in this world");
			yield break;
		}
		List<WorldLayout.PlacedNode> list = new List<WorldLayout.PlacedNode>();
		for (WorldLayout.PlacedNode placedNode2 = placedNode; placedNode2 != null; placedNode2 = ((placedNode2.FromEdge != null) ? placedNode2.FromEdge.From : null))
		{
			list.Insert(0, placedNode2);
		}
		Debug.Log("CoopTour pillar is node " + placedNode.Id + ", " + list.Count + " nodes away");
		yield return new WaitForSeconds(session.IsHost ? 6f : 12f);
		foreach (WorldLayout.PlacedNode item in list)
		{
			while (item.Instance == null)
			{
				yield return null;
			}
			Teleport(item.Instance.transform.position);
			yield return new WaitForSeconds(1f);
		}
		Debug.Log("CoopTour reached the pillar");
		yield return new WaitForSeconds(3f);
		CharacterElevatorInteraction component2 = CoopPlayers.Instance.Local.GetComponent<CharacterElevatorInteraction>();
		Vector3? vector = TaggedCenter("ElevatorTrigger");
		if (!vector.HasValue)
		{
			Debug.Log("CoopTour no elevator trigger found");
			yield break;
		}
		Teleport(vector.Value);
		yield return new WaitForSeconds(2f);
		if (session.IsHost)
		{
			Debug.Log("CoopTour host presses interact, friend aboard: " + Aboard());
			component2.DebugInteract();
			yield return new WaitForSeconds(10f);
			Debug.Log("CoopTour host presses interact again, friend aboard: " + Aboard());
			component2.DebugInteract();
		}
		for (int i = 0; i < 40; i++)
		{
			GameObject gameObject = GameObject.FindWithTag("EndingScreen");
			Debug.Log("CoopTour elevator " + ElevatorState() + " ending screen " + (gameObject != null && gameObject.transform.GetChild(0).gameObject.activeSelf));
			yield return new WaitForSeconds(2f);
		}
	}

	private IEnumerator StandInElevator()
	{
		List<WorldLayout.PlacedNode> route = RouteToPillar();
		if (route == null)
		{
			yield break;
		}
		yield return new WaitForSeconds(session.IsHost ? 3f : 5f);
		foreach (WorldLayout.PlacedNode node in route)
		{
			while (node.Instance == null)
			{
				yield return null;
			}
			Teleport(node.Instance.transform.position);
			yield return new WaitForSeconds(0.4f);
		}
		Vector3? controls = TaggedCenter("ElevatorTrigger");
		if (!controls.HasValue)
		{
			Debug.Log("CoopTour no elevator trigger found");
			yield break;
		}
		Vector3 spot = controls.Value;
		if (!session.IsHost)
		{
			Vector3? floor = NearestTriggerCenter("Elevator", controls.Value);
			if (floor.HasValue)
			{
				spot = Vector3.Lerp(controls.Value, floor.Value, 0.5f);
			}
		}
		// cast from just above the floor, higher hits the roof
		Teleport(spot, 0.5f);
		yield return new WaitForSeconds(2f);
		Debug.Log("CoopTour standing in the elevator, friend aboard: " + Aboard());
		StartCoroutine(LogRide());
		if (!session.IsHost)
		{
			yield break;
		}
		for (float waited = 0f; !Aboard() && waited < 20f; waited += 0.5f)
		{
			yield return new WaitForSeconds(0.5f);
		}
		yield return new WaitForSeconds(2f);
		Debug.Log("CoopTour host closes the gate, friend aboard: " + Aboard());
		CoopPlayers.Instance.Local.GetComponent<CharacterElevatorInteraction>().DebugInteract();
	}

	private IEnumerator LogRide()
	{
		CoopPlayers players = CoopPlayers.Instance;
		for (int i = 0; i < 160 && players.Local != null && players.Local.gameObject.activeInHierarchy; i++)
		{
			SetPositionConstraintElevator constraint = players.Local.GetComponent<SetPositionConstraintElevator>();
			Transform floor = constraint.target != null ? constraint.target : players.Remote.GetComponent<SetPositionConstraintElevator>().target;
			string heights = floor == null ? "no elevator" : "local " + (players.Local.position.y - floor.position.y).ToString("F2") + " remote " + (players.Remote.position.y - floor.position.y).ToString("F2") + " floor " + floor.position.y.ToString("F1");
			Vector3 apart = players.Remote.position - players.Local.position;
			apart.y = 0f;
			Debug.Log("CoopRide " + Time.timeSinceLevelLoad.ToString("F1") + " " + heights + " apart " + apart.magnitude.ToString("F2") + " localParent " + (players.Local.parent != null ? players.Local.parent.name : "-") + " remoteParent " + (players.Remote.parent != null ? players.Remote.parent.name : "-"));
			yield return new WaitForSeconds(0.5f);
		}
	}

	private static List<WorldLayout.PlacedNode> RouteToPillar()
	{
		WorldGenerator worldGenerator = GameSession.Get<WorldGenerator>();
		NodeData nodeData = worldGenerator.GetComponent<NodeData>();
		WorldLayout.PlacedNode pillar = null;
		foreach (WorldLayout.PlacedNode node in worldGenerator.Layout.Nodes)
		{
			if (node.Def.Prefab == nodeData.pillarEdge && (pillar == null || node.Id < pillar.Id))
			{
				pillar = node;
			}
		}
		if (pillar == null)
		{
			Debug.Log("CoopTour no pillar in this world");
			return null;
		}
		List<WorldLayout.PlacedNode> route = new List<WorldLayout.PlacedNode>();
		for (WorldLayout.PlacedNode node = pillar; node != null; node = node.FromEdge != null ? node.FromEdge.From : null)
		{
			route.Insert(0, node);
		}
		return route;
	}

	private static bool Aboard()
	{
		Vector3? vector = TaggedCenter("ElevatorTrigger");
		if (vector.HasValue)
		{
			return Vector3.Distance(vector.Value, CoopPlayers.Instance.Remote.position) < 4f;
		}
		return false;
	}

	private static string ElevatorState()
	{
		GameObject[] array = GameObject.FindGameObjectsWithTag("Elevator");
		foreach (GameObject gameObject in array)
		{
			Animator component = gameObject.GetComponent<Animator>();
			if (component != null)
			{
				AnimatorStateInfo currentAnimatorStateInfo = component.GetCurrentAnimatorStateInfo(0);
				return "open " + component.GetBool("isGateOpen") + " closing " + component.GetBool("CloseGate") + " enabled " + component.enabled + " state " + currentAnimatorStateInfo.shortNameHash + " at " + currentAnimatorStateInfo.normalizedTime.ToString("F2") + " length " + currentAnimatorStateInfo.length.ToString("F1");
			}
		}
		return "none";
	}

	// other buildings have elevators too
	private static Vector3? NearestTriggerCenter(string tag, Vector3 near)
	{
		Vector3? best = null;
		foreach (GameObject gameObject in GameObject.FindGameObjectsWithTag(tag))
		{
			foreach (Collider collider in gameObject.GetComponents<Collider>())
			{
				if (collider.isTrigger && (!best.HasValue || Vector3.Distance(collider.bounds.center, near) < Vector3.Distance(best.Value, near)))
				{
					best = collider.bounds.center;
				}
			}
		}
		return best;
	}

	private static Vector3? TaggedCenter(string tag)
	{
		GameObject[] array = GameObject.FindGameObjectsWithTag(tag);
		foreach (GameObject gameObject in array)
		{
			Collider component = gameObject.GetComponent<Collider>();
			if (component != null)
			{
				return component.bounds.center;
			}
		}
		return null;
	}

	private static void Teleport(Vector3 position, float castHeight = 3f)
	{
		Transform local = CoopPlayers.Instance.Local;
		if (Physics.Raycast(position + Vector3.up * castHeight, Vector3.down, out var hitInfo, 30f, -1, QueryTriggerInteraction.Ignore))
		{
			position = hitInfo.point + Vector3.up * 0.1f;
		}
		CharacterController component = local.GetComponent<CharacterController>();
		component.enabled = false;
		local.position = position;
		component.enabled = true;
	}
}
#endif
