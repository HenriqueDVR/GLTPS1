using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public class PillarNodeAnimationEvents : MonoBehaviour
{
    public AudioSource _audioSource;
    [SerializeField] private AudioClip[] _audioClip;
    public int index = 0;
    private bool closeGate;
    [SerializeField] private GameObject dummyKettengrad;

    [SerializeField] SetCameraRadius changeCameraRadiusScript;
    [SerializeField] private float[] freelookRadius;
    [SerializeField] private float[] freelookHeight;
    [SerializeField] private float smoothRadiusSpeed = 0.2f;
    [SerializeField] private GameObject endScreen;

    void Start()
    {
        changeCameraRadiusScript = GameObject.FindWithTag("CMFreeLookCamera").GetComponent<SetCameraRadius>();
        endScreen = GameObject.FindWithTag("EndingScreen").transform.GetChild(0).gameObject;
    }

    public void PlayElevatorSequenceAudioClip()
    {
#if DUMP_WORLDGEN
        Debug.Log("CoopTour ending event PlayElevatorSequenceAudioClip");
#endif
        _audioSource.PlayOneShot(_audioClip[index]);
        index++;
    }

    public void ReplaceKettengradWithDummy()
    {
#if DUMP_WORLDGEN
        Debug.Log("CoopTour ending event ReplaceKettengradWithDummy");
#endif
        // if (GameObject.FindWithTag("Vehicle") != null)
        // {
        //     GameObject kettengrad = GameObject.FindWithTag("Vehicle");
        //     if (kettengrad.GetComponent<VehicleElevatorCheckCollision>().isInElevator)
        //     {
        //         GameObject newDummyKettengrad = Instantiate(dummyKettengrad, kettengrad.transform.position, kettengrad.transform.rotation);
        //         newDummyKettengrad.transform.parent = transform;
        //         Destroy(kettengrad);
        //     }
        // }
    }

    public void ChangeCameraRadius()
    {
#if DUMP_WORLDGEN
        Debug.Log("CoopTour ending event ChangeCameraRadius");
#endif
        changeCameraRadiusScript.SetRadius(freelookRadius[0], freelookRadius[1], freelookRadius[2], smoothRadiusSpeed);
        changeCameraRadiusScript.SetHeight(freelookHeight[0], freelookHeight[1], freelookHeight[2]);
    }

    public void PauseGameState()
    {
#if DUMP_WORLDGEN
        Debug.Log("CoopTour ending event PauseGameState");
#endif
        // can fire again after they're already off
        GameObject characterManager = GameObject.FindWithTag("CharacterManager");
        if (characterManager != null)
        {
            characterManager.SetActive(false);
        }
    }

    public void DisableCamera()
    {
#if DUMP_WORLDGEN
        Debug.Log("CoopTour ending event DisableCamera");
#endif
        GameObject freeLookCamera = GameObject.FindWithTag("CMFreeLookCamera");
        if (freeLookCamera != null)
        {
            freeLookCamera.SetActive(false);
        }
    }

    public void PlayEndingSequence()
    {
#if DUMP_WORLDGEN
        Debug.Log("CoopTour ending event PlayEndingSequence");
#endif
        endScreen.SetActive(true);
    }
    public void QuitToMenu()
    {
#if DUMP_WORLDGEN
        Debug.Log("CoopTour ending event QuitToMenu");
#endif
        SceneManager.LoadScene(0);
    }
}
