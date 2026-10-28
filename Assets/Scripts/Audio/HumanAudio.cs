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
    AudioSource voiceSource;
    HumanAI.State lastState;
    float stepTimer;
    float screamTimer;

    void Awake()
    {
        ai = GetComponent<HumanAI>();
        agent = GetComponent<NavMeshAgent>();
        source = GetComponent<AudioSource>();
        voiceSource = CloneSource(source);
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
            // The next scream waits until the current one has finished.
            if (screamTimer <= 0f && !voiceSource.isPlaying)
            {
                Say(fleeScreams);
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
                PlayStep(running ? runSteps : walkSteps);
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
                Say(alertedGasps);
                break;
            case HumanAI.State.Fleeing:
                Say(fleeScreams);
                screamTimer = Random.Range(fleeScreamInterval.x, fleeScreamInterval.y);
                break;
            case HumanAI.State.Captured:
                source.Stop();
                Say(capturedScreams);
                break;
            case HumanAI.State.Dead:
                Say(deathSounds);
                break;
        }
    }

    // One voice at a time: a new gasp or scream replaces the one still playing instead of stacking on it.
    void Say(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume;
        voiceSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        voiceSource.Play();
    }

    // Each step cuts off the previous one.
    void PlayStep(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        source.Stop();
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.PlayOneShot(clip, footstepVolume);
    }

    // A second source with the same 3D settings, so one kind of sound can cut itself off without touching the rest.
    AudioSource CloneSource(AudioSource from)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.outputAudioMixerGroup = from.outputAudioMixerGroup;
        s.spatialBlend = from.spatialBlend;
        s.rolloffMode = from.rolloffMode;
        s.minDistance = from.minDistance;
        s.maxDistance = from.maxDistance;
        s.dopplerLevel = from.dopplerLevel;
        s.spread = from.spread;
        if (from.rolloffMode == AudioRolloffMode.Custom)
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, from.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        return s;
    }
}
