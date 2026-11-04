using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small pill-shaped health bar that floats above a human or a police officer.
///
/// - Fades in when the vampire is near and fades out when he is far away.
/// - A thin black border around a dark track; the fill is green at full health, turns yellow, then red as health drops.
/// - Fades out and is removed when the character is dead (and while he is being bitten).
///
/// You do not have to add it by hand: EntityHealthBarInstaller (below) adds it to every HumanAI and PoliceAI
/// when a scene starts. To switch it off, untick "Auto Install" on the HealthBarInstaller object or set
/// EntityHealthBarInstaller.AutoInstall = false.
/// </summary>
[DisallowMultipleComponent]
public class EntityHealthBar : MonoBehaviour
{
    [Header("Visibility")]
    [Tooltip("The bar fades in when the vampire is closer than this (metres).")]
    public float showDistance = 9f;
    [Tooltip("It fades out again once the vampire is this much further than Show Distance. Stops it flickering at the edge.")]
    public float hideMargin = 1.5f;
    [Tooltip("How fast it fades, in opacity per second (4 = a quarter of a second).")]
    public float fadeSpeed = 4f;
    [Tooltip("While the vampire bites someone the bar drains along with the bite animation. This is only the fallback duration (seconds) if the animation cannot be read.")]
    public float biteDrainTime = 1.2f;
    [Tooltip("After the victim dies, the empty bar stays this long (seconds) before it fades out.")]
    public float deathLinger = 0.35f;


    [Header("Look")]
    [Tooltip("Width of the bar in metres. The height follows (about 1/7.5 of the width).")]
    public float barWidth = 1.2f;
    [Tooltip("Gap between the top of the character and the bar (metres).")]
    public float heightAboveHead = 0.3f;
    [Tooltip("Extra offset on top of the automatic head height, if a character needs adjusting.")]
    public float extraHeight = 0f;
    public Color fullColor = new Color(0.25f, 0.85f, 0.30f);
    public Color halfColor = new Color(1.00f, 0.85f, 0.15f);
    public Color emptyColor = new Color(0.90f, 0.15f, 0.12f);
    public Color trackColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
    [Tooltip("Border colour of a thrall's bar, so the vampire's allies are easy to tell from enemies.")]
    public Color thrallBorderColor = new Color(0.62f, 0.32f, 1f);

    [Tooltip("How fast the fill slides to a new value (fraction of the bar per second).")]
    public float fillSpeed = 1.5f;

    // Layout in canvas units (the canvas is scaled down to barWidth metres).
    const float BarW = 480f, BarH = 64f;     // black border pill
    const float BorderThickness = 6f;
    const float FillPadding = 2f;
    const float TrackW = BarW - 2f * BorderThickness, TrackH = BarH - 2f * BorderThickness;
    const float FillMaxW = TrackW - 2f * FillPadding, FillH = TrackH - 2f * FillPadding;

    HumanAI human;
    PoliceAI police;
    Transform player;
    Camera cam;
    GameObject root;
    Canvas canvas;
    CanvasGroup group;
    RectTransform fillRect;
    Image fillImage;
    float headTop = 1.8f;
    float shown = 1f;
    float alpha;
    bool near;
    VampireController vampireController;
    Animator vampireAnimator;
    bool wasCaptured, sawBite;
    float captureStartShown, captureTime, biteProgress;
    float deadTime = -1f;
    ThrallAI thrall;
    Image borderImage;
    // Officers only: shows when he can be bitten (his health is at or below PoliceAI.biteHealthFraction).
    Image tickImage, biteIcon;
    RectTransform tickRect;
    GameObject biteRing;
    Material biteRingMaterial;
    static Sprite fangSprite;
    static Texture ringTexture;



    static Sprite borderSprite, trackSprite, fillSprite;

void Start()
    {
        human = GetComponent<HumanAI>();
        police = GetComponent<PoliceAI>();
        if (human == null && police == null) { Destroy(this); return; }

        headTop = ComputeHeadTop();
        ReadState(out bool alive, out _, out float fraction);
        shown = fraction;
        if (!alive) deadTime = Time.time - 1000f; // already dead when the scene starts: no bar at all
        Build();
    }

