
using UnityEngine;

/// <summary>
/// Makes an officer's guard easy to see from the game camera's distance.
///
/// When the vampire attacks, an officer may raise his shield (PoliceAI.ConsiderBlock): for a few seconds he takes only a quarter
/// of the damage and does not flinch. From far away that looked like the punch had simply failed. This shows what is happening:
///
///  - While his shield is up: a faint golden honeycomb dome around him and a small shield symbol above his head.
///  - When a blow lands on it: the dome and the symbol flash, a shock ring spreads, sparks fly and a metallic clang plays.

///
/// It is added to every officer automatically (EntityHealthBarInstaller), so nothing has to be set up in the scene.
/// The dome is gold on purpose: it is easy to tell apart from the vampire's own blue Shield.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PoliceAI))]
public class OfficerGuardFeedback : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Colour of the dome, the sparks and the text.")]
    public Color guardColor = new Color(1f, 0.82f, 0.35f);
    [Tooltip("Width of the dome in metres.")]
    public float domeDiameter = 2.4f;
    [Tooltip("How visible the dome is while his shield is up and nothing is hitting it.")]
    [Range(0f, 1f)] public float guardGlow = 0.14f;
    [Tooltip("Size of the shield symbol above his head (metres).")]
    public float badgeSize = 0.4f;
    
    

    [Header("Sound")]
    public bool playSound = true;
    [Range(0f, 1f)] public float volume = 0.8f;

    PoliceAI police;
    Camera cam;
    bool built, wasBlocking;
    GameObject dome, hexLayer, rippleLayer, badge;
    Material domeMaterial, hexMaterial, rippleMaterial, badgeMaterial;
    ParticleSystem sparks;
    
    AudioSource audioSource;
    float guard, flash, ripple = 1f, headTop = 1.8f;

    // Shared by every officer, so each texture is made only once.
    static Texture2D bubbleTexture, hexTexture, ringTexture, badgeTexture;
    static AudioClip clang;

    Vector3 Chest => transform.position + Vector3.up * 1f;

    void Start()
    {
        police = GetComponent<PoliceAI>();
        police.BlockedHit += OnBlockedHit;
    }

void OnDestroy()
    {
        if (police != null) police.BlockedHit -= OnBlockedHit;
        if (dome != null) Destroy(dome);
        if (hexLayer != null) Destroy(hexLayer);
        if (rippleLayer != null) Destroy(rippleLayer);
        if (badge != null) Destroy(badge);
        if (sparks != null) Destroy(sparks.gameObject);
    }

    // ------------------------------------------------------------------ the hit

void OnBlockedHit(float damageThatGotThrough)
    {
        Build();
        flash = 1f;    // the dome and the shield symbol flash
        ripple = 0f;

        sparks.transform.position = Chest;
        sparks.Emit(16);

        if (playSound && audioSource != null)
        {
            audioSource.pitch = Random.Range(0.92f, 1.08f);
            audioSource.PlayOneShot(clang, volume);
        }
    }

    // ------------------------------------------------------------------ every frame

void LateUpdate()
    {
        if (police == null) return;
        bool blocking = police.IsBlocking && police.IsAlive;
        if (!built && !blocking) return;   // nothing to show: costs nothing for officers who never guard
        Build();
        if (cam == null) cam = Camera.main;

        if (blocking && !wasBlocking) ripple = 0f;   // a ring as the shield comes up
        wasBlocking = blocking;

        guard = Mathf.MoveTowards(guard, blocking ? 1f : 0f, 8f * Time.deltaTime);
        flash = Mathf.MoveTowards(flash, 0f, 3f * Time.deltaTime);

        bool visible = guard > 0.001f;
        dome.SetActive(visible);
        hexLayer.SetActive(visible);
        badge.SetActive(visible);
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        Vector3 toCamera = cam != null ? -cam.transform.forward : Vector3.up;
        Vector3 center = Chest;

        if (visible)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);

            // The dome: faint, so it never hides the officer.
            float size = domeDiameter * Mathf.Lerp(0.85f, 1f, guard) * (1f + 0.04f * flash);
            dome.transform.SetPositionAndRotation(center, face);
            dome.transform.localScale = new Vector3(size, size, 1f);
            Color c = Color.Lerp(guardColor, Color.white, flash);
            domeMaterial.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((guardGlow * (0.85f + 0.15f * pulse) + flash * 0.25f) * guard));

            hexLayer.transform.SetPositionAndRotation(center + toCamera * 0.02f, face * Quaternion.Euler(0f, 0f, Time.time * -10f));
            hexLayer.transform.localScale = new Vector3(size * 0.98f, size * 0.98f, 1f);
            hexMaterial.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((guardGlow * 1.2f + flash * 0.35f) * guard));

            // The shield symbol above his head (to the side of the fang badge when he can also be bitten). It bobs, and pops on a hit.
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            float side = police.CanBeBitten ? badgeSize * 1.05f : 0f;
            float height = headTop + 0.3f + 0.16f + 0.05f + badgeSize * 0.5f + 0.04f * pulse;   // just above where his health bar floats
            badge.transform.SetPositionAndRotation(transform.position + Vector3.up * height + right * side + toCamera * 0.06f, face);
            float badgeScale = badgeSize * Mathf.Lerp(0.6f, 1f, guard) * (1f + 0.1f * pulse + 0.35f * flash);
            badge.transform.localScale = new Vector3(badgeScale, badgeScale, 1f);
            badgeMaterial.color = new Color(1f, Mathf.Lerp(1f, 0.92f, flash), Mathf.Lerp(1f, 0.6f, flash), guard);
        }

        // A shock ring spreading outward.
        if (ripple < 1f)
        {
            ripple += Time.deltaTime / 0.4f;
            float rs = domeDiameter * Mathf.Lerp(0.8f, 1.5f, ripple);
            rippleLayer.SetActive(true);
            rippleLayer.transform.SetPositionAndRotation(center + toCamera * 0.04f, face);
            rippleLayer.transform.localScale = new Vector3(rs, rs, 1f);
            rippleMaterial.color = new Color(1f, 0.95f, 0.8f, Mathf.Clamp01(1f - ripple) * 0.6f);
        }
        else rippleLayer.SetActive(false);
    }

    // ------------------------------------------------------------------ building

