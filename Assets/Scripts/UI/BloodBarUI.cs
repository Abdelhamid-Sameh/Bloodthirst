using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-left blood bar: a bat in front of a blood moon whose right wing unfolds into the bar when the
/// vampire leaves his coffin. The blood fills the wing membrane and drains continuously. A hit drops it
/// at once, shakes the bar, flashes it, spills drops off the wing and reddens the screen edges, and a pale
/// "ghost" trails behind (the 2009 Wolverine HUD idea). Feeding pours blood back in with a flash. Under the
/// low threshold the moon beats like a heart, faster as he empties. On death the wing folds away.
/// Positions are in design units: the art canvas is canvasWidth wide, 1 unit = 1 canvas pixel before scaling.
/// </summary>
public class BloodBarUI : MonoBehaviour
{
    public VampireBlood blood;

    [Header("Parts")]
    [Tooltip("Pivots at the shoulder; scaling it unfolds the wing.")]
    public RectTransform wing;
    public CanvasGroup wingGroup;
    [Tooltip("The closed right wing, shown while the bar is folded.")]
    public Image foldedWing;
    public Image fill;
    public Image ghost;
    public Image flash;
    public RectTransform edgeGlow;
    public Image edgeGlowImage;
    [Tooltip("Beats when blood is low.")]
    public RectTransform moon;
    public Image eyes;
    [Tooltip("Full-screen red edges for hits and the low-blood heartbeat.")]
    public Image vignette;
    public Sprite dropSprite;

    [Header("Art geometry (design units)")]
    public float canvasWidth = 660f;
    public float slotXMin = 98f, slotXMax = 640f;

    [Header("Colours")]
    public Color fillColor = Color.white;
    public Color lowPulseColor = new Color(1f, 0.55f, 0.45f);
    public Color ghostColor = new Color(0.95f, 0.71f, 0.71f, 0.85f);
    public Color eyeColor = Color.white;
    public Color dropColor = new Color(0.62f, 0.02f, 0.05f);
    public Color vignetteColor = new Color(0.55f, 0f, 0.02f);

    [Header("Motion")]
    public float unfurlTime = 0.8f;
    public float foldTime = 0.45f;
    [Tooltip("Fraction of the bar per second the fill rises when he feeds or rises from a coffin.")]
    public float riseSpeed = 0.8f;
    [Tooltip("Seconds the pale ghost waits after a hit before it drains down.")]
    public float ghostDelay = 0.45f;
    public float ghostSpeed = 0.6f;
    [Tooltip("Shake distance in design units for a hard hit.")]
    public float shakeAmount = 9f;

    const float FoldedScale = 0.03f;
    const int MaxDrops = 16;

    float shown, ghostValue, ghostHold, flashAlpha, beatClock, shake, hitRed;
    Color flashColor;
    Vector2 restPosition;
    RectTransform rect;
    Coroutine wingRoutine;

    // Small pool of falling blood drops.
    RectTransform[] drops;
    Image[] dropImages;
    Vector2[] dropVelocity;
    float[] dropLife;

    void Awake()
    {
        if (blood == null) blood = FindFirstObjectByType<VampireBlood>();
        rect = (RectTransform)transform;
        restPosition = rect.anchoredPosition;
        BuildDrops();
        SetWing(FoldedScale);
    }

    void OnEnable()
    {
        if (blood == null) return;
        blood.Activated += Unfurl;
        blood.Deactivated += Fold;
        blood.Fed += OnFed;
        blood.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (blood == null) return;
        blood.Activated -= Unfurl;
        blood.Deactivated -= Fold;
        blood.Fed -= OnFed;
        blood.Damaged -= OnDamaged;
    }

    void Start()
    {
        if (blood != null && blood.IsActive) Unfurl();
    }

    void Update()
    {
        if (blood == null) return;
        float dt = Time.deltaTime;

        // Gains pour in; losses are instant and leave the ghost behind.
        float target = blood.Fraction;
        shown = target > shown ? Mathf.MoveTowards(shown, target, riseSpeed * dt) : target;
        if (ghostHold > 0f) ghostHold -= dt;
        else ghostValue = Mathf.MoveTowards(ghostValue, shown, ghostSpeed * dt);
        ghostValue = Mathf.Max(ghostValue, shown);
        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, dt * 2.5f);

        fill.fillAmount = ToFill(shown);
        ghost.fillAmount = ToFill(ghostValue);
        ghost.color = ghostColor;
        flash.fillAmount = ToFill(shown);
        Color fc = flashColor; fc.a = flashAlpha * 0.85f;
        flash.color = fc;

        // Heartbeat when low: faster as he gets closer to empty.
        float pulse = 0f;
        if (blood.IsActive && blood.IsLow && shown > 0f)
        {
            float beatsPerSecond = Mathf.Lerp(2.2f, 1.1f, blood.Fraction / Mathf.Max(0.01f, blood.lowThreshold));
            beatClock += dt * beatsPerSecond;
            pulse = Heartbeat(beatClock % 1f);
        }
        else beatClock = 0f;

        fill.color = Color.Lerp(fillColor, lowPulseColor, pulse);
        moon.localScale = Vector3.one * (1f + 0.07f * pulse);
        Color e = eyeColor;
        e.a = blood.IsActive ? Mathf.Max(0.75f + 0.25f * Mathf.Sin(Time.time * 1.6f), pulse) : 0.35f;
        eyes.color = e;

        // Glowing meniscus riding the end of the blood (clipped to the membrane by its mask).
        edgeGlow.anchoredPosition = new Vector2(Mathf.Lerp(slotXMin, slotXMax, shown), edgeGlow.anchoredPosition.y);
        Color g = edgeGlowImage.color;
        g.a = shown > 0.01f ? 0.35f + 0.2f * Mathf.PerlinNoise(Time.time * 6f, 0f) + 0.5f * flashAlpha : 0f;
        edgeGlowImage.color = g;

