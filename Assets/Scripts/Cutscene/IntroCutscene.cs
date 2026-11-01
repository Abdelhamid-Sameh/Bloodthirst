using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Level intro. A TV in a dark room shows a live news report over the city streets (headline, ticker,
/// anchor voice with subtitles) about coffins appearing and an anonymous warning of a vampire. The TV
/// glitches out, the film cuts to a close-up of the start coffin, the lid opens and the vampire climbs out.
/// Then it fades into play. Space, Enter or Esc skips. Empty audio slots are silent; without a voice clip
/// the subtitles are timed from the script length, so the scene works before the sounds exist.
/// </summary>
public class IntroCutscene : MonoBehaviour
{
    public bool playOnStart = true;

    [Header("Scene")]
    public VampireController vampire;
    [Tooltip("Empty = the coffin nearest the vampire.")]
    public CoffinCheckpoint startCoffin;
    public Camera gameplayCamera;
    public GameUI gameUI;
    public MusicPlayer music;

    [Header("Cameras")]
    [Tooltip("Films the TV room, then the coffin close-up.")]
    public Camera cutsceneCamera;
    public Transform tvShotStart;
    public Transform tvShotEnd;
    [Tooltip("Renders the street into the TV screen.")]
    public Camera newsCamera;
    [Tooltip("The news camera glides through these points in order over the report.")]
    public Transform[] newsPath;

    [Header("TV")]
    public Renderer tvScreen;
    public Light tvLight;
    public RectTransform ticker;
    public float tickerSpeed = 120f;

    [Header("Overlay")]
    public CanvasGroup black;
    public TMP_Text subtitle;
    public CanvasGroup skipHint;

    [Header("Audio (Assets/Audio/Cutscene)")]
    public AudioClip newsJingle;
    public AudioClip newsVoice;
    [Tooltip("Looping street sounds under the report (sirens, crowd).")]
    public AudioClip newsBed;
    public AudioClip tvStatic;
    public AudioClip coffinStinger;
    [Range(0f, 1f)] public float voiceVolume = 1f;
    [Tooltip("Volume of the news bed under the anchor's voice.")]
    [Range(0f, 1f)] public float bedVolume = 0.25f;
    [Tooltip("Volume of the news bed before the anchor starts (its opening jingle).")]
    [Range(0f, 1f)] public float bedIntroVolume = 0.7f;
    [Tooltip("Loop the news bed. Off by default so a jingle at its start doesn't replay mid-report.")]
    public bool loopBed = false;
    [Tooltip("Seconds after the TV turns on before the anchor speaks (lets the bed's jingle play). Ignored when a separate jingle clip is set.")]
    public float voiceDelay = 2.5f;

    [Header("Report")]
    [TextArea(6, 14)]
    public string newsScript =
        "Good evening. We interrupt our programming with breaking news. " +
        "Over the past few hours, coffins have been appearing throughout the city: in alleyways, in parks, and in the middle of our streets. " +
        "No one has claimed responsibility. " +
        "Tonight, police received an anonymous warning about what the sender calls a vampire, a creature that stays alive by biting people to death. " +
        "Authorities are urging residents to stay indoors and to report anything unusual. " +
        "We will bring you more on this story as it develops.";
    [Tooltip("Reading speed used to time subtitles when there is no voice clip.")]
    public float charsPerSecond = 15f;

    [Header("Coffin shot")]
    [Tooltip("Camera offset: x = out from the coffin on the side he climbs out, y = up, z = to the side.")]
    public Vector3 coffinCamStart = new Vector3(6f, 2.8f, 2.2f);
    public Vector3 coffinCamEnd = new Vector3(4.2f, 2.0f, 1.4f);
    public float coffinLookHeight = 0.6f;
    public float holdAfterEmerge = 1f;

    bool skip, emerged, finished, tvOver;
    AudioSource sfx, voice, bed, staticLoop, glitchSource;
    Material screenMat;
    Texture screenFeed;
    Texture2D staticTex;
    float staticUntil;
    float tvLightBase;

    void Awake()
    {
        if (!playOnStart) { gameObject.SetActive(false); return; }
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        // He must start in the coffin the cutscene films.
        if (vampire != null && startCoffin != null) vampire.startCoffin = startCoffin;
        if (gameUI == null) gameUI = FindFirstObjectByType<GameUI>();
        if (music == null) music = FindFirstObjectByType<MusicPlayer>();
        if (gameplayCamera == null) gameplayCamera = Camera.main;

        // Runs before the vampire's Start, so he stays lying in the closed coffin until the coffin shot.
        if (vampire != null) vampire.waitForCutscene = true;
        if (music != null) music.enabled = false;          // its Start (music fade-in) runs when re-enabled
        if (gameUI != null)
        {
            gameUI.enabled = false;
            var group = gameUI.GetComponent<CanvasGroup>();
            if (group == null) group = gameUI.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
        }
        if (gameplayCamera != null) gameplayCamera.enabled = false;
        cutsceneCamera.enabled = true;
        newsCamera.enabled = true;
        black.alpha = 1f;
        if (subtitle != null) subtitle.text = "";
        if (skipHint != null) skipHint.alpha = 0f;

        // The street waits for the cutscene: no one wanders, screams or shoots until the game starts.
        foreach (var h in FindObjectsByType<HumanAI>(FindObjectsSortMode.None)) Freeze(h);
        foreach (var h in FindObjectsByType<HumanAudio>(FindObjectsSortMode.None)) Freeze(h);
        foreach (var p in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None)) Freeze(p);

        sfx = MakeSource(false);
        voice = MakeSource(false);
        bed = MakeSource(true);
        staticLoop = MakeSource(true);
        glitchSource = MakeSource(false);

        screenMat = tvScreen.material;
        screenFeed = screenMat.GetTexture("_BaseMap");
        staticTex = new Texture2D(160, 90, TextureFormat.RGB24, false) { filterMode = FilterMode.Point };
        if (tvLight != null) tvLightBase = tvLight.intensity;
    }

    readonly List<Behaviour> frozen = new List<Behaviour>();

    void Freeze(Behaviour b)
    {
        if (b == null || !b.enabled) return;
        b.enabled = false;
        frozen.Add(b);
    }

    AudioSource MakeSource(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;
        return s;
    }

    void Start()
    {
        if (vampire != null) vampire.onEmerged.AddListener(OnEmerged);
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (vampire != null) vampire.onEmerged.RemoveListener(OnEmerged);
        if (staticTex != null) Destroy(staticTex);
    }

    void OnEmerged() => emerged = true;

    void Update()
    {
        if (finished) return;
        var kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
            skip = true;

        // TV screen: live feed, or animated static while glitching.
        bool glitch = Time.time < staticUntil;
        if (!glitch && glitchSource.isPlaying) glitchSource.Stop();   // long static clips are cut to the glitch
        if (glitch)
        {
            var px = staticTex.GetPixels32();
            for (int i = 0; i < px.Length; i++) { byte v = (byte)Random.Range(20, 255); px[i] = new Color32(v, v, v, 255); }
            staticTex.SetPixels32(px);
            staticTex.Apply();
        }
        screenMat.SetTexture("_BaseMap", glitch ? staticTex : screenFeed);

        if (tvLight != null)
            tvLight.intensity = tvLightBase * (glitch ? Random.Range(0.6f, 1.6f) : 0.8f + 0.35f * Mathf.PerlinNoise(Time.time * 3f, 0.3f));

        if (ticker != null)
        {
            var p = ticker.anchoredPosition;
            p.x -= tickerSpeed * Time.deltaTime;
            if (p.x < -ticker.rect.width) p.x = ((RectTransform)ticker.parent).rect.width;
            ticker.anchoredPosition = p;
        }
    }

    IEnumerator Run()
    {
        // ---- Shot 1: the TV report -------------------------------------------------
        Glitch(0.5f, 0.2f);                                  // the TV flickers on
        StartCoroutine(Fade(black, 0f, 1.5f));
        StartCoroutine(Fade(skipHint, 1f, 1f));
        if (newsBed != null) { bed.clip = newsBed; bed.loop = loopBed; bed.volume = bedIntroVolume; bed.Play(); }
        if (tvStatic != null) { staticLoop.clip = tvStatic; staticLoop.volume = 0.025f; staticLoop.Play(); }
        if (newsJingle != null) sfx.PlayOneShot(newsJingle, 0.8f);

        float voiceStart = newsJingle != null ? Mathf.Min(newsJingle.length, 3f) : voiceDelay;
        float voiceLength = newsVoice != null ? newsVoice.length : newsScript.Length / Mathf.Max(1f, charsPerSecond);
        float tvLength = voiceStart + voiceLength + 1.2f;
        StartCoroutine(Subtitles(voiceStart, voiceLength));

        float t = 0f;
        while (t < tvLength && !skip)
        {
            if (newsVoice != null && !voice.isPlaying && t >= voiceStart && t < voiceStart + 0.5f)
            { voice.clip = newsVoice; voice.volume = voiceVolume; voice.Play(); }
            float k = t / tvLength;
            Place(cutsceneCamera.transform, tvShotStart, tvShotEnd, Mathf.SmoothStep(0f, 1f, k));
            cutsceneCamera.transform.position += new Vector3(Mathf.PerlinNoise(t * 0.4f, 1f) - 0.5f, Mathf.PerlinNoise(1f, t * 0.4f) - 0.5f, 0f) * 0.03f; // hand-held drift
            FlyNewsCamera(k);
            bed.volume = Mathf.MoveTowards(bed.volume, t >= voiceStart ? bedVolume : bedIntroVolume, Time.deltaTime * 0.8f); // duck under the anchor
            t += Time.deltaTime;
            yield return null;
        }
        if (skip) { yield return Finish(); yield break; }

        // ---- Cut: the signal breaks up ---------------------------------------------
        Glitch(0.6f, 0.3f);
        yield return new WaitForSeconds(0.6f);
        black.alpha = 1f;
        tvOver = true;
        voice.Stop(); bed.Stop(); staticLoop.Stop();
        if (subtitle != null) subtitle.text = "";
        newsCamera.enabled = false;
        yield return new WaitForSeconds(0.5f);

        // ---- Shot 2: the coffin ---------------------------------------------------
        CoffinCheckpoint coffin = ResolveCoffin();
        if (coffinStinger != null) sfx.PlayOneShot(coffinStinger, 0.9f);
        StartCoroutine(Fade(black, 0f, 0.8f));
        t = 0f;
        float push = 0f;
        bool released = false;
        while (!emerged && !skip)
        {
            // A beat on the closed coffin before the lid moves.
            if (!released && t >= 1.4f && vampire != null) { vampire.ReleaseFromCoffin(); released = true; }
            push = Mathf.Min(1f, push + Time.deltaTime / 6f);
            PlaceCoffinCamera(coffin, Mathf.SmoothStep(0f, 1f, push));
            t += Time.deltaTime;
            if (t > 20f) break;                                 // safety: never hang the game
            yield return null;
        }
        t = 0f;
        while (t < holdAfterEmerge && !skip)
        {
            push = Mathf.Min(1f, push + Time.deltaTime / 6f);
            PlaceCoffinCamera(coffin, Mathf.SmoothStep(0f, 1f, push));
            t += Time.deltaTime;
            yield return null;
        }
        yield return Finish();
    }

    /// <summary>Fade to black, hand over to the gameplay camera and UI, fade back in.</summary>
    IEnumerator Finish()
    {
        finished = true;
        StartCoroutine(Fade(skipHint, 0f, 0.3f));
        yield return Fade(black, 1f, skip ? 0.3f : 0.9f);
        if (vampire != null) vampire.ReleaseFromCoffin();   // a skip before the coffin shot: he rises in game view
        voice.Stop(); bed.Stop(); staticLoop.Stop();
        if (subtitle != null) subtitle.text = "";

        cutsceneCamera.enabled = false;
        newsCamera.enabled = false;
        if (gameplayCamera != null)
        {
            gameplayCamera.enabled = true;
            if (gameplayCamera.TryGetComponent(out IsometricCamera iso)) iso.SnapToTarget();
        }
        if (music != null) music.enabled = true;
        if (gameUI != null)
        {
            gameUI.enabled = true;
            gameUI.GetComponent<CanvasGroup>().alpha = 1f;
            if (emerged) gameUI.ShowControlsCard();             // otherwise its own listener shows it when he is out
        }
        if (tvLight != null) tvLight.enabled = false;
        foreach (var b in frozen) if (b != null) b.enabled = true;
        frozen.Clear();

        yield return Fade(black, 0f, 1f);
        gameObject.SetActive(false);                           // the TV room stops rendering
    }

    IEnumerator Subtitles(float start, float length)
    {
        if (subtitle == null) yield break;
        var lines = SplitSentences(newsScript);
        int total = 0;
        foreach (var l in lines) total += l.Length;
        yield return new WaitForSeconds(start);
        // The clock follows the voice clip when there is one, so frame hitches can't pull the subtitles out of sync.
        float clock = 0f, end = 0f;
        foreach (var line in lines)
        {
            if (skip || finished || tvOver) break;
            subtitle.text = line;
            end += length * line.Length / Mathf.Max(1, total);
            while (!skip && !finished && !tvOver)
            {
                clock = newsVoice != null && voice.isPlaying ? voice.time : clock + Time.deltaTime;
                if (clock >= end) break;
                yield return null;
            }
        }
        subtitle.text = "";
    }

    static List<string> SplitSentences(string text)
    {
        var result = new List<string>();
        int from = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '.' && text[i] != '!' && text[i] != '?') continue;
            string s = text.Substring(from, i - from + 1).Trim();
            if (s.Length > 0) result.Add(s);
            from = i + 1;
        }
        if (from < text.Length && text.Substring(from).Trim().Length > 0) result.Add(text.Substring(from).Trim());
        return result;
    }

    void Glitch(float seconds, float volume)
    {
        staticUntil = Time.time + seconds;
        if (tvStatic == null) return;
        glitchSource.Stop();
        glitchSource.clip = tvStatic;
        glitchSource.volume = volume;
        glitchSource.time = Random.Range(0f, Mathf.Max(0f, tvStatic.length - seconds - 0.1f)); // a random slice of the static
        glitchSource.Play();
    }

    void FlyNewsCamera(float k)
    {
        if (newsPath == null || newsPath.Length == 0) return;
        if (newsPath.Length == 1) { Place(newsCamera.transform, newsPath[0], newsPath[0], 0f); return; }
        float f = Mathf.Clamp01(k) * (newsPath.Length - 1);
        int i = Mathf.Min(Mathf.FloorToInt(f), newsPath.Length - 2);
        Place(newsCamera.transform, newsPath[i], newsPath[i + 1], Mathf.SmoothStep(0f, 1f, f - i));
    }

    static void Place(Transform t, Transform a, Transform b, float k)
    {
        t.SetPositionAndRotation(Vector3.Lerp(a.position, b.position, k), Quaternion.Slerp(a.rotation, b.rotation, k));
    }

    CoffinCheckpoint ResolveCoffin()
    {
        if (startCoffin != null) return startCoffin;
        if (vampire != null && vampire.startCoffin != null) return vampire.startCoffin;
        CoffinCheckpoint best = null;
        float bestD = float.MaxValue;
        foreach (var c in FindObjectsByType<CoffinCheckpoint>(FindObjectsSortMode.None))
        {
            float d = vampire != null ? (c.transform.position - vampire.transform.position).sqrMagnitude : 0f;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    void PlaceCoffinCamera(CoffinCheckpoint coffin, float k)
    {
        if (coffin == null) return;
        // Offsets follow the side he climbs out of, so any coffin rotation or scale frames correctly.
        Vector3 center = coffin.transform.position;
        Vector3 outward = coffin.RespawnPosition - center;
        outward.y = 0f;
        outward = outward.sqrMagnitude > 0.001f ? outward.normalized : coffin.transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        Vector3 o = Vector3.Lerp(coffinCamStart, coffinCamEnd, k);
        Vector3 pos = center + outward * o.x + Vector3.up * o.y + side * o.z;
        Vector3 look = center + Vector3.up * coffinLookHeight;
        cutsceneCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
    }

    static IEnumerator Fade(CanvasGroup g, float to, float time)
    {
        if (g == null) yield break;
        float from = g.alpha, t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, time);
            g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
            yield return null;
        }
    }
}
