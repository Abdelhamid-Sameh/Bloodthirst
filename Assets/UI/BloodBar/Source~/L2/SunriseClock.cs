using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The race against the dawn in the courtyard. Begin() starts it (the courtyard checkpoint does), Stop() ends it (the boss is beaten).
/// While it runs, a countdown sits at the top of the screen and the moonlight slowly turns into a low orange sun.
/// At zero the sun is up and the vampire dies; he gets up at his last checkpoint with the time it showed then
/// (never less than respawnMinimum, so a late checkpoint cannot become a trap).
/// </summary>
public class SunriseClock : MonoBehaviour
{
    public static SunriseClock Instance { get; private set; }

    [Tooltip("Seconds from the start of the courtyard fight to sunrise.")]
    public float duration = 300f;
    [Tooltip("After dying he always gets at least this many seconds back.")]
    public float respawnMinimum = 60f;
    public VampireController vampire;

    [Header("Sky")]
    public Light moon;
    public Color nightColor = new Color(0.55f, 0.66f, 1f);
    public float nightIntensity = 0.45f;
    public Vector3 nightAngles = new Vector3(55f, -35f, 0f);
    public Color dawnColor = new Color(1f, 0.58f, 0.3f);
    public float dawnIntensity = 2.4f;
    public Vector3 dawnAngles = new Vector3(8f, 100f, 0f);
    [Tooltip("Ambient light at sunrise (the scene's own ambient is the night value).")]
    public Color dawnAmbient = new Color(1.1f, 0.62f, 0.42f);

    public UnityEvent onSunrise = new UnityEvent();

    public bool Running { get; private set; }
    public bool Started { get; private set; }
    public float Remaining { get; private set; }
    /// <summary>0 at night, 1 at sunrise.</summary>
    public float Progress => duration > 0f ? 1f - Remaining / duration : 0f;

    Color ambientSky, ambientEquator, ambientGround;
    CanvasGroup ui;
    TMP_Text timeText;
    RectTransform barFill;
    Image barImage;

    void Awake()
    {
        Instance = this;
        Remaining = duration;
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        ambientSky = RenderSettings.ambientSkyColor;
        ambientEquator = RenderSettings.ambientEquatorColor;
        ambientGround = RenderSettings.ambientGroundColor;
        BuildUI();
        ApplySky();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RenderSettings.ambientSkyColor = ambientSky;   // the scene's lighting is shared: leave it as it was
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
    }

    public void Begin()
    {
        if (Started) return;
        Started = Running = true;
        Remaining = duration;
        LevelToast.Show("Sunrise is coming", "Win before the sun clears the walls");
    }

    /// <summary>He won: the clock stops where it is.</summary>
    public void Stop() => Running = false;

    public void RestoreForRespawn(float seconds)
    {
        Remaining = Mathf.Clamp(Mathf.Max(seconds, respawnMinimum), 0f, duration);
        Started = Running = true;
    }

    void Update()
    {
        if (Running && !GameState.Paused && vampire != null && !vampire.IsDead)
        {
            Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
            if (Remaining <= 0f)
            {
                Running = false;
                onSunrise.Invoke();
                LevelToast.Show("Sunrise", "The sun found you");
                vampire.Die();
            }
        }
        ApplySky();
        UpdateUI();
    }

    void ApplySky()
    {
        float t = Progress * Progress;   // the dark lasts, then dawn comes fast
        if (moon != null)
        {
            moon.color = Color.Lerp(nightColor, dawnColor, t);
            moon.intensity = Mathf.Lerp(nightIntensity, dawnIntensity, t);
            moon.transform.rotation = Quaternion.Slerp(Quaternion.Euler(nightAngles), Quaternion.Euler(dawnAngles), t);
        }
        RenderSettings.ambientSkyColor = Color.Lerp(ambientSky, dawnAmbient, t);
        RenderSettings.ambientEquatorColor = Color.Lerp(ambientEquator, dawnAmbient * 0.8f, t);
        RenderSettings.ambientGroundColor = Color.Lerp(ambientGround, dawnAmbient * 0.4f, t);
    }

    // ------------------------------------------------------------------ countdown

    void UpdateUI()
    {
        bool show = Started && (Running || (vampire != null && vampire.IsDead));
        ui.alpha = Mathf.MoveTowards(ui.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 2f);
        if (ui.alpha <= 0f) return;
        int s = Mathf.CeilToInt(Remaining);
        timeText.text = (s / 60) + ":" + (s % 60).ToString("00");
        bool late = Remaining < 60f;
        timeText.color = late ? Color.Lerp(new Color(1f, 0.55f, 0.25f), new Color(1f, 0.2f, 0.15f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f))
                              : new Color(0.93f, 0.87f, 0.75f);
        barFill.anchorMax = new Vector2(Progress, 1f);
        barImage.color = Color.Lerp(new Color(0.35f, 0.4f, 0.7f), new Color(1f, 0.55f, 0.2f), Progress);
    }

    void BuildUI()
    {
        var root = new GameObject("SunriseUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        ui = root.AddComponent<CanvasGroup>();
        ui.alpha = 0f;
        ui.blocksRaycasts = ui.interactable = false;

        var panel = NewRect("Panel", root.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -24f);
        panel.sizeDelta = new Vector2(360f, 96f);

        var label = NewText("Label", panel, "UNTIL SUNRISE", 20f);
        label.rectTransform.anchorMin = new Vector2(0f, 0.72f); label.rectTransform.anchorMax = Vector2.one;
        label.color = new Color(0.93f, 0.87f, 0.75f, 0.75f);
        label.characterSpacing = 8f;
        timeText = NewText("Time", panel, "5:00", 46f);
        timeText.rectTransform.anchorMin = new Vector2(0f, 0.2f); timeText.rectTransform.anchorMax = new Vector2(1f, 0.78f);

        var bar = NewRect("Horizon", panel);
        bar.anchorMin = new Vector2(0.1f, 0.04f); bar.anchorMax = new Vector2(0.9f, 0.1f);
        bar.offsetMin = bar.offsetMax = Vector2.zero;
        bar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        barFill = NewRect("Fill", bar);
        barFill.anchorMin = Vector2.zero; barFill.anchorMax = new Vector2(0f, 1f);
        barFill.offsetMin = barFill.offsetMax = Vector2.zero;
        barImage = barFill.gameObject.AddComponent<Image>();
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, string text, float size)
    {
        var rt = NewRect(name, parent);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}
