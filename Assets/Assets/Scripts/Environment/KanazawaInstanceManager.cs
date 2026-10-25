using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KanazawaInstanceManager : MonoBehaviour
{
    [SerializeField] private Node node;
    private NodeData nodeDataScript;
    [SerializeField] private GameObject kanazawaCompound;
    public GameObject firstKanazawa;
    [SerializeField] private PlayerMapController playerMapControllerScript;
    private bool isPlayerOnNode;


    private bool flag;

    // once the map is unlocked only the kanazawa the player met stays. pieces WorldGenerator
    // had switched off at that moment missed the cleanup, it calls RemoveLeftovers when they come back
    private static bool othersGone;
    private static GameObject kept;

    public static void ResetLevel()
    {
        othersGone = false;
        kept = null;
    }

    public static void RemoveLeftovers(GameObject piece)
    {
        if (!othersGone)
        {
            return;
        }
        foreach (Transform child in piece.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.CompareTag("Kanazawa") && child.gameObject != kept)
            {
                Destroy(child.parent.parent.gameObject);
            }
        }
    }

    void Start()
    {
        nodeDataScript = GameObject.FindWithTag("NodeData").GetComponent<NodeData>();
        playerMapControllerScript = GameObject.Find("ChitoParent").GetComponent<PlayerMapController>();
    }

    void Update()
    {
        // coop: yuuri's player has the map on yuuri
        PlayerMapController playerMapController = CoopKanazawa.LocalMap(playerMapControllerScript);

        if (kanazawaCompound != null)
        {
            if (node.isDeadEnd && !kanazawaCompound.activeInHierarchy && !playerMapController.enabled)
            {
                kanazawaCompound.SetActive(true);
            }
        }

        if (playerMapController.enabled && isPlayerOnNode && !flag)
        {
            // hide all kanazawas except the one the player interacted with 
            GameObject[] kanazawasToHide = GameObject.FindGameObjectsWithTag("Kanazawa");

            foreach (GameObject kanazawa in kanazawasToHide)
            {
                if (kanazawa != firstKanazawa)
                {
                    Destroy(kanazawa.transform.parent.transform.parent.gameObject);
                }
            }

            flag = true;
            othersGone = true;
            kept = firstKanazawa;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            isPlayerOnNode = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            isPlayerOnNode = false;
        }
    }
}
