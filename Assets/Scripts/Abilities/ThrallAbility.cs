using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Special ability "Shadow Bite".
///
/// 1. Press the button at the bottom right (or T): a ring shows how far the ability reaches around the vampire, and every
///    human and officer inside it is marked on the ground.
/// 2. Move the cursor over one of them: a straight line of black dust shows the path to him.
/// 3. Left-click: the vampire turns to black dust, streaks along that line, appears next to the target and bites him.
///    The victim becomes his thrall (see ThrallAI) and fights for him until he is dead.
///
/// Right click, Esc, T or the button again cancel. The component is added to the vampire automatically when the game
/// starts (ThrallAbilityInstaller below), so nothing has to be set up in the scene.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VampireController))]
public class ThrallAbility : MonoBehaviour
{
    enum Mode { Idle, Aiming, Casting }

    [Header("Ability")]
    [Tooltip("Key that starts or cancels aiming (the button at the bottom right does the same).")]
    public Key hotkey = Key.T;
    [Tooltip("How far around the vampire a target can be (metres).")]
    public float range = 10f;
    [Tooltip("Seconds before the ability can be used again.")]
    public float cooldown = 12f;
    [Tooltip("How many thralls can be alive at the same time.")]
    public int maxThralls = 1;
    [Tooltip("A target counts as hovered when the cursor is within this many screen pixels of him.")]
    public float hoverRadiusPixels = 90f;

    [Header("Shadow dash")]
    [Tooltip("Seconds the vampire spends as dust, streaking toward the target.")]
    public float dashDuration = 0.35f;
    [Tooltip("How close to the target the vampire appears (metres).")]
    public float arriveDistance = 0.9f;

    [Header("Look")]
    public Color accent = new Color(0.62f, 0.32f, 1f);
    [Tooltip("Colour of the creeping shadow pool, its ring and the dust around it (the ability's range).")]
    public Color shadowColor = new Color(0.34f, 0.07f, 0.55f);


    VampireController controller;
    VampireAudio sfx;
    Animator animator;
    CharacterController body;
    Camera cam;

    Mode mode;
    float cooldownRemaining;
    float flashUntil;
    bool uiShown;
    Transform hovered;

    // aiming visuals
    GameObject disc, disc2;
    Material discMaterial, disc2Material;
    LineRenderer rangeRing, previewLine;
    Material lineMaterial;
    readonly Dictionary<Transform, LineRenderer> targetRings = new Dictionary<Transform, LineRenderer>();
    readonly List<Transform> candidates = new List<Transform>();
    readonly List<Transform> staleRings = new List<Transform>();
    ParticleSystem dust, rangeDust, innerDust;
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();

    // UI
    GameObject uiRoot;
    CanvasGroup uiGroup;
    Image ringImage, cooldownImage;
    TMP_Text cooldownText, countText, keyText, hintText;

    const int RingPoints = 96;
    const int TargetRingPoints = 40;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        controller = GetComponent<VampireController>();
        sfx = GetComponent<VampireAudio>();
        animator = GetComponent<Animator>();
        body = GetComponent<CharacterController>();
        BuildAimVisuals();
        BuildDust();
        BuildUI();
        ShowAimVisuals(false);
    }

    void OnEnable() { controller.onEmerged.AddListener(OnEmerged); }

    void OnDisable()
    {
        controller.onEmerged.RemoveListener(OnEmerged);
        if (mode == Mode.Aiming) CancelAiming();
    }

    void Start()
    {
        if (!controller.startInCoffin) uiShown = true;
    }

    void OnEmerged() { uiShown = true; }

