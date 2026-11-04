using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shared look for the three ability buttons (Blood Leap, Shield, Shadow Bite):
/// - the triangle they sit in at the bottom right (slot 0 bottom-left = 1, slot 1 bottom-right = 2, slot 2 top = 3),
/// - the mana cost badge, key labels and the generated sprites (rounded panel, mana drop, mouse buttons),
/// - the aiming prompt shown while a target is being chosen,
/// - the "Not enough mana" toast, raised by VampireMana.NotEnough.
/// </summary>
public static class AbilityHUD
{
    public const float ButtonScale = 0.88f;
    public static readonly Color ManaColor = new Color(0.55f, 0.62f, 1f);
    public static readonly Color DeniedColor = new Color(1f, 0.32f, 0.32f);

    /// <summary>Bottom-right anchored position (pivot bottom-right) of a 124-unit button scaled by ButtonScale.</summary>
    public static Vector2 SlotPosition(int slot)
    {
        const float size = 124f * ButtonScale, gap = 26f, right = -70f, bottom = 64f;
        switch (slot)
        {
            case 0: return new Vector2(right - size - gap, bottom);                       // bottom left
            case 1: return new Vector2(right, bottom);                                    // bottom right
            default: return new Vector2(right - (size + gap) * 0.5f, bottom + size + 14f); // top
        }
    }

    /// <summary>"Digit1" → "1", "Escape" → "Esc".</summary>
    public static string KeyLabel(Key key)
    {
        string s = key.ToString();
        if (s.StartsWith("Digit")) return s.Substring(5);
        if (s.StartsWith("Numpad")) return s.Substring(6);
        if (key == Key.Escape) return "Esc";
        return s;
    }

    // ------------------------------------------------------------------ cost badge

