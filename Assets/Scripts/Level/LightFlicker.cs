using UnityEngine;

/// <summary>
/// Makes a light behave like a real one instead of a constant bulb. Put it on (or above) a Light.
/// - Sodium: an old orange street lamp. A slow hum of a few percent and, now and then, a short dip.
/// - Faulty: a dying lamp. It buzzes, stutters off and on in bursts, and sometimes stays dark for a moment.
/// - Fluorescent: an alley tube. Mostly steady with quick double blinks.
/// - Window: light from a room behind a window. A slow drift, sometimes a TV-like flutter.
/// - Strobe: a police light bar. Hard double flashes; give red and blue different phases so they alternate.
/// The lamp's glowing bulb (an emissive material on the lamp head) dims along with it.
/// </summary>
[DisallowMultipleComponent]
public class LightFlicker : MonoBehaviour
{
    public enum Kind { Sodium, Faulty, Fluorescent, Window, Strobe }

    public Kind kind = Kind.Sodium;
    public Light target;
    [Tooltip("Other lights of the same lamp (a street lamp's ground spill) that dim together with the target.")]
    public Light[] followers = new Light[0];
    [Tooltip("Strobe only: flashes per second.")]
    public float strobeRate = 2.2f;
    [Tooltip("Strobe only: 0 or 0.5, so the red and blue lamps of one car alternate.")]
    [Range(0f, 1f)] public float strobePhase;

    float[] followerBase;
    float baseIntensity, seed, dipUntil, dipDepth, nextDip, burstUntil, nextBurst, darkUntil;
    Renderer[] bulbs;
    Color[] bulbEmission;
    MaterialPropertyBlock block;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        if (target == null) target = GetComponent<Light>();
        if (target == null) target = GetComponentInChildren<Light>();
        if (target == null) { enabled = false; return; }
        baseIntensity = target.intensity;
        followerBase = new float[followers.Length];
        for (int i = 0; i < followers.Length; i++) if (followers[i] != null) followerBase[i] = followers[i].intensity;
        seed = Random.value * 100f;
        nextDip = Time.time + Random.Range(3f, 12f);
        nextBurst = Time.time + Random.Range(0.5f, 4f);
        FindBulbs();
    }

    // Bulbs are the emissive materials under the object this component sits on (put it on the lamp's root).
    void FindBulbs()
    {
        Transform head = transform;
        var found = new System.Collections.Generic.List<Renderer>();
        var colors = new System.Collections.Generic.List<Color>();
        foreach (Renderer r in head.GetComponentsInChildren<Renderer>())
        {
            Material m = r.sharedMaterial;
            if (m == null || !m.HasProperty(EmissionId) || !m.IsKeywordEnabled("_EMISSION")) continue;
            Color e = m.GetColor(EmissionId);
            if (e.maxColorComponent < 0.05f) continue;
            found.Add(r);
            colors.Add(e);
        }
        bulbs = found.ToArray();
        bulbEmission = colors.ToArray();
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        float k = Level(Time.time);
        target.intensity = baseIntensity * k;
        target.enabled = k > 0.01f;
        for (int i = 0; i < followers.Length; i++) if (followers[i] != null) { followers[i].intensity = followerBase[i] * k; followers[i].enabled = k > 0.01f; }
        for (int i = 0; i < bulbs.Length; i++)
        {
            if (bulbs[i] == null) continue;
            bulbs[i].GetPropertyBlock(block);
            block.SetColor(EmissionId, bulbEmission[i] * Mathf.Lerp(0.15f, 1f, k));
            bulbs[i].SetPropertyBlock(block);
        }
    }

    /// <summary>Brightness multiplier (0..~1.05) at time t.</summary>
    float Level(float t)
    {
        float hum = Mathf.PerlinNoise(seed, t * 1.3f) - 0.5f;
        switch (kind)
        {
            case Kind.Sodium:
                if (t >= nextDip) { dipUntil = t + Random.Range(0.06f, 0.18f); dipDepth = Random.Range(0.35f, 0.7f); nextDip = t + Random.Range(6f, 20f); }
                return (t < dipUntil ? dipDepth : 1f) * (1f + 0.05f * hum);

            case Kind.Faulty:
                if (t >= nextBurst)
                {
                    burstUntil = t + Random.Range(0.25f, 1.1f);
                    if (Random.value < 0.25f) darkUntil = burstUntil + Random.Range(0.4f, 1.8f);   // gives up for a moment
                    nextBurst = t + Random.Range(1.2f, 5f);
                }
                if (t < darkUntil && t >= burstUntil) return 0f;
                if (t < burstUntil) return Mathf.PerlinNoise(seed, t * 35f) > 0.5f ? Random.Range(0.6f, 1f) : Random.Range(0f, 0.12f);
                return 0.85f + 0.15f * Mathf.PerlinNoise(seed, t * 8f);   // even when lit it buzzes

            case Kind.Fluorescent:
                if (t >= nextBurst) { burstUntil = t + 0.16f; nextBurst = t + Random.Range(2.5f, 9f); }
                if (t < burstUntil) return Mathf.Repeat((burstUntil - t) * 25f, 1f) < 0.5f ? 0.15f : 1f;   // a quick double blink
                return 1f + 0.03f * hum;

            case Kind.Window:
                float drift = 1f + 0.12f * (Mathf.PerlinNoise(seed, t * 0.25f) - 0.5f) * 2f;
                float tv = Mathf.PerlinNoise(seed + 7f, t * 6f) > 0.72f ? 0.75f + 0.5f * Mathf.PerlinNoise(seed, t * 18f) : 1f;
                return drift * tv;

            default: // Strobe
                float cycle = Mathf.Repeat(t * strobeRate + strobePhase, 1f);
                return cycle < 0.18f || (cycle > 0.26f && cycle < 0.40f) ? 1.05f : 0f;   // double flash, then dark
        }
    }
}
