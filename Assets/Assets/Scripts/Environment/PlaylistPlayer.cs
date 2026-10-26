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

    // the window losing focus pauses the audio, isPlaying goes false for a bit after coming back.
    // only a song that got this close to its end counts as finished
    private const float endTolerance = 1.5f;

    // stopped this many frames without being near the end, play on from where it was
    private const int resumeAfterFrames = 30;

    private float lastTime;

    private int stoppedFrames;

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
        lastTime = time;
        stoppedFrames = 0;
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
        if (ended)
        {
            return;
        }
        if (audioSource.isPlaying)
        {
            lastTime = audioSource.time;
            stoppedFrames = 0;
            return;
        }
        // single player doesn't run in the background, alt-tabbing paused it, not the song ending
        if (!Application.isFocused)
        {
            return;
        }
        if (lastTime >= audioSource.clip.length - endTolerance)
        {
            PlayTrack((currentTrackIndex + 1) % playlist.Length);
        }
        else if (++stoppedFrames > resumeAfterFrames)
        {
            PlayTrack(currentTrackIndex, lastTime);
        }
    }
}
