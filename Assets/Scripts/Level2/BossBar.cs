using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The boss health bar, Souls style: a long thin bar along the bottom of the screen with the boss's name above it.
/// Lost health first shows as a pale trail that catches up a moment later.
/// Call BossBar.Show(name, maxHealth) when the fight starts, BossBar.SetHealth(current) whenever he takes damage,
/// BossBar.Hide() when he falls. Builds itself on first use.
/// </summary>
public class BossBar : MonoBehaviour
{
    static BossBar instance;

    public float trailDelay = 0.6f;
    public float trailSpeed = 0.5f;   // part of the bar per second

    CanvasGroup group;
    TMP_Text nameText;
    RectTransform fill, trail;
    float max = 1f, shown = 1f, trailValue = 1f, lastHit;
    bool visible;

    public static void Show(string bossName, float maxHealth)
    {
        Ensure();
        instance.max = Mathf.Max(1f, maxHealth);
        instance.shown = instance.trailValue = 1f;
        instance.nameText.text = bossName;
        instance.visible = true;
        instance.Apply();
    }

    public static void SetHealth(float current)
    {
        if (instance == null) return;
        float f = Mathf.Clamp01(current / instance.max);
        if (f < instance.shown) instance.lastHit = Time.time;
        instance.shown = f;
        if (instance.trailValue < f) instance.trailValue = f;   // healed: no trail
        instance.Apply();
    }

    public static void Hide()
    {
        if (instance != null) instance.visible = false;
    }

    public static bool Visible => instance != null && instance.visible;

    static void Ensure()
    {
        if (instance != null) return;
        instance = new GameObject("BossBar").AddComponent<BossBar>();
        instance.Build();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * (visible ? 1.5f : 0.8f));
        if (Time.time - lastHit > trailDelay && trailValue > shown)
        {
            trailValue = Mathf.MoveTowards(trailValue, shown, trailSpeed * Time.deltaTime);
            Apply();
        }
    }

    void Apply()
    {
        fill.anchorMax = new Vector2(shown, 1f);
        trail.anchorMax = new Vector2(trailValue, 1f);
    }

    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = group.interactable = false;

        var panel = Rect("Panel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 110f);
        panel.sizeDelta = new Vector2(1000f, 60f);

        nameText = Rect("Name", panel).gameObject.AddComponent<TextMeshProUGUI>();
        var nr = nameText.rectTransform;
        nr.anchorMin = new Vector2(0f, 0.45f); nr.anchorMax = Vector2.one;
        nr.offsetMin = new Vector2(4f, 0f); nr.offsetMax = Vector2.zero;
        nameText.fontSize = 26f;
        nameText.alignment = TextAlignmentOptions.BottomLeft;
        nameText.color = new Color(0.93f, 0.89f, 0.8f);
        nameText.raycastTarget = false;

        var frame = Rect("Frame", panel);   // thin dark edge around the bar
        frame.anchorMin = new Vector2(0f, 0f); frame.anchorMax = new Vector2(1f, 0.32f);
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        Image(frame, new Color(0.05f, 0.04f, 0.04f, 0.9f));
        var back = Rect("Back", frame);
        back.anchorMin = Vector2.zero; back.anchorMax = Vector2.one;
        back.offsetMin = new Vector2(2f, 2f); back.offsetMax = new Vector2(-2f, -2f);
        Image(back, new Color(0.12f, 0.1f, 0.1f, 0.85f));
        trail = Rect("Trail", back);
        trail.anchorMin = Vector2.zero; trail.anchorMax = Vector2.one;
        trail.offsetMin = trail.offsetMax = Vector2.zero;
        Image(trail, new Color(0.85f, 0.72f, 0.42f));
        fill = Rect("Fill", back);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        Image(fill, new Color(0.55f, 0.06f, 0.05f));
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Image(RectTransform rt, Color c)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
    }
}
