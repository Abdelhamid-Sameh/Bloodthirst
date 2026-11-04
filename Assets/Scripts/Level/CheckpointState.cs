using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Makes the coffin a real checkpoint, like in any other game.
///
/// When the vampire enters a coffin to rest, the state of the world is saved. When he dies and rises again, the screen fades
/// to black, the world is put back exactly as it was when he entered the coffin, and he rises from it.
/// (At the start of the level a first save is made, so dying before any coffin restores the level as it began.)
///
/// What is saved and put back:
///  - Every human and every officer: alive or dead, where he stood, which way he faced, and how hurt he was. Anyone killed since
///    comes back; anyone who had already died when the checkpoint was saved stays dead. Officers come back calm.
///  - The vampire's mana.
///  - Thralls made since the checkpoint are dismissed (they become ordinary again).
/// His blood is not touched: rising after death still gives a full reserve, as before.
///
/// The component is added to the vampire automatically (ThrallAbilityInstaller), so nothing has to be set up in the scene.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VampireController))]
public class CheckpointState : MonoBehaviour
{
    [Tooltip("Seconds the screen takes to fade to black before the world is put back.")]
    public float fadeOutTime = 0.25f;
    [Tooltip("Seconds the screen takes to fade back in.")]
    public float fadeInTime = 0.7f;
    [Tooltip("Put the vampire's mana back to what it was at the checkpoint.")]
    public bool restoreMana = true;

    /// <summary>A human as he was when the checkpoint was saved (and what his body looked like, so a corpse can be turned back).</summary>
    sealed class HumanSave
    {
        public HumanAI human;
        public Vector3 position;
        public Quaternion rotation;
        public float health;
        public Renderer[] renderers;
        public Material[][] materials;
        public ShadowCastingMode[] shadows;
        public Collider[] colliders;
        public bool[] collidersEnabled;
    }

    sealed class OfficerSave
    {
        public PoliceAI officer;
        public Vector3 position;
        public Quaternion rotation;
        public float health;
    }

    readonly List<HumanSave> humans = new List<HumanSave>();
    readonly List<OfficerSave> officers = new List<OfficerSave>();
    VampireController controller;
    CanvasGroup fade;
    GameObject fadeRoot;
    Coroutine restoring;
    float savedMana;

