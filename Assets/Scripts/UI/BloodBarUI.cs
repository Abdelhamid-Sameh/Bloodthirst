using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-left blood bar using the itch.io bat-wing sheet (100 frames, full to empty) beside the vampire head icon.
/// The wing expands out of the head when the vampire leaves his coffin and folds away when he dies.
/// The blood drains continuously. A hit drops it at once, shakes the bar, plays the blood-splash VFX, spills
/// drops and reddens the screen edges, while a pale "ghost" frame trails behind (the 2009 Wolverine HUD idea).
/// Feeding pours blood back in with a flash. Under the low threshold the head beats like a heart.
/// </summary>
public class BloodBarUI : MonoBehaviour
{
    public VampireBlood blood;

    [Header("Parts")]
    [Tooltip("Pivots at its left edge; scaling it expands the wing out of the head.")]
    public RectTransform bar;
    public CanvasGroup barGroup;
    [Tooltip("Full wing tinted dark, so the empty part reads as an empty vein.")]
    public Image track;
    public Image ghost;
    public Image frame;
    public Image flash;
    public Image hitVfx;
    public RectTransform icon;
    [Tooltip("Full-screen red edges for hits and the low-blood heartbeat.")]
    public Image vignette;
    public Sprite dropSprite;

    [Header("Sprites")]
    [Tooltip("Bar frames from full (first) to empty (last).")]
    public Sprite[] frames;
    public Sprite[] vfxFrames;
    [Tooltip("Canvas units per sheet pixel.")]
    public float pixelScale = 3f;
    [Tooltip("Right edge of the blood in each frame, in sheet pixels (measured by the builder).")]
    public float[] fillEnds;

    [Header("Colours")]
    public Color trackColor = new Color(0.08f, 0.01f, 0.02f, 0.85f);
    public Color ghostColor = new Color(1f, 0.82f, 0.82f, 0.9f);
    public Color lowPulseColor = new Color(1f, 0.35f, 0.25f);
    public Color dropColor = new Color(0.62f, 0.02f, 0.05f);
    public Color vignetteColor = new Color(0.55f, 0f, 0.02f);

    [Header("Motion")]
    public float unfurlTime = 0.7f;
    public float foldTime = 0.4f;
    [Tooltip("Fraction of the bar per second the blood rises when he feeds or rises from a coffin.")]
    public float riseSpeed = 0.8f;
    [Tooltip("Seconds the pale ghost waits after a hit before it drains down.")]
    public float ghostDelay = 0.45f;
    public float ghostSpeed = 0.6f;
    public float shakeAmount = 9f;
    public float vfxFps = 18f;

    const float FoldedScale = 0.03f;
    const int MaxDrops = 16;

    float shown, ghostValue, ghostHold, flashAlpha, beatClock, shake, hitRed, vfxTime = -1f;
    Color flashColor;
    Vector2 restPosition;
    RectTransform rect;
    Coroutine barRoutine;

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
        if (hitVfx != null) hitVfx.enabled = false;
        if (track != null && frames != null && frames.Length > 0) { track.sprite = frames[0]; track.color = trackColor; }
        SetBar(FoldedScale);
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

        frame.sprite = Frame(shown);
        // The sheet's blood is see-through, so the ghost only covers the part the hit just emptied.
        ghost.sprite = Frame(ghostValue);
        Color gc = ghostColor; gc.a *= Mathf.Clamp01((ghostValue - shown) * 50f);
        ghost.color = gc;
        ghost.fillAmount = 1f - End(shown) / Mathf.Max(1f, frames != null && frames.Length > 0 ? frames[0].rect.width : 1f);
        flash.sprite = frame.sprite;

        // Heartbeat when low: faster as he gets closer to empty.
        float pulse = 0f;
        if (blood.IsActive && blood.IsLow && shown > 0f)
        {
            float beatsPerSecond = Mathf.Lerp(2.2f, 1.1f, blood.Fraction / Mathf.Max(0.01f, blood.lowThreshold));
            beatClock += dt * beatsPerSecond;
            pulse = Heartbeat(beatClock % 1f);
        }
        else beatClock = 0f;

