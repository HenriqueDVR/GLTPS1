using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class NPCInteraction : MonoBehaviour, IInteractionSource
{
    [SerializeField] private PlayerInputInitialize playerInputScript;
    private GameObject kanazawa;
    [SerializeField] private GameObject node;
    private bool isInteractable;
    public bool triggerDialogue;
    public Animator dialogueBoxAnim;
    public CharacterPossession characterPossessionScript;
    public void Collect(List<Interaction> into)
    {
        // mid conversation the next line comes first, whatever else is around
        if (triggerDialogue && kanazawa != null)
        {
            into.Add(new Interaction { Verb = Verb.Talk, Press = Press.Tap, Priority = 6, Run = () => kanazawa.GetComponent<DialogueManager>().NextLine(), Source = "next line" });
        }
        else if (isInteractable && kanazawa != null && dialogueBoxAnim.GetCurrentAnimatorStateInfo(0).IsTag("Close"))
        {
            into.Add(new Interaction { Verb = Verb.Talk, Press = Press.Tap, Target = kanazawa.transform, Priority = 1, Run = StartTalking, Source = "kanazawa" });
        }
    }

    private void StartTalking()
    {
        {
            // stop agent patrol
            kanazawa.GetComponent<NavMeshAgentPatrol>().patrol = false;

            // rotate agent to player
            kanazawa.GetComponent<RotateToPlayer>().enabled = true;
            kanazawa.GetComponent<RotateToPlayer>().player = transform;

            // set agent destination to in place
            kanazawa.GetComponent<NavMeshAgentPatrol>().agent.destination = kanazawa.transform.position;

            // trigger dialogue
            kanazawa.GetComponent<DialogueManager>().enabled = true;
            kanazawa.GetComponent<DialogueManager>().ShowDialogueBox();
            triggerDialogue = true;

            // disable switch character
            characterPossessionScript.enabled = false;

        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Kanazawa") && this.enabled)
        {
            kanazawa = other.gameObject;
            kanazawa.GetComponent<DialogueManager>().NPCInteractionScript = this;
            // coop: map goes to whoever talked to him
            kanazawa.GetComponent<UnlockDialogueItem>().dialogueItems[0].reference[0] = CoopKanazawa.Recipient(Girls.Chito.gameObject);
            kanazawa.GetComponent<UnlockDialogueItem>().dialogueItems[1].reference[0] = CoopKanazawa.Recipient(Girls.Chito.gameObject);
            node.GetComponent<KanazawaInstanceManager>().firstKanazawa = kanazawa;
            isInteractable = true;
        }

        if (other.gameObject.CompareTag("Node") && other.gameObject.GetComponent<Node>() != null)
        {
            if (other.gameObject.GetComponent<Node>().isActive)
            {
                node = other.gameObject;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Kanazawa") && this.enabled)
        {
            isInteractable = false;

            // start agent patrol
            other.gameObject.GetComponent<NavMeshAgentPatrol>().patrol = true;
            other.gameObject.GetComponent<RotateToPlayer>().enabled = false;
            StartCoroutine(other.gameObject.GetComponent<NavMeshAgentPatrol>().SetDestination());

            //reset dialogue
            if (!dialogueBoxAnim.GetCurrentAnimatorStateInfo(0).IsTag("Close"))
            {
                other.gameObject.GetComponent<DialogueManager>().audioSource.Stop();
                other.gameObject.GetComponent<DialogueManager>().HideDialogueBox();
            }
        }
    }
}
