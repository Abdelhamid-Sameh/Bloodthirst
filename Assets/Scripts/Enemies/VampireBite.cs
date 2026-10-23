using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the vampire bite police officers in the Enemies_pathfinding scene.
/// Press E (or left mouse click) while an officer is within reach in front of him.
/// The officer is grabbed, held in front of him for a moment, and then dies.
/// </summary>
public class VampireBite : MonoBehaviour
{
    [Header("Bite")]
    public float biteRange = 2.2f;
    [Tooltip("How wide the bite cone in front of him is (degrees).")]
    [Range(0f, 360f)] public float biteAngle = 180f;
    [Tooltip("Distance in front of the vampire where the officer is held.")]
    public float victimHoldDistance = 0.75f;
    [Tooltip("Seconds between grabbing the officer and the officer dying.")]
    public float biteDuration = 1.2f;

    /// <summary>The officer he would bite if E were pressed now, or null.</summary>
    public PoliceAI TargetInReach { get; private set; }
    public bool IsBiting { get; private set; }

    PlayerMovement movement;
    Animator animator;
    VampireHealth health;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int TurnHash = Animator.StringToHash("Turn");

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        animator = GetComponent<Animator>();
        health = GetComponent<VampireHealth>();
    }

    void OnDisable()
    {
        TargetInReach = null;
    }

    void Update()
    {
        if (IsBiting || (health != null && health.IsDead))
        {
            TargetInReach = null;
            return;
        }

        TargetInReach = FindVictim();

        bool pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                       || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (pressed && TargetInReach != null)
            StartCoroutine(BiteRoutine(TargetInReach));
    }

    PoliceAI FindVictim()
    {
        PoliceAI best = null;
        float bestDistance = biteRange;
        foreach (PoliceAI officer in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
        {
            if (!officer.CanBeBitten) continue;
            Vector3 to = Vector3.ProjectOnPlane(officer.transform.position - transform.position, Vector3.up);
            float distance = to.magnitude;
            if (distance > bestDistance) continue;
            if (distance > 0.2f && Vector3.Angle(transform.forward, to) > biteAngle * 0.5f) continue;
            best = officer;
            bestDistance = distance;
        }
        return best;
    }

    IEnumerator BiteRoutine(PoliceAI victim)
    {
        IsBiting = true;
        if (movement != null) movement.enabled = false;
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.SetFloat(TurnHash, 0f);
        }

        // Turn toward the officer, grab him and hold him in front of the vampire.
        Vector3 to = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up);
        if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
        victim.OnCaptured(transform);
        Vector3 hold = transform.position + transform.forward * victimHoldDistance;
        hold.y = victim.transform.position.y;
        victim.transform.position = hold;

        float elapsed = 0f;
        while (elapsed < biteDuration)
        {
            if (health != null && health.IsDead)
            {
                // The vampire was shot dead mid-bite: the officer is let go.
                victim.OnReleased();
                IsBiting = false;
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        victim.Die();
        if (movement != null) movement.enabled = true;
        IsBiting = false;
    }

    void OnGUI()
    {
        if (TargetInReach == null || IsBiting) return;
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0f, Screen.height - 90f, Screen.width, 40f), "Press E to bite", style);
    }
}