    void OnEnable() { if (root != null) root.SetActive(true); }
    void OnDisable() { if (root != null) root.SetActive(false); if (biteRing != null) biteRing.SetActive(false); }
    void OnDestroy() { if (root != null) Destroy(root); if (biteRing != null) Destroy(biteRing); }

    // ------------------------------------------------------------------ build

    void Build()
    {
        if (borderSprite == null)
        {
            borderSprite = MakePill((int)BarH);
            trackSprite = MakePill((int)TrackH);
            fillSprite = MakePill((int)FillH);
        }

        // Not a child of the character, so his rotation, scale and animations never affect the bar.
        root = new GameObject(name + "_HealthBar");
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        group = root.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(BarW, BarH);
        rt.localScale = Vector3.one * (barWidth / BarW);

        borderImage = AddImage("Border", borderSprite, Color.black, new Vector2(BarW, BarH), new Vector2(0.5f, 0.5f), Vector2.zero);
        AddImage("Track", trackSprite, trackColor, new Vector2(TrackW, TrackH), new Vector2(0.5f, 0.5f), Vector2.zero);

        fillImage = AddImage("Fill", fillSprite, fullColor, new Vector2(FillMaxW, FillH), new Vector2(0f, 0.5f),
                             new Vector2(BorderThickness + FillPadding, 0f));
        fillRect = fillImage.rectTransform;
        if (police != null) BuildBiteHints();
        canvas.enabled = false;
        UpdateFill();
    }