void OnDestroy()
    {
        if (uiRoot != null) Destroy(uiRoot);
        if (disc != null) Destroy(disc);
        if (disc2 != null) Destroy(disc2);
        if (rangeRing != null) Destroy(rangeRing.gameObject);
        if (previewLine != null) Destroy(previewLine.gameObject);
        if (dust != null) Destroy(dust.gameObject);
        if (rangeDust != null) Destroy(rangeDust.gameObject);
        if (innerDust != null) Destroy(innerDust.gameObject);
        foreach (LineRenderer lr in targetRings.Values) if (lr != null) Destroy(lr.gameObject);
    }

    int ActiveThralls => ThrallAI.All.Count;

    // ------------------------------------------------------------------ input

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (!uiShown && !controller.IsBusy && !controller.IsDead && Time.timeSinceLevelLoad > 2f) uiShown = true;
        if (Time.timeScale > 0f) cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Time.deltaTime);
        UpdateUI();

        if (GameState.Paused)
        {
            if (mode == Mode.Aiming) CancelAiming();
            return;
        }
        if (mode == Mode.Casting) return;

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (kb != null && kb[hotkey].wasPressedThisFrame) ToggleAiming();
        if (mode != Mode.Aiming) return;

        if (controller.IsDead || controller.IsBusy) { CancelAiming(); return; }
        if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
        {
            CancelAiming();
            return;
        }
        UpdateAiming(mouse);
    }

    /// <summary>Starts or cancels target selection (called by the button and the hotkey).</summary>
    public void ToggleAiming()
    {
        if (mode == Mode.Aiming) { CancelAiming(); return; }
        if (mode != Mode.Idle || !uiShown || GameState.Paused) return;
        if (controller.IsDead || controller.IsBusy) return;
        if (cooldownRemaining > 0f || ActiveThralls >= maxThralls)
        {
            flashUntil = Time.unscaledTime + 0.35f;   // the ring blinks red: not ready
            return;
        }
        mode = Mode.Aiming;
        controller.AbilityAiming = true;
        ShowAimVisuals(true);
    }

    void CancelAiming()
    {
        if (mode == Mode.Aiming) mode = Mode.Idle;
        controller.AbilityAiming = false;
        hovered = null;
        ShowAimVisuals(false);
    }

    // ------------------------------------------------------------------ aiming

