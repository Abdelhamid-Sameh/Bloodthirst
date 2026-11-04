using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Special ability "Shield".
///
/// Press the button in the ability triangle (or 2, costs 30 mana): an almost invisible bubble surrounds the vampire for a few seconds and he takes
/// no damage and does not flinch. The bubble lights up for a moment whenever something hits it (a bullet or a punch).
///
/// How it blocks damage: VampireBlood.TakeDamage, VampireHealth.TakeDamage and VampireController.OnHurt ask ShieldAbility first
/// (Absorb / Active), so no enemy script needs to know about the shield.
/// The component is added to the vampire automatically (ThrallAbilityInstaller), so nothing has to be set up in the scene.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VampireController))]
public class ShieldAbility : MonoBehaviour
{
    [Header("Ability")]
    [Tooltip("Key that raises the shield (the button at the bottom right does the same).")]
    public Key hotkey = Key.Digit2;
    [Tooltip("Seconds the shield lasts.")]
    public float duration = 10f;
    [Tooltip("Seconds before it can be used again (counted from when the shield ends).")]
    public float cooldown = 15f;
    [Tooltip("Mana it costs (VampireMana).")]
    public float manaCost = 30f;

    [Header("Look")]
    [Tooltip("Width of the bubble in metres.")]
    public float bubbleDiameter = 2.8f;
    [Tooltip("How visible the bubble is when nothing hits it. 0 = completely invisible.")]
    [Range(0f, 1f)] public float idleGlow = 0.35f;
    public Color bubbleColor = new Color(0.30f, 0.70f, 1f);
    [Tooltip("Colour of the honeycomb pattern on the bubble.")]
    public Color hexColor = new Color(0.65f, 0.92f, 1f);
    [Tooltip("Colour of the button.")]
    public Color accent = new Color(0.45f, 0.75f, 1f);

    static ShieldAbility instance;
    /// <summary>True while the shield is up.</summary>
    public static bool Active => instance != null && instance.timeLeft > 0f;

    /// <summary>Called by the damage code before it hurts the vampire. Returns true if the shield took the hit (and makes the bubble light up).</summary>
public static bool Absorb()
    {
        if (!Active) return false;
        instance.Hit();
        return true;
    }

    /// <summary>The bubble takes a hit (or has just appeared): it lights up and a shock ring spreads out.</summary>
    void Hit()
    {
        flash = 1f;
        ripple = 0f;
    }

    VampireController controller;
    Camera cam;
    float timeLeft, cooldownRemaining, flash, shown, flashUntil;
    bool uiShown;

    GameObject bubble, hexLayer, rippleLayer, groundRing;
    Material bubbleMaterial, hexMaterial, rippleMaterial, groundMaterial;
    ParticleSystem sparks;
    float ripple = 1f;   // 0 = a shock ring has just started spreading, 1 = finished

    GameObject uiRoot;
    CanvasGroup uiGroup;
    Image ringImage, cooldownImage;
    TMP_Text centerText, costText;
    Image iconImage;
    RectTransform buttonRect;
    Vector2 buttonRest;
    float shakeUntil;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        instance = this;
        controller = GetComponent<VampireController>();
        BuildBubble();
        BuildUI();
    }

void OnDestroy()
    {
        if (instance == this) instance = null;
        if (bubble != null) Destroy(bubble);
        if (hexLayer != null) Destroy(hexLayer);
        if (rippleLayer != null) Destroy(rippleLayer);
        if (groundRing != null) Destroy(groundRing);
        if (sparks != null) Destroy(sparks.gameObject);
        if (uiRoot != null) Destroy(uiRoot);
    }

    void Update()
    {
        if (!uiShown && !controller.IsBusy && !controller.IsDead && !IntroCutscene.Playing && Time.timeSinceLevelLoad > 2f) uiShown = true;

        if (!GameState.Paused)
        {
            float dt = Time.deltaTime;
            if (timeLeft > 0f)
            {
                timeLeft -= dt;
                if (timeLeft <= 0f) { timeLeft = 0f; cooldownRemaining = cooldown; }   // the shield is over: the cooldown starts
            }
            else
            {
                cooldownRemaining = Mathf.Max(0f, cooldownRemaining - dt);
            }
            flash = Mathf.MoveTowards(flash, 0f, 3f * dt);
        }
        if (controller.IsDead) timeLeft = 0f;   // the shield does not outlive him

        UpdateUI();

        if (IntroCutscene.Playing || GameState.Paused || controller.IsDead || !uiShown) return;
        Keyboard kb = Keyboard.current;
        if (kb != null && kb[hotkey].wasPressedThisFrame) Activate();
    }

    /// <summary>Raises the shield (called by the button and the hotkey).</summary>
    public void Activate()
    {
        if (controller.IsDead || !uiShown || GameState.Paused) return;
        if (timeLeft > 0f || cooldownRemaining > 0f)
        {
            flashUntil = Time.unscaledTime + 0.35f;   // the ring blinks red: not ready
            return;
        }
        if (!VampireMana.TrySpend(manaCost))
        {
            flashUntil = shakeUntil = Time.unscaledTime + 0.35f;   // red ring + shake; the mana wing blinks and a toast explains
            return;
        }
        timeLeft = duration;
        Hit();   // the bubble lights up as it appears...
        if (sparks != null) sparks.Emit(60);   // ...and bursts into sparks
    }

    // ------------------------------------------------------------------ the bubble