void Build()
    {
        if (built) return;
        built = true;

        if (bubbleTexture == null) bubbleTexture = ShieldAbility.MakeBubbleTexture(256);
        if (hexTexture == null) hexTexture = ShieldAbility.MakeHexTexture(512);
        if (ringTexture == null) ringTexture = ThrallAbility.MakeCircleSprite(256, 0.5f, 0.46f).texture;
        if (badgeTexture == null) badgeTexture = MakeBadgeTexture(128);
        if (clang == null) clang = MakeClang();
        headTop = ComputeHeadTop();

        Shader shader = Shader.Find("Sprites/Default");
        dome = MakeLayer("GuardDome", shader, bubbleTexture, 0, out domeMaterial);
        hexLayer = MakeLayer("GuardHexagons", shader, hexTexture, 1, out hexMaterial);
        rippleLayer = MakeLayer("GuardRipple", shader, ringTexture, 2, out rippleMaterial);
        badge = MakeLayer("GuardBadge", shader, badgeTexture, 3, out badgeMaterial);
        sparks = BuildSparks();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 10f;
        audioSource.maxDistance = 80f;
    }

    /// <summary>Top of his visible mesh above his feet (the same measure the health bar uses), so the symbol sits over his head.</summary>
    float ComputeHeadTop()
    {
        float top = 0f;
        bool found = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
            float t = r.bounds.max.y - transform.position.y;
            if (!found || t > top) { top = t; found = true; }
        }
        return found ? Mathf.Clamp(top, 1f, 3f) : 1.8f;
    }

    /// <summary>A round badge like the fang badge: dark gold disc, white rim, and a white shield with a dark centre.</summary>
    static Texture2D MakeBadgeTexture(int size)
    {
        Texture2D tex = ThrallAbility.NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        Color disc = new Color(0.62f, 0.40f, 0.04f);
        Color panel = new Color(0.36f, 0.22f, 0.02f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float inside = Mathf.Clamp01(c - 1f - d + 0.5f);
                float rim = Mathf.Clamp01(d - (c - 10f) + 0.5f);
                float outer = 0f, inner = 0f;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = (x + (sx + 0.5f) / 2f) / size, v = (y + (sy + 0.5f) / 2f) / size;
                        if (ShieldAbility.InShield(u, v, 0.78f)) outer += 0.25f;
                        if (ShieldAbility.InShield(u, v, 0.78f * 0.68f)) inner += 0.25f;
                    }
                Color col = Color.Lerp(disc, Color.white, Mathf.Max(rim, outer));
                col = Color.Lerp(col, panel, inner);
                px[y * size + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(inside * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>One flat quad of the dome: no collider (it must never block bullets), drawn in a fixed order.</summary>
    static GameObject MakeLayer(string layerName, Shader shader, Texture texture, int order, out Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = layerName;
        Destroy(go.GetComponent<Collider>());
        material = new Material(shader) { mainTexture = texture };
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.sortingOrder = order;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    ParticleSystem BuildSparks()
    {
        GameObject go = new GameObject("GuardSparks");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(guardColor, Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;

        var emission = ps.emission;
        emission.enabled = false;   // bursts are emitted by hand
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = fade;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        pr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = ThrallAbility.MakeBlobTexture(64) };
        ps.Play();
        return ps;
    }



    // ------------------------------------------------------------------ sound

    /// <summary>A short metallic clang made from three ringing tones and a burst of noise, so no audio file is needed.</summary>
    static AudioClip MakeClang()
    {
        const int rate = 44100;
        int samples = (int)(rate * 0.45f);
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float ring = Mathf.Sin(2f * Mathf.PI * 1180f * t) * Mathf.Exp(-t * 11f)
                       + 0.7f * Mathf.Sin(2f * Mathf.PI * 1760f * t + 1f) * Mathf.Exp(-t * 14f)
                       + 0.5f * Mathf.Sin(2f * Mathf.PI * 2970f * t) * Mathf.Exp(-t * 20f);
            float hit = (Random.value * 2f - 1f) * Mathf.Exp(-t * 70f);   // the sharp impact at the start
            data[i] = Mathf.Clamp((ring * 0.45f + hit * 0.7f), -1f, 1f);
        }
        AudioClip clip = AudioClip.Create("GuardClang", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
