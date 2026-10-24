using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// The vampire's health is his blood. It drains while he goes without feeding, biting refills it,
/// and at zero he dies and rises again from his last coffin with a full reserve.
/// Draining only runs once he is out of the coffin, and pauses during bites and coffin sequences.
/// </summary>
[RequireComponent(typeof(VampireController))]
public class VampireBlood : MonoBehaviour
{
    public float maxBlood = 100f;
    [Tooltip("Blood lost per second without feeding. 1.2 = about 83 seconds from full to empty.")]
    public float drainPerSecond = 1.2f;
    [Tooltip("Blood gained from biting a civilian.")]
    public float humanBlood = 30f;
    [Tooltip("Blood gained from biting a police officer (riskier, so worth more).")]
    public float policeBlood = 45f;
    [Tooltip("Below this fraction the bar starts its heartbeat warning.")]
    [Range(0f, 1f)] public float lowThreshold = 0.25f;

    public float Current { get; private set; }
    public float Fraction => maxBlood > 0f ? Current / maxBlood : 0f;
    public bool IsLow => Fraction <= lowThreshold;
    /// <summary>True while the clock is running: he is out of the coffin and alive.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Blood gained by feeding (amount).</summary>
    public event Action<float> Fed;
    /// <summary>Blood lost to a hit (amount). Draining over time does not fire this.</summary>
    public event Action<float> Damaged;
    /// <summary>He is out of the coffin with a full reserve (the bar's wing unfolds).</summary>
    public event Action Activated;
    /// <summary>He died (the bar's wing folds).</summary>
    public event Action Deactivated;

    VampireController controller;

    void Awake()
    {
        controller = GetComponent<VampireController>();
        Current = maxBlood;
    }

    void OnEnable()
    {
        controller.onEmerged.AddListener(OnEmerged);
        controller.onBite.AddListener(OnBite);
        controller.onDeath.AddListener(OnDeath);
    }

    void OnDisable()
    {
        controller.onEmerged.RemoveListener(OnEmerged);
        controller.onBite.RemoveListener(OnBite);
        controller.onDeath.RemoveListener(OnDeath);
    }

    IEnumerator Start()
    {
        // If the level does not open with him rising from a coffin, start the clock straight away.
        yield return null;
        if (!IsActive && !controller.IsBusy && !controller.IsDead) OnEmerged();
    }

    void Update()
    {
        if (!IsActive || controller.IsDead || controller.IsBusy) return;
        Current = Mathf.Max(0f, Current - drainPerSecond * Time.deltaTime);
        if (Current <= 0f) controller.Die();
    }

    public void Feed(float amount)
    {
        if (amount <= 0f || controller.IsDead) return;
        Current = Mathf.Min(maxBlood, Current + amount);
        Fed?.Invoke(amount);
    }

    /// <summary>Call from anything that hurts him (police bullets, hazards).</summary>
    public void TakeDamage(float amount)
    {
        if (!IsActive || controller.IsDead || amount <= 0f) return;
        Current = Mathf.Max(0f, Current - amount);
        Damaged?.Invoke(amount);
        if (Current <= 0f) controller.Die();
    }

    void OnEmerged()
    {
        Current = maxBlood;
        IsActive = true;
        Activated?.Invoke();
    }

    void OnBite(GameObject victim)
    {
        if (victim == null) return;
        Feed(victim.GetComponent<PoliceAI>() != null ? policeBlood : humanBlood);
    }

    void OnDeath()
    {
        IsActive = false;
        Current = 0f;
        Deactivated?.Invoke();
    }

    [ContextMenu("Debug: take 20 damage")]
    void DebugDamage() => TakeDamage(20f);
}
