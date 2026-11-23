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

    [Header("Sky (night values are the scene's own)")]
    [Tooltip("The scene's directional light: the moon at night, then the same light rises as the sun.")]
    public Light moon;
    [Tooltip("Part of the countdown the moon takes to set; the sun rises over the rest.")]
    [Range(0.1f, 0.9f)] public float moonsetPart = 0.45f;
    [Tooltip("Where the sun rises: the direction its light travels (270 = it rises in the east and shines west).")]
    public float sunYaw = 270f;
    [Tooltip("How high the sun is when the clock hits zero (degrees).")]
    public float sunEndElevation = 30f;
    public float dayIntensity = 3.2f;
    public Gradient sunColor = DefaultSunColor();
    [Tooltip("Ambient light at sunrise (the scene's own ambient is the night value).")]
    public Color dawnAmbient = new Color(1.1f, 0.62f, 0.42f);
    public Color dayAmbient = new Color(1.05f, 1f, 0.92f);
    public float dayExposure = 1.25f;
    [Tooltip("Moonlight kept through the dark before dawn (part of the night moon), so the yard never goes black.")]
    [Range(0f, 1f)] public float moonlightKept = 0.4f;
    [Tooltip("Seconds he smokes in the full sun before he falls.")]
    public float burnTime = 1.5f;

    public UnityEvent onSunrise = new UnityEvent();

    public bool Running { get; private set; }
    public bool Started { get; private set; }
    public float Remaining { get; private set; }
    /// <summary>0 at night, 1 at sunrise.</summary>
    public float Progress => duration > 0f ? 1f - Remaining / duration : 0f;

    Color ambientSky, ambientEquator, ambientGround, fogNight, nightColor;
    Quaternion nightRotation;
    float nightIntensity;
    Material nightSky, sky;
    Light moonFill;   // a soft, shadowless moon that stays while the real light sets and rises again as the sun
    bool burning;
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
        fogNight = RenderSettings.fogColor;
        if (moon != null)
        {
            nightRotation = moon.transform.rotation; nightColor = moon.color; nightIntensity = moon.intensity;
            moonFill = new GameObject("MoonFill").AddComponent<Light>();
            moonFill.transform.SetParent(transform, false);
            moonFill.transform.rotation = nightRotation;
            moonFill.type = LightType.Directional;
            moonFill.color = nightColor;
            moonFill.shadows = LightShadows.None;
            moonFill.intensity = 0f;
        }
        nightSky = RenderSettings.skybox;
        if (nightSky != null) RenderSettings.skybox = sky = new Material(nightSky);   // a copy: Level 1 shares the night sky asset
        BuildUI();
        ApplySky();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RenderSettings.ambientSkyColor = ambientSky;   // the scene's lighting is shared: leave it as it was
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.fogColor = fogNight;
        if (nightSky != null) RenderSettings.skybox = nightSky;
        if (sky != null) Destroy(sky);
        LightExposure.sun = null;
        LightExposure.sunBurnsEverywhere = false;
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
        burning = false;
        LightExposure.sunBurnsEverywhere = false;
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
                StartCoroutine(BurnToDeath());
            }
        }
        ApplySky();
        UpdateUI();
    }

    System.Collections.IEnumerator BurnToDeath()
    {
        burning = true;
        LightExposure.sunBurnsEverywhere = true;   // the sun is over the walls: no shade left anywhere
        yield return new WaitForSeconds(burnTime);
        if (burning && vampire != null && !vampire.IsDead) vampire.Die();
    }

    /// <summary>
    /// A real night-to-day turn: the moon sinks and fades (the dark blue hour), then the sun comes up in the east,
    /// red at the horizon, gold, then white and bright. The procedural sky follows the light on its own.
    /// </summary>
    void ApplySky()
    {
        float k = Progress;
        float moonT = Mathf.Clamp01(k / moonsetPart);
        float sunT = Mathf.Clamp01((k - moonsetPart) / (1f - moonsetPart));
        if (moon != null)
        {
            if (k < moonsetPart)
            {
                Vector3 e = nightRotation.eulerAngles;
                moon.transform.rotation = Quaternion.Slerp(nightRotation, Quaternion.Euler(2f, e.y, 0f), moonT);
                moon.color = nightColor;
                moon.intensity = Mathf.Lerp(nightIntensity, 0f, moonT * moonT);   // bright until late, so the yard stays readable
            }
            else
            {
                float elevation = Mathf.Lerp(-6f, sunEndElevation, 1f - (1f - sunT) * (1f - sunT));   // climbs fast, then slows
                moon.transform.rotation = Quaternion.Euler(elevation, sunYaw, 0f);
                moon.color = sunColor.Evaluate(sunT);
                moon.intensity = dayIntensity * Mathf.Clamp01(elevation / 6f) * Mathf.Sqrt(sunT);   // nothing while it is under the horizon
            }
        }
        if (moonFill != null)   // takes over as the moon sets, fades as the day comes
            moonFill.intensity = nightIntensity * moonlightKept * (k < moonsetPart ? moonT * moonT : 1f - sunT);
        LightExposure.sun = sunT > 0f ? moon : null;

        Color ambient = sunT < 0.5f ? Color.Lerp(Color.black, dawnAmbient, sunT * 2f) : Color.Lerp(dawnAmbient, dayAmbient, sunT * 2f - 1f);
        float night = 1f - sunT;   // how much of the night ambient is left
        RenderSettings.ambientSkyColor = ambientSky * night + ambient;
        RenderSettings.ambientEquatorColor = ambientEquator * night + ambient * 0.8f;
        RenderSettings.ambientGroundColor = ambientGround * night + ambient * 0.4f;
        RenderSettings.fogColor = Color.Lerp(fogNight,Color.Lerp(new Color(0.75f, 0.52f, 0.42f), new Color(0.7f, 0.72f, 0.76f), sunT), sunT);

        if (sky != null && nightSky != null)
        {
            sky.SetFloat("_Exposure", Mathf.Lerp(nightSky.GetFloat("_Exposure") * Mathf.Lerp(1f, 0.7f, moonT), dayExposure, Mathf.Pow(sunT, 1.5f)));
            sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(nightSky.GetFloat("_AtmosphereThickness"), 1f, sunT) + 0.6f * Mathf.Sin(sunT * Mathf.PI));   // redder halfway through
            sky.SetColor("_SkyTint", Color.Lerp(nightSky.GetColor("_SkyTint"), new Color(0.5f, 0.5f, 0.5f), sunT));
            sky.SetColor("_GroundColor", Color.Lerp(nightSky.GetColor("_GroundColor"), new Color(0.37f, 0.35f, 0.34f), sunT));
        }
    }

    static Gradient DefaultSunColor()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.35f, 0.15f), 0f), new GradientColorKey(new Color(1f, 0.68f, 0.38f), 0.45f), new GradientColorKey(new Color(1f, 0.94f, 0.84f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
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
