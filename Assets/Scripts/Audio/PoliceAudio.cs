using UnityEngine;

/// Officer voice lines: a shout when he spots the vampire, a grunt when punched, a scream when bitten,
/// a last breath when he dies. Only one officer shouts at a time so the street doesn't turn into a chorus.
[RequireComponent(typeof(PoliceAI))]
public class PoliceAudio : MonoBehaviour
{
    public AudioClip[] spotted;
    public AudioClip[] hurt;
    public AudioClip[] bitten;
    public AudioClip[] death;
    [Range(0f, 1f)] public float volume = 0.9f;
    [Tooltip("Seconds between 'spotted' shouts across all officers.")]
    public float shoutCooldown = 6f;

    static float nextShout;
    PoliceAI ai;
    AudioSource voice;
    bool wasEngaged;
    float lastHealth = -1f;
    PoliceAI.State lastState;

    void Awake()
    {
        ai = GetComponent<PoliceAI>();
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 6f;
        voice.maxDistance = 45f;
        lastState = ai.currentState;
    }

    void Update()
    {
        if (!ai.enabled) return;     // frozen in the cutscene or taken over by the arrest
        if (ai.IsEngaged && !wasEngaged && ai.IsAlive && Time.time >= nextShout)
        {
            nextShout = Time.time + shoutCooldown;
            Say(spotted);
        }
        wasEngaged = ai.IsEngaged;

        float h = ai.HealthFraction;
        if (lastHealth >= 0f && h < lastHealth - 0.001f && ai.currentState != PoliceAI.State.Dead) Say(hurt);
        lastHealth = h;

        if (ai.currentState != lastState)
        {
            if (ai.currentState == PoliceAI.State.Captured) Say(bitten);
            else if (ai.currentState == PoliceAI.State.Dead) Say(death);
            lastState = ai.currentState;
        }
    }

    void Say(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        voice.Stop();                // a new line replaces the old one
        voice.pitch = Random.Range(0.94f, 1.06f);
        voice.clip = clip;
        voice.volume = volume;
        voice.Play();
    }
}