    Image AddImage(string childName, Sprite sprite, Color color, Vector2 size, Vector2 anchor, Vector2 position)
    {
        GameObject go = new GameObject(childName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root.transform, false);
        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(anchor.x, 0.5f);
        r.anchoredPosition = position;
        r.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced; // the rounded ends stay round however wide the bar is
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>A white pill (stadium) sprite of the given height, drawn with a soft edge. Sliced so the caps never stretch.</summary>
    static Sprite MakePill(int h)
    {
        int w = h * 3;
        float r = h * 0.5f;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, r, w - r);
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - r) * (py - r));
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(r - d + 0.5f) * 255f);
                pixels[y * w + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        float cap = Mathf.Round(r);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                             new Vector4(cap, 0f, cap, 0f));
    }

    /// <summary>Top of the character's visible mesh above his feet, so the bar sits over his head whatever his size.</summary>
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

    // ------------------------------------------------------------------ "you can bite him now" (officers)

    void BuildBiteHints()
    {
        // A thin white mark on the bar, at the health where he becomes bitable.
        tickImage = AddImage("BiteMark", null, new Color(1f, 1f, 1f, 0.95f), new Vector2(5f, TrackH + 14f), new Vector2(0f, 0.5f), Vector2.zero);
        tickRect = tickImage.rectTransform;
        tickRect.pivot = new Vector2(0.5f, 0.5f);

        // A red fang badge above the bar, only while he can be bitten.
        if (fangSprite == null) fangSprite = MakeFangSprite(128);
        biteIcon = AddImage("BiteIcon", fangSprite, Color.white, new Vector2(150f, 150f), new Vector2(0.5f, 1f), new Vector2(0f, 8f));
        biteIcon.type = Image.Type.Simple;
        biteIcon.rectTransform.pivot = new Vector2(0.5f, 0f);
        biteIcon.gameObject.SetActive(false);

        // A red ring on the ground under him: easy to spot from far away.
        if (ringTexture == null) ringTexture = ThrallAbility.MakeCircleSprite(256, 0.5f, 0.40f).texture;
        biteRing = GameObject.CreatePrimitive(PrimitiveType.Quad);
        biteRing.name = name + "_BiteRing";
        Destroy(biteRing.GetComponent<Collider>());
        biteRing.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lies on the ground
        biteRingMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = ringTexture };
        Renderer rr = biteRing.GetComponent<Renderer>();
        rr.sharedMaterial = biteRingMaterial;
        rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rr.receiveShadows = false;
        biteRing.SetActive(false);
    }

    void UpdateBiteHints(bool aliveAndFree)
    {
        if (police == null || tickImage == null) return;
        bool isThrall = thrall != null;
        bool biteReady = aliveAndFree && !isThrall && police.CanBeBitten;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);

        // The mark shows where on the bar he can be bitten.
        tickImage.gameObject.SetActive(!isThrall);
        tickRect.anchoredPosition = new Vector2(BorderThickness + FillPadding + FillMaxW * police.biteHealthFraction, 0f);

        // While he can be bitten: a pulsing red border, a bobbing fang badge, and a ring on the ground.
        if (biteReady && borderImage != null) borderImage.color = Color.Lerp(new Color(1f, 0.1f, 0.1f), Color.white, pulse * 0.6f);
        biteIcon.gameObject.SetActive(biteReady);
        if (biteReady)
        {
            biteIcon.rectTransform.localScale = Vector3.one * (1f + 0.15f * pulse);
            biteIcon.rectTransform.anchoredPosition = new Vector2(0f, 8f + 12f * pulse);
        }
        bool ringOn = biteReady && alpha > 0.01f;
        biteRing.SetActive(ringOn);
        if (ringOn)
        {
            biteRing.transform.position = transform.position + Vector3.up * 0.07f;
            float size = 1.5f + 0.25f * pulse;
            biteRing.transform.localScale = new Vector3(size, size, 1f);
            biteRingMaterial.color = new Color(1f, 0.12f, 0.12f, (0.5f + 0.4f * pulse) * alpha);
        }
    }

    /// <summary>A dark-red round badge with a white rim and two white fangs.</summary>
    static Sprite MakeFangSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        Vector2[] left = { new Vector2(0.28f, 0.66f), new Vector2(0.48f, 0.66f), new Vector2(0.38f, 0.20f) };
        Vector2[] right = { new Vector2(0.52f, 0.66f), new Vector2(0.72f, 0.66f), new Vector2(0.62f, 0.20f) };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float disc = Mathf.Clamp01(c - 1f - d + 0.5f);
                float rim = Mathf.Clamp01(d - (c - 10f) + 0.5f);
                float fang = 0f;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        Vector2 p = new Vector2((x + (sx + 0.5f) / 2f) / size, (y + (sy + 0.5f) / 2f) / size);
                        if (InTriangle(p, left) || InTriangle(p, right)) fang += 0.25f;
                    }
                Color col = Color.Lerp(new Color(0.60f, 0.03f, 0.06f), Color.white, Mathf.Max(rim, fang));
                px[y * size + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(disc * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static bool InTriangle(Vector2 p, Vector2[] t)
    {
        float d1 = Side(p, t[0], t[1]), d2 = Side(p, t[1], t[2]), d3 = Side(p, t[2], t[0]);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    static float Side(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

    
// ------------------------------------------------------------------ per frame

void LateUpdate()
    {
        if (root == null) return;
        if (cam == null) { cam = Camera.main; if (cam != null) canvas.worldCamera = cam; }
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null)
            {
                player = p.transform;
                vampireController = p.GetComponent<VampireController>();
                vampireAnimator = p.GetComponent<Animator>();
            }
        }

        ReadState(out bool alive, out bool captured, out float fraction);
        UpdateBiteHints(alive && !captured);

        // Float above his head and face the screen.
        root.transform.position = transform.position + Vector3.up * (headTop + heightAboveHead + extraHeight);
        if (cam != null) root.transform.rotation = cam.transform.rotation;

        // Near the vampire -> visible. A small margin stops it flickering at the edge.
        if (player != null)
        {
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist <= showDistance) near = true;
            else if (dist > showDistance + hideMargin) near = false;
        }

        // Being bitten does not hide the bar (it drains instead). Once he is dead, the empty bar lingers a moment, then fades out.
        if (!alive && deadTime < 0f) deadTime = Time.time;
        bool lingering = !alive && Time.time - deadTime < deathLinger;
        float target = near && (alive || lingering) ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * (alive ? 1f : 1.5f) * Time.deltaTime);
        group.alpha = alpha;
        canvas.enabled = alpha > 0.001f;

        if (captured)
        {
            // The bite drains him: the fill empties along with the bite animation, so it goes green -> yellow -> red.
            if (!wasCaptured)
            {
                wasCaptured = true;
                sawBite = false;
                biteProgress = 0f;
                captureStartShown = shown;
                captureTime = Time.time;
            }
            biteProgress = Mathf.Max(biteProgress, CurrentBiteProgress());
            shown = captureStartShown * (1f - biteProgress);
        }
        else
        {
            wasCaptured = false;
            shown = Mathf.MoveTowards(shown, alive ? fraction : 0f, fillSpeed * Time.deltaTime);
        }
        UpdateFill();

        // Dead and faded out: the bar is no longer needed.
        if (!alive && alpha <= 0.001f && !lingering)
        {
            Destroy(root);
            root = null;
            enabled = false;
        }
    }

