using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Room 4's sound and the end of the game.
/// Desert stage: desert wind, and the march to the castle replaces the level music.
/// Ending stage (he walks through the castle gate): the crowd pours in, the gate swings shut behind them,
/// the screen fades, and a live news report plays over shots of the empty, burning city and the closed castle
/// (voice, headline, ticker, subtitles). Then the title card, the credits over the credits music, and the main menu.
/// Holding Esc, Enter or Space for a second skips the report or the credits.
/// </summary>
public class CastleEnding : MonoBehaviour
{
    [Header("Desert")]
    public AudioClip marchIntro;
    public AudioClip marchLoop;
    [Range(0f, 1f)] public float marchVolume = 0.55f;
    public AudioClip wind;
    [Range(0f, 1f)] public float windVolume = 0.35f;

    [Header("Gate")]
    [Tooltip("The two door leaves; open now, they swing shut (to no rotation) behind the crowd.")]
    public Transform gateLeafA;
    public Transform gateLeafB;
    [Tooltip("Seconds after he passes the gate before it closes, so his followers get in.")]
    public float gateDelay = 4f;
    [Tooltip("How long the doors take to swing shut (the creak's thud lands at about 5.5 s).")]
    public float gateCloseTime = 5.5f;
    public AudioClip gateSound;

    [Header("News report")]
    public AudioClip newsVoice;
    [Tooltip("The report as spoken, without the voice tags, one subtitle per line: the subtitles are timed from it.")]
    [TextArea(6, 16)]
    public string newsScript =
        "Good evening. We interrupt our program, once again, with breaking news.\n" +
        "Less than an hour ago, the man arrested last night escaped from police custody.\n" +
        "Officers say an experimental serum, H-7, was meant to hold him... It did not.\n" +
        "Shortly after, witnesses saw hundreds of people walking out of the city... police officers among them... in complete silence.\n" +
        "They did not answer when their names were called. They did not stop.\n" +
        "Fires are burning tonight across the streets they left behind.\n" +
        "Their trail leads west, along the desert road, to the old castle beyond the city limits. Its gates are now closed.\n" +
        "Authorities are urging everyone to stay indoors, to lock their doors... and not to open them, even for someone you know.\n" +
        "I'm being told... our crew at the castle has stopped responding.\n" +
        "We'll bring you more on this story... if we can.";
    public string headline = "THE CITY WALKS INTO THE DESERT";
    public string tickerText = "POLICE STATION BREACHED  •  SERUM H-7 FAILED  •  HUNDREDS MISSING  •  FIRES IN THE CITY CENTRE  •  STAY INDOORS AFTER DARK  •  CASTLE CREW NOT RESPONDING  •  ";
    [Tooltip("Camera spots for the report, in order; the camera drifts forward a little in each. They share the report's length.")]
    public Transform[] shots = new Transform[0];
    public float shotDrift = 3f;
    [Tooltip("Shown (switched on) only during the report: fires in the empty streets.")]
    public GameObject fires;

    [Header("End")]
    public string title = "BLOODTHIRST";
    public string subtitle = "Thank you for playing";
    [TextArea(10, 40)]
    public string credits =
        "BLOODTHIRST\n\nA DMET 909 project\nGerman University in Cairo\n\n\n" +
        "Abdelhamid Sameh Ali\nLevel Design & Environments, Camera, UI, Cutscenes, Audio & Music, Integration & Builds\n\n" +
        "Sohaila Omara\nCharacters & Animation\n\n" +
        "Ahmed Mohamed El-Sayed\nPolice AI & Combat, the Armoured Officer\n\n" +
        "Ali Hesham\nVampire Abilities & Gameplay\n\n\n" +
        "Third-party assets\n\nPOLYGON city pack\nSimple Vehicle Pack\nLow Poly Police Station (free)\nModular Castle\nEmaceArt - Raft on the Desert\n" +
        "Batwing health bar (itch.io)\nCharacters and animations: Mixamo\nSand texture: ambientCG (CC0)\n" +
        "Sound effects: Pixabay, freesound, ElevenLabs\nVoices: ElevenLabs\nMusic made in Bosca Ceoil\n\n\n\nThe night is his.";
    public AudioClip creditsMusic;
    [Range(0f, 1f)] public float creditsVolume = 0.6f;
    [Tooltip("How long the credits take to scroll.")]
    public float creditsSeconds = 70f;
    public string menuScene = "MainMenu";
    public float holdToSkip = 1f;

