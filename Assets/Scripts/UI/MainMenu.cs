using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The title screen (MainMenu scene). A coffin waits in the rain under a buzzing street lamp while the camera breathes
/// slowly; the menu sits in the dark on the left: Rise (play), Rules of the Night, Controls, Settings (brightness with a
/// calibration strip, master and music volume) and Leave. Mouse, keyboard (arrows / WASD, Enter, Esc) and gamepad all work.
/// Rise opens the lid, pushes the camera in, fades to black and loads the level, where the intro cutscene starts.
/// The UI is built in code at start, like the in-game ability HUD.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Flow")]
    public string levelScene = "Camera_and_level1_completion";

    [Header("Fonts")]
    public TMP_FontAsset titleFont;
    public TMP_FontAsset headingFont;
    public TMP_FontAsset bodyFont;

    [Header("Scene")]
    public Camera menuCamera;
    public Transform coffinLid;
    public Light coffinGlow;
    [Tooltip("Where the lid slides to (local offset) and how it turns when the player rises.")]
    public Vector3 lidOpenOffset = new Vector3(1.0f, 0.05f, 0.15f);
    public Vector3 lidOpenEuler = new Vector3(0f, 28f, -18f);
    [Tooltip("The camera moves toward this point when the player rises.")]
    public Transform pushTarget;

    [Header("Audio")]
    public AudioClip music;
    [Range(0f, 1f)] public float musicVolume = 0.4f;
    public AudioClip ambience;
    [Range(0f, 1f)] public float ambienceVolume = 0.5f;
    public AudioClip heartbeat;
    [Range(0f, 1f)] public float heartbeatVolume = 0.25f;
    public AudioClip hoverSound;
    public AudioClip selectSound;
    public AudioClip riseSound;
    public AudioClip lidSound;

    static readonly Color Blood = new Color(0.70f, 0.04f, 0.06f);
    static readonly Color BloodBright = new Color(1f, 0.2f, 0.18f);
    static readonly Color Bone = new Color(0.91f, 0.87f, 0.81f);
    static readonly Color Ash = new Color(0.78f, 0.74f, 0.71f, 0.8f);
    static readonly Color Faint = new Color(0.78f, 0.74f, 0.71f, 0.45f);
    static readonly Vector2 PagePosition = new Vector2(-96f, 10f);

    class Page
    {
        public CanvasGroup group;
        public RectTransform rect;
        public Selectable first;
    }

    RectTransform canvasRoot;
    CanvasGroup fader, mainGroup, titleGroup, footer;
    TMP_Text title;
    readonly List<Button> mainButtons = new List<Button>();
    readonly Dictionary<string, Page> pages = new Dictionary<string, Page>();
    Page openPage;
    GameObject lastMainSelection;
    bool starting, introDone;
    AudioSource musicSource, ambienceSource, heartSource, sfx;
    readonly AudioSource[] uiVoices = new AudioSource[4];
    int nextVoice;
    readonly Dictionary<AudioClip, float> onsets = new Dictionary<AudioClip, float>();
    Vector3 camPos; Quaternion camRot;
    float push;   // 0..1 camera push toward the coffin while rising
    Image[] calibration;
    TMP_Text brightnessValue, masterValue, musicValue;
    Slider brightnessSlider, masterSlider, musicSlider;
    float titleFlickerAt;
    bool backPressSounded;   // the Back button already played its sound on press

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        GameState.Paused = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        if (menuCamera == null) menuCamera = Camera.main;
        camPos = menuCamera.transform.position;
        camRot = menuCamera.transform.rotation;

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        musicSource = MakeSource(music, true);
        ambienceSource = MakeSource(ambience, true);
        heartSource = MakeSource(heartbeat, true);
        sfx = MakeSource(null, false);
        for (int i = 0; i < uiVoices.Length; i++) uiVoices[i] = MakeSource(null, false);
        foreach (var c in new[] { hoverSound, selectSound, riseSound, lidSound }) Onset(c);   // scan once up front

        BuildAtmosphere();
        BuildUI();
    }

    IEnumerator Start()
    {
        if (music != null) musicSource.Play();
        if (ambience != null) ambienceSource.Play();
        if (heartbeat != null) heartSource.Play();

        // From black: the scene first, then the title flickers on like a dying sign, then the menu.
        fader.alpha = 1f;
        mainGroup.alpha = 0f;
        titleGroup.alpha = 0f;
        footer.alpha = 0f;
        StartCoroutine(FadeGroup(fader, 0f, 2.6f));
        yield return new WaitForSecondsRealtime(1.1f);
        yield return FlickerOn(titleGroup);
        yield return new WaitForSecondsRealtime(0.3f);
        StartCoroutine(FadeGroup(mainGroup, 1f, 0.9f));
        StartCoroutine(FadeGroup(footer, 1f, 1.5f));
        for (int i = 0; i < mainButtons.Count; i++) StartCoroutine(SlideIn(mainButtons[i].transform as RectTransform, i * 0.09f));
        introDone = true;
        Select(mainButtons[0].gameObject, false);
        titleFlickerAt = Time.unscaledTime + Random.Range(5f, 10f);
    }

    void Update()
    {
        float t = Time.unscaledTime;
        float fade = Mathf.Clamp01(Time.timeSinceLevelLoad / 4f) * (starting ? Mathf.Clamp01(1f - push * 1.4f) : 1f);
        musicSource.volume = musicVolume * GameSettings.MusicVolume * fade;
        ambienceSource.volume = ambienceVolume * fade;
        heartSource.volume = heartbeatVolume * fade * (openPage != null ? 0.6f : 1f);

        UpdateCamera(t);
        if (t >= titleFlickerAt && introDone && !starting) { StartCoroutine(TitleStutter()); titleFlickerAt = t + Random.Range(6f, 14f); }
        if (calibration != null) UpdateCalibration();

        if (!introDone || starting) return;
        bool back = (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.backspaceKey.wasPressedThisFrame))
                    || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
        if (back && openPage != null) ClosePage();

        // Clicking empty space drops the selection; any navigation key brings it back.
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && NavPressed())
            Select(openPage != null ? openPage.first.gameObject : (lastMainSelection != null ? lastMainSelection : mainButtons[0].gameObject), false);
    }

    static bool NavPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame
                           || kb.enterKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) return true;
        var gp = Gamepad.current;
        return gp != null && (gp.dpad.up.wasPressedThisFrame || gp.dpad.down.wasPressedThisFrame || gp.leftStick.ReadValue().sqrMagnitude > 0.25f);
    }

    void UpdateCamera(float t)
    {
        Vector2 mouse = Vector2.zero;
        if (Mouse.current != null)
        {
            Vector2 m = Mouse.current.position.ReadValue();
            mouse = new Vector2(Mathf.Clamp01(m.x / Mathf.Max(1, Screen.width)) - 0.5f, Mathf.Clamp01(m.y / Mathf.Max(1, Screen.height)) - 0.5f);
        }
        // Slow breathing drift plus a little parallax from the mouse.
        Vector3 drift = camRot * new Vector3(Mathf.Sin(t * 0.11f) * 0.25f + mouse.x * 0.35f, Mathf.Sin(t * 0.17f) * 0.07f + mouse.y * 0.15f, 0f);
        Quaternion look = camRot * Quaternion.Euler(-mouse.y * 1.4f + Mathf.Sin(t * 0.09f) * 0.4f, mouse.x * 2.2f, Mathf.Sin(t * 0.07f) * 0.5f);
        Vector3 pos = camPos + drift;
        if (pushTarget != null && push > 0f)
        {
            float k = Mathf.SmoothStep(0f, 1f, push);
            pos = Vector3.Lerp(pos, pushTarget.position, k * 0.62f);
            look = Quaternion.Slerp(look, Quaternion.LookRotation(pushTarget.position - pos), k);
        }
        Transform ct = menuCamera.transform;
        float follow = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f);
        ct.position = Vector3.Lerp(ct.position, pos, follow);
        ct.rotation = Quaternion.Slerp(ct.rotation, look, follow);
    }

    // ------------------------------------------------------------------ actions

    void Rise()
    {
        if (starting) return;
        starting = true;
        GameSettings.Save();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        mainGroup.interactable = false;
        StartCoroutine(RiseRoutine());
    }

    IEnumerator RiseRoutine()
    {
        if (riseSound != null) PlayNow(riseSound, 0.9f);
        StartCoroutine(FadeGroup(mainGroup, 0f, 0.6f));
        StartCoroutine(FadeGroup(titleGroup, 0f, 1.4f));
        StartCoroutine(FadeGroup(footer, 0f, 0.6f));
        yield return new WaitForSecondsRealtime(0.4f);
        if (lidSound != null) PlayNow(lidSound, 1f);

        Vector3 lidFrom = coffinLid != null ? coffinLid.localPosition : Vector3.zero;
        Quaternion lidRotFrom = coffinLid != null ? coffinLid.localRotation : Quaternion.identity;
        float glowFrom = coffinGlow != null ? coffinGlow.intensity : 0f;
        AsyncOperation load = SceneManager.LoadSceneAsync(levelScene);
        if (load != null) load.allowSceneActivation = false;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / 2.4f;
            push = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            if (coffinLid != null)
            {
                float lk = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.6f));
                coffinLid.localPosition = Vector3.Lerp(lidFrom, lidFrom + lidOpenOffset, lk);
                coffinLid.localRotation = Quaternion.Slerp(lidRotFrom, lidRotFrom * Quaternion.Euler(lidOpenEuler), lk);
            }
            if (coffinGlow != null) coffinGlow.intensity = glowFrom * (1f + 3f * push);
            if (t > 0.55f) fader.alpha = Mathf.Clamp01((t - 0.55f) / 0.45f);
            yield return null;
        }
        fader.alpha = 1f;
        if (load != null)
        {
            while (load.progress < 0.9f) yield return null;
            load.allowSceneActivation = true;
        }
        else SceneManager.LoadScene(levelScene);
    }

    void Leave()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OpenPage(string key)
    {
        Page p;
        if (!pages.TryGetValue(key, out p) || openPage == p) return;
        if (openPage != null) openPage.group.gameObject.SetActive(false);
        lastMainSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        openPage = p;
        p.group.gameObject.SetActive(true);
        mainGroup.interactable = false;
        StartCoroutine(FadeGroup(mainGroup, 0.35f, 0.25f));
        StartCoroutine(PageIn(p));
        Select(p.first.gameObject, false);
    }

    void ClosePage()
    {
        if (openPage == null) return;
        if (!backPressSounded) PlaySfx(selectSound, 0.35f);   // Back already sounded on press; Esc sounds here
        backPressSounded = false;
        GameSettings.Save();
        openPage.group.gameObject.SetActive(false);
        openPage = null;
        mainGroup.interactable = true;
        StartCoroutine(FadeGroup(mainGroup, 1f, 0.25f));
        Select(lastMainSelection != null ? lastMainSelection : mainButtons[0].gameObject, false);
    }

    void Select(GameObject go, bool sound)
    {
        if (EventSystem.current == null || go == null) return;
        MenuItemFx.Silent = !sound;
        EventSystem.current.SetSelectedGameObject(go);
        MenuItemFx.Silent = false;
    }

    internal void PlaySfx(AudioClip clip, float volume)
    {
        if (clip != null && !starting) PlayNow(clip, volume);
    }

    internal void PlayHover() => PlaySfx(hoverSound, 0.22f);
    internal void PlaySelect() => PlaySfx(selectSound, 0.5f);
    internal void MarkBackSounded() => backPressSounded = true;

    /// <summary>Plays a clip from its first audible sample: the swing and thud clips open with 0.1-0.25 s of silence,
    /// which made the menu feel like it answered late.</summary>
    void PlayNow(AudioClip clip, float volume)
    {
        var v = uiVoices[nextVoice];
        nextVoice = (nextVoice + 1) % uiVoices.Length;
        v.Stop();
        v.clip = clip;
        v.volume = volume;
        v.time = Onset(clip);
        v.Play();
    }

    float Onset(AudioClip clip)
    {
        if (clip == null) return 0f;
        float t;
        if (onsets.TryGetValue(clip, out t)) return t;
        t = 0f;
        if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.channels > 0)
        {
            var data = new float[clip.samples * clip.channels];
            if (clip.GetData(data, 0))
            {
                float peak = 0f;
                foreach (float x in data) peak = Mathf.Max(peak, Mathf.Abs(x));
                for (int i = 0; i < data.Length; i++)
                    if (Mathf.Abs(data[i]) > peak * 0.15f) { t = Mathf.Max(0f, i / clip.channels / (float)clip.frequency - 0.01f); break; }
            }
        }
        onsets[clip] = t;
        return t;
    }

    // ------------------------------------------------------------------ UI

    void BuildUI()
    {
        var canvasGo = new GameObject("MenuCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRoot = (RectTransform)canvasGo.transform;

        // Darkness pooling on the left and bottom so the menu reads over the scene.
        Stretch(NewImage(canvasRoot, "LeftShade", GradientSprite(false), Color.black), 0f, 0f, 0.66f, 1f);
        Stretch(NewImage(canvasRoot, "BottomShade", GradientSprite(true), new Color(0f, 0f, 0f, 0.65f)), 0f, 0f, 1f, 0.22f);

        // Title with dripping blood.
        titleGroup = NewGroup(canvasRoot, "Title");
        var tr = (RectTransform)titleGroup.transform;
        Place(tr, new Vector2(0f, 1f), new Vector2(132f, -96f), new Vector2(1000f, 260f), new Vector2(0f, 1f));
        title = Text(tr, "Bloodthirst", titleFont, 196f, Blood, TextAlignmentOptions.TopLeft, 2f);
        Stretch(title.rectTransform, 0f, 0f, 1f, 1f);
        var mat = new Material(title.fontSharedMaterial);
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor("_UnderlayColor", new Color(0.55f, 0f, 0f, 0.55f));
        mat.SetFloat("_UnderlaySoftness", 0.75f);
        mat.SetFloat("_UnderlayDilate", 0.35f);
        title.fontMaterial = mat;
        var tagline = Text(tr, "THE CITY SLEEPS.   YOU NEVER WILL.", headingFont, 24f, Ash, TextAlignmentOptions.TopLeft, 9f);
        Place(tagline.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -222f), new Vector2(900f, 40f), new Vector2(0f, 1f));
        StartCoroutine(Drips());

        // Main column.
        mainGroup = NewGroup(canvasRoot, "MainColumn");
        var col = (RectTransform)mainGroup.transform;
        Place(col, new Vector2(0f, 0.5f), new Vector2(112f, -60f), new Vector2(660f, 440f), new Vector2(0f, 0.5f));
        string[] labels = { "RISE", "RULES OF THE NIGHT", "CONTROLS", "SETTINGS", "LEAVE" };
        System.Action[] actions = { Rise, () => OpenPage("rules"), () => OpenPage("controls"), () => OpenPage("settings"), Leave };
        for (int i = 0; i < labels.Length; i++)
        {
            Button b = MenuButton(col, labels[i], i == 0 ? 46f : 34f, actions[i]);
            var brt = (RectTransform)b.transform;
            Place(brt, new Vector2(0f, 1f), new Vector2(0f, -i * 76f - (i > 0 ? 18f : 0f)), brt.sizeDelta, new Vector2(0f, 1f));
            mainButtons.Add(b);
        }
        LinkVertical(mainButtons.ToArray());

        footer = NewGroup(canvasRoot, "Footer");
        Stretch((RectTransform)footer.transform, 0f, 0f, 1f, 1f);
        footer.blocksRaycasts = false;
        var hint = Text(footer.transform, "Arrows / W S  choose      Enter  confirm      Esc  back", bodyFont, 24f, Faint, TextAlignmentOptions.BottomLeft, 1f);
        Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(140f, 44f), new Vector2(900f, 40f), new Vector2(0f, 0f));
        var ver = Text(footer.transform, "Milestone build", bodyFont, 22f, Faint, TextAlignmentOptions.BottomRight, 1f);
        Place(ver.rectTransform, new Vector2(1f, 0f), new Vector2(-60f, 44f), new Vector2(400f, 40f), new Vector2(1f, 0f));

        BuildRulesPage();
        BuildControlsPage();
        BuildSettingsPage();

        fader = NewGroup(canvasRoot, "Fader");
        Stretch((RectTransform)fader.transform, 0f, 0f, 1f, 1f);
        Stretch(NewImage(fader.transform, "Black", null, Color.black), 0f, 0f, 1f, 1f);
        fader.blocksRaycasts = false;
        fader.interactable = false;
    }

    Button MenuButton(Transform parent, string label, float size, System.Action onClick)
    {
        var go = NewUI("Btn_" + label, parent);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(640f, size + 26f);
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var b = go.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => onClick());   // the select sound plays on press (MenuItemFx), not on release

        var bar = NewImage(go.transform, "Blade", AbilityHUD.Rounded(), Blood);
        bar.type = Image.Type.Sliced;
        Place(bar.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 3f), new Vector2(0f, 0.5f));
        var drop = NewImage(go.transform, "Drop", AbilityHUD.Drop(), new Color(1f, 0.2f, 0.18f, 0f));
        drop.preserveAspect = true;
        Place(drop.rectTransform, new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(16f, 22f), new Vector2(0.5f, 0.5f));
        var text = Text(go.transform, label, headingFont, size, Bone, TextAlignmentOptions.MidlineLeft, 10f);
        Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(620f, size + 20f), new Vector2(0f, 0.5f));

        var fx = go.AddComponent<MenuItemFx>();
        fx.menu = this;
        fx.text = text;
        fx.blade = bar.rectTransform;
        fx.drop = drop;
        return b;
    }

    // ---- pages

    Page NewPage(string key, string heading, float height)
    {
        var group = NewGroup(canvasRoot, "Page_" + key);
        var rt = (RectTransform)group.transform;
        Place(rt, new Vector2(1f, 0.5f), PagePosition, new Vector2(900f, height), new Vector2(1f, 0.5f));
        var border = NewImage(rt, "Border", AbilityHUD.Rounded(), new Color(0.42f, 0.03f, 0.05f, 0.85f));
        border.type = Image.Type.Sliced;
        Stretch(border.rectTransform, 0f, 0f, 1f, 1f);
        border.rectTransform.offsetMin = new Vector2(-2f, -2f);
        border.rectTransform.offsetMax = new Vector2(2f, 2f);
        var bg = NewImage(rt, "Card", AbilityHUD.Rounded(), new Color(0.025f, 0.012f, 0.016f, 0.94f));
        bg.type = Image.Type.Sliced;
        Stretch(bg.rectTransform, 0f, 0f, 1f, 1f);
        var h = Text(rt, heading, headingFont, 40f, BloodBright, TextAlignmentOptions.Top, 14f);
        Place(h.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(820f, 56f), new Vector2(0.5f, 1f));
        Ornament(rt, -106f);

        var back = MenuButton(rt, "BACK", 26f, ClosePage);
        Place((RectTransform)back.transform, new Vector2(1f, 0f), new Vector2(-22f, 18f), new Vector2(180f, 52f), new Vector2(1f, 0f));
        var page = new Page { group = group, rect = rt, first = back };
        pages[key] = page;
        group.gameObject.SetActive(false);
        return page;
    }

    void Ornament(Transform parent, float y)
    {
        var line = NewImage(parent, "Rule", null, new Color(0.55f, 0.05f, 0.07f, 0.7f));
        Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(560f, 1.5f), new Vector2(0.5f, 0.5f));
        var d = NewImage(parent, "RuleDrop", AbilityHUD.Drop(), BloodBright);
        d.preserveAspect = true;
        Place(d.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - 2f), new Vector2(14f, 20f), new Vector2(0.5f, 0.5f));
    }

    void BuildRulesPage()
    {
        var p = NewPage("rules", "RULES OF THE NIGHT", 880f);
        string[,] rules =
        {
            { "I", "BLOOD IS LIFE", "Your blood drains every second, faster when you sprint. Bite humans to drink. An empty vein is a second death." },
            { "II", "FEAR THE LAMPLIGHT", "Lamp light burns you: you smoke and bleed out far faster. Keep to the shadows. A dead lamp is safe." },
            { "III", "POWER IS EARNED", "Mana only comes from landing blows. Spend it on Blood Leap (humans only), Shield, and Shadow Bite (it turns a victim but gives no blood)." },
            { "IV", "THE LAW HUNTS YOU", "Officers shoot on sight and raise their shields. Wound one before you drink; beat anyone to death and the blood is wasted." },
            { "V", "COFFINS ARE SANCTUARY", "Rest in a coffin to recover blood and mark where you rise again. Climbing out, you are untouchable for a moment." },
            { "VI", "FOLLOW THE BLOOD", "A trail of blood on the street leads to what waits for you tonight." },
        };
        for (int i = 0; i < rules.GetLength(0); i++)
        {
            float y = -138f - i * 112f;
            var num = Text(p.rect, rules[i, 0], titleFont, 56f, Blood, TextAlignmentOptions.TopRight, 0f);
            Place(num.rectTransform, new Vector2(0f, 1f), new Vector2(54f, y + 6f), new Vector2(90f, 70f), new Vector2(0f, 1f));
            var head = Text(p.rect, rules[i, 1], headingFont, 23f, Bone, TextAlignmentOptions.TopLeft, 5f);
            Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(172f, y), new Vector2(680f, 32f), new Vector2(0f, 1f));
            var body = Text(p.rect, rules[i, 2], bodyFont, 25f, Ash, TextAlignmentOptions.TopLeft, 0.5f);
            body.enableWordWrapping = true;
            body.lineSpacing = -6f;
            Place(body.rectTransform, new Vector2(0f, 1f), new Vector2(172f, y - 32f), new Vector2(680f, 70f), new Vector2(0f, 1f));
        }
    }

    void BuildControlsPage()
    {
        var p = NewPage("controls", "CONTROLS", 880f);
        string[][] rows =
        {
            new[] { "W", "A", "S", "D", "|Move" },
            new[] { "Shift", "|Sprint (hold, uses stamina)" },
            new[] { "Mouse", "|Face the cursor" },
            new[] { "Space", "|Jump" },
            new[] { "E", "/", "Left click", "|Bite a victim within reach" },
            new[] { "Q", "/", "Right click", "|Attack: lunge and strike" },
            new[] { "F", "|Rest in a coffin (checkpoint)" },
            new[] { "1", "|Blood Leap: leap onto a human, not police  (20 mana)" },
            new[] { "2", "|Shield: block all damage for 10 s  (30 mana)" },
            new[] { "3", "|Shadow Bite: turn a victim to fight for you, no blood  (50 mana)" },
            new[] { "Esc", "|Pause" },
        };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = -140f - i * 56f;
            var keys = NewUI("Keys", p.rect);
            Place((RectTransform)keys.transform, new Vector2(0f, 1f), new Vector2(296f, y), new Vector2(280f, 40f), new Vector2(1f, 1f));
            var h = keys.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childAlignment = TextAnchor.MiddleRight;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            string label = "";
            foreach (string k in rows[i])
            {
                if (k.StartsWith("|")) { label = k.Substring(1); continue; }
                if (k == "/") { var sl = Text(keys.transform, "/", bodyFont, 24f, Faint, TextAlignmentOptions.Center, 0f); AbilityHUD.Fixed(sl, 12f, 30f); continue; }
                AbilityHUD.KeyCap(keys.transform, k);
            }
            var t = Text(p.rect, label, bodyFont, 25f, Ash, TextAlignmentOptions.MidlineLeft, 0.3f);
            Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(318f, y), new Vector2(560f, 40f), new Vector2(0f, 1f));
        }
    }

    void BuildSettingsPage()
    {
        var p = NewPage("settings", "SETTINGS", 820f);
        brightnessSlider = SettingSlider(p.rect, "BRIGHTNESS", -150f, GameSettings.MinBrightness, GameSettings.MaxBrightness, GameSettings.Brightness,
                                         v => { GameSettings.SetBrightness(v); RefreshValues(); }, out brightnessValue);

        // Calibration: three drops on black, from near-black to dim. Set it so the left one is only just visible.
        var strip = NewImage(p.rect, "Calibration", AbilityHUD.Rounded(), Color.black);
        strip.type = Image.Type.Sliced;
        Place(strip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(560f, 130f), new Vector2(0.5f, 1f));
        calibration = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            calibration[i] = NewImage(strip.transform, "Drop" + i, AbilityHUD.Drop(), Color.black);
            calibration[i].preserveAspect = true;
            Place(calibration[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 160f, 0f), new Vector2(56f, 76f), new Vector2(0.5f, 0.5f));
        }
        var note = Text(p.rect, "Adjust until the left drop is barely visible.", bodyFont, 24f, Faint, TextAlignmentOptions.Top, 0.5f);
        Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -380f), new Vector2(700f, 34f), new Vector2(0.5f, 1f));

        masterSlider = SettingSlider(p.rect, "MASTER VOLUME", -440f, 0f, 1f, GameSettings.MasterVolume, v => { GameSettings.SetMasterVolume(v); RefreshValues(); }, out masterValue);
        musicSlider = SettingSlider(p.rect, "MUSIC VOLUME", -528f, 0f, 1f, GameSettings.MusicVolume, v => { GameSettings.SetMusicVolume(v); RefreshValues(); }, out musicValue);

        var reset = MenuButton(p.rect, "RESET TO DEFAULTS", 24f, () =>
        {
            GameSettings.ResetToDefaults();
            brightnessSlider.SetValueWithoutNotify(GameSettings.Brightness);
            masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
            musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
            RefreshValues();
        });
        Place((RectTransform)reset.transform, new Vector2(0f, 0f), new Vector2(30f, 18f), new Vector2(420f, 52f), new Vector2(0f, 0f));

        var back = (Button)p.first;
        LinkVertical(brightnessSlider, masterSlider, musicSlider, reset, back);
        var nr = reset.navigation; nr.selectOnRight = back; reset.navigation = nr;
        var nb = back.navigation; nb.selectOnLeft = reset; back.navigation = nb;
        p.first = brightnessSlider;
        RefreshValues();
    }

    Slider SettingSlider(RectTransform parent, string label, float y, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange, out TMP_Text valueText)
    {
        var row = NewUI("Row_" + label, parent);
        Place((RectTransform)row.transform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(820f, 76f), new Vector2(0.5f, 1f));
        var glow = NewImage(row.transform, "Highlight", AbilityHUD.Rounded(), new Color(0.5f, 0.03f, 0.05f, 0f));
        glow.type = Image.Type.Sliced;
        Stretch(glow.rectTransform, 0f, 0f, 1f, 1f);
        var name = Text(row.transform, label, headingFont, 22f, Bone, TextAlignmentOptions.MidlineLeft, 5f);
        Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(300f, 40f), new Vector2(0f, 0.5f));
        valueText = Text(row.transform, "", bodyFont, 26f, Ash, TextAlignmentOptions.MidlineRight, 0f);
        Place(valueText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(90f, 40f), new Vector2(1f, 0.5f));

        var sgo = NewUI("Slider", row.transform);
        Place((RectTransform)sgo.transform, new Vector2(0f, 0.5f), new Vector2(330f, 0f), new Vector2(360f, 36f), new Vector2(0f, 0.5f));
        var hitArea = sgo.AddComponent<Image>();
        hitArea.color = new Color(0f, 0f, 0f, 0f);
        var track = NewImage(sgo.transform, "Track", AbilityHUD.Rounded(), new Color(0.16f, 0.1f, 0.1f, 1f));
        track.type = Image.Type.Sliced;
        Stretch(track.rectTransform, 0f, 0.38f, 1f, 0.62f);
        var fillArea = NewUI("FillArea", sgo.transform);
        Stretch((RectTransform)fillArea.transform, 0f, 0.38f, 1f, 0.62f);
        var fill = NewImage(fillArea.transform, "Fill", AbilityHUD.Rounded(), Blood);
        fill.type = Image.Type.Sliced;
        Stretch(fill.rectTransform, 0f, 0f, 1f, 1f);
        var handleArea = NewUI("HandleArea", sgo.transform);
        var hart = (RectTransform)handleArea.transform;
        Stretch(hart, 0f, 0f, 1f, 1f);
        hart.offsetMin = new Vector2(10f, 0f);
        hart.offsetMax = new Vector2(-10f, 0f);
        var handle = NewImage(handleArea.transform, "Handle", AbilityHUD.Drop(), Bone);
        handle.preserveAspect = true;
        handle.rectTransform.sizeDelta = new Vector2(22f, 30f);

        var s = sgo.AddComponent<Slider>();
        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = hitArea;
        s.transition = Selectable.Transition.None;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.SetValueWithoutNotify(value);
        s.onValueChanged.AddListener(onChange);

        var fx = sgo.AddComponent<MenuItemFx>();
        fx.menu = this;
        fx.text = name;
        fx.rowGlow = glow;
        return s;
    }

    void RefreshValues()
    {
        if (brightnessValue != null) brightnessValue.text = (GameSettings.Brightness >= 0f ? "+" : "") + GameSettings.Brightness.ToString("0.0");
        if (masterValue != null) masterValue.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%";
        if (musicValue != null) musicValue.text = Mathf.RoundToInt(GameSettings.MusicVolume * 100f) + "%";
    }

    void UpdateCalibration()
    {
        // UI is drawn after post-processing, so the strip mimics the exposure change itself.
        float gain = Mathf.Pow(2f, GameSettings.Brightness);
        float[] levels = { 0.018f, 0.05f, 0.12f };
        for (int i = 0; i < calibration.Length; i++)
        {
            float v = Mathf.Clamp01(levels[i] * gain);
            calibration[i].color = new Color(Mathf.Clamp01(v * 1.15f), v * 0.85f, v * 0.85f, 1f);
        }
    }

    // ---- animation

    IEnumerator FlickerOn(CanvasGroup g)
    {
        float[] steps = { 0.6f, 0f, 0.9f, 0.1f, 0f, 0.7f, 0.3f, 1f };
        foreach (float a in steps)
        {
            g.alpha = a;
            yield return new WaitForSecondsRealtime(Random.Range(0.04f, 0.11f));
        }
        g.alpha = 1f;
    }

    IEnumerator TitleStutter()
    {
        int n = Random.Range(2, 5);
        for (int i = 0; i < n && !starting; i++)
        {
            titleGroup.alpha = Random.Range(0.25f, 0.6f);
            yield return new WaitForSecondsRealtime(Random.Range(0.03f, 0.08f));
            titleGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(Random.Range(0.04f, 0.12f));
        }
    }

    IEnumerator Drips()
    {
        yield return null;
        title.ForceMeshUpdate();
        var info = title.textInfo;
        // Drips hang from a few letters' feet.
        int[] at = { 1, 3, 5, 8, 10 };
        foreach (int index in at)
        {
            if (index >= info.characterCount) continue;
            var c = info.characterInfo[index];
            if (!c.isVisible) continue;
            Vector2 foot = new Vector2((c.bottomLeft.x + c.bottomRight.x) * 0.5f + Random.Range(-6f, 6f), c.baseLine + 6f);
            StartCoroutine(Drip(foot, Random.Range(0.5f, 6f)));
        }
    }

    IEnumerator Drip(Vector2 foot, float delay)
    {
        // TMP character positions are in the text's local space, i.e. relative to its pivot (the rect's centre here).
        var stem = NewImage(title.transform, "Drip", AbilityHUD.Rounded(), Blood);
        stem.type = Image.Type.Sliced;
        var srt = stem.rectTransform;
        srt.anchorMin = srt.anchorMax = title.rectTransform.pivot;
        srt.pivot = new Vector2(0.5f, 1f);
        srt.anchoredPosition = foot;
        srt.sizeDelta = Vector2.zero;
        var drop = NewImage(title.transform, "Falling", AbilityHUD.Drop(), new Color(0f, 0f, 0f, 0f));
        drop.preserveAspect = true;
        var drt = drop.rectTransform;
        drt.anchorMin = drt.anchorMax = title.rectTransform.pivot;
        drt.sizeDelta = new Vector2(12f, 17f);
        float width = Random.Range(5f, 8f);
        yield return new WaitForSecondsRealtime(delay);
        while (true)
        {
            // Swell slowly, then let a drop go.
            float length = Random.Range(18f, 46f), grow = Random.Range(2.5f, 5f), t = 0f;
            drop.color = Blood;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / grow;
                float k = Mathf.SmoothStep(0f, 1f, t);
                srt.sizeDelta = new Vector2(width, Mathf.Max(width, length * k));
                drt.anchoredPosition = foot - new Vector2(0f, length * k + 4f);
                drt.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, k);
                yield return null;
            }
            float y = 0f, v = 0f;
            while (y < 520f)
            {
                v += 1800f * Time.unscaledDeltaTime;
                y += v * Time.unscaledDeltaTime;
                drt.anchoredPosition = foot - new Vector2(0f, length + 4f + y);
                drop.color = new Color(Blood.r, Blood.g, Blood.b, 1f - y / 520f);
                srt.sizeDelta = new Vector2(width, Mathf.Max(width, length * (1f - Mathf.Clamp01(y / 60f) * 0.6f)));
                yield return null;
            }
            drop.color = new Color(0f, 0f, 0f, 0f);
            yield return new WaitForSecondsRealtime(Random.Range(2f, 7f));
        }
    }

    IEnumerator SlideIn(RectTransform rt, float delay)
    {
        Vector2 to = rt.anchoredPosition, from = to - new Vector2(40f, 0f);
        rt.anchoredPosition = from;
        yield return new WaitForSecondsRealtime(delay);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / 0.6f;
            rt.anchoredPosition = Vector2.Lerp(from, to, 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f));
            yield return null;
        }
    }

    IEnumerator PageIn(Page p)
    {
        float t = 0f;
        while (t < 1f && openPage == p)
        {
            t += Time.unscaledDeltaTime / 0.35f;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            p.group.alpha = k;
            p.rect.anchoredPosition = PagePosition + new Vector2(60f * (1f - k), 0f);
            yield return null;
        }
        p.group.alpha = 1f;
        p.rect.anchoredPosition = PagePosition;
    }

    static IEnumerator FadeGroup(CanvasGroup g, float to, float duration)
    {
        float from = g.alpha, t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
            g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
    }

    // ------------------------------------------------------------------ rain and ground mist

    void BuildAtmosphere()
    {
        Transform cam = menuCamera.transform;
        Vector3 ahead = cam.position + Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized * 7f;

        var rain = MakeParticles("Rain", ahead + Vector3.up * 9f, new Vector3(26f, 0.1f, 18f));
        var main = rain.main;
        main.startLifetime = 1.1f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.014f);
        main.startColor = new Color(0.62f, 0.68f, 0.85f, 0.22f);
        main.maxParticles = 4000;
        var em = rain.emission; em.rateOverTime = 1800f;
        var vel = rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-1.2f, -0.8f); vel.y = new ParticleSystem.MinMaxCurve(-15f, -13f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var rr = rain.GetComponent<ParticleSystemRenderer>();
        rr.renderMode = ParticleSystemRenderMode.Stretch;
        rr.velocityScale = 0.035f;
        var rs = rain.shape; rs.position = new Vector3(0f, 0f, 4f);   // keep streaks off the lens
        rr.lengthScale = 1f;
        rr.material = new Material(Shader.Find("Sprites/Default"));
        rain.Play();

        var mist = MakeParticles("GroundMist", ahead + Vector3.up * 0.4f, new Vector3(24f, 0.6f, 14f));
        var mm = mist.main;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        mm.startSpeed = 0f;
        mm.startSize = new ParticleSystem.MinMaxCurve(4f, 8f);
        mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        mm.startColor = new Color(0.55f, 0.6f, 0.75f, 0.07f);
        mm.maxParticles = 120;
        mm.prewarm = true;
        var me = mist.emission; me.rateOverTime = 8f;
        var mv = mist.velocityOverLifetime; mv.enabled = true; mv.space = ParticleSystemSimulationSpace.World;
        mv.x = new ParticleSystem.MinMaxCurve(0.15f, 0.35f); mv.y = new ParticleSystem.MinMaxCurve(0f, 0.02f); mv.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        var mc = mist.colorOverLifetime; mc.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        mc.color = g;
        var mr = mist.GetComponent<ParticleSystemRenderer>();
        mr.renderMode = ParticleSystemRenderMode.Billboard;
        mr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = ThrallAbility.MakeBlobTexture(64) };
        mist.Play();
    }

    ParticleSystem MakeParticles(string objectName, Vector3 position, Vector3 box)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    // ------------------------------------------------------------------ small builders

    AudioSource MakeSource(AudioClip clip, bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = loop ? 0f : 1f;
        return s;
    }

    static GameObject NewUI(string objectName, Transform parent)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static CanvasGroup NewGroup(Transform parent, string objectName) => NewUI(objectName, parent).AddComponent<CanvasGroup>();

    static Image NewImage(Transform parent, string objectName, Sprite sprite, Color color)
    {
        var img = NewUI(objectName, parent).AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text Text(Transform parent, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align, float spacing)
    {
        var t = NewUI("Text", parent).AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.characterSpacing = spacing;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    static T Stretch<T>(T g, float x0, float y0, float x1, float y1) where T : Graphic
    {
        Stretch(g.rectTransform, x0, y0, x1, y1);
        return g;
    }

    static void Stretch(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void LinkVertical(params Selectable[] items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var n = new Navigation { mode = Navigation.Mode.Explicit };
            n.selectOnUp = items[(i - 1 + items.Length) % items.Length];
            n.selectOnDown = items[(i + 1) % items.Length];
            items[i].navigation = n;
        }
    }

    static Sprite gradient, gradientUp;

    /// <summary>White fading to clear (left to right, or bottom to top).</summary>
    static Sprite GradientSprite(bool vertical)
    {
        if (!vertical && gradient != null) return gradient;
        if (vertical && gradientUp != null) return gradientUp;
        const int n = 256;
        var tex = new Texture2D(vertical ? 1 : n, vertical ? n : 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            float k = i / (n - 1f);
            // Solid for the first part: the scene is HDR, so even 95% black lets bright windows glow through.
            float alpha = k < 0.42f ? 1f : Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, (k - 0.42f) / 0.58f), 1.3f);
            px[i] = new Color32(255, 255, 255, (byte)(alpha * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        if (vertical) gradientUp = s; else gradient = s;
        return s;
    }
}

/// <summary>Hover/selection look of a menu item: the text warms to blood red and slides right, a blade grows in.</summary>
public class MenuItemFx : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerDownHandler, ISubmitHandler
{
    internal static bool Silent;
    internal MainMenu menu;
    internal TMP_Text text;
    internal RectTransform blade;
    internal Image drop;
    internal Image rowGlow;

    static readonly Color Bone = new Color(0.91f, 0.87f, 0.81f);
    static readonly Color Hot = new Color(1f, 0.24f, 0.2f);
    bool selected, hasBase;
    float k;
    Vector2 textBase;

    public void OnSelect(BaseEventData e)
    {
        selected = true;
        if (!Silent && menu != null) menu.PlayHover();
    }

    public void OnDeselect(BaseEventData e) => selected = false;

    // Buttons answer the moment they are pressed.
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Pressed(); }
    public void OnSubmit(BaseEventData e) => Pressed();

    void Pressed()
    {
        if (blade == null || menu == null) return;   // sliders make no click
        var sel = GetComponent<Selectable>();
        if (sel != null && !sel.IsInteractable()) return;
        menu.PlaySelect();
        if (name == "Btn_BACK") menu.MarkBackSounded();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        var sel = GetComponent<Selectable>();
        if (sel != null && sel.IsInteractable() && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject)
            EventSystem.current.SetSelectedGameObject(gameObject);
    }

    void Update()
    {
        if (!hasBase && text != null) { textBase = text.rectTransform.anchoredPosition; hasBase = true; }
        k = Mathf.MoveTowards(k, selected ? 1f : 0f, Time.unscaledDeltaTime * 7f);
        float e = 1f - (1f - k) * (1f - k);
        if (text != null) text.color = Color.Lerp(Bone, Hot, e);
        if (blade != null)
        {
            blade.sizeDelta = new Vector2(22f * e, 3f);
            text.rectTransform.anchoredPosition = textBase + new Vector2(30f * e, 0f);
            drop.color = new Color(1f, 0.2f, 0.18f, e);
            drop.rectTransform.anchoredPosition = new Vector2(22f * e + 8f, Mathf.Sin(Time.unscaledTime * 4f) * 2f * e);
        }
        if (rowGlow != null) rowGlow.color = new Color(0.5f, 0.03f, 0.05f, 0.35f * e);
    }
}
