using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A room checkpoint (Level 2 has no coffins: every room is a checkpoint, the courtyard has two).
/// The first time the vampire walks into its box (or a script calls Reach), it becomes the place he gets up after dying,
/// the world is saved (CheckpointState: every officer and civilian, his mana) and the level moves on to its stage.
/// When he dies he gets up here, the world is put back as it was when he arrived, the sunrise clock goes back
/// to the time it showed then, and onRespawnHere runs (reset the boss, re-arm a wave...).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RoomCheckpoint : MonoBehaviour
{
    public static RoomCheckpoint Current { get; private set; }

    public string roomName = "Room";
    public Level2Director.Stage stage;
    [Tooltip("Where he stands when he gets up here (this object if empty).")]
    public Transform spawnPoint;
    [Tooltip("Reached by walking into the box. Off for a checkpoint a script reaches (the boss checkpoint: when the waves are beaten).")]
    public bool reachOnEnter = true;
    [Tooltip("Runs once, when he first reaches it (close the doors behind him, start the sunrise clock).")]
    public UnityEvent onReached = new UnityEvent();
    [Tooltip("Runs every time he gets up here after dying (reset the boss, re-arm the waves).")]
    public UnityEvent onRespawnHere = new UnityEvent();

    public bool Reached { get; private set; }
    float sunriseRemaining = -1f;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        if (reachOnEnter && !Reached && other.GetComponent<VampireController>() != null) Reach();
    }

    /// <summary>Makes this the active checkpoint and saves the world.</summary>
    public void Reach()
    {
        VampireController v = Level2Director.Instance != null ? Level2Director.Instance.vampire : FindFirstObjectByType<VampireController>();
        if (v == null || v.IsDead) return;
        bool first = !Reached;
        Reached = true;
        Current = this;
        Transform p = spawnPoint != null ? spawnPoint : transform;
        v.SetRespawnPoint(p.position, p.rotation);
        if (Level2Director.Instance != null) Level2Director.Instance.Advance(stage);
        if (first) onReached.Invoke();   // first, so what it changes (doors, the clock) is part of the save

        CheckpointState state = v.GetComponent<CheckpointState>();
        if (state != null) state.Capture();
        SunriseClock clock = SunriseClock.Instance;
        sunriseRemaining = clock != null && clock.Running ? clock.Remaining : -1f;
        if (first) LevelToast.Show("Checkpoint", roomName);
    }

    /// <summary>Called by Level2Director when he gets up here after dying.</summary>
    public void OnRespawned()
    {
        if (sunriseRemaining >= 0f && SunriseClock.Instance != null) SunriseClock.Instance.RestoreForRespawn(sunriseRemaining);
        onRespawnHere.Invoke();
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;
        if (spawnPoint != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            Gizmos.DrawWireSphere(spawnPoint.position + Vector3.up, 0.5f);
            Gizmos.DrawRay(spawnPoint.position + Vector3.up, spawnPoint.forward * 1.5f);
        }
    }
}
