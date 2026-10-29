using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations;

public class CharacterFuelInteraction : MonoBehaviour, IInteractionSource
{
    [SerializeField] private PlayerInputInitialize playerInputScript;
    [SerializeField] private CharacterBarrelInteraction characterBarrelInteractionScript;
    [SerializeField] private bool inFuelingStationInteractArea;
    public bool isFueling = true;

    private Collider other;

    [SerializeField] AudioSource _audioSource;
    [SerializeField] AudioClip _audioClip;
    public void Collect(List<Interaction> into)
    {
        if (inFuelingStationInteractArea && !characterBarrelInteractionScript.isCarrying && other != null)
        {
            into.Add(new Interaction { Verb = Verb.Refuel, Press = Press.Tap, Target = other.transform, Priority = 1, Run = Toggle, Source = "fuel station" });
        }
    }

    private void Toggle()
    {
        {
            if (isFueling)
            {
                other.gameObject.GetComponentInChildren<ParticleSystem>().Play();
                isFueling = false;

                if (_audioSource != null)
                {
                    _audioSource.Stop();
                    _audioSource.PlayOneShot(_audioClip);
                }
            }
            else
            {
                other.gameObject.GetComponentInChildren<ParticleSystem>().Stop();
                isFueling = true;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("FuelingStation"))
        {
            inFuelingStationInteractArea = true;
            this.other = other;
        }
    }
    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("FuelingStation"))
        {
            inFuelingStationInteractArea = false;
        }
    }
}
