using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A coffin the vampire rests in to save his progress (press F next to it).
/// Controls the sliding lid, knows where the vampire lies inside it, and where he reappears.
/// The active coffin glows brighter and is where he rises after dying.
/// </summary>
public class CoffinCheckpoint : MonoBehaviour
{
    public enum LidState { Ajar, Open, Closed }

    /// <summary>Every coffin currently in the scene.</summary>
    public static readonly List<CoffinCheckpoint> All = new List<CoffinCheckpoint>();

    /// <summary>The last coffin the vampire used, or null.</summary>
    public static CoffinCheckpoint Current { get; private set; }

    [Tooltip("Where the vampire reappears, relative to the coffin (he faces away from it).")]
    public Vector3 respawnOffset = new Vector3(-1.3f, 0f, 0f);

    [Tooltip("Where the vampire's feet stand inside the coffin before he lies down (coffin space). He faces the foot end.")]
    public Vector3 insideStandOffset = new Vector3(0f, 0.12f, 0.27f);

    [Header("Lid")]
    public Transform lid;
    public LidState startLid = LidState.Ajar;
    public Vector3 lidClosedPosition = new Vector3(0f, 0.49f, 0f);
    public Vector3 lidClosedEuler = Vector3.zero;
    public Vector3 lidAjarPosition = new Vector3(0.36f, 0.5f, -0.04f);
    public Vector3 lidAjarEuler = new Vector3(0f, 11f, -4f);
    public Vector3 lidOpenPosition = new Vector3(0.86f, 0.4f, -0.08f);
    public Vector3 lidOpenEuler = new Vector3(0f, 14f, -22f);

    [Header("Glow")]
    public Light glow;
    public float idleGlowIntensity = 14f;
    public float activeGlowIntensity = 32f;
    public float activeGlowRange = 7.5f;

    [Header("Sound (optional)")]
    public AudioSource audioSource;
    public AudioClip lidOpenClip;
    public AudioClip lidCloseClip;

    float idleGlowRange;
    Coroutine lidRoutine;

    public bool IsActive => Current == this;
    public LidState Lid { get; private set; }

    public Vector3 RespawnPosition
    {
        get
        {
            Vector3 p = transform.TransformPoint(respawnOffset);
            p.y = transform.position.y;
            return p;
        }
    }

    /// <summary>Faces away from the coffin.</summary>
    public Quaternion RespawnRotation
    {
        get
        {
            Vector3 away = Vector3.ProjectOnPlane(RespawnPosition - transform.position, Vector3.up);
            return away.sqrMagnitude > 0.001f ? Quaternion.LookRotation(away) : transform.rotation;
        }
    }

    /// <summary>Vampire root position inside the coffin, at the start of the lie-down animation.</summary>
    public Vector3 InsidePosition => transform.TransformPoint(insideStandOffset);

    /// <summary>Facing the foot end, so lying back puts his head at the head end.</summary>
    public Quaternion InsideRotation => Quaternion.LookRotation(Vector3.ProjectOnPlane(-transform.forward, Vector3.up));

    void Awake()
    {
        if (lid == null) lid = transform.Find("Lid");
        if (glow == null) glow = GetComponentInChildren<Light>();
        if (glow != null) idleGlowRange = glow.range;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        SetLidImmediate(startLid);
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
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

    // ---------------------------------------------------------------- lid

    public void SetLidImmediate(LidState state)
    {
        if (lid == null) return;
        GetLidPose(state, out Vector3 pos, out Quaternion rot);
        lid.SetLocalPositionAndRotation(pos, rot);
        Lid = state;
    }

    /// <summary>Slide the lid to a new state. Yield on the result to wait for it.</summary>
    public Coroutine MoveLid(LidState state, float duration)
    {
        if (lidRoutine != null) StopCoroutine(lidRoutine);
        lidRoutine = StartCoroutine(MoveLidRoutine(state, duration));
        return lidRoutine;
    }

    IEnumerator MoveLidRoutine(LidState state, float duration)
    {
        if (lid == null) yield break;
        AudioClip clip = state == LidState.Closed ? lidCloseClip : lidOpenClip;
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);

        Vector3 fromPos = lid.localPosition;
        Quaternion fromRot = lid.localRotation;
        GetLidPose(state, out Vector3 toPos, out Quaternion toRot);
        // Lift slightly while sliding so the lid clears the rim.
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Vector3 p = Vector3.Lerp(fromPos, toPos, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.06f;
            lid.SetLocalPositionAndRotation(p, Quaternion.Slerp(fromRot, toRot, k));
            yield return null;
        }
        lid.SetLocalPositionAndRotation(toPos, toRot);
        Lid = state;
        lidRoutine = null;
    }

    void GetLidPose(LidState state, out Vector3 pos, out Quaternion rot)
    {
        switch (state)
        {
            case LidState.Closed: pos = lidClosedPosition; rot = Quaternion.Euler(lidClosedEuler); break;
            case LidState.Open: pos = lidOpenPosition; rot = Quaternion.Euler(lidOpenEuler); break;
            default: pos = lidAjarPosition; rot = Quaternion.Euler(lidAjarEuler); break;
        }
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
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(InsidePosition, 0.15f);
        Gizmos.DrawRay(InsidePosition, InsideRotation * Vector3.forward * 0.6f);
    }
}