void UpdateAiming(Mouse mouse)
    {
        controller.AbilityAiming = true;
        Vector3 center = transform.position;
        float groundY = center.y + 0.06f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
        float dt = Time.unscaledDeltaTime;

        // The range: two pools of creeping shadow turning against each other, a writhing dark ring, and dust drifting around its edge.
        Vector3 ground = new Vector3(center.x, groundY, center.z);
        disc.transform.position = ground;
        disc2.transform.position = ground + Vector3.up * 0.01f;
        disc.transform.localScale = new Vector3(range * 2f, range * 2f, 1f);
        disc2.transform.localScale = new Vector3(range * 1.9f, range * 1.9f, 1f);
        disc.transform.Rotate(Vector3.up, 7f * dt, Space.World);
        disc2.transform.Rotate(Vector3.up, -11f * dt, Space.World);
        float breathe = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 1.7f);
        discMaterial.color = new Color(1f, 1f, 1f, breathe);
        disc2Material.color = new Color(1f, 1f, 1f, 0.55f * breathe);
        SetWobblyCircle(rangeRing, center, range * 0.97f, groundY + 0.02f, RingPoints, Time.unscaledTime);
        float flicker = 0.55f + 0.45f * Mathf.PerlinNoise(Time.unscaledTime * 7f, 3.7f);
        rangeRing.startColor = rangeRing.endColor = new Color(shadowColor.r + 0.25f, shadowColor.g + 0.08f, shadowColor.b + 0.25f, flicker);
        rangeDust.transform.position = ground;
        innerDust.transform.position = ground;

        CollectCandidates();
        UpdateTargetRings(groundY, pulse);
        UpdateHover(mouse);

        if (hovered == null)
        {
            previewLine.enabled = false;
            return;
        }

        // The straight line of shadow from the vampire to the target.
        Vector3 a = center + Vector3.up * 1f, b = hovered.position + Vector3.up * 1f;
        previewLine.enabled = true;
        previewLine.SetPosition(0, a);
        previewLine.SetPosition(1, b);
        previewLine.widthMultiplier = 0.1f + 0.05f * pulse;
        for (int i = 0; i < 3; i++) EmitDust(Vector3.Lerp(a, b, Random.value) + Random.insideUnitSphere * 0.15f, 0.4f);

        if (mouse != null && mouse.leftButton.wasPressedThisFrame) StartCast(hovered);
    }

    void CollectCandidates()
    {
        candidates.Clear();
        Vector3 c = transform.position;
        foreach (HumanAI h in FindObjectsByType<HumanAI>(FindObjectsSortMode.None))
            if (h.CanBeBitten && FlatDistance(h.transform.position, c) <= range) candidates.Add(h.transform);
        foreach (PoliceAI p in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
            if (p.CanBeHit && FlatDistance(p.transform.position, c) <= range) candidates.Add(p.transform);
    }

    void UpdateTargetRings(float groundY, float pulse)
    {
        // Drop the rings of anyone who left the range or went down.
        staleRings.Clear();
        foreach (Transform t in targetRings.Keys)
            if (t == null || !candidates.Contains(t)) staleRings.Add(t);
        foreach (Transform t in staleRings)
        {
            if (targetRings[t] != null) Destroy(targetRings[t].gameObject);
            targetRings.Remove(t);
        }

        foreach (Transform t in candidates)
        {
            if (!targetRings.TryGetValue(t, out LineRenderer lr) || lr == null)
            {
                lr = MakeLine("ShadowTargetRing", TargetRingPoints, true, 0.07f);
                targetRings[t] = lr;
            }
            bool isHovered = t == hovered;
            float radius = isHovered ? 0.8f + 0.1f * pulse : 0.6f;
            SetCircle(lr, t.position, radius, groundY, TargetRingPoints);
            lr.widthMultiplier = isHovered ? 0.12f : 0.07f;
            Color c = isHovered ? Color.Lerp(accent, Color.white, 0.4f + 0.3f * pulse) : new Color(accent.r, accent.g, accent.b, 0.55f);
            lr.startColor = lr.endColor = c;
        }
    }

    void UpdateHover(Mouse mouse)
    {
        hovered = null;
        if (mouse == null || cam == null || PointerOverUI()) return;
        Vector2 m = mouse.position.ReadValue();
        float best = hoverRadiusPixels;
        foreach (Transform t in candidates)
        {
            Vector3 sp = cam.WorldToScreenPoint(t.position + Vector3.up * 1f);
            if (sp.z <= 0f) continue;
            float d = Vector2.Distance(m, new Vector2(sp.x, sp.y));
            if (d < best) { best = d; hovered = t; }
        }
    }

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ------------------------------------------------------------------ casting

    void StartCast(Transform target)
    {
        mode = Mode.Casting;
        controller.AbilityAiming = false;
        hovered = null;
        ShowAimVisuals(false);
        StartCoroutine(CastRoutine(target));
    }

    IEnumerator CastRoutine(Transform target)
    {
        controller.BeginAbility();   // no input and no damage while he is dust

        // The victim freezes in place while the vampire streaks toward him.
        HumanAI human = target.GetComponent<HumanAI>();
        PoliceAI police = target.GetComponent<PoliceAI>();
        if (human != null) human.FreezeInFear(6f);
        if (police != null) police.HoldForFight(6f);

        Vector3 start = transform.position;
        Vector3 dir = target.position - start;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
        Vector3 arrival = ChooseArrival(target, dir);
        transform.rotation = Quaternion.LookRotation(dir);

        // 1. He dissolves into black dust...
        if (sfx != null) sfx.PlayVanish();
        EmitBurst(start + Vector3.up * 0.9f, 45, 1f);
        SetVisible(false);

        // 2. ...which streaks along a straight line to the target.
        float t = 0f;
        Vector3 last = start;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, dashDuration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Vector3 p = Vector3.Lerp(start, arrival, k);
            MoveBody(p);
            EmitAlong(last + Vector3.up * 0.9f, p + Vector3.up * 0.9f);
            last = p;
            yield return null;
        }
        MoveBody(arrival);

        // 3. The dust gathers next to the target and he appears.
        Vector3 toVictim = target != null ? target.position - arrival : dir;
        toVictim.y = 0f;
        if (toVictim.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(toVictim);
        EmitBurst(arrival + Vector3.up * 0.9f, 55, 1.2f);
        if (sfx != null) sfx.PlayAppear();
        SetVisible(true);
        yield return new WaitForSeconds(0.12f);

        // 4. He bites. At the moment the fangs sink in the victim turns.
        bool valid = target != null && IsStillValid(target);
        if (valid)
        {
            Vector3 face = transform.position - target.position;
            face.y = 0f;
            if (face.sqrMagnitude > 0.001f) target.rotation = Quaternion.LookRotation(face);
        }
        animator.SetFloat("Speed", 0f);
        animator.ResetTrigger("Bite");
        animator.SetTrigger("Bite");
        if (sfx != null) sfx.PlayBite();

        bool converted = false;
        yield return WaitForBite(progress =>
        {
            if (!converted && progress >= controller.killAtNormalizedTime)
            {
                converted = true;
                Convert(target);
            }
        });
        if (!converted) Convert(target);

        SetVisible(true);
        controller.EndAbility();
        cooldownRemaining = cooldown;
        mode = Mode.Idle;
    }

    bool IsStillValid(Transform target)
    {
        HumanAI h = target.GetComponent<HumanAI>();
        if (h != null) return h.IsAlive && h.currentState != HumanAI.State.Captured && !h.IsThrall;
        PoliceAI p = target.GetComponent<PoliceAI>();
        return p != null && p.IsAlive && p.currentState != PoliceAI.State.Captured && !p.IsThrall;
    }

    void Convert(Transform target)
    {
        if (target == null || !IsStillValid(target)) return;
        EmitBurst(target.position + Vector3.up * 1f, 25, 0.8f);
        if (sfx != null) sfx.PlayFeed();
        ThrallAI.Convert(target.gameObject, transform);
    }

    /// <summary>Where he appears: next to the target on the side he came from (or the nearest free side).</summary>
    Vector3 ChooseArrival(Transform target, Vector3 dir)
    {
        float y = transform.position.y;
        float[] angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f };
        Vector3 first = default;
        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 spot = target.position + Quaternion.Euler(0f, angles[i], 0f) * (-dir) * arriveDistance;
            spot.y = y;
            if (i == 0) first = spot;
            if (IsFreeSpot(spot)) return spot;
        }
        return first;
    }

    bool IsFreeSpot(Vector3 p)
    {
        Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            if (c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<PoliceAI>() != null || c.GetComponentInParent<HumanAI>() != null) continue;   // people do not block him
            return false;
        }
        return true;
    }

    void MoveBody(Vector3 position)
    {
        bool wasEnabled = body.enabled;
        body.enabled = false;
        transform.position = position;
        body.enabled = wasEnabled;
        if (controller.capeCloth != null) controller.capeCloth.ClearTransformMotion();
    }

    void SetVisible(bool visible)
    {
        if (visible)
        {
            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            hiddenRenderers.Clear();
            if (controller.capeCloth != null) controller.capeCloth.ClearTransformMotion();
        }
        else
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                hiddenRenderers.Add(r);
                r.enabled = false;
            }
        }
    }

    /// <summary>Waits through the vampire's bite animation, reporting its progress (0-1 of the bite state).</summary>
    IEnumerator WaitForBite(System.Action<float> onProgress)
    {
        float waited = 0f;
        while (waited < 0.75f && !animator.GetCurrentAnimatorStateInfo(0).IsName("Bite"))
        {
            waited += Time.deltaTime;
            yield return null;
        }
        float safety = 8f;
        while (safety > 0f)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName("Bite")) break;
            onProgress?.Invoke(info.normalizedTime);
            if (info.normalizedTime >= 0.92f) break;
            safety -= Time.deltaTime;
            yield return null;
        }
        onProgress?.Invoke(1f);
    }

    // ------------------------------------------------------------------ black dust

    void BuildDust()
    {
        GameObject go = new GameObject("ShadowDust");
        dust = go.AddComponent<ParticleSystem>();
        var main = dust.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 2500;

        var emission = dust.emission;
        emission.enabled = false;
        var shape = dust.shape;
        shape.enabled = false;

        var colorOverLife = dust.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = fade;

        var sizeOverLife = dust.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.7f));

        var noise = dust.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.3f;

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        pr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = MakeBlobTexture(64) };
        dust.Play();
    }

    void EmitDust(Vector3 position, float sizeScale)
    {
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = position;
        ep.velocity = Random.insideUnitSphere * 0.5f + Vector3.up * 0.2f;
        ep.startSize = Random.Range(0.5f, 1.1f) * sizeScale;
        ep.startLifetime = Random.Range(0.7f, 1.4f);
        float v = Random.value;
        ep.startColor = Color.Lerp(new Color(0.01f, 0f, 0.03f, 0.95f), new Color(0.18f, 0.05f, 0.30f, 0.85f), v * v);
        ep.rotation = Random.Range(0f, 360f);
        dust.Emit(ep, 1);
    }

    void EmitBurst(Vector3 center, int count, float spread)
    {
        for (int i = 0; i < count; i++)
            EmitDust(center + Random.insideUnitSphere * 0.5f * spread, 1.1f);
    }

    /// <summary>A dense trail of dust between two points: the straight black line the vampire leaves behind.</summary>
    void EmitAlong(Vector3 from, Vector3 to)
    {
        int count = Mathf.CeilToInt(Vector3.Distance(from, to) * 16f) + 3;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, Random.value);
            p += new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.6f, 0.7f), Random.Range(-0.2f, 0.2f));
            EmitDust(p, 1f);
        }
    }

    // ------------------------------------------------------------------ aiming visuals

