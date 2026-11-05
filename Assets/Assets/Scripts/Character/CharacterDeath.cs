using System.Collections.Generic;
using UnityEngine;

// solo: a girl out of something for too long goes down. the other one gets her up: the AI comes over and sees to it, the
// player holds interact on her. both down, or nobody gets to her in time, and they wake at the last shelter (WakeFlow).
// co-op has CoopDowned
public class CharacterDeath : MonoBehaviour, IInteractionSource
{
    // both down this long, then the night
    private const float bothDownDelay = 6f;

    // the AI can't get her up in this long: no way there, or nothing to do it with
    private const float aiTriesFor = 40f;

    // the player's walked off and left the AI girl lying there
    private const float aloneFor = 120f;

    private const float fireEvery = 5f;

    private sealed class Fallen
    {
        public bool Down;

        public float Since;

        public InputContextStack.Token Input;
    }

    public bool isChitoDead;
    public bool isYuuriDead;

    private readonly Dictionary<Girl, Fallen> fallen = new Dictionary<Girl, Fallen>();

    private CharacterStatus status;

    private float reviving;

    private int heldFrame = -10;

    private float nextFire;

    private void Awake()
    {
        InteractionPicker.AddGlobal(this);
    }

    private void OnDestroy()
    {
        InteractionPicker.RemoveGlobal(this);
    }

    private Fallen Of(Girl girl)
    {
        if (!fallen.TryGetValue(girl, out Fallen state))
        {
            state = new Fallen();
            fallen[girl] = state;
        }
        return state;
    }

    private bool IsDown(Girl girl)
    {
        return Of(girl).Down;
    }

    // the player's way of getting the AI girl up, only when it can work
    public void Collect(List<Interaction> into)
    {
        if (CoopSession.InGame || Girls.Local == null)
        {
            return;
        }
        Girl other = Girls.Other(Girls.Local);
        if (other != null && !IsDown(Girls.Local) && IsDown(other) && Near(Girls.Local, other) && GirlNeeds.HasAll(other.Items, Status()))
        {
            into.Add(new Interaction { Verb = Verb.Revive, Press = Press.Continuous, Target = other.transform, Priority = 2, Label = "get her up", Run = () => heldFrame = Time.frameCount, Source = "revive" });
        }
    }

    private CharacterStatus Status()
    {
        if (status == null)
        {
            status = FindObjectOfType<CharacterStatus>();
        }
        return status;
    }

    private static bool Near(Girl a, Girl b)
    {
        return Vector3.Distance(a.transform.position, b.transform.position) < GirlNeeds.ReviveReach;
    }

    void LateUpdate()
    {
        // coop uses CoopDowned instead, this would swap girls
        if (CoopSession.InGame || Girls.Chito == null || Girls.Yuuri == null || WakeFlow.Busy)
        {
            return;
        }
        foreach (Girl girl in Girls.All)
        {
            if (!IsDown(girl) && GirlNeeds.DueDown(girl.Items))
            {
                GoDown(girl);
            }
        }
        isChitoDead = IsDown(Girls.Chito);
        isYuuriDead = IsDown(Girls.Yuuri);
        Girl player = Girls.Local;
        Girl other = Girls.Other(player);
        bool playerDown = IsDown(player);
        bool otherDown = IsDown(other);
        if (playerDown && otherDown)
        {
            if (Time.time >= Mathf.Max(Of(player).Since, Of(other).Since) + bothDownDelay)
            {
                WakeFlow.Begin();
            }
        }
        else if (playerDown)
        {
            AiSeesTo(other, player);
        }
        else if (otherDown)
        {
            PlayerSeesTo(player, other);
        }
    }

    private void GoDown(Girl girl)
    {
        Fallen state = Of(girl);
        state.Down = true;
        state.Since = Time.time;
        reviving = 0f;
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
        if (state.Input == null)
        {
            state.Input = girl.Input.Contexts.Push(InputContext.Downed, "CharacterDeath");
        }
        girl.Animator.SetTrigger("die");
        // no switching till they're both up
        Girls.Local.Possession.enabled = false;
    }

    private void GetUp(Girl girl)
    {
        Fallen state = Of(girl);
        if (!state.Down)
        {
            return;
        }
        state.Down = false;
        GirlNeeds.Raise(girl.Items, NeedsRules.RevivedLevel);
        // death anim has no exit so force it back
        girl.Animator.Rebind();
        girl.Animator.Update(0f);
        state.Input?.Release();
        state.Input = null;
        if (girl.Control == GirlControl.AI)
        {
            girl.Agent.enabled = true;
            girl.AgentVehicle.enabled = true;
        }
        reviving = 0f;
        Girl other = Girls.Other(girl);
        if (!IsDown(other))
        {
            Girls.Local.Possession.enabled = true;
            foreach (Girl any in Girls.All)
            {
                if (any.Ai != null)
                {
                    any.Ai.Tend = null;
                }
            }
        }
    }

    private void AiSeesTo(Girl ai, Girl down)
    {
        if (ai.Ai != null)
        {
            ai.Ai.Tend = down.transform;
        }
        CharacterItemInteraction hurt = down.Items;
        if (Near(ai, down))
        {
            if (!GirlNeeds.HasAll(hurt, Status()))
            {
                // a fire she can light by the ket, the rest she either has or she hasn't
                if (hurt.temperatureLevel <= 0f && Time.time >= nextFire)
                {
                    nextFire = Time.time + fireEvery;
                    ai.Items.LightFireNow();
                }
            }
            else
            {
                reviving += Time.deltaTime;
                if (reviving >= GirlNeeds.ReviveDuration)
                {
                    GirlNeeds.Pay(hurt, Status(), ai.Items);
                    GetUp(down);
                    Speaker.Say(GirlNeeds.NameOf(ai), (ai.Id == GirlId.Chito) ? "Up. Come on. Up." : "Chi-chan. It's not time to sleep.");
                    return;
                }
            }
        }
        if (Time.time >= Of(down).Since + aiTriesFor)
        {
            WakeFlow.Begin();
        }
    }

    private void PlayerSeesTo(Girl player, Girl down)
    {
        bool held = heldFrame >= Time.frameCount - 1 && Near(player, down) && GirlNeeds.HasAll(down.Items, Status());
        reviving = held ? reviving + Time.deltaTime : Mathf.Max(0f, reviving - Time.deltaTime * 2f);
        if (reviving >= GirlNeeds.ReviveDuration)
        {
            GirlNeeds.Pay(down.Items, Status(), player.Items);
            GetUp(down);
            return;
        }
        if (Time.time >= Of(down).Since + aloneFor)
        {
            WakeFlow.Begin();
        }
    }

    // the night's over, everyone's up
    public void AllUp()
    {
        foreach (Girl girl in Girls.All)
        {
            GetUp(girl);
        }
    }
}
