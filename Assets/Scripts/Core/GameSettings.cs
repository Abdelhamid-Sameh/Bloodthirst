using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Player settings from the main menu, saved in PlayerPrefs and applied in every scene:
/// brightness (an exposure offset on top of the scene's own grade), master volume and music volume.
/// Brightness works through a top-priority global Volume that only overrides post exposure,
/// so the night grade of each scene stays as designed and the player just shifts it up or down.
/// </summary>
public static class GameSettings
{
    public const float MinBrightness = -1f, MaxBrightness = 1.5f;

    const string BrightnessKey = "settings.brightness";
    const string MasterKey = "settings.masterVolume";
    const string MusicKey = "settings.musicVolume";

    /// <summary>Exposure offset in stops (0 = as designed).</summary>
    public static float Brightness { get; private set; }
    public static float MasterVolume { get; private set; } = 1f;
    public static float MusicVolume { get; private set; } = 1f;

    static Volume brightnessVolume;
    static ColorAdjustments brightnessAdjust;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        Brightness = PlayerPrefs.GetFloat(BrightnessKey, 0f);
        MasterVolume = PlayerPrefs.GetFloat(MasterKey, 1f);
        MusicVolume = PlayerPrefs.GetFloat(MusicKey, 1f);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    public static void SetBrightness(float stops)
    {
        Brightness = Mathf.Clamp(stops, MinBrightness, MaxBrightness);
        PlayerPrefs.SetFloat(BrightnessKey, Brightness);
        ApplyBrightness();
    }

    public static void SetMasterVolume(float v)
    {
        MasterVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(MasterKey, MasterVolume);
        AudioListener.volume = MasterVolume;
    }

    public static void SetMusicVolume(float v)
    {
        MusicVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(MusicKey, MusicVolume);
    }

    public static void ResetToDefaults()
    {
        SetBrightness(0f);
        SetMasterVolume(1f);
        SetMusicVolume(1f);
    }

    public static void Save() => PlayerPrefs.Save();

    public static void Apply()
    {
        AudioListener.volume = MasterVolume;
        ApplyBrightness();
    }

    static void ApplyBrightness()
    {
        // The volume lives in the current scene, so a scene change destroys it and it is made again here.
        if (brightnessVolume == null)
        {
            var go = new GameObject("Settings_BrightnessVolume");
            brightnessVolume = go.AddComponent<Volume>();
            brightnessVolume.isGlobal = true;
            brightnessVolume.priority = 1000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Settings_Brightness";
            brightnessAdjust = profile.Add<ColorAdjustments>();
            brightnessAdjust.postExposure.overrideState = true;
            brightnessVolume.sharedProfile = profile;
        }

        // Base exposure = the scene's own grade (highest-priority other global volume that sets it).
        float baseExposure = 0f, bestPriority = float.MinValue;
        foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (v == brightnessVolume || !v.isGlobal || !v.isActiveAndEnabled || v.sharedProfile == null) continue;
            if (v.sharedProfile.TryGet(out ColorAdjustments ca) && ca.active && ca.postExposure.overrideState && v.priority > bestPriority)
            {
                bestPriority = v.priority;
                baseExposure = ca.postExposure.value;
            }
        }
        brightnessAdjust.postExposure.value = baseExposure + Brightness;
    }
}
