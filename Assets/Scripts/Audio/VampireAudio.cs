using UnityEngine;

/// <summary>
/// The vampire's sound effects. Drop clips into the slots (or run Bloodthirst > Assign Sounds); empty slots are silent.
/// Arrays pick a random clip each time so repeated sounds vary. VampireController calls the Play methods;
/// hits and the low-blood heartbeat come from VampireBlood.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VampireAudio : MonoBehaviour
{
    [Header("Footsteps (played while running)")]
    public AudioClip[] footsteps;
    [Tooltip("Points in the run cycle (0-1) where a foot lands; each step plays as the animation passes one, so steps follow the legs at any speed (sprint too).")]
    public float[] footfallPhases = { 0.40f, 0.88f };
    [Range(0f, 1f)] public float footstepVolume = 0.55f;

    [Header("Actions")]
    public AudioClip[] jump;
    public AudioClip[] land;
    [Tooltip("Played when he lunges and sinks his fangs in.")]
    public AudioClip[] bite;
    [Tooltip("Played when the victim dies (drinking / gulp).")]
    public AudioClip[] feed;
    [Tooltip("Pain grunt when a bullet hits him.")]
    public AudioClip[] hurt;
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

    [Header("Low blood")]
    [Tooltip("Seamless heartbeat loop; fades in below the low-blood threshold and speeds up as he empties.")]
    public AudioClip heartbeat;
    [Range(0f, 1f)] public float heartbeatVolume = 0.8f;
    [Tooltip("Playback speed of the loop when the blood is almost empty.")]
    public float heartbeatMaxPitch = 1.5f;

    [Tooltip("Random pitch variation so repeated sounds don't sound identical.")]
    [Range(0f, 0.3f)] public float pitchJitter = 0.08f;

    AudioSource source;
    AudioSource stepSource;
    AudioSource heartSource;
    VampireController controller;
    VampireBlood blood;
    Animator animator;
    float lastPhase = -1f;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        stepSource = CloneSource(source);
        controller = GetComponent<VampireController>();
        animator = GetComponentInChildren<Animator>();
        blood = GetComponent<VampireBlood>();
        if (heartbeat != null)
        {
            heartSource = gameObject.AddComponent<AudioSource>();
            heartSource.clip = heartbeat;
            heartSource.loop = true;
            heartSource.playOnAwake = false;
            heartSource.spatialBlend = 0f; // it is in his head, not in the street
            heartSource.volume = 0f;
        }
    }

    void OnEnable() { if (blood != null) blood.Damaged += OnDamaged; }
    void OnDisable() { if (blood != null) blood.Damaged -= OnDamaged; }

    void OnDamaged(float amount) => Play(hurt, actionVolume);

    void Update()
    {
        UpdateHeartbeat();
        if (controller == null || GameState.Paused) return;
        if (controller.IsRunning && controller.IsGrounded && !controller.IsBusy && animator != null)
        {
            float phase = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
            if (lastPhase >= 0f)
                foreach (float f in footfallPhases)
                    if (Passed(lastPhase, phase, f)) { PlayStep(); break; }
            lastPhase = phase;
        }
        else
        {
            lastPhase = -1f;
        }
    }

    void UpdateHeartbeat()
    {
        if (heartSource == null) return;
        bool low = blood != null && blood.IsActive && blood.IsLow && !GameState.Paused;
        float target = low ? heartbeatVolume : 0f;
        heartSource.volume = Mathf.MoveTowards(heartSource.volume, target, Time.unscaledDeltaTime * 1.5f);
        if (low)
        {
            float emptiness = 1f - blood.Fraction / Mathf.Max(0.01f, blood.lowThreshold);
            heartSource.pitch = Mathf.Lerp(1f, heartbeatMaxPitch, emptiness);
            if (!heartSource.isPlaying) heartSource.Play();
        }
        else if (heartSource.isPlaying && heartSource.volume <= 0f) heartSource.Stop();
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

    // True if the cycle moved past `mark` going from `from` to `to` (handles the wrap from 1 back to 0).
    static bool Passed(float from, float to, float mark) => from <= to ? mark > from && mark <= to : mark > from || mark <= to;

    // Each step cuts off the previous one, so long tails never stack into a smear.
    void PlayStep()
    {
        if (footsteps == null || footsteps.Length == 0) return;
        AudioClip clip = footsteps[Random.Range(0, footsteps.Length)];
        if (clip == null) return;
        stepSource.Stop();
        stepSource.clip = clip;
        stepSource.volume = footstepVolume;
        stepSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        stepSource.Play();
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