void BuildAimVisuals()
    {
        Shader shader = Shader.Find("Sprites/Default");
        lineMaterial = new Material(shader);

        // Two layers of creeping shadow that slowly turn against each other.
        disc = MakeShadowDisc("ShadowRange", shader, MakeShadowPoolTexture(384, 0f), out discMaterial);
        disc2 = MakeShadowDisc("ShadowRange2", shader, MakeShadowPoolTexture(384, 37f), out disc2Material);

        rangeRing = MakeLine("ShadowRangeRing", RingPoints, true, 0.12f);
        previewLine = MakeLine("ShadowPreviewLine", 2, false, 0.1f);
        previewLine.startColor = new Color(0.04f, 0.0f, 0.08f, 0.95f);
        previewLine.endColor = new Color(0.20f, 0.06f, 0.34f, 0.9f);

        // Dark dust swirling around the edge, and thin wisps rising from inside the pool.
        rangeDust = MakeAuraDust("ShadowRangeDust", 150f, 0.05f, 1.0f, 2.4f, 0.85f, 0.85f, 0.22f);
        innerDust = MakeAuraDust("ShadowInnerDust", 40f, 1f, 0.6f, 1.4f, 0.4f, 0.55f, 0f);
    }

    LineRenderer MakeLine(string lineName, int points, bool loop, float width)
    {
        GameObject go = new GameObject(lineName);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.positionCount = points;
        lr.widthMultiplier = width;
        lr.numCapVertices = 4;
        lr.material = lineMaterial;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    static void SetCircle(LineRenderer lr, Vector3 center, float radius, float y, int points)
    {
        for (int i = 0; i < points; i++)
        {
            float a = i / (float)points * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, y, center.z + Mathf.Sin(a) * radius));
        }
    }

