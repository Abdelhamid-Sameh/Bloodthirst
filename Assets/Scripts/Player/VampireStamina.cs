using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sprint stamina. Holding Shift spends it (about 3 s from full); it refills after a short pause.
/// Running it dry leaves him winded: no sprint until it is back to recoverFraction, and he walks a little slower meanwhile.
/// A starving vampire recovers slower (regen scales down while blood is under half) and a bite refills it at once,
/// so sprinting is a burst for chasing, escaping or crossing lamplight, not a way to run past the level.
/// Sprinting also makes the blood drain faster (VampireBlood.sprintDrainMultiplier).
/// A thin bar over his head shows it while it is being used or refilling. Added by VampireController.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VampireController))]
public class VampireStamina : MonoBehaviour
{
    public float maxStamina = 100f;
    [Tooltip("Stamina spent per second of sprinting.")]
    public float sprintCost = 30f;
    [Tooltip("Stamina regained per second once he stops sprinting.")]
    public float regenPerSecond = 22f;
    [Tooltip("Seconds after the last sprint before it starts to refill.")]
    public float regenDelay = 0.9f;
    [Tooltip("After running dry he cannot sprint again until stamina is back to this fraction.")]
    [Range(0f, 1f)] public float recoverFraction = 0.4f;
    [Tooltip("Walking speed multiplier while winded.")]
    [Range(0.3f, 1f)] public float windedSpeed = 0.8f;
    [Tooltip("Regen multiplier when his blood is empty; it rises to 1 at half blood. Hunger makes him weak.")]
    [Range(0f, 1f)] public float starvingRegen = 0.5f;

    public float Current { get; private set; }
    public float Fraction => maxStamina > 0f ? Current / maxStamina : 0f;
    public bool Winded { get; private set; }
    public bool CanSprint => !Winded && Current > 0f;
    public float SpeedMultiplier => Winded ? windedSpeed : 1f;

    VampireController controller;
    VampireBlood blood;
    CharacterController body;
    float lastSprint = -10f;

    // Bar over his head.
    RectTransform canvasRect, bar, fill;
    CanvasGroup group;
    Image fillImage;
    float visible;
    static readonly Color Normal = new Color(0.96f, 0.86f, 0.62f);
    static readonly Color Low = new Color(1f, 0.55f, 0.2f);
    static readonly Color Empty = new Color(1f, 0.18f, 0.16f);
    const float BarWidth = 104f;

    void Awake()
    {
        controller = GetComponent<VampireController>();
        blood = GetComponent<VampireBlood>();
        body = GetComponent<CharacterController>();
        Current = maxStamina;
        BuildBar();
    }

    void OnEnable()
    {
        controller.onBite.AddListener(OnBite);
        controller.onEmerged.AddListener(Refill);
    }

    void OnDisable()
    {
        controller.onBite.RemoveListener(OnBite);
        controller.onEmerged.RemoveListener(Refill);
    }

    void OnDestroy()
    {
        if (canvasRect != null) Destroy(canvasRect.gameObject);
    }

    void OnBite(GameObject victim) => Refill();

    public void Refill()
    {
        Current = maxStamina;
        Winded = false;
    }

    void Update()
    {
        if (GameState.Paused) return;
        float dt = Time.deltaTime;
        if (controller.IsSprinting)
        {
            Current = Mathf.Max(0f, Current - sprintCost * dt);
            lastSprint = Time.time;
            if (Current <= 0f) Winded = true;
        }
        else if (Time.time - lastSprint >= regenDelay)
        {
            float hunger = blood != null ? Mathf.Lerp(starvingRegen, 1f, Mathf.Clamp01(blood.Fraction / 0.5f)) : 1f;
            Current = Mathf.Min(maxStamina, Current + regenPerSecond * hunger * dt);
        }
        if (Winded && Current >= maxStamina * recoverFraction) Winded = false;
        UpdateBar();
    }

    // ------------------------------------------------------------------ bar

    void BuildBar()
    {
        var root = new GameObject("StaminaUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 8;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)root.transform;

        var b = new GameObject("Stamina", typeof(RectTransform));
        b.transform.SetParent(root.transform, false);
        bar = (RectTransform)b.transform;
        bar.sizeDelta = new Vector2(BarWidth + 6f, 12f);
        group = b.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = group.interactable = false;
        var back = b.AddComponent<Image>();
        back.sprite = AbilityHUD.Rounded();
        back.type = Image.Type.Sliced;
        back.pixelsPerUnitMultiplier = 2f;
        back.color = new Color(0.03f, 0.02f, 0.03f, 0.8f);
        back.raycastTarget = false;

        var f = new GameObject("Fill", typeof(RectTransform));
        f.transform.SetParent(b.transform, false);
        fill = (RectTransform)f.transform;
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = new Vector2(3f, 0f);
        fill.sizeDelta = new Vector2(BarWidth, 6f);
        fillImage = f.AddComponent<Image>();
        fillImage.sprite = AbilityHUD.Rounded();
        fillImage.type = Image.Type.Sliced;
        fillImage.pixelsPerUnitMultiplier = 4f;
        fillImage.raycastTarget = false;

        // A faint notch where sprinting unlocks again after running dry.
        var n = new GameObject("RecoverMark", typeof(RectTransform));
        n.transform.SetParent(b.transform, false);
        var nrt = (RectTransform)n.transform;
        nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 0.5f);
        nrt.anchoredPosition = new Vector2(3f + BarWidth * recoverFraction, 0f);
        nrt.sizeDelta = new Vector2(2f, 10f);
        var notch = n.AddComponent<Image>();
        notch.color = new Color(1f, 1f, 1f, 0.35f);
        notch.raycastTarget = false;
    }

    void UpdateBar()
    {
        Camera cam = Camera.main;
        bool hidden = cam == null || controller.IsDead || IntroCutscene.Playing;
        bool wanted = !hidden && (controller.IsSprinting || Current < maxStamina - 0.01f);
        // Linger briefly once full so it does not pop away.
        visible = wanted ? 1.2f : Mathf.Max(0f, visible - Time.unscaledDeltaTime);
        group.alpha = Mathf.MoveTowards(group.alpha, hidden ? 0f : Mathf.Clamp01(visible / 0.4f), Time.unscaledDeltaTime * 6f);
        if (group.alpha <= 0f || cam == null) return;

        // Above his head (his root is scaled, so use the character controller's real height).
        float height = body != null ? (body.center.y + body.height * 0.5f) * transform.lossyScale.y : 2f;
        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * (height + 0.35f));
        if (screen.z <= 0f) { group.alpha = 0f; return; }
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
        bar.anchoredPosition = local + new Vector2(0f, 14f);

        fill.sizeDelta = new Vector2(Mathf.Max(0f, BarWidth * Fraction), 6f);
        fillImage.color = Winded ? Color.Lerp(Empty, Low, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f)) : (Fraction < 0.3f ? Low : Normal);
    }
}