        // The flash layer carries both the feed/hit flash and the low-blood throb.
        Color fc = flashAlpha >= pulse * 0.6f ? flashColor : lowPulseColor;
        fc.a = Mathf.Max(flashAlpha * 0.85f, pulse * 0.6f);
        flash.color = fc;
        icon.localScale = Vector3.one * (1f + 0.08f * pulse);

        shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
        rect.anchoredPosition = restPosition + Random.insideUnitCircle * shakeAmount * shake * shake;
        hitRed = Mathf.MoveTowards(hitRed, 0f, dt * 1.8f);
        if (vignette != null)
        {
            Color v = vignetteColor;
            v.a = Mathf.Max(hitRed * 0.65f, pulse * 0.3f);
            vignette.color = v;
        }

        UpdateVfx(dt);
        UpdateDrops(dt);
    }

    Sprite Frame(float fraction)
    {
        if (frames == null || frames.Length == 0) return null;
        int i = Mathf.RoundToInt((1f - Mathf.Clamp01(fraction)) * (frames.Length - 1));
        return frames[i];
    }

    float End(float fraction)
    {
        if (fillEnds == null || fillEnds.Length == 0) return Mathf.Lerp(25f, 140f, fraction);
        return fillEnds[Mathf.RoundToInt((1f - Mathf.Clamp01(fraction)) * (fillEnds.Length - 1))];
    }

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
        vfxTime = 0f;
        SpillDrops(3 + Mathf.RoundToInt(amount / 4f));
    }

    void UpdateVfx(float dt)
    {
        if (hitVfx == null || vfxFrames == null || vfxFrames.Length == 0 || vfxTime < 0f) return;
        int i = Mathf.FloorToInt(vfxTime * vfxFps);
        if (i >= vfxFrames.Length) { hitVfx.enabled = false; vfxTime = -1f; return; }
        hitVfx.enabled = true;
        hitVfx.sprite = vfxFrames[i];
        vfxTime += dt;
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
            go.transform.SetParent(bar, false);
            drops[i] = (RectTransform)go.transform;
            drops[i].anchorMin = drops[i].anchorMax = new Vector2(0f, 0.5f);
            drops[i].sizeDelta = new Vector2(4f, 5.5f) * pixelScale;
            dropImages[i] = go.GetComponent<Image>();
            dropImages[i].sprite = dropSprite;
            dropImages[i].raycastTarget = false;
            go.SetActive(false);
        }
    }

    void SpillDrops(int count)
    {
        if (dropSprite == null) return;
        float end = End(shown);
        for (int i = 0; i < MaxDrops && count > 0; i++)
        {
            if (drops[i].gameObject.activeSelf) continue;
            count--;
            float x = Mathf.Clamp(end + Random.Range(-20f, 5f), 20f, 135f);
            drops[i].anchoredPosition = new Vector2(x, Random.Range(-12f, -6f)) * pixelScale;
            drops[i].localScale = Vector3.one * Random.Range(0.6f, 1.1f);
            dropVelocity[i] = new Vector2(Random.Range(-8f, 8f), Random.Range(-3f, 12f)) * pixelScale;
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
            dropVelocity[i].y -= 230f * pixelScale * dt;
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
        AnimateBar(1f, unfurlTime, true, 0f);
    }

    void Fold() => AnimateBar(FoldedScale, foldTime, false, 0.8f);

    void AnimateBar(float to, float time, bool overshoot, float delay)
    {
        if (barRoutine != null) StopCoroutine(barRoutine);
        barRoutine = StartCoroutine(BarRoutine(to, time, overshoot, delay));
    }

    IEnumerator BarRoutine(float to, float time, bool overshoot, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float from = bar.localScale.x;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, time);
            float k = Mathf.Clamp01(t);
            k = overshoot ? EaseOutBack(k) : k * k;
            SetBar(Mathf.LerpUnclamped(from, to, k));
            yield return null;
        }
        SetBar(to);
        barRoutine = null;
    }

    // The wing spreads out of the head and fades in, so the squashed claw never shows as a dot.
    void SetBar(float sx)
    {
        bar.localScale = new Vector3(sx, Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(sx)), 1f);
        if (barGroup != null) barGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FoldedScale, 0.35f, sx));
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.3f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
