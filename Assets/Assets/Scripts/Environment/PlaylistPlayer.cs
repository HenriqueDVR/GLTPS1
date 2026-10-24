using System.Collections;
using UnityEngine;

public class PlaylistPlayer : MonoBehaviour
{
    public AudioClip[] playlist; // Array of audio clips for the playlist
    private AudioSource audioSource;
    private int currentTrackIndex = 0;

    // resync if off by more than this
    private const float maxDrift = 0.5f;

    private const float endFadeDuration = 3f;

    private bool ended;

    public int CurrentTrack => currentTrackIndex;

    public float Position => audioSource.time;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        // loop was on so it never got to the next song
        audioSource.loop = false;
        PlayTrack(currentTrackIndex);
        CoopMusic.Register(this);
    }

    // Play the specified track from the playlist
    void PlayTrack(int trackIndex, float time = 0f)
    {
        currentTrackIndex = trackIndex;
        audioSource.clip = playlist[trackIndex];
        audioSource.Play();
        audioSource.time = time;
        CoopMusic.SendCurrent();
    }

    public void NextTrack()
    {
        if (ended)
        {
            return;
        }
        PlayTrack((currentTrackIndex + 1) % playlist.Length);
    }

    public void FollowHost(int trackIndex, float time)
    {
        // past the end, host already moved on
        if (ended || trackIndex >= playlist.Length || time >= playlist[trackIndex].length)
        {
            return;
        }
        if (trackIndex != currentTrackIndex || !audioSource.isPlaying || Mathf.Abs(audioSource.time - time) > maxDrift)
        {
            PlayTrack(trackIndex, time);
        }
    }

    public void EndMusic()
    {
        if (ended)
        {
            return;
        }
        ended = true;
        StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float startVolume = audioSource.volume;
        for (float elapsed = 0f; elapsed < endFadeDuration; elapsed += Time.unscaledDeltaTime)
        {
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / endFadeDuration);
            yield return null;
        }
        audioSource.Stop();
#if DUMP_WORLDGEN
        Debug.Log("CoopDebug music faded out for the ending");
#endif
    }

    void Update()
    {
        // Check if the current track has finished playing
        if (!ended && !audioSource.isPlaying)
        {
            // Move to the next track in the playlist
            // Play the next track
            PlayTrack((currentTrackIndex + 1) % playlist.Length);
        }
    }
}
