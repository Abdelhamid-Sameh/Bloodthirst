using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Level 2: the vampire breaks out of the police station, wins the courtyard before sunrise, then takes the city.
/// Knows the current stage, gets him up at the last RoomCheckpoint after he dies, and moves him between areas with a fade.
/// Listen to onStageChanged to start your part; call Advance to move on; call BossDefeated when the armored officer falls.
/// Blood does not drain in Level 2 (VampireBlood drain is 0 in this scene: the police serum). Damage and bites still count.
/// Debug (vampire's debugKeys on): F5..F10 jump to each checkpoint in order, F11 = the boss is beaten.
/// </summary>
public class Level2Director : MonoBehaviour
{
    public enum Stage { Cell, HoldingRoom, Corridor, Courtyard, Boss, Streets, Desert, Ending }

    public static Level2Director Instance { get; private set; }

    public VampireController vampire;
    [Tooltip("Mana when the level starts: he wakes up in the cell exhausted.")]
    public float startMana = 0f;
    [Tooltip("Seconds after dying before he gets up at the last checkpoint.")]
    public float respawnDelay = 3f;
    [Tooltip("The checkpoints in level order (debug keys jump to them).")]
    public RoomCheckpoint[] checkpoints = new RoomCheckpoint[0];

    /// <summary>How close the camera sits and how eagerly it hides things that block the view.</summary>
    [System.Serializable]
    public class Framing
    {
        [Tooltip("Hide things that block the view. Off indoors: the walls facing the camera are built low instead, so nothing ever vanishes.")]
        public bool cutaway = false;
        public float distance = 22f, minDistance = 18f, maxDistance = 30f;
        [Tooltip("Width of the line of sight that hides walls in the way (smaller = only what really covers him).")]
        public float cutawayRadius = 0.5f;
        [Tooltip("Props (not walls) are hidden only if at least this tall. High = crates, bars and doors never vanish.")]
        public float minPropHeight = 3.5f;
    }

    [Header("Camera per area")]
    [Tooltip("Cells and corridor: close, the building feels shut in.")]
    public Framing indoors = new Framing();
    [Tooltip("The courtyard and the boss fight.")]
    public Framing courtyard = new Framing { distance = 30f, minDistance = 22f, maxDistance = 40f, cutawayRadius = 0.6f, minPropHeight = 3.5f };
    [Tooltip("The city and the desert (Level 1's framing).")]
    public Framing outside = new Framing { cutaway = true, distance = 38f, minDistance = 24f, maxDistance = 56f, cutawayRadius = 1.5f, minPropHeight = 1.8f };

    public Stage Current { get; private set; }
    public UnityEvent<Stage> onStageChanged = new UnityEvent<Stage>();
    [Tooltip("The armored officer is down: stop the sunrise clock, open the gate to the streets.")]
    public UnityEvent onBossDefeated = new UnityEvent();

    CanvasGroup fade;
    bool travelling;

    void Awake()
    {
        Instance = this;
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        BuildFade();
    }

    void OnEnable()
    {
        vampire.onDeath.AddListener(OnDeath);
        vampire.onRespawn.AddListener(OnRespawn);
    }

    void OnDisable()
    {
        vampire.onDeath.RemoveListener(OnDeath);
        vampire.onRespawn.RemoveListener(OnRespawn);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    IEnumerator Start()
    {
        yield return null;   // VampireMana is added by ThrallAbilityInstaller when the game starts
        if (VampireMana.Instance != null) VampireMana.Instance.Restore(startMana);
        if (checkpoints.Length > 0 && checkpoints[0] != null) checkpoints[0].Reach();
        ApplyFraming(Current);
        var cam = FindFirstObjectByType<IsometricCamera>();
        if (cam != null) cam.SnapToTarget();   // start already framed close, no zoom-in at the start
        onStageChanged.Invoke(Current);
    }

    /// <summary>Moves the level forward to this stage (never back).</summary>
    public void Advance(Stage stage)
    {
        if (stage <= Current) return;
        SetStage(stage);
    }

    void SetStage(Stage stage)
    {
        Current = stage;
        Debug.Log("[Level2] Stage: " + stage);
        ApplyFraming(stage);
        onStageChanged.Invoke(stage);
    }

    void ApplyFraming(Stage stage)
    {
        var cam = FindFirstObjectByType<IsometricCamera>();
        if (cam == null) return;
        Framing f = stage <= Stage.Corridor ? indoors : stage <= Stage.Boss ? courtyard : outside;
        cam.minDistance = f.minDistance;
        cam.maxDistance = f.maxDistance;
        cam.distance = f.distance;   // the camera eases to it (zoomSmoothTime)
        cam.cutawayRadius = f.cutawayRadius;
        cam.minPropHeight = f.minPropHeight;
        if (cam.cutaway && !f.cutaway) { cam.enabled = false; cam.enabled = true; }   // OnDisable shows again whatever it had hidden
        cam.cutaway = f.cutaway;
    }

    public void BossDefeated()
    {
        if (Current >= Stage.Streets) return;
        LevelToast.Show("The armored officer falls", "The gate is open");
        onBossDefeated.Invoke();
    }

    // ------------------------------------------------------------------ death and moving between areas

    void OnDeath() => StartCoroutine(GetUpLater());

    IEnumerator GetUpLater()
    {
        yield return new WaitForSeconds(respawnDelay);
        if (vampire.IsDead) vampire.Respawn();   // R does the same sooner (debug keys)
    }

    void OnRespawn()
    {
        if (RoomCheckpoint.Current != null) RoomCheckpoint.Current.OnRespawned();
    }

    /// <summary>Fades to black, moves him to the checkpoint and reaches it (the courtyard gate to the streets uses this).</summary>
    public void TravelTo(RoomCheckpoint checkpoint)
    {
        if (!travelling && checkpoint != null) StartCoroutine(Travel(checkpoint));
    }

    IEnumerator Travel(RoomCheckpoint checkpoint)
    {
        travelling = true;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.6f) { fade.alpha = t; yield return null; }
        fade.alpha = 1f;
        Place(checkpoint);
        checkpoint.Reach();
        yield return new WaitForSecondsRealtime(0.3f);
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.8f) { fade.alpha = t; yield return null; }
        fade.alpha = 0f;
        travelling = false;
    }

    void Place(RoomCheckpoint checkpoint)
    {
        Transform to = checkpoint.spawnPoint != null ? checkpoint.spawnPoint : checkpoint.transform;
        var body = vampire.GetComponent<CharacterController>();
        body.enabled = false;   // a CharacterController overrides a direct position change
        vampire.transform.SetPositionAndRotation(to.position, to.rotation);
        body.enabled = true;
        var cam = FindFirstObjectByType<IsometricCamera>();
        if (cam != null) cam.SnapToTarget();
    }

    // ------------------------------------------------------------------ debug

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || vampire == null || !vampire.debugKeys || GameState.Paused || vampire.IsBusy || vampire.IsDead) return;
        Key[] keys = { Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10 };
        for (int i = 0; i < keys.Length && i < checkpoints.Length; i++)
        {
            if (!kb[keys[i]].wasPressedThisFrame || checkpoints[i] == null) continue;
            Place(checkpoints[i]);
            checkpoints[i].Reach();
            SetStage(checkpoints[i].stage);
        }
        if (kb.f11Key.wasPressedThisFrame) BossDefeated();
    }

    void BuildFade()
    {
        var root = new GameObject("Level2Fade", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;
        fade = root.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        fade.blocksRaycasts = fade.interactable = false;
        var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(root.transform, false);
        var rt = (RectTransform)black.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = black.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
    }
}
