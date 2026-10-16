using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A coffin the vampire rests in to save his progress (press F next to it).
/// The active coffin glows brighter and is where he respawns after dying.
/// </summary>
public class CoffinCheckpoint : MonoBehaviour
{
    /// <summary>Every coffin currently in the scene.</summary>
    public static readonly List<CoffinCheckpoint> All = new List<CoffinCheckpoint>();

    /// <summary>The last coffin the vampire used, or null.</summary>
    public static CoffinCheckpoint Current { get; private set; }

    [Tooltip("Where the vampire reappears after dying, relative to the coffin.")]
    public Vector3 respawnOffset = new Vector3(-1.3f, 0f, 0f);

    [Header("Glow")]
    public Light glow;
    public float idleGlowIntensity = 14f;
    public float activeGlowIntensity = 32f;
    public float activeGlowRange = 7.5f;

    float idleGlowRange;

    public bool IsActive => Current == this;

    public Vector3 RespawnPosition
    {
        get
        {
            Vector3 p = transform.TransformPoint(respawnOffset);
            p.y = transform.position.y;
            return p;
        }
    }

    public Quaternion RespawnRotation => Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up));

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (glow == null) glow = GetComponentInChildren<Light>();
        if (glow != null) idleGlowRange = glow.range;
    }

    void OnDisable()
    {
        All.Remove(this);
        if (Current == this) Current = null;
    }

    void Start() => RefreshGlow();

    /// <summary>Make this the active checkpoint.</summary>
    public void Activate()
    {
        CoffinCheckpoint previous = Current;
        Current = this;
        if (previous != null && previous != this) previous.RefreshGlow();
        RefreshGlow();
    }

    void RefreshGlow()
    {
        if (glow == null) return;
        glow.intensity = IsActive ? activeGlowIntensity : idleGlowIntensity;
        glow.range = IsActive ? activeGlowRange : idleGlowRange;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(RespawnPosition + Vector3.up * 0.9f, 0.4f);
    }
}
