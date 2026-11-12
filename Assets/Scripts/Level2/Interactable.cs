using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Anything the vampire can use with F: a lever, a button, a document, a cell door.
/// Walk within range and the HUD shows "F  label"; pressing F runs onInteract (hook a Door.Open, a ReadableNote.Read, your own method).
/// </summary>
public class Interactable : MonoBehaviour
{
    public static readonly List<Interactable> All = new List<Interactable>();

    public string label = "Use";
    public float range = 1.8f;
    [Tooltip("Can only be used once (a lever that opens a door). Off for things you can use again (a document).")]
    public bool oneShot = true;
    public UnityEvent onInteract = new UnityEvent();

    public bool Used { get; private set; }

    static VampireController vampire;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>The closest usable interactable within reach of the vampire, or null.</summary>
    public static Interactable InReach(VampireController v)
    {
        if (v == null) return null;
        Interactable best = null;
        float bestDistance = float.MaxValue;
        Vector3 p = v.transform.position;
        foreach (Interactable i in All)
        {
            if (i.Used) continue;
            Vector3 d = i.transform.position - p;
            d.y = 0f;
            float distance = d.magnitude;
            if (distance <= i.range && distance < bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }

    void Update()
    {
        if (vampire == null) vampire = FindFirstObjectByType<VampireController>();
        Keyboard kb = Keyboard.current;
        if (kb == null || vampire == null || !kb.fKey.wasPressedThisFrame) return;
        if (GameState.Paused || vampire.IsBusy || vampire.IsDead || vampire.AbilityAiming) return;
        if (InReach(vampire) == this) Interact();
    }

    public void Interact()
    {
        if (Used) return;
        if (oneShot) Used = true;
        onInteract.Invoke();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
