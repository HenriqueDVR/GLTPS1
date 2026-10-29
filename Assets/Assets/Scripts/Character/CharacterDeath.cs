using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CharacterDeath : MonoBehaviour
{
    public bool isChitoDead;
    public bool isYuuriDead;

    // the 4s handover to the other girl, started once
    private bool chitoHandover;
    private bool yuuriHandover;

    private bool gameOver;
    void LateUpdate()
    {
        // coop uses CoopDowned instead, this would swap girls
        if (CoopSession.InGame || Girls.Chito == null || Girls.Yuuri == null)
        {
            return;
        }

        isChitoDead = IsDead(Girls.Chito, isChitoDead);
        isYuuriDead = IsDead(Girls.Yuuri, isYuuriDead);

        //check yuuri death sequence
        if (isYuuriDead)
        {
            DeathSequence(Girls.Yuuri, isChitoDead, ref yuuriHandover);
        }
        //check chito death sequence
        if (isChitoDead)
        {
            DeathSequence(Girls.Chito, isYuuriDead, ref chitoHandover);
        }

        if (isYuuriDead && isChitoDead && !gameOver)
        {
            StartCoroutine(DelayReloadScene());
            gameOver = true;
        }
    }

    bool IsDead(Girl girl, bool isDead)
    {
        CharacterItemInteraction items = girl.Items;
        if ((items.thirstLevel <= 0 ||
            items.hungerLevel <= 0) ||
            items.temperatureLevel <= 0 &&
            !girl.Animator.GetCurrentAnimatorStateInfo(1).IsTag("Ket"))
        {
            //check if navmesg agent is enable and that the navmesh agent has exited the vehicle
            if (girl.Agent.enabled)
            {
                girl.Agent.enabled = false;
                girl.AgentVehicle.enabled = false;
            }

            //check if player is holding barrel
            if (girl.Animator.GetCurrentAnimatorStateInfo(2).IsTag("Carry"))
            {
                girl.Barrel.DropBarrel();
            }
            if (!girl.Input.Contexts.Has(InputContext.Downed))
            {
                girl.Input.Contexts.Push(InputContext.Downed, "CharacterDeath");
            }
            girl.Animator.SetTrigger("die");
            isDead = true;
        }
        return isDead;
    }

    void DeathSequence(Girl dead, bool otherIsDead, ref bool handover)
    {
        // no more switching
        Girls.Other(dead).Possession.enabled = false;
        //check if player is controlling dead character
        if (dead.Possession.enabled && !otherIsDead && !handover)
        {
            handover = true;
            StartCoroutine(DelayCharacterPosession(dead));
        }
    }

    IEnumerator DelayCharacterPosession(Girl dead)
    {
        yield return new WaitForSeconds(4);
        dead.Possession.PossessCharacter(true);
    }

    IEnumerator DelayReloadScene()
    {
        yield return new WaitForSeconds(6);
        SceneManager.LoadScene(Scenes.Title);
    }
}


