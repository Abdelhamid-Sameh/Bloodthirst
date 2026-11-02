using System;
using UnityEngine;

/// <summary>
/// The vampire's mana for his three abilities (Blood Leap 20, Shield 30, Shadow Bite 50).
/// He starts with a full pool and mana only comes back by fighting: every blow that lands on a human or an officer
/// gives a little (2.5 by default, so a full 4-blow combo gives 10). Biting, resting in a coffin or waiting give none,
/// so the abilities stay a reward for getting into fights rather than something to spam.
/// Added to the vampire automatically (ThrallAbilityInstaller); the mana wing (ManaBarUI) shows it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VampireController))]
public class VampireMana : MonoBehaviour
{
    public float maxMana = 100f;
    [Tooltip("Mana gained for each blow that lands on a human or an officer.")]
    public float manaPerHit = 2.5f;

    public float Current { get; private set; }
    public float Fraction => maxMana > 0f ? Current / maxMana : 0f;

    /// <summary>The vampire's mana (null before he exists).</summary>
    public static VampireMana Instance { get; private set; }

    /// <summary>Raised when an ability is tried without enough mana (argument: the cost).</summary>
    public static event Action<float> NotEnough;

    VampireController controller;

    void Awake()
    {
        Instance = this;
        controller = GetComponent<VampireController>();
        Current = maxMana;
    }

    void OnEnable() { controller.onHit.AddListener(OnHit); }
    void OnDisable() { controller.onHit.RemoveListener(OnHit); }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnHit(GameObject victim)
    {
        if (victim == null || controller.IsDead) return;
        Current = Mathf.Min(maxMana, Current + manaPerHit);
    }

    public static bool CanAfford(float cost) => Instance == null || Instance.Current >= cost - 0.001f;

    /// <summary>Spends the cost if there is enough mana; otherwise raises NotEnough and returns false.</summary>
    public static bool TrySpend(float cost)
    {
        if (Instance == null) return true;   // no mana system in this scene: abilities are free
        if (!CanAfford(cost)) { NotEnough?.Invoke(cost); return false; }
        Instance.Current = Mathf.Max(0f, Instance.Current - cost);
        return true;
    }

    /// <summary>Tells the UI an ability was refused for lack of mana (nothing is spent).</summary>
    public static void ReportNotEnough(float cost) => NotEnough?.Invoke(cost);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; NotEnough = null; }
}