void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        shown = Mathf.MoveTowards(shown, timeLeft > 0f ? 1f : 0f, 5f * Time.unscaledDeltaTime);
        bool visible = shown > 0.001f;
        bubble.SetActive(visible);
        hexLayer.SetActive(visible);
        groundRing.SetActive(visible);
        var emission = sparks.emission;
        emission.enabled = visible && timeLeft > 0f;
        Vector3 center = transform.position + Vector3.up * 1f;
        sparks.transform.position = center;
        if (!visible) { rippleLayer.SetActive(false); return; }

        // Flat layers that always face the camera look like a sphere from any side. They are nudged toward the camera in turn so they never fight.
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        Vector3 toCamera = cam != null ? -cam.transform.forward : Vector3.up;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
        float blink = timeLeft > 0f && timeLeft < 1f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 20f) : 1f;   // flickers in the last second
        float size = bubbleDiameter * Mathf.Lerp(0.8f, 1f, shown) * (1f + 0.05f * flash + 0.015f * pulse);

        // The dome.
        bubble.transform.SetPositionAndRotation(center, face);
        bubble.transform.localScale = new Vector3(size, size, 1f);
        Color c = Color.Lerp(bubbleColor, Color.white, flash);
        bubbleMaterial.color = new Color(c.r, c.g, c.b, Mathf.Clamp01((idleGlow * (0.8f + 0.2f * pulse) + flash * 0.55f) * shown * blink));

        // The honeycomb: it turns slowly, and blazes up when the bubble is hit.
        hexLayer.transform.SetPositionAndRotation(center + toCamera * 0.02f, face * Quaternion.Euler(0f, 0f, Time.time * 14f));
        hexLayer.transform.localScale = new Vector3(size * 0.98f, size * 0.98f, 1f);
        Color h = Color.Lerp(hexColor, Color.white, flash);
        hexMaterial.color = new Color(h.r, h.g, h.b, Mathf.Clamp01((idleGlow * 1.3f + flash * 0.8f) * shown * blink));

        // A glowing ring on the ground under his feet.
        groundRing.transform.position = new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z);
        groundRing.transform.Rotate(Vector3.up, 25f * Time.deltaTime, Space.World);
        float groundSize = bubbleDiameter * 0.9f;
        groundRing.transform.localScale = new Vector3(groundSize, groundSize, 1f);
        groundMaterial.color = new Color(bubbleColor.r, bubbleColor.g, bubbleColor.b, Mathf.Clamp01((0.5f + 0.2f * pulse + flash * 0.3f) * shown * blink));

        // A shock ring that spreads outward on every hit.
        if (ripple < 1f)
        {
            ripple += Time.deltaTime / 0.45f;
            float rippleSize = bubbleDiameter * Mathf.Lerp(0.95f, 1.5f, ripple);
            rippleLayer.SetActive(true);
            rippleLayer.transform.SetPositionAndRotation(center + toCamera * 0.04f, face);
            rippleLayer.transform.localScale = new Vector3(rippleSize, rippleSize, 1f);
            rippleMaterial.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - ripple) * 0.9f);
        }
        else rippleLayer.SetActive(false);
    }

