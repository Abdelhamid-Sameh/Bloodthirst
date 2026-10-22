using System.Collections;
using UnityEngine;

/// <summary>
/// Looping background music plus an optional ambience bed (night city, wind), faded in at the start.
/// Music keeps playing in the pause menu, quieter; everything else pauses.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    public AudioClip music;
    [Range(0f, 1f)] public float musicVolume = 0.45f;
    public AudioClip ambience;
    [Range(0f, 1f)] public float ambienceVolume = 0.35f;
    public float fadeInTime = 3f;
    [Tooltip("Music volume multiplier while the pause menu is open.")]
    [Range(0f, 1f)] public float pausedVolumeScale = 0.4f;

    AudioSource musicSource;
    AudioSource ambienceSource;
    float fade;

    void Awake()
    {
        musicSource = MakeSource(music);
        ambienceSource = MakeSource(ambience);
        musicSource.ignoreListenerPause = true;
    }

    AudioSource MakeSource(AudioClip clip)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        return s;
    }

    IEnumerator Start()
    {
        if (music != null) musicSource.Play();
        if (ambience != null) ambienceSource.Play();
        while (fade < 1f)
        {
            fade += Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeInTime);
            yield return null;
        }
        fade = 1f;
    }

    void Update()
    {
        float pauseScale = GameState.Paused ? pausedVolumeScale : 1f;
        musicSource.volume = Mathf.MoveTowards(musicSource.volume, musicVolume * fade * pauseScale, Time.unscaledDeltaTime);
        ambienceSource.volume = ambienceVolume * fade;
    }
}
