using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BulletEffect : MonoBehaviour
{
    [SerializeField]
    private PipeWaterLevel pipeWaterLevelScript;
    [SerializeField]
    private ParticleSystem waterStream;
    private ParticleSystem.EmissionModule emission;
    private float elasped = 0f;
    [SerializeField]
    private float duration = 2f;
    private float waterLossRate = 1f;
    public AudioSource _audioSource;
    [SerializeField] private AudioClip[] _audioClip;
    // the hole stays on whatever was shot. only the hit itself makes a sound: on the ket its trigger would drive
    // through walls and the road, ringing every time
    IEnumerator Start()
    {
        emission = waterStream.emission;
        for (int i = 0; i < 3; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        GetComponent<Collider>().enabled = false;
    }
    void Update()
    {
        //check if the reference to the pipe is null
        //note: the reference is set in OnTriggerEnter
        if (pipeWaterLevelScript != null)
        {
            //decrease water level
            pipeWaterLevelScript.waterlevel -= waterLossRate * Time.deltaTime;

            //lerp particle rate to 0
            if (pipeWaterLevelScript.waterlevel <= 0)
            {
                elasped += Time.deltaTime;
                emission.rateOverTime = Mathf.Lerp(emission.rateOverTime.constant, 0, elasped / duration);

                if (emission.rateOverTime.constant == 0)
                {
                    waterStream.Stop();
                }
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Pipe"))
        {
            _audioSource.PlayOneShot(_audioClip[Random.Range(0, 2)]);

            pipeWaterLevelScript = other.gameObject.GetComponent<PipeWaterLevel>();
            if (pipeWaterLevelScript.waterlevel > 0)
            {
                waterStream.Play();
            }
        }

        if (other.gameObject.CompareTag("Metal") || other.gameObject.CompareTag("Vehicle"))
        {
            _audioSource.PlayOneShot(_audioClip[Random.Range(0, 2)]);
        }

        if (other.gameObject.CompareTag("Untagged") || other.gameObject.CompareTag("Food") || other.gameObject.CompareTag("Ammo"))
        {
            _audioSource.PlayOneShot(_audioClip[2]);
        }
    }
}