void BuildBubble()
    {
        Shader shader = Shader.Find("Sprites/Default");
        bubble = MakeLayer("ShieldBubble", shader, MakeBubbleTexture(256), 0, false, out bubbleMaterial);
        hexLayer = MakeLayer("ShieldHexagons", shader, MakeHexTexture(512), 1, false, out hexMaterial);
        rippleLayer = MakeLayer("ShieldRipple", shader, ThrallAbility.MakeCircleSprite(256, 0.5f, 0.46f).texture, 2, false, out rippleMaterial);
        groundRing = MakeLayer("ShieldGroundRing", shader, MakeGroundTexture(256), 0, true, out groundMaterial);
        sparks = BuildSparks();
    }

    /// <summary>A soft round bubble: almost clear in the middle, brighter toward the rim.</summary>
    internal static Texture2D MakeBubbleTexture(int size)
    {
        Texture2D tex = ThrallAbility.NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / c;
                float rim = ThrallAbility.Smooth(0.55f, 0.97f, r);
                float a = (0.12f + 0.88f * rim * rim) * (1f - ThrallAbility.Smooth(0.95f, 1f, r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // ------------------------------------------------------------------ the layers of the bubble

    /// <summary>One flat quad of the bubble: no collider (it must never block bullets or movement), drawn in a fixed order.</summary>
    GameObject MakeLayer(string layerName, Shader shader, Texture texture, int order, bool flat, out Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = layerName;
        Destroy(go.GetComponent<Collider>());
        if (flat) go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lies on the ground
        material = new Material(shader) { mainTexture = texture };
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.sortingOrder = order;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    /// <summary>Small bright sparks that circle over the surface of the bubble.</summary>
    ParticleSystem BuildSparks()
    {
        GameObject go = new GameObject("ShieldSparks");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.85f, 1f, 0.9f), new Color(1f, 1f, 1f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 30f;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = bubbleDiameter * 0.5f;
        shape.radiusThickness = 0f;   // only on the surface

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.orbitalX = 0f;
        vel.orbitalY = 1.2f;   // they circle around him
        vel.orbitalZ = 0f;

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = fade;

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        pr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = ThrallAbility.MakeBlobTexture(64) };
        ps.Play();
        return ps;
    }

    /// <summary>A honeycomb of thin glowing lines, faint in the middle and strong toward the rim.</summary>
    internal static Texture2D MakeHexTexture(int size)
    {
        Texture2D tex = ThrallAbility.NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f - c) / c, v = (y + 0.5f - c) / c;
                float r = Mathf.Sqrt(u * u + v * v);
                float edge = HexEdge(new Vector2(u, v) * 5.5f);   // about 5 hexagons from the middle to the rim
                float line = 1f - ThrallAbility.Smooth(0.03f, 0.10f, edge);
                float radial = ThrallAbility.Smooth(0.10f, 0.85f, r) * (1f - ThrallAbility.Smooth(0.94f, 1f, r));
                float a = (line * 0.95f + 0.06f) * radial;
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Distance to the nearest border of a hexagonal grid: 0 on a border, 0.5 in the middle of a hexagon.</summary>
    static float HexEdge(Vector2 p)
    {
        Vector2 size = new Vector2(1f, 1.73205f), half = size * 0.5f;
        Vector2 a = new Vector2(Wrap(p.x, size.x), Wrap(p.y, size.y)) - half;
        Vector2 b = new Vector2(Wrap(p.x - half.x, size.x), Wrap(p.y - half.y, size.y)) - half;
        Vector2 g = a.sqrMagnitude < b.sqrMagnitude ? a : b;
        g = new Vector2(Mathf.Abs(g.x), Mathf.Abs(g.y));
        float d = Mathf.Max(Vector2.Dot(g, new Vector2(0.5f, 0.866025f)), g.x);
        return 0.5f - d;
    }

    static float Wrap(float x, float m) => x - m * Mathf.Floor(x / m);

    /// <summary>The ring on the ground: a bright outer band, a thinner inner one and a faint glow between.</summary>
    static Texture2D MakeGroundTexture(int size)
    {
        Texture2D tex = ThrallAbility.NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / c;
                float outer = ThrallAbility.Smooth(0.82f, 0.90f, r) * (1f - ThrallAbility.Smooth(0.95f, 1f, r));
                float inner = ThrallAbility.Smooth(0.56f, 0.59f, r) * (1f - ThrallAbility.Smooth(0.61f, 0.64f, r)) * 0.6f;
                float glow = (1f - ThrallAbility.Smooth(0f, 0.9f, r)) * 0.18f;
                float a = Mathf.Clamp01(outer + inner + glow);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    
// ------------------------------------------------------------------ UI (the button sits just left of Shadow Bite)

    void BuildUI()
    {
        uiRoot = new GameObject("ShieldAbilityUI");
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

        Sprite disc256 = ThrallAbility.MakeCircleSprite(256, 0.5f, 0f);
        Sprite ring256 = ThrallAbility.MakeCircleSprite(256, 0.5f, 0.455f);

        // Bottom right of the ability triangle (AbilityHUD), same size as the other buttons.
        GameObject button = ThrallAbility.NewUI("ShieldButton", uiRoot.transform);
        RectTransform brt = button.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 0f);
        brt.anchoredPosition = buttonRest = AbilityHUD.SlotPosition(1);
        brt.sizeDelta = new Vector2(124f, 124f);
        brt.localScale = Vector3.one * AbilityHUD.ButtonScale;
        buttonRect = brt;
        Image bg = button.AddComponent<Image>();
        bg.sprite = disc256;
        bg.color = new Color(0.04f, 0.06f, 0.10f, 0.92f);
        Button btn = button.AddComponent<Button>();
        btn.targetGraphic = bg;
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(Activate);

        ringImage = ThrallAbility.AddChildImage(button.transform, "Ring", ring256, accent, new Vector2(124f, 124f));
        iconImage = ThrallAbility.AddChildImage(button.transform, "Icon", MakeShieldIcon(128), Color.white, new Vector2(80f, 80f));
        cooldownImage = ThrallAbility.AddChildImage(button.transform, "Cooldown", disc256, new Color(0f, 0f, 0f, 0.7f), new Vector2(118f, 118f));
        cooldownImage.type = Image.Type.Filled;
        cooldownImage.fillMethod = Image.FillMethod.Radial360;
        cooldownImage.fillOrigin = (int)Image.Origin360.Top;
        cooldownImage.fillClockwise = true;
        cooldownImage.fillAmount = 0f;
        centerText = ThrallAbility.MakeText(button.transform, "CenterText", "", 42, Color.white, TextAlignmentOptions.Center,
                                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 60f), 0f);

        // Key cap on the lower left of the button.
        GameObject cap = ThrallAbility.AddChildImage(button.transform, "KeyCap", disc256, new Color(0.08f, 0.10f, 0.16f, 1f), new Vector2(40f, 40f)).gameObject;
        RectTransform crt = cap.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0f, 0f);
        crt.anchoredPosition = new Vector2(12f, 12f);
        ThrallAbility.AddChildImage(cap.transform, "KeyCapRing", ring256, accent, new Vector2(40f, 40f));
        ThrallAbility.MakeText(cap.transform, "KeyText", AbilityHUD.KeyLabel(hotkey), 24, Color.white, TextAlignmentOptions.Center,
                               new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f), 0f);

        // Mana cost, top right (the name is in the controls guide).
        costText = AbilityHUD.AddCostBadge(button.transform, manaCost);
    }

    void UpdateUI()
    {
        if (uiGroup == null) return;
        uiGroup.alpha = Mathf.MoveTowards(uiGroup.alpha, uiShown && !GameState.Paused && !IntroCutscene.Playing ? 1f : 0f, Time.unscaledDeltaTime * 3f);

        bool active = timeLeft > 0f;
        bool onCooldown = cooldownRemaining > 0f;
        cooldownImage.fillAmount = onCooldown && cooldown > 0f ? cooldownRemaining / cooldown : 0f;
        if (active) { centerText.text = Mathf.CeilToInt(timeLeft).ToString(); centerText.color = new Color(0.75f, 0.9f, 1f, 1f); }   // seconds of shield left
        else if (onCooldown) { centerText.text = Mathf.CeilToInt(cooldownRemaining).ToString(); centerText.color = Color.white; }
        else centerText.text = "";

        bool affordable = active || VampireMana.CanAfford(manaCost);
        AbilityHUD.ShowAffordable(iconImage, costText, affordable);
        buttonRect.anchoredPosition = buttonRest + AbilityHUD.Shake(shakeUntil);

        Color ring = accent;
        if (active) ring = Color.Lerp(accent, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
        else if (onCooldown || !affordable || controller.IsDead) ring = new Color(0.4f, 0.38f, 0.45f, 1f);
        if (Time.unscaledTime < flashUntil) ring = new Color(1f, 0.2f, 0.25f, 1f);
        ringImage.color = ring;
    }

    // ------------------------------------------------------------------ icon

    /// <summary>A heater-shaped shield: light border, dark blue panel.</summary>
    static Sprite MakeShieldIcon(int size)
    {
        Texture2D tex = ThrallAbility.NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        Color border = new Color(0.78f, 0.90f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float outer = 0f, inner = 0f;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = (x + (sx + 0.5f) / 2f) / size, v = (y + (sy + 0.5f) / 2f) / size;
                        if (InShield(u, v, 1f)) outer += 0.25f;
                        if (InShield(u, v, 0.74f)) inner += 0.25f;
                    }
                Color panel = Color.Lerp(new Color(0.20f, 0.36f, 0.70f), new Color(0.10f, 0.18f, 0.42f), (float)y / size);
                Color c = Color.Lerp(border, panel, inner);
                px[y * size + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(outer * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>Is the point (u, v) inside a shield scaled by s? Flat top, sides curving in to a point at the bottom.</summary>
    internal static bool InShield(float u, float v, float s)
    {
        float x = (u - 0.5f) / s, y = (v - 0.52f) / s;
        if (y > 0.36f || y < -0.40f) return false;
        float halfWidth = y >= -0.05f ? 0.30f : 0.30f * Mathf.Sqrt((y + 0.40f) / 0.35f);
        return Mathf.Abs(x) <= halfWidth;
    }
}