    AudioSource marchA, marchB, windSource, voice, music;
    MusicPlayer levelMusic;
    float marchTarget, windTarget;
    bool ended;

    CanvasGroup black, news, endCard, creditsGroup, skipHint;
    TMP_Text subtitleText, creditsText;
    RectTransform tickerRect, creditsRect;
    Coroutine subtitleRoutine;
    float skipHeld;

    void Start()
    {
        var director = Level2Director.Instance;
        if (director == null) return;
        marchA = Source(marchIntro, false);
        marchB = Source(marchLoop, true);
        windSource = Source(wind, true);
        director.onStageChanged.AddListener(OnStage);
        if (director.Current >= Level2Director.Stage.Desert) OnStage(director.Current);
    }

    AudioSource Source(AudioClip clip, bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.ignoreListenerPause = true;
        s.volume = 0f;
        return s;
    }

    void OnStage(Level2Director.Stage stage)
    {
        if (stage >= Level2Director.Stage.Desert && marchTarget == 0f && !ended) StartDesert();
        if (stage == Level2Director.Stage.Ending && !ended) { ended = true; StartCoroutine(Ending()); }
    }

    void StartDesert()
    {
        levelMusic = FindFirstObjectByType<MusicPlayer>();
        if (levelMusic != null) levelMusic.duck = 0f;
        double start = AudioSettings.dspTime + 0.1;
        if (marchIntro != null) { marchA.PlayScheduled(start); start += (double)marchIntro.samples / marchIntro.frequency; }
        if (marchLoop != null) marchB.PlayScheduled(start);
        if (wind != null) windSource.Play();
        marchTarget = 1f;
        windTarget = 1f;
    }

    void Update()
    {
        if (marchA == null) return;
        float pause = GameState.Paused ? 0.4f : 1f;
        float m = Mathf.MoveTowards(marchA.volume, marchTarget * marchVolume * GameSettings.MusicVolume * pause, Time.unscaledDeltaTime / 2f);
        marchA.volume = marchB.volume = m;
        windSource.volume = Mathf.MoveTowards(windSource.volume, windTarget * windVolume, Time.unscaledDeltaTime / 2f);
        if (tickerRect != null && news.alpha > 0f)
        {
            tickerRect.anchoredPosition += Vector2.left * 140f * Time.unscaledDeltaTime;
            if (tickerRect.anchoredPosition.x < -tickerRect.rect.width * 0.5f) tickerRect.anchoredPosition = Vector2.zero;   // the text is doubled, so the wrap is seamless
        }
    }

    // ------------------------------------------------------------------ the ending

