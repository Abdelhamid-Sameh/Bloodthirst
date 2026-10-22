using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sound effects for a civilian. Watches the HumanAI state, so HumanAI itself needs no changes.
/// Drop clips into the slots; empty slots are simply silent.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(HumanAI))]
public class HumanAudio : MonoBehaviour
{
    [Header("Footsteps")]
    public AudioClip[] walkSteps;
    public AudioClip[] runSteps;
    public float walkStepInterval = 0.5f;
    public float runStepInterval = 0.3f;
    [Tooltip("Agent speed at or above which run steps are used.")]
    public float runSpeedThreshold = 3f;
    [Range(0f, 1f)] public float footstepVolume = 0.45f;

    [Header("Voice")]
    [Tooltip("Gasp when they first spot the vampire.")]
    public AudioClip[] alertedGasps;
    [Tooltip("Screams while running away.")]
    public AudioClip[] fleeScreams;
    [Tooltip("Scream when the vampire grabs them.")]
    public AudioClip[] capturedScreams;
    [Tooltip("Last gasp when the bite kills them.")]
    public AudioClip[] deathSounds;
    [Range(0f, 1f)] public float voiceVolume = 1f;
    [Tooltip("Seconds between screams while fleeing (random in this range).")]
    public Vector2 fleeScreamInterval = new Vector2(2.5f, 5f);

    [Range(0f, 0.3f)] public float pitchJitter = 0.1f;

    HumanAI ai;
    NavMeshAgent agent;
    AudioSource source;
    HumanAI.State lastState;
    float stepTimer;
    float screamTimer;

    void Awake()
    {
        ai = GetComponent<HumanAI>();
        agent = GetComponent<NavMeshAgent>();
        source = GetComponent<AudioSource>();
        lastState = ai.currentState;
    }

    void Update()
    {
        if (GameState.Paused) return;

        HumanAI.State state = ai.currentState;
        if (state != lastState)
        {
            OnStateChanged(state);
            lastState = state;
        }

        if (state == HumanAI.State.Fleeing)
        {
            screamTimer -= Time.deltaTime;
            if (screamTimer <= 0f)
            {
                Play(fleeScreams, voiceVolume);
                screamTimer = Random.Range(fleeScreamInterval.x, fleeScreamInterval.y);
            }
        }

        float speed = agent != null && agent.enabled ? agent.velocity.magnitude : 0f;
        bool moving = speed > 0.3f && (state == HumanAI.State.Walking || state == HumanAI.State.Fleeing);
        if (moving)
        {
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f)
            {
                bool running = speed >= runSpeedThreshold;
                Play(running ? runSteps : walkSteps, footstepVolume);
                stepTimer = running ? runStepInterval : walkStepInterval;
            }
        }
        else
        {
            stepTimer = 0.1f;
        }
    }

    void OnStateChanged(HumanAI.State state)
    {
        switch (state)
        {
            case HumanAI.State.Alerted:
                Play(alertedGasps, voiceVolume);
                break;
            case HumanAI.State.Fleeing:
                Play(fleeScreams, voiceVolume);
                screamTimer = Random.Range(fleeScreamInterval.x, fleeScreamInterval.y);
                break;
            case HumanAI.State.Captured:
                source.Stop();
                Play(capturedScreams, voiceVolume);
                break;
            case HumanAI.State.Dead:
                Play(deathSounds, voiceVolume);
                break;
        }
    }

    void Play(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.PlayOneShot(clip, volume);
    }
}
