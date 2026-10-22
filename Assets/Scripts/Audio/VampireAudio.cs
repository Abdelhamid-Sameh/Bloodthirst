using UnityEngine;

/// <summary>
/// The vampire's sound effects. Drop clips into the slots; empty slots are simply silent.
/// Arrays pick a random clip each time so repeated sounds vary. VampireController calls the Play methods.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VampireAudio : MonoBehaviour
{
    [Header("Footsteps (played while running)")]
    public AudioClip[] footsteps;
    [Tooltip("Seconds between steps; match it to the run cycle.")]
    public float stepInterval = 0.32f;
    [Range(0f, 1f)] public float footstepVolume = 0.55f;

    [Header("Actions")]
    public AudioClip[] jump;
    public AudioClip[] land;
    [Tooltip("Played when he lunges and sinks his fangs in.")]
    public AudioClip[] bite;
    [Tooltip("Played when the victim dies (drinking / gulp).")]
    public AudioClip[] feed;
    public AudioClip[] death;
    [Range(0f, 1f)] public float actionVolume = 0.9f;

    [Header("Coffin")]
    [Tooltip("Cloth rustle / creak as he lies down in the coffin.")]
    public AudioClip lieDown;
    [Tooltip("Played as he rises out of the coffin.")]
    public AudioClip rise;
    [Tooltip("Smoke whoosh when he vanishes from the closed coffin.")]
    public AudioClip vanish;
    [Tooltip("Smoke whoosh when he reappears in front of it.")]
    public AudioClip appear;

    [Tooltip("Random pitch variation so repeated sounds don't sound identical.")]
    [Range(0f, 0.3f)] public float pitchJitter = 0.08f;

    AudioSource source;
    VampireController controller;
    float stepTimer;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        controller = GetComponent<VampireController>();
    }

    void Update()
    {
        if (controller == null || GameState.Paused) return;
        if (controller.IsRunning && controller.IsGrounded && !controller.IsBusy)
        {
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f)
            {
                Play(footsteps, footstepVolume);
                stepTimer = stepInterval;
            }
        }
        else
        {
            stepTimer = 0.05f; // first step lands right after he starts running
        }
    }

    public void PlayJump() => Play(jump, actionVolume);
    public void PlayLand() => Play(land, actionVolume * 0.8f);
    public void PlayBite() => Play(bite, actionVolume);
    public void PlayFeed() => Play(feed, actionVolume);
    public void PlayDeath() => Play(death, actionVolume);
    public void PlayLieDown() => Play(lieDown, actionVolume);
    public void PlayRise() => Play(rise, actionVolume);
    public void PlayVanish() => Play(vanish, actionVolume);
    public void PlayAppear() => Play(appear, actionVolume);

    void Play(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return;
        Play(clips[Random.Range(0, clips.Length)], volume);
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || source == null) return;
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.PlayOneShot(clip, volume);
    }
}