    IEnumerator Ending()
    {
        var vampire = Level2Director.Instance.vampire;
        vampire.BeginAbility();   // he stops in the courtyard; the crowd catches up
        HideGameplayUI();
        BuildUI();

        yield return new WaitForSeconds(gateDelay);
        if (gateSound != null && gateLeafA != null) AudioSource.PlayClipAtPoint(gateSound, gateLeafA.position, 1f);
        yield return CloseGate();
        yield return new WaitForSeconds(1.2f);

        marchTarget = windTarget = 0f;
        yield return Fade(black, 1f, 1.8f);

        // the report
        var cam = Camera.main;
        var iso = cam != null ? cam.GetComponent<IsometricCamera>() : null;
        if (iso != null) iso.enabled = false;
        if (fires != null) fires.SetActive(true);
        news.alpha = 1f;
        voice = Source(newsVoice, false);
        voice.volume = 1f;
        float length = newsVoice != null ? newsVoice.length : newsScript.Length / 15f;
        voice.PlayDelayed(0.6f);
        subtitleRoutine = StartCoroutine(Subtitles(length));
        yield return Fade(black, 0f, 0.8f);
        float perShot = length / Mathf.Max(1, shots.Length);
        bool skipped = false;
        for (int i = 0; i < shots.Length && !skipped; i++)
        {
            if (shots[i] == null || cam == null) continue;
            if (i > 0) yield return Fade(black, 1f, 0.25f);
            Vector3 from = shots[i].position;
            cam.transform.SetPositionAndRotation(from, shots[i].rotation);
            if (i > 0) StartCoroutine(Fade(black, 0f, 0.25f));
            for (float t = 0f; t < perShot && !skipped; t += Time.deltaTime)
            {
                cam.transform.position = from + shots[i].forward * (shotDrift * t / perShot);
                skipped = Skipping();
                yield return null;
            }
        }
        if (shots.Length == 0) yield return WaitOrSkip(length);
        if (subtitleRoutine != null) StopCoroutine(subtitleRoutine);
        subtitleText.text = "";
        yield return Fade(black, 1f, 0.6f);
        if (voice != null) voice.Stop();
        news.alpha = 0f;

        // the title card, then the credits
        yield return Fade(endCard, 1f, 1.5f);
        yield return WaitOrSkip(3f);
        music = Source(creditsMusic, false);
        music.volume = creditsVolume * GameSettings.MusicVolume;
        music.Play();
        yield return Fade(endCard, 0f, 1f);
        creditsGroup.alpha = 1f;
        float travel = creditsRect.rect.height + 1080f;
        for (float t = 0f; t < creditsSeconds; t += Time.deltaTime)
        {
            creditsRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-540f, travel - 540f, t / creditsSeconds));
            if (Skipping()) break;
            yield return null;
        }
        float startVolume = music.volume;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 2f) { music.volume = Mathf.Lerp(startVolume, 0f, t); yield return null; }
        SceneManager.LoadScene(menuScene);
    }

    IEnumerator CloseGate()
    {
        if (gateLeafA == null || gateLeafB == null) yield break;
        Quaternion a0 = gateLeafA.localRotation, b0 = gateLeafB.localRotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, gateCloseTime))
        {
            float k = t * t;   // heavy: slow to start, then it swings home
            gateLeafA.localRotation = Quaternion.Slerp(a0, Quaternion.identity, k);
            gateLeafB.localRotation = Quaternion.Slerp(b0, Quaternion.identity, k);
            yield return null;
        }
        gateLeafA.localRotation = gateLeafB.localRotation = Quaternion.identity;
    }

    IEnumerator Subtitles(float length)
    {
        string[] lines = newsScript.Split('\n');
        int total = 0;
        foreach (var l in lines) total += Mathf.Max(1, l.Trim().Length);
        yield return new WaitForSeconds(0.6f);
        foreach (var l in lines)
        {
            subtitleText.text = l.Trim();
            yield return new WaitForSeconds(length * Mathf.Max(1, l.Trim().Length) / total);   // each line gets its share of the voice
        }
        subtitleText.text = "";
    }

    bool Skipping()
    {
        var kb = Keyboard.current;
        bool held = kb != null && (kb.escapeKey.isPressed || kb.enterKey.isPressed || kb.spaceKey.isPressed);
        skipHeld = held ? skipHeld + Time.unscaledDeltaTime : 0f;
        skipHint.alpha = Mathf.MoveTowards(skipHint.alpha, held ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        if (skipHeld < holdToSkip) return false;
        skipHeld = 0f;
        return true;
    }

    IEnumerator WaitOrSkip(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) { if (Skipping()) yield break; yield return null; }
    }

    static IEnumerator Fade(CanvasGroup g, float to, float time)
    {
        float from = g.alpha;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, time)) { g.alpha = Mathf.Lerp(from, to, t); yield return null; }
        g.alpha = to;
    }

    static void HideGameplayUI()
    {
        var gameUI = FindFirstObjectByType<GameUI>();
        if (gameUI != null) gameUI.enabled = false;   // no pause menu over the ending
        // every HUD canvas (blood, stamina, abilities, sunrise); the fades and toasts sort above 40 and stay
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.sortingOrder < 40) c.enabled = false;
    }

    // ------------------------------------------------------------------ screen

    void BuildUI()
    {
        var root = new GameObject("EndingUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // the broadcast
        news = Group("News", root.transform);
        var live = Box("Live", news.transform, new Color(0.75f, 0.06f, 0.06f), new Vector2(0f, 1f), new Vector2(60f, -60f), new Vector2(110f, 44f));
        Text("Text", live, "LIVE", 28f, Color.white, TextAlignmentOptions.Center);
        var tag = Box("Breaking", news.transform, new Color(0.75f, 0.06f, 0.06f), Vector2.zero, new Vector2(60f, 150f), new Vector2(300f, 50f));
        Text("Text", tag, "BREAKING NEWS", 28f, Color.white, TextAlignmentOptions.Center);
        var head = Box("Headline", news.transform, new Color(0.96f, 0.95f, 0.92f), Vector2.zero, new Vector2(60f, 80f), new Vector2(1100f, 70f));
        Text("Text", head, headline, 38f, new Color(0.08f, 0.08f, 0.1f), TextAlignmentOptions.MidlineLeft).margin = new Vector4(24f, 0f, 0f, 0f);
        var strip = Box("Ticker", news.transform, new Color(0.05f, 0.06f, 0.12f), Vector2.zero, Vector2.zero, new Vector2(0f, 44f));
        strip.anchorMax = new Vector2(1f, 0f);
        strip.gameObject.AddComponent<RectMask2D>();
        var tick = Text("Text", strip, tickerText + tickerText, 24f, new Color(0.95f, 0.9f, 0.7f), TextAlignmentOptions.MidlineLeft);
        tick.textWrappingMode = TextWrappingModes.NoWrap;
        tickerRect = tick.rectTransform;
        tickerRect.anchorMin = Vector2.zero; tickerRect.anchorMax = new Vector2(0f, 1f); tickerRect.pivot = new Vector2(0f, 0.5f);
        tickerRect.sizeDelta = new Vector2(tick.GetPreferredValues().x, 0f);
        subtitleText = Text("Subtitle", news.transform, "", 34f, Color.white, TextAlignmentOptions.Bottom);
        var sr = subtitleText.rectTransform;
        sr.anchorMin = new Vector2(0.15f, 0f); sr.anchorMax = new Vector2(0.85f, 0f);
        sr.offsetMin = new Vector2(0f, 230f); sr.offsetMax = new Vector2(0f, 360f);
        subtitleText.outlineWidth = 0.25f; subtitleText.outlineColor = Color.black;
        news.alpha = 0f;

        black = Group("Black", root.transform);
        var img = black.gameObject.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        black.alpha = 0f;

        // title card and credits, on the black
        endCard = Group("EndCard", root.transform);
        Text("Title", endCard.transform, title, 110f, new Color(0.75f, 0.08f, 0.08f), TextAlignmentOptions.Center).characterSpacing = 18f;
        var sub = Text("Subtitle", endCard.transform, subtitle, 30f, new Color(0.9f, 0.86f, 0.78f), TextAlignmentOptions.Center);
        sub.rectTransform.offsetMin = new Vector2(0f, -200f); sub.rectTransform.offsetMax = new Vector2(0f, -200f);
        endCard.alpha = 0f;
        creditsGroup = Group("Credits", root.transform);
        creditsText = Text("Text", creditsGroup.transform, credits, 34f, new Color(0.92f, 0.88f, 0.8f), TextAlignmentOptions.Top);
        creditsRect = creditsText.rectTransform;
        creditsRect.anchorMin = creditsRect.anchorMax = new Vector2(0.5f, 0.5f);
        creditsRect.pivot = new Vector2(0.5f, 1f);
        creditsRect.sizeDelta = new Vector2(1400f, creditsText.GetPreferredValues(credits, 1400f, 0f).y + 40f);
        creditsRect.anchoredPosition = new Vector2(0f, -540f);
        creditsGroup.alpha = 0f;

        skipHint = Group("SkipHint", root.transform);
        var hint = Text("Text", skipHint.transform, "Hold to skip", 24f, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.BottomRight);
        hint.margin = new Vector4(0f, 0f, 40f, 60f);
        skipHint.alpha = 0f;
    }

    static CanvasGroup Group(string name, Transform parent)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var g = rt.gameObject.AddComponent<CanvasGroup>();
        g.blocksRaycasts = g.interactable = false;
        return g;
    }

    static RectTransform Box(string name, Transform parent, Color color, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0f, anchor.y);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    static TMP_Text Text(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