void UpdateFill()
    {
        float f = Mathf.Clamp01(shown);
        // A nearly empty bar stays a small red pill, which then shrinks away smoothly as it reaches zero.
        float w = f <= 0.0001f ? 0f : Mathf.Max(FillMaxW * f, FillH * Mathf.Clamp01(f / 0.04f));
        fillRect.sizeDelta = new Vector2(w, FillH);
        fillImage.enabled = w > 0.5f;
        fillImage.color = f >= 0.5f
            ? Color.Lerp(halfColor, fullColor, (f - 0.5f) * 2f)
            : Color.Lerp(emptyColor, halfColor, f * 2f);
    }

/// <summary>How far through the bite he is (0 = just grabbed, 1 = the moment the victim dies), read from the vampire's bite animation.</summary>
    float CurrentBiteProgress()
    {
        if (vampireController != null && vampireAnimator != null)
        {
            AnimatorStateInfo info = vampireAnimator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Bite"))
            {
                sawBite = true;
                return Mathf.Clamp01(info.normalizedTime / Mathf.Max(0.01f, vampireController.killAtNormalizedTime));
            }
            if (sawBite || Time.time - captureTime < 0.8f) return 0f; // the bite is about to start (or already over)
        }
        // Fallback if the animation cannot be read: drain over a fixed time.
        return Mathf.Clamp01((Time.time - captureTime) / Mathf.Max(0.1f, biteDrainTime));
    }


void ReadState(out bool alive, out bool captured, out float fraction)
    {
        if (thrall == null) thrall = GetComponent<ThrallAI>();
        bool isThrall = thrall != null;
        if (borderImage != null) borderImage.color = isThrall ? thrallBorderColor : Color.black;

        if (isThrall)
        {
            // A thrall has his own health (he can really die), separate from the fight health he had as an enemy.
            alive = thrall.IsAlive;
            captured = false;
            fraction = thrall.HealthFraction;
        }
        else if (human != null)
        {
            alive = human.IsAlive;
            captured = human.currentState == HumanAI.State.Captured;
            fraction = human.HealthFraction;
        }
        else if (police != null)
        {
            alive = police.IsAlive;
            captured = police.currentState == PoliceAI.State.Captured;
            fraction = police.HealthFraction;
        }
        else
        {
            alive = false;
            captured = false;
            fraction = 0f;
        }
    }
}

/// <summary>
/// Adds an EntityHealthBar to every human and police officer when the game starts (and to any that appear later),
/// so nothing has to be set up in the scenes.
/// </summary>
public class EntityHealthBarInstaller : MonoBehaviour
{
    [Tooltip("Untick to stop adding health bars automatically.")]
    public bool autoInstall = true;
    public static bool AutoInstall = true;

    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<EntityHealthBarInstaller>() != null) return;
        GameObject go = new GameObject("HealthBarInstaller");
        go.AddComponent<EntityHealthBarInstaller>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1f;
        // Every officer shows when his shield is up and when it blocks a blow (independent of the health bars).
        foreach (PoliceAI officer in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
            if (officer.GetComponent<OfficerGuardFeedback>() == null) officer.gameObject.AddComponent<OfficerGuardFeedback>();

        if (!autoInstall || !AutoInstall) return;

        foreach (HumanAI h in FindObjectsByType<HumanAI>(FindObjectsSortMode.None))
            if (h.GetComponent<EntityHealthBar>() == null) h.gameObject.AddComponent<EntityHealthBar>();
        foreach (PoliceAI p in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
            if (p.GetComponent<EntityHealthBar>() == null) p.gameObject.AddComponent<EntityHealthBar>();
    }
}
