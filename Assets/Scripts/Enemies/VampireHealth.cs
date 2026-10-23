using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Health for the vampire in the Enemies_pathfinding scene. Police bullets call TakeDamage.
/// Draws a simple health bar on screen. When he dies he falls over; press R to get up at the start spot.
/// </summary>
public class VampireHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("After dying, pressing R brings the vampire back at the spot where the scene started.")]
    public bool allowRespawnWithR = true;

    [Header("Events")]
    public UnityEvent onDamaged = new UnityEvent();
    public UnityEvent onDeath = new UnityEvent();
    public UnityEvent onRespawn = new UnityEvent();

    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    CharacterController controller;
    Animator animator;
    PlayerMovement movement;
    VampireBite bite;
    Vector3 startPosition;
    Quaternion startRotation;
    float damageFlashTimer;
    bool collapsed;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int TurnHash = Animator.StringToHash("Turn");

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        movement = GetComponent<PlayerMovement>();
        bite = GetComponent<VampireBite>();
        CurrentHealth = maxHealth;
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    void Update()
    {
        if (damageFlashTimer > 0f) damageFlashTimer -= Time.deltaTime;

        if (IsDead && collapsed && allowRespawnWithR && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            Respawn();
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        damageFlashTimer = 0.2f;
        onDamaged.Invoke();
        if (CurrentHealth <= 0f) Die();
    }

    void Die()
    {
        IsDead = true;
        collapsed = false;
        if (movement != null) movement.enabled = false;
        if (bite != null) bite.enabled = false;
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.SetFloat(TurnHash, 0f);
        }
        onDeath.Invoke();
        StartCoroutine(Collapse());
    }

    // Placeholder until the vampire gets a death animation: tip over backwards and freeze.
    IEnumerator Collapse()
    {
        if (controller != null) controller.enabled = false;
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(-90f, 0f, 0f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.7f;
            float k = Mathf.Clamp01(t);
            k *= k;
            transform.rotation = Quaternion.Slerp(start, end, k);
            yield return null;
        }
        collapsed = true;
    }

    public void Respawn()
    {
        if (!IsDead) return;
        StopAllCoroutines();
        if (controller != null) controller.enabled = false;
        transform.SetPositionAndRotation(startPosition, startRotation);
        if (controller != null) controller.enabled = true;

        CurrentHealth = maxHealth;
        IsDead = false;
        collapsed = false;
        if (movement != null) movement.enabled = true;
        if (bite != null) bite.enabled = true;
        onRespawn.Invoke();
    }

    void OnGUI()
    {
        const float width = 240f, height = 22f;
        Rect back = new Rect(20f, 20f, width, height);
        Color old = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);

        float fraction = maxHealth > 0f ? CurrentHealth / maxHealth : 0f;
        GUI.color = damageFlashTimer > 0f ? Color.white : new Color(0.75f, 0.05f, 0.08f, 1f);
        GUI.DrawTexture(new Rect(back.x + 2f, back.y + 2f, (width - 4f) * fraction, height - 4f), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUIStyle small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        GUI.Label(new Rect(back.x, back.y + height + 2f, width, 24f), "Vampire health: " + Mathf.CeilToInt(CurrentHealth), small);

        if (IsDead && collapsed && allowRespawnWithR)
        {
            GUIStyle big = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 50f), "The vampire was shot!  Press R to get up again.", big);
        }
        GUI.color = old;
    }
}