void ShowAimVisuals(bool show)
    {
        if (disc != null) disc.SetActive(show);
        if (disc2 != null) disc2.SetActive(show);
        if (rangeRing != null) rangeRing.enabled = show;
        if (previewLine != null) previewLine.enabled = false;
        SetAuraEmitting(rangeDust, show);
        SetAuraEmitting(innerDust, show);
        if (!show)
        {
            foreach (LineRenderer lr in targetRings.Values) if (lr != null) Destroy(lr.gameObject);
            targetRings.Clear();
        }
    }

    // ------------------------------------------------------------------ UI

    void BuildUI()
    {
        uiRoot = new GameObject("ThrallAbilityUI");
        Canvas canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 11;
        CanvasScaler scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        uiRoot.AddComponent<GraphicRaycaster>();
        uiGroup = uiRoot.AddComponent<CanvasGroup>();
        uiGroup.alpha = 0f;

        Sprite disc256 = MakeCircleSprite(256, 0.5f, 0f);
        Sprite ring256 = MakeCircleSprite(256, 0.5f, 0.455f);

        // The round button, bottom right.
        GameObject button = NewUI("ShadowBiteButton", uiRoot.transform);
        RectTransform brt = button.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 0f);
        brt.anchoredPosition = new Vector2(-70f, 70f);
        brt.sizeDelta = new Vector2(124f, 124f);
        Image bg = button.AddComponent<Image>();
        bg.sprite = disc256;
        bg.color = new Color(0.05f, 0.04f, 0.09f, 0.92f);
        Button btn = button.AddComponent<Button>();
        btn.targetGraphic = bg;
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(ToggleAiming);

        ringImage = AddChildImage(button.transform, "Ring", ring256, accent, new Vector2(124f, 124f));
        AddChildImage(button.transform, "Icon", MakeIconSprite(128), Color.white, new Vector2(86f, 86f));
        cooldownImage = AddChildImage(button.transform, "Cooldown", disc256, new Color(0f, 0f, 0f, 0.7f), new Vector2(118f, 118f));
        cooldownImage.type = Image.Type.Filled;
        cooldownImage.fillMethod = Image.FillMethod.Radial360;
        cooldownImage.fillOrigin = (int)Image.Origin360.Top;
        cooldownImage.fillClockwise = true;
        cooldownImage.fillAmount = 0f;
        cooldownText = MakeText(button.transform, "CooldownText", "", 42, Color.white, TextAlignmentOptions.Center,
                                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 60f), 0f);

        // Key cap on the lower left of the button.
        GameObject cap = AddChildImage(button.transform, "KeyCap", disc256, new Color(0.10f, 0.08f, 0.16f, 1f), new Vector2(40f, 40f)).gameObject;
        RectTransform crt = cap.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0f, 0f);
        crt.anchoredPosition = new Vector2(12f, 12f);
        AddChildImage(cap.transform, "KeyCapRing", ring256, accent, new Vector2(40f, 40f));
        keyText = MakeText(cap.transform, "KeyText", hotkey.ToString(), 24, Color.white, TextAlignmentOptions.Center,
                           new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f), 0f);

        // How many thralls are alive, top right of the button.
        countText = MakeText(button.transform, "CountText", "0/" + maxThralls, 22, new Color(0.85f, 0.75f, 1f, 0.95f), TextAlignmentOptions.Center,
                             new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-8f, -8f), new Vector2(70f, 30f), 0f);

        // Name above the button.
        MakeText(uiRoot.transform, "Label", "SHADOW BITE", 22, new Color(0.82f, 0.72f, 1f, 0.9f), TextAlignmentOptions.Center,
                 new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(-132f, 204f), new Vector2(300f, 34f), 8f);

        // Hint while aiming.
        hintText = MakeText(uiRoot.transform, "Hint", "Choose a target     Left click: Shadow Bite     Right click / " + hotkey + ": cancel", 26,
                            new Color(1f, 1f, 1f, 0.92f), TextAlignmentOptions.Right,
                            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-70f, 246f), new Vector2(1000f, 40f), 0f);
        hintText.gameObject.SetActive(false);
    }

    void UpdateUI()
    {
        if (uiGroup == null) return;
        uiGroup.alpha = Mathf.MoveTowards(uiGroup.alpha, uiShown && !GameState.Paused ? 1f : 0f, Time.unscaledDeltaTime * 3f);

        bool aiming = mode == Mode.Aiming;
        bool onCooldown = cooldownRemaining > 0f;
        bool full = ActiveThralls >= maxThralls;
        cooldownImage.fillAmount = cooldown > 0f ? cooldownRemaining / cooldown : 0f;
        cooldownText.text = onCooldown ? Mathf.CeilToInt(cooldownRemaining).ToString() : "";
        countText.text = ActiveThralls + "/" + maxThralls;
        hintText.gameObject.SetActive(aiming);

        Color ring = accent;
        if (aiming) ring = Color.Lerp(accent, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
        else if (onCooldown || full || controller.IsDead) ring = new Color(0.4f, 0.38f, 0.45f, 1f);
        if (Time.unscaledTime < flashUntil) ring = new Color(1f, 0.2f, 0.25f, 1f);
        ringImage.color = ring;
    }

    static GameObject NewUI(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static Image AddChildImage(Transform parent, string objectName, Sprite sprite, Color color, Vector2 size)
    {
        GameObject go = NewUI(objectName, parent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text MakeText(Transform parent, string objectName, string text, float size, Color color, TextAlignmentOptions align,
                             Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 sizeDelta, float spacing)
    {
        GameObject go = NewUI(objectName, parent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = sizeDelta;
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = spacing;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    // ------------------------------------------------------------------ creepy range helpers

    /// <summary>The usual smoothstep: 0 below edge0, 1 above edge1, a smooth curve between. (Mathf.SmoothStep is something different.)</summary>
    static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }


    GameObject MakeShadowDisc(string objectName, Shader shader, Texture2D texture, out Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = objectName;
        Destroy(go.GetComponent<Collider>());
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lies flat on the ground
        material = new Material(shader) { mainTexture = texture };
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    /// <summary>Dark dust that rises and swirls around a circle of radius = range (a thin edge, or the whole disc when radiusThickness is 1).</summary>
    ParticleSystem MakeAuraDust(string objectName, float rate, float radiusThickness, float minSize, float maxSize, float darkAlpha, float violetAlpha, float orbital)
    {
        GameObject go = new GameObject(objectName);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.0f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.02f, 0f, 0.05f, darkAlpha), new Color(shadowColor.r * 1.5f, shadowColor.g * 1.8f, shadowColor.b * 1.45f, violetAlpha));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 700;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = range;
        shape.radiusThickness = radiusThickness;
        shape.arc = 360f;
        shape.rotation = new Vector3(90f, 0f, 0f);   // the circle lies flat

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.9f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.orbitalX = 0f;
        vel.orbitalY = orbital;   // drifts around the circle
        vel.orbitalZ = 0f;

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = fade;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.5f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.4f;
        noise.damping = true;

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        pr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = MakeBlobTexture(64) };
        ps.Play();
        return ps;
    }

    void SetAuraEmitting(ParticleSystem ps, bool on)
    {
        if (ps == null) return;
        var emission = ps.emission;
        emission.enabled = on;
        if (on)
        {
            var shape = ps.shape;
            shape.radius = range * 0.97f;
        }
    }

    /// <summary>A circle whose edge writhes like something is moving under the ground.</summary>
    static void SetWobblyCircle(LineRenderer lr, Vector3 center, float radius, float y, int points, float time)
    {
        for (int i = 0; i < points; i++)
        {
            float a = i / (float)points * Mathf.PI * 2f;
            float n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.7f + 10f + time * 0.5f, Mathf.Sin(a) * 1.7f + 10f - time * 0.35f);
            float n2 = Mathf.PerlinNoise(Mathf.Cos(a) * 5f + 40f - time * 1.3f, Mathf.Sin(a) * 5f + 40f);
            float r = radius * (1f + 0.04f * (n - 0.5f) * 2f + 0.015f * (n2 - 0.5f) * 2f);
            lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, y, center.z + Mathf.Sin(a) * r));
        }
    }

    /// <summary>A pool of near-black shadow with a ragged, tendril-like edge, mottled like smoke, and a faint violet glow along its rim.</summary>
    static Texture2D MakeShadowPoolTexture(int size, float seed)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        Color inner = new Color(0.03f, 0f, 0.06f);
        Color glow = new Color(0.55f, 0.14f, 0.85f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                // The edge creeps outward in tendrils (noise sampled round a circle, so it joins up seamlessly).
                float t1 = Mathf.PerlinNoise(seed + Mathf.Cos(ang) * 2.2f + 5f, seed + Mathf.Sin(ang) * 2.2f + 5f);
                float t2 = Mathf.PerlinNoise(seed + Mathf.Cos(ang) * 6f + 20f, seed + Mathf.Sin(ang) * 6f + 20f);
                float edge = 0.94f + 0.06f * (t1 * 0.7f + t2 * 0.3f);
                float cloud = Mathf.PerlinNoise(seed + x * 0.025f, seed + y * 0.025f) * 0.65f + Mathf.PerlinNoise(seed + x * 0.07f + 3f, seed + y * 0.07f + 3f) * 0.35f;
                float inside = 1f - Smooth(edge - 0.10f, edge, r);
                float a = inside * Mathf.Lerp(0.55f, 0.95f, cloud) * Mathf.Lerp(0.8f, 1f, r);
                float rim = Smooth(edge - 0.16f, edge - 0.03f, r) * (1f - Smooth(edge - 0.03f, edge, r));
                Color col = Color.Lerp(inner, glow, Mathf.Clamp01(rim * 0.9f + (cloud - 0.5f) * 0.12f));
                px[y * size + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    
// ------------------------------------------------------------------ generated textures

    static Texture2D NewTexture(int w, int h)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    /// <summary>A white disc (inner = 0) or ring (inner &gt; 0), with soft edges. outer and inner are fractions of the size.</summary>
    static Sprite MakeCircleSprite(int size, float outer, float inner)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f, ro = size * outer, ri = size * inner;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float a = Mathf.Clamp01(ro - d + 0.5f);
                if (inner > 0f) a *= Mathf.Clamp01(d - ri + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>The ability's icon: a drifting cloud of violet shadow with a pair of white fangs.</summary>
    static Sprite MakeIconSprite(int size)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float[,] blobs = { { 0.30f, 0.40f, 0.26f }, { 0.52f, 0.30f, 0.30f }, { 0.72f, 0.42f, 0.24f },
                           { 0.42f, 0.60f, 0.28f }, { 0.64f, 0.64f, 0.25f }, { 0.50f, 0.80f, 0.20f } };
        Vector2[] leftFang = { new Vector2(0.35f, 0.72f), new Vector2(0.48f, 0.72f), new Vector2(0.415f, 0.32f) };
        Vector2[] rightFang = { new Vector2(0.52f, 0.72f), new Vector2(0.65f, 0.72f), new Vector2(0.585f, 0.32f) };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float smoke = 0f;
                for (int i = 0; i < blobs.GetLength(0); i++)
                {
                    float d = Mathf.Sqrt((u - blobs[i, 0]) * (u - blobs[i, 0]) + (v - blobs[i, 1]) * (v - blobs[i, 1])) / blobs[i, 2];
                    smoke = Mathf.Max(smoke, 1f - Smooth(0.5f, 1f, d));
                }
                // Fangs, anti-aliased with 3x3 samples.
                int inside = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        Vector2 p = new Vector2((x + (sx + 0.5f) / 3f) / size, (y + (sy + 0.5f) / 3f) / size);
                        if (InTriangle(p, leftFang) || InTriangle(p, rightFang)) inside++;
                    }
                float fang = inside / 9f;
                Color smokeColor = Color.Lerp(new Color(0.18f, 0.06f, 0.36f), new Color(0.50f, 0.24f, 0.85f), v);
                Color c = Color.Lerp(smokeColor, Color.white, fang);
                float a = Mathf.Max(smoke * 0.92f, fang);
                px[y * size + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static bool InTriangle(Vector2 p, Vector2[] t)
    {
        float d1 = Sign(p, t[0], t[1]), d2 = Sign(p, t[1], t[2]), d3 = Sign(p, t[2], t[0]);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

    /// <summary>The soft fill of the range disc: faint in the middle, stronger toward the edge.</summary>
    static Texture2D MakeDiscTexture(int size)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / c;
                float a = r >= 1f ? 0f : Mathf.Lerp(0.10f, 0.34f, r * r * r) * Mathf.Clamp01((1f - r) * c);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>A soft round puff for the dust particles.</summary>
    static Texture2D MakeBlobTexture(int size)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / c;
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}

/// <summary>Adds the Shadow Bite ability to the vampire when the game starts, so no scene needs changing.</summary>
public class ThrallAbilityInstaller : MonoBehaviour
{
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ThrallAbilityInstaller>() != null) return;
        GameObject go = new GameObject("ThrallAbilityInstaller");
        go.AddComponent<ThrallAbilityInstaller>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1f;
        VampireController vampire = FindFirstObjectByType<VampireController>();
        if (vampire != null && vampire.GetComponent<ThrallAbility>() == null) vampire.gameObject.AddComponent<ThrallAbility>();
    }
}
