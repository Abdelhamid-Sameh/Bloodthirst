using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Special ability "Blood Leap": the same targeting as Shadow Bite (press G or the button, a ring shows the reach,
/// pick a human with the mouse, left-click), but the vampire lands next to the human and drinks him dry instead of
/// turning him. A fast way to feed from a distance. Humans only; added to the vampire automatically like Shadow Bite.
/// </summary>
[DisallowMultipleComponent]
public class BloodLeapAbility : ThrallAbility
{
    protected override void Awake()
    {
        hotkey = Key.G;
        range = 8f;
        cooldown = 10f;
        accent = new Color(1f, 0.22f, 0.2f);
        shadowColor = new Color(0.45f, 0.03f, 0.05f);
        base.Awake();
    }

    protected override string Title => "BLOOD LEAP";
    protected override float ButtonOffsetX => -230f;   // left of the Shadow Bite button
    protected override Color IconTint => new Color(1f, 0.45f, 0.45f);
    protected override Color PoolGlow => new Color(0.85f, 0.08f, 0.10f);
    protected override Color DustColor => new Color(0.35f, 0.02f, 0.04f, 0.85f);
    protected override bool TargetsOfficers => false;
    protected override bool AtCapacity => false;
    protected override string CountLabel => "";

    protected override IEnumerator BiteOnArrival(Transform target)
    {
        HumanAI human = target != null ? target.GetComponent<HumanAI>() : null;
        if (human == null || !human.CanBeBitten) yield break;   // he died or was taken meanwhile: the leap still lands
        yield return Controller.BiteHuman(human);
    }
}
