using System.Collections;
using UnityEngine;

/// <summary>
/// The corridor alarm: a siren and spinning red beacons that run only during the squad fight, never the whole level.
/// Trigger() when the squad bursts in, Stop() when it is beaten. As a safety net it also fades out by itself after
/// maxSeconds, when he dies, and when he leaves the corridor.
/// </summary>
public class Alarm : MonoBehaviour
{
    public AudioSource siren;
    [Range(0f, 1f)] public float volume = 0.45f;
    [Tooltip("The alarm never runs longer than this, even if the fight is still going.")]
    public float maxSeconds = 12f;
    public float fadeOutTime = 2f;
    [Tooltip("The red beacon lights; they spin while the alarm runs.")]
    public Light[] beacons = new Light[0];
    public float beaconIntensity = 35f;
    public float spinSpeed = 300f;

    public bool IsOn { get; private set; }

    float startedAt;
    Coroutine fading;

    void Awake()
    {
        if (siren != null) { siren.loop = true; siren.playOnAwake = false; siren.Stop(); }
        SetBeacons(false);
    }

    void Start()
    {
        var director = Level2Director.Instance;
        if (director == null) return;
        director.onStageChanged.AddListener(s => { if (s > Level2Director.Stage.Corridor) Stop(); });
        director.vampire.onDeath.AddListener(StopNow);
    }

    public void Trigger()
    {
        if (IsOn) return;
        IsOn = true;
        startedAt = Time.time;
        if (fading != null) { StopCoroutine(fading); fading = null; }
        SetBeacons(true);
        if (siren != null) { siren.volume = volume; siren.Play(); }
    }

    /// <summary>Fades the siren out and switches the beacons off.</summary>
    public void Stop()
    {
        if (!IsOn) return;
        IsOn = false;
        fading = StartCoroutine(FadeOut());
    }

    void StopNow()
    {
        IsOn = false;
        if (fading != null) { StopCoroutine(fading); fading = null; }
        if (siren != null) siren.Stop();
        SetBeacons(false);
    }

    void Update()
    {
        if (!IsOn) return;
        foreach (var b in beacons) if (b != null) b.transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        if (Time.time - startedAt >= maxSeconds) Stop();
    }

    IEnumerator FadeOut()
    {
        float from = siren != null ? siren.volume : 0f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, fadeOutTime))
        {
            if (siren != null) siren.volume = Mathf.Lerp(from, 0f, t);
            foreach (var b in beacons) if (b != null) b.intensity = Mathf.Lerp(beaconIntensity, 0f, t);
            yield return null;
        }
        fading = null;
        StopNow();
    }

    void SetBeacons(bool on)
    {
        foreach (var b in beacons) if (b != null) { b.enabled = on; b.intensity = beaconIntensity; }
    }
}