        // Hit shake and red screen edges.
        shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
        rect.anchoredPosition = restPosition + Random.insideUnitCircle * shakeAmount * shake * shake;
        hitRed = Mathf.MoveTowards(hitRed, 0f, dt * 1.8f);
        if (vignette != null)
        {
            Color v = vignetteColor;
            v.a = Mathf.Max(hitRed * 0.65f, pulse * 0.3f);
            vignette.color = v;
        }

        UpdateDrops(dt);
    }

    float ToFill(float fraction) => Mathf.Lerp(slotXMin, slotXMax, fraction) / canvasWidth;

    // Two thumps per beat: lub-dub.
    static float Heartbeat(float t) => Mathf.Max(Bump(t, 0f, 0.12f), 0.65f * Bump(t, 0.22f, 0.12f));
    static float Bump(float t, float at, float width)
    {
        float d = (t - at) / width;
        return d < 0f || d > 1f ? 0f : Mathf.Sin(d * Mathf.PI);
    }

    void OnFed(float amount)
    {
        flashColor = new Color(1f, 0.85f, 0.75f);
        flashAlpha = 1f;
        ghostHold = 0f;
    }

    void OnDamaged(float amount)
    {
        // The bar has not redrawn yet, so `shown` is still the value before the hit.
        ghostValue = Mathf.Max(ghostValue, shown);
        ghostHold = ghostDelay;
        float hard = Mathf.Clamp01(amount / 20f);
        flashColor = Color.white;
        flashAlpha = 0.6f + 0.4f * hard;
        shake = Mathf.Max(shake, 0.6f + 0.4f * hard);
        hitRed = 1f;
        SpillDrops(3 + Mathf.RoundToInt(amount / 4f));
    }

    void BuildDrops()
    {
        drops = new RectTransform[MaxDrops];
        dropImages = new Image[MaxDrops];
        dropVelocity = new Vector2[MaxDrops];
        dropLife = new float[MaxDrops];
        if (dropSprite == null) return;
        for (int i = 0; i < MaxDrops; i++)
        {
            var go = new GameObject("Drop", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(wing, false);
            drops[i] = (RectTransform)go.transform;
            drops[i].anchorMin = drops[i].anchorMax = new Vector2(0f, 1f);
            drops[i].sizeDelta = new Vector2(13f, 18f);
            dropImages[i] = go.GetComponent<Image>();
            dropImages[i].sprite = dropSprite;
            dropImages[i].raycastTarget = false;
            go.SetActive(false);
        }
    }

    void SpillDrops(int count)
    {
        if (dropSprite == null) return;
        float end = Mathf.Lerp(slotXMin, slotXMax, shown);
        for (int i = 0; i < MaxDrops && count > 0; i++)
        {
            if (drops[i].gameObject.activeSelf) continue;
            count--;
            float x = Mathf.Clamp(end + Random.Range(-60f, 20f), slotXMin + 10f, slotXMax - 60f);
            // Roughly the trailing edge of the membrane at x.
            float y = Mathf.Lerp(108f, 84f, Mathf.InverseLerp(slotXMin, slotXMax, x)) - Random.Range(0f, 10f);
            drops[i].anchoredPosition = new Vector2(x, -y);
            drops[i].localScale = Vector3.one * Random.Range(0.6f, 1.1f);
            dropVelocity[i] = new Vector2(Random.Range(-25f, 25f), Random.Range(-10f, 40f));
            dropLife[i] = 0f;
            drops[i].gameObject.SetActive(true);
        }
    }

    void UpdateDrops(float dt)
    {
        if (dropSprite == null) return;
        for (int i = 0; i < MaxDrops; i++)
        {
            if (!drops[i].gameObject.activeSelf) continue;
            dropLife[i] += dt;
            dropVelocity[i].y -= 700f * dt;
            drops[i].anchoredPosition += dropVelocity[i] * dt;
            Color c = dropColor;
            c.a = 1f - Mathf.InverseLerp(0.35f, 0.75f, dropLife[i]);
            dropImages[i].color = c;
            if (dropLife[i] > 0.75f) drops[i].gameObject.SetActive(false);
        }
    }

    void Unfurl()
    {
        shown = 0f;
        ghostValue = 0f;
        AnimateWing(1f, unfurlTime, true, 0f);
    }

    void Fold() => AnimateWing(FoldedScale, foldTime, false, 0.8f);

    void AnimateWing(float to, float time, bool overshoot, float delay)
    {
        if (wingRoutine != null) StopCoroutine(wingRoutine);
        wingRoutine = StartCoroutine(WingRoutine(to, time, overshoot, delay));
    }

    IEnumerator WingRoutine(float to, float time, bool overshoot, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float from = wing.localScale.x;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, time);
            float k = Mathf.Clamp01(t);
            k = overshoot ? EaseOutBack(k) : k * k;
            SetWing(Mathf.LerpUnclamped(from, to, k));
            yield return null;
        }
        SetWing(to);
        wingRoutine = null;
    }

    // The wing spreads sideways and opens vertically as it unfolds; it fades in so the
    // squashed claw never shows as a dot, while the closed wing on the bat fades out.
    void SetWing(float sx)
    {
        wing.localScale = new Vector3(sx, Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(sx)), 1f);
        float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FoldedScale, 0.35f, sx));
        if (wingGroup != null) wingGroup.alpha = open;
        if (foldedWing != null) foldedWing.color = new Color(1f, 1f, 1f, 1f - open);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.3f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