    /// <summary>A small pill with a mana drop and the cost, at the top right of a button. Returns its text.</summary>
    public static TMP_Text AddCostBadge(Transform button, float cost)
    {
        GameObject pill = ThrallAbility.NewUI("ManaCost", button);
        RectTransform rt = pill.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-14f, -10f);
        rt.sizeDelta = new Vector2(62f, 30f);
        Image bg = pill.AddComponent<Image>();
        bg.sprite = Rounded();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.03f, 0.04f, 0.10f, 0.95f);
        bg.raycastTarget = false;
        Image drop = ThrallAbility.AddChildImage(pill.transform, "Drop", Drop(), ManaColor, new Vector2(14f, 19f));
        drop.rectTransform.anchoredPosition = new Vector2(-15f, 0f);
        return ThrallAbility.MakeText(pill.transform, "Cost", Mathf.RoundToInt(cost).ToString(), 19, ManaColor, TextAlignmentOptions.Center,
                                      new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(9f, 0f), new Vector2(34f, 30f), 0f);
    }

    /// <summary>Greys the icon and reddens the cost while the ability cannot be afforded.</summary>
    public static void ShowAffordable(Image icon, TMP_Text cost, bool affordable)
    {
        if (icon != null) icon.color = affordable ? Color.white : new Color(0.45f, 0.45f, 0.5f, 0.85f);
        if (cost != null) cost.color = affordable ? ManaColor : DeniedColor;
    }

    /// <summary>Shake offset for a refused button (zero once the shake is over).</summary>
    public static Vector2 Shake(float until)
    {
        float left = until - Time.unscaledTime;
        if (left <= 0f) return Vector2.zero;
        return new Vector2(Mathf.Sin(Time.unscaledTime * 70f) * 7f * Mathf.Clamp01(left / 0.35f), 0f);
    }

    // ------------------------------------------------------------------ aiming prompt

    /// <summary>
    /// The prompt shown while choosing a target: the ability's name and what to do on the left, then
    /// [left mouse] confirm and [right mouse] / [Esc] / [key] cancel, on a dark rounded panel at the bottom centre.
    /// </summary>
    public static CanvasGroup BuildAimPrompt(Transform canvasRoot, Color accent, string title, string subtitle, string confirm, Key hotkey)
    {
        GameObject panel = ThrallAbility.NewUI(title + "Prompt", canvasRoot);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 205f);
        Image bg = panel.AddComponent<Image>();
        bg.sprite = Rounded();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.035f, 0.025f, 0.05f, 0.9f);
        bg.raycastTarget = false;
        HorizontalLayoutGroup row = panel.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(16, 22, 12, 12);
        row.spacing = 14f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        ContentSizeFitter fit = panel.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        CanvasGroup group = panel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = group.interactable = false;

        Fixed(Block(panel.transform, "Accent", accent), 5f, 48f);

        GameObject titles = ThrallAbility.NewUI("Titles", panel.transform);
        VerticalLayoutGroup col = titles.AddComponent<VerticalLayoutGroup>();
        col.spacing = 0f;
        col.childControlWidth = col.childControlHeight = true;
        col.childForceExpandWidth = col.childForceExpandHeight = false;
        Label(titles.transform, title, 22, Color.Lerp(accent, Color.white, 0.35f), 4f);
        Label(titles.transform, subtitle, 16, new Color(1f, 1f, 1f, 0.6f), 0f).fontStyle = FontStyles.Normal;

        Fixed(Block(panel.transform, "Divider", new Color(1f, 1f, 1f, 0.15f)), 2f, 40f);

        Fixed(Img(panel.transform, "LeftMouse", Mouse(true), Color.white), 24f, 34f);
        Label(panel.transform, confirm, 20, Color.white, 1f);

        Fixed(Block(panel.transform, "Gap", new Color(0f, 0f, 0f, 0f)), 6f, 10f);

        Fixed(Img(panel.transform, "RightMouse", Mouse(false), Color.white), 24f, 34f);
        Label(panel.transform, "/", 20, new Color(1f, 1f, 1f, 0.5f), 0f);
        KeyCap(panel.transform, "Esc");
        Label(panel.transform, "/", 20, new Color(1f, 1f, 1f, 0.5f), 0f);
        KeyCap(panel.transform, KeyLabel(hotkey));
        Label(panel.transform, "Cancel", 20, new Color(1f, 1f, 1f, 0.8f), 1f);
        return group;
    }

    static Image Block(Transform parent, string name, Color color)
    {
        Image img = ThrallAbility.NewUI(name, parent).AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Image Img(Transform parent, string name, Sprite sprite, Color color)
    {
        Image img = Block(parent, name, color);
        img.sprite = sprite;
        img.preserveAspect = true;
        return img;
    }

    public static void Fixed(Graphic g, float w, float h)
    {
        LayoutElement le = g.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = w;
        le.preferredHeight = le.minHeight = h;
    }

    public static TMP_Text Label(Transform parent, string text, float size, Color color, float spacing)
    {
        GameObject go = ThrallAbility.NewUI("Text", parent);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = spacing;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>A light keyboard key with dark text, like the controls card.</summary>
    public static void KeyCap(Transform parent, string key)
    {
        GameObject cap = ThrallAbility.NewUI("Key_" + key, parent);
        Image bg = cap.AddComponent<Image>();
        bg.sprite = Rounded();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.93f, 0.91f, 0.88f, 1f);
        bg.raycastTarget = false;
        HorizontalLayoutGroup h = cap.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(9, 9, 3, 3);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        LayoutElement le = cap.AddComponent<LayoutElement>();
        le.minWidth = 32f;
        le.minHeight = 30f;
        TMP_Text t = Label(cap.transform, key, 17, new Color(0.08f, 0.07f, 0.09f), 0f);
        if (KeyFont() != null) t.font = KeyFont();   // key caps stay in a plain sans so they read like real keys
    }

    static TMP_FontAsset keyFont;
    static TMP_FontAsset KeyFont()
    {
        if (keyFont == null) keyFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return keyFont;
    }

    // ------------------------------------------------------------------ "not enough mana" toast

    static TMP_Text toastTitle, toastDetail;
    static CanvasGroup toastGroup;
    static RectTransform toastRect;
    static float toastStart = -10f;
    static bool hooked;

    /// <summary>Hooks the toast to VampireMana.NotEnough (once per play session).</summary>
    public static void EnsureToast()
    {
        if (hooked) return;
        VampireMana.NotEnough += ShowNotEnough;
        hooked = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { hooked = false; toastGroup = null; toastStart = -10f; rounded = null; drop = null; mouseL = mouseR = null; }

    static void ShowNotEnough(float cost)
    {
        if (toastGroup == null) BuildToast();
        float have = VampireMana.Instance != null ? VampireMana.Instance.Current : 0f;
        toastTitle.text = "NOT ENOUGH MANA";
        toastDetail.text = "Needs " + Mathf.RoundToInt(cost) + "  ·  you have " + Mathf.FloorToInt(have) + "  ·  land hits to recover";
        toastStart = Time.unscaledTime;
        toastGroup.GetComponent<ToastAnimator>().enabled = true;
    }

    static void BuildToast()
    {
        GameObject root = new GameObject("AbilityToastUI");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = ThrallAbility.NewUI("Toast", root.transform);
        toastRect = panel.GetComponent<RectTransform>();
        toastRect.anchorMin = toastRect.anchorMax = new Vector2(1f, 0f);
        toastRect.pivot = new Vector2(1f, 0f);
        toastRect.anchoredPosition = new Vector2(-50f, 330f);
        toastRect.sizeDelta = new Vector2(460f, 74f);
        Image bg = panel.AddComponent<Image>();
        bg.sprite = Rounded();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.06f, 0.02f, 0.04f, 0.92f);
        bg.raycastTarget = false;
        Image bar = ThrallAbility.AddChildImage(panel.transform, "Accent", null, DeniedColor, new Vector2(5f, 50f));
        bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        bar.rectTransform.anchoredPosition = new Vector2(16f, 0f);
        Image icon = ThrallAbility.AddChildImage(panel.transform, "Drop", Drop(), ManaColor, new Vector2(24f, 32f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(44f, 0f);
        toastTitle = ThrallAbility.MakeText(panel.transform, "Title", "", 24, DeniedColor, TextAlignmentOptions.Left,
                                            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, 12f), new Vector2(380f, 30f), 4f);
        toastDetail = ThrallAbility.MakeText(panel.transform, "Detail", "", 16, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Left,
                                             new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, -14f), new Vector2(380f, 24f), 0f);
        toastDetail.fontStyle = FontStyles.Normal;
        toastGroup = panel.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastGroup.blocksRaycasts = toastGroup.interactable = false;
        panel.AddComponent<ToastAnimator>();
    }

    /// <summary>Pops the toast in, holds it, fades it out.</summary>
    sealed class ToastAnimator : MonoBehaviour
    {
        void Update()
        {
            float t = Time.unscaledTime - toastStart;
            const float hold = 1.4f, fade = 0.4f;
            toastGroup.alpha = t < 0f ? 0f : t < hold ? 1f : Mathf.Clamp01(1f - (t - hold) / fade);
            float pop = 1f + 0.12f * Mathf.Clamp01(1f - t / 0.18f);
            toastRect.localScale = new Vector3(pop, pop, 1f);
            toastTitle.color = Color.Lerp(DeniedColor, Color.white, 0.35f * Mathf.Max(0f, Mathf.Sin(t * 18f)) * Mathf.Clamp01(1f - t / 0.6f));
            if (t > hold + fade) enabled = false;
        }
    }

    // ------------------------------------------------------------------ generated sprites

    static Sprite rounded, drop, mouseL, mouseR;

    /// <summary>A white rounded rectangle with a 9-slice border, for panels and key caps.</summary>
    public static Sprite Rounded()
    {
        if (rounded != null) return rounded;
        const int size = 48, radius = 12;
        Texture2D tex = ThrallAbility.NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - d + 0.5f) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return rounded;
    }

    /// <summary>A white drop (round bottom, pointed top).</summary>
    public static Sprite Drop()
    {
        if (drop != null) return drop;
        const int w = 48, h = 64;
        Texture2D tex = ThrallAbility.NewTexture(w, h);
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = 0f;
                for (int s = 0; s < 4; s++)
                {
                    float u = (x + (s % 2 + 0.5f) / 2f) / w, v = (y + (s / 2 + 0.5f) / 2f) / h;
                    if (InDrop(u, v)) a += 0.25f;
                }
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        drop = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return drop;
    }

    /// <summary>Inside a drop shape in 0..1 coordinates (v up): a circle at the bottom tapering to a point at the top.</summary>
    public static bool InDrop(float u, float v)
    {
        const float cx = 0.5f, cy = 0.36f, r = 0.34f;
        float dx = u - cx, dy = v - cy;
        if (dx * dx + dy * dy <= r * r) return true;
        if (v < cy || v > 0.97f) return false;
        float k = (v - cy) / (0.97f - cy);              // 0 at the circle's middle, 1 at the tip
        float half = r * Mathf.Sqrt(1f - k) * (1f - 0.35f * k);
        return Mathf.Abs(dx) <= half;
    }

    /// <summary>A computer mouse outline with its left (or right) button filled.</summary>
    public static Sprite Mouse(bool left)
    {
        if (left && mouseL != null) return mouseL;
        if (!left && mouseR != null) return mouseR;
        const int w = 48, h = 68;
        Texture2D tex = ThrallAbility.NewTexture(w, h);
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;   // -1..1
                float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u) / 0.92f, 2.6f) + Mathf.Pow(Mathf.Abs(v) / 0.95f, 2.6f), 1f / 2.6f);   // rounded box
                float a = 0f;
                if (d <= 1f && d >= 0.84f) a = 1f;                                      // outline
                bool topHalf = v > 0.08f && d < 0.84f;
                if (topHalf && Mathf.Abs(u) < 0.06f) a = 1f;                            // split between the buttons
                if (Mathf.Abs(v - 0.08f) < 0.05f && d < 0.84f) a = 1f;                  // bottom edge of the buttons
                if (topHalf && (left ? u < -0.1f : u > 0.1f) && v > 0.16f) a = 1f;      // the pressed button
                else if (d < 0.84f && a == 0f) a = 0.12f;                               // faint body
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        if (left) mouseL = s; else mouseR = s;
        return s;
    }
}