    /// <summary>True once something has been saved (the level start counts).</summary>
    public bool HasSnapshot { get; private set; }

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        controller = GetComponent<VampireController>();
        BuildFade();
    }

    void OnEnable()
    {
        controller.onEmerged.AddListener(OnEmerged);
        controller.onCheckpoint.AddListener(OnCheckpoint);
        controller.onRespawn.AddListener(OnRespawn);
    }

    void OnDisable()
    {
        controller.onEmerged.RemoveListener(OnEmerged);
        controller.onCheckpoint.RemoveListener(OnCheckpoint);
        controller.onRespawn.RemoveListener(OnRespawn);
    }

    void OnDestroy()
    {
        if (fadeRoot != null) Destroy(fadeRoot);
    }

    void Update()
    {
        // A level that does not open with him rising from a coffin still gets its first save.
        if (!HasSnapshot && Time.timeSinceLevelLoad > 3f && !controller.IsBusy && !controller.IsDead && !IntroCutscene.Playing) Capture();
    }

    void OnEmerged() { if (!HasSnapshot) Capture(); }                 // the level start
    void OnCheckpoint(CoffinCheckpoint coffin) { Capture(); }        // he enters a coffin to rest

    void OnRespawn()
    {
        if (!HasSnapshot) return;
        if (restoring != null) StopCoroutine(restoring);
        restoring = StartCoroutine(RestoreRoutine());
    }

    // ------------------------------------------------------------------ save

    /// <summary>Saves the world as it is now.</summary>
    public void Capture()
    {
        humans.Clear();
        officers.Clear();

        foreach (HumanAI h in FindObjectsByType<HumanAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!h.IsAlive || !h.gameObject.activeInHierarchy) continue;   // already dead at the checkpoint: he stays dead
            humans.Add(SaveHuman(h));
        }
        foreach (PoliceAI p in FindObjectsByType<PoliceAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!p.IsAlive || !p.gameObject.activeInHierarchy || p.currentState == PoliceAI.State.Captured) continue;
            officers.Add(new OfficerSave { officer = p, position = p.transform.position, rotation = p.transform.rotation, health = p.HealthRaw });
        }

        savedMana = VampireMana.Instance != null ? VampireMana.Instance.Current : -1f;
        HasSnapshot = true;
    }

    static HumanSave SaveHuman(HumanAI h)
    {
        HumanSave s = new HumanSave { human = h, position = h.transform.position, rotation = h.transform.rotation, health = h.HealthRaw };
        s.renderers = h.GetComponentsInChildren<Renderer>(true);
        s.materials = new Material[s.renderers.Length][];
        s.shadows = new ShadowCastingMode[s.renderers.Length];
        for (int i = 0; i < s.renderers.Length; i++)
        {
            s.materials[i] = s.renderers[i].sharedMaterials;
            s.shadows[i] = s.renderers[i].shadowCastingMode;
        }
        s.colliders = h.GetComponentsInChildren<Collider>(true);
        s.collidersEnabled = new bool[s.colliders.Length];
        for (int i = 0; i < s.colliders.Length; i++) s.collidersEnabled[i] = s.colliders[i].enabled;
        return s;
    }

    // ------------------------------------------------------------------ restore

    IEnumerator RestoreRoutine()
    {
        yield return FadeTo(1f, fadeOutTime);   // the screen goes black, so nobody visibly pops back
        Apply();
        yield return null;
        yield return FadeTo(0f, fadeInTime);
        restoring = null;
    }

    /// <summary>Puts the world back to the saved state.</summary>
    public void Apply()
    {
        foreach (HumanSave s in humans) RestoreHuman(s);
        foreach (OfficerSave s in officers) RestoreOfficer(s);

        // Any thrall still left (made since the checkpoint) becomes an ordinary character again.
        foreach (ThrallAI t in new List<ThrallAI>(ThrallAI.All)) if (t != null) t.Dismiss();

        if (restoreMana && savedMana >= 0f && VampireMana.Instance != null) VampireMana.Instance.Restore(savedMana);
    }

    static void RestoreHuman(HumanSave s)
    {
        HumanAI h = s.human;
        if (h == null) return;
        GameObject go = h.gameObject;
        bool wasGone = !h.IsAlive || !go.activeSelf;

        ThrallAI thrall = go.GetComponent<ThrallAI>();
        if (thrall != null) thrall.Dismiss();

        if (wasGone)
        {
            EntityHealthBar bar = go.GetComponent<EntityHealthBar>();
            if (bar != null) Destroy(bar);                            // the installer gives him a fresh health bar
            CorpseFade fadeOut = go.GetComponent<CorpseFade>();
            if (fadeOut != null) fadeOut.ResetFade();
            go.SetActive(true);

            // CorpseFade made his materials see-through and switched off his shadows and colliders: put them back.
            for (int i = 0; i < s.renderers.Length; i++)
            {
                Renderer r = s.renderers[i];
                if (r == null) continue;
                Material[] fading = r.sharedMaterials;
                r.sharedMaterials = s.materials[i];
                foreach (Material copy in fading)
                    if (copy != null && System.Array.IndexOf(s.materials[i], copy) < 0) Destroy(copy);   // the see-through copies
                r.shadowCastingMode = s.shadows[i];
            }
            for (int i = 0; i < s.colliders.Length; i++)
                if (s.colliders[i] != null) s.colliders[i].enabled = s.collidersEnabled[i];
        }

        h.RestoreFromCheckpoint(s.position, s.rotation, s.health);
    }

    static void RestoreOfficer(OfficerSave s)
    {
        PoliceAI p = s.officer;
        if (p == null) return;
        GameObject go = p.gameObject;
        bool wasGone = !p.IsAlive || !go.activeSelf;

        ThrallAI thrall = go.GetComponent<ThrallAI>();
        if (thrall != null) thrall.Dismiss();

        if (wasGone)
        {
            EntityHealthBar bar = go.GetComponent<EntityHealthBar>();
            if (bar != null) Destroy(bar);
            go.SetActive(true);
        }

        p.RestoreFromCheckpoint(s.position, s.rotation, s.health);
    }

    // ------------------------------------------------------------------ the black screen

    void BuildFade()
    {
        fadeRoot = new GameObject("CheckpointFade");
        Canvas canvas = fadeRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        fade = fadeRoot.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        fade.interactable = false;
        fade.blocksRaycasts = false;

        GameObject black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(fadeRoot.transform, false);
        RectTransform rt = black.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        Image img = black.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
    }

    IEnumerator FadeTo(float target, float seconds)
    {
        float from = fade.alpha, t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds);
            fade.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(t));
            yield return null;
        }
        fade.alpha = target;
    }
}
