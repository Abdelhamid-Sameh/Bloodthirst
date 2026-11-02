using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The mana bar: the blue vertical bat wing to the left of the head icon (its sheet mirrors the health wing, so the two frame the head).
/// It unfolds and folds together with the blood bar. Not linked to the abilities yet: call SetMana(0..1) to drive it.
/// A drop in mana plays the mana sparkle VFX; a gain pours in.
/// </summary>
public class ManaBarUI : MonoBehaviour
{
    public VampireBlood blood;
    public RectTransform bar;
    public CanvasGroup barGroup;
    public Image track;
    public Image frame;
    public Image vfx;

    [Tooltip("Bar frames from full (first) to empty (last).")]
    public Sprite[] frames;
    public Sprite[] vfxFrames;
    public Color trackColor = new Color(0.02f, 0.02f, 0.08f, 0.85f);
    public float riseSpeed = 0.8f;
    public float vfxFps = 14f;
    public float unfurlTime = 0.7f;
    public float foldTime = 0.4f;

    [Range(0f, 1f)] public float mana = 1f;

    float shown = 1f, vfxTime = -1f;
    Coroutine routine;
    const float FoldedScale = 0.03f;

    /// <summary>Sets the mana level (0 = empty, 1 = full).</summary>
    public void SetMana(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        if (fraction < mana) vfxTime = 0f;
        mana = fraction;
    }

    void Awake()
    {
        if (blood == null) blood = FindFirstObjectByType<VampireBlood>();
        if (track != null && frames != null && frames.Length > 0) { track.sprite = frames[0]; track.color = trackColor; }
        if (vfx != null) vfx.enabled = false;
        SetBar(FoldedScale);
    }

    void OnEnable() { if (blood != null) { blood.Activated += Unfurl; blood.Deactivated += Fold; } }
    void OnDisable() { if (blood != null) { blood.Activated -= Unfurl; blood.Deactivated -= Fold; } }
    void Start() { if (blood != null && blood.IsActive) Unfurl(); }

    void Update()
    {
        shown = mana > shown ? Mathf.MoveTowards(shown, mana, riseSpeed * Time.deltaTime) : mana;
        if (frames != null && frames.Length > 0)
            frame.sprite = frames[Mathf.RoundToInt((1f - shown) * (frames.Length - 1))];
        if (vfx == null || vfxFrames == null || vfxFrames.Length == 0 || vfxTime < 0f) return;
        int i = Mathf.FloorToInt(vfxTime * vfxFps);
        if (i >= vfxFrames.Length) { vfx.enabled = false; vfxTime = -1f; return; }
        vfx.enabled = true;
        vfx.sprite = vfxFrames[i];
        vfxTime += Time.deltaTime;
    }

    void Unfurl() { shown = 0f; Animate(1f, unfurlTime, 0f); }   // pours up from empty, like the blood bar
    void Fold() => Animate(FoldedScale, foldTime, 0.8f);

    void Animate(float to, float time, float delay)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Run(to, time, delay));
    }

    IEnumerator Run(float to, float time, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float from = bar.localScale.y, t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, time);
            float k = Mathf.Clamp01(t);
            k = to > from ? 1f + 2.3f * Mathf.Pow(k - 1f, 3f) + 1.3f * Mathf.Pow(k - 1f, 2f) : k * k;   // overshoot on the way out
            SetBar(Mathf.LerpUnclamped(from, to, k));
            yield return null;
        }
        SetBar(to);
        routine = null;
    }

    void SetBar(float s)
    {
        float sx = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(s));
        bar.localScale = new Vector3(sx, s, 1f);   // the mana sheet is already drawn as the mirror of the health wing
        if (barGroup != null) barGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FoldedScale, 0.35f, s));
    }
}
