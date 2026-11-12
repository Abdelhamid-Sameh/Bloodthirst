using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Level 2 (inside the police station): which part of the level the player is in.
/// Zones advance when the vampire walks into a StageTrigger, or when an encounter script calls Advance
/// (for example the corridor squad is beaten). Listen to onStageChanged to start your part of the level.
/// Blood does not drain in the station: the VampireBlood drain is 0 in this scene (the police serum, see the notes).
/// Debug (only while the vampire's debugKeys is on): F5..F8 jump to the start of zone 1..4.
/// </summary>
public class Level2Director : MonoBehaviour
{
    public enum Stage { Cell, HoldingRoom, Corridor, Arena, Outside, Ending }

    public static Level2Director Instance { get; private set; }

    public VampireController vampire;
    [Tooltip("Mana when the level starts: he wakes up in the cell exhausted.")]
    public float startMana = 0f;
    [Tooltip("Where each zone starts, in order: 1 cell block, 2 corridor, 3 arena, 4 outside.")]
    public Transform[] zoneStarts = new Transform[4];

    public Stage Current { get; private set; }
    public UnityEvent<Stage> onStageChanged = new UnityEvent<Stage>();

    void Awake()
    {
        Instance = this;
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    IEnumerator Start()
    {
        yield return null;   // VampireMana is added by ThrallAbilityInstaller when the game starts
        if (VampireMana.Instance != null) VampireMana.Instance.Restore(startMana);
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
        onStageChanged.Invoke(stage);
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || vampire == null || !vampire.debugKeys || GameState.Paused) return;
        if (kb.f5Key.wasPressedThisFrame) Jump(0, Stage.Cell);
        if (kb.f6Key.wasPressedThisFrame) Jump(1, Stage.Corridor);
        if (kb.f7Key.wasPressedThisFrame) Jump(2, Stage.Arena);
        if (kb.f8Key.wasPressedThisFrame) Jump(3, Stage.Outside);
    }

    void Jump(int zone, Stage stage)
    {
        if (zone >= zoneStarts.Length || zoneStarts[zone] == null || vampire.IsBusy || vampire.IsDead) return;
        var body = vampire.GetComponent<CharacterController>();
        body.enabled = false;   // a CharacterController overrides a direct position change
        vampire.transform.SetPositionAndRotation(zoneStarts[zone].position, zoneStarts[zone].rotation);
        body.enabled = true;
        var cam = FindFirstObjectByType<IsometricCamera>();
        if (cam != null) cam.SnapToTarget();
        SetStage(stage);
    }
}
