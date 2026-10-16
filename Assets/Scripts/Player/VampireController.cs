using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Gameplay controls for the vampire in the isometric main scene.
///
/// WASD / arrows : run, relative to the camera (W = up the screen)
/// Mouse         : when standing still he turns on the spot to face the cursor
/// Space         : jump
/// E / left click: bite the nearest human in front of him (bites the air if nobody is close)
/// F             : rest in a nearby coffin (sets the checkpoint)
/// K / R         : debug only - die / respawn at the last coffin
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class VampireController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    [Tooltip("How fast he turns to face where he is running (degrees per second).")]
    public float runTurnSpeed = 720f;
    [Tooltip("How fast he turns on the spot toward the mouse (degrees per second).")]
    public float idleTurnSpeed = 200f;
    [Tooltip("How far the cursor must be off his facing before he turns on the spot (degrees).")]
    public float idleTurnThreshold = 25f;
    public bool faceMouseWhenIdle = true;
    [Tooltip("Camera that defines up/down/left/right on screen. Defaults to the main camera.")]
    public Camera viewCamera;

    [Header("Jumping & Gravity")]
    public float jumpHeight = 2f;
    public float gravity = -19.62f;
    public float groundedGraceTime = 0.15f;

    [Header("Bite")]
    public float biteRange = 2.2f;
    [Range(0, 360)] public float biteAngle = 120f;
    [Tooltip("Distance in front of him where the victim is held during the bite.")]
    public float victimHoldDistance = 0.75f;
    [Tooltip("Point in the bite animation (0-1) at which the victim dies.")]
    [Range(0, 1)] public float killAtNormalizedTime = 0.7f;

    [Header("Coffin checkpoints")]
    public float coffinRange = 2.8f;

    [Header("Cape")]
    public Cloth capeCloth;
    public Vector3 jumpWindForce = new Vector3(0f, 5f, -2f);

    [Header("Debug")]
    [Tooltip("K kills the vampire and R respawns him at the last coffin, for testing the death animation.")]
    public bool debugKeys = true;

    [Header("Events - hook health, UI and sound here")]
    [Tooltip("Fires when a bite finishes. The argument is the victim, or null if he bit the air.")]
    public UnityEvent<GameObject> onBite = new UnityEvent<GameObject>();
    public UnityEvent<CoffinCheckpoint> onCheckpoint = new UnityEvent<CoffinCheckpoint>();
    public UnityEvent onDeath = new UnityEvent();
    public UnityEvent onRespawn = new UnityEvent();

    public bool IsDead { get; private set; }
    /// <summary>True while an action animation (bite, coffin) is playing and movement is locked.</summary>
    public bool IsBusy { get; private set; }

    // Animator state names and parameters (VampireGameplay.controller)
    const string BiteState = "Bite", CoffinState = "Coffin Checkpoint";
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int TurnHash = Animator.StringToHash("Turn");
    static readonly int GroundedHash = Animator.StringToHash("IsGrounded");
    static readonly int JumpHash = Animator.StringToHash("Jump");
    static readonly int BiteHash = Animator.StringToHash("Bite");
    static readonly int CheckpointHash = Animator.StringToHash("Checkpoint");
    static readonly int DieHash = Animator.StringToHash("Die");
    static readonly int RespawnHash = Animator.StringToHash("Respawn");

    CharacterController controller;
    Animator animator;
    float verticalVelocity;
    float timeSinceGrounded;
    bool wasAnimGrounded = true;
    bool turningInPlace;
    Vector3 respawnPosition;
    Quaternion respawnRotation;
    HumanAI currentVictim;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        if (viewCamera == null) viewCamera = Camera.main;
        respawnPosition = transform.position;
        respawnRotation = transform.rotation;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (IsDead)
        {
            UpdateGravity(false);
            if (debugKeys && kb.rKey.wasPressedThisFrame) Respawn();
            return;
        }

        if (debugKeys && kb.kKey.wasPressedThisFrame)
        {
            Die();
            return;
        }

        if (IsBusy)
        {
            UpdateGravity(false);
            return;
        }

        bool bitePressed = kb.eKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (bitePressed && IsGroundedForActions())
        {
            StartCoroutine(BiteRoutine(FindVictim()));
            return;
        }

        if (kb.fKey.wasPressedThisFrame && IsGroundedForActions())
        {
            CoffinCheckpoint coffin = FindCoffin();
            if (coffin != null)
            {
                StartCoroutine(CheckpointRoutine(coffin));
                return;
            }
        }

        Move(kb);
    }

    // ---------------------------------------------------------------- movement

    void Move(Keyboard kb)
    {
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 forward = Vector3.forward, right = Vector3.right;
        if (viewCamera != null)
        {
            forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        }
        Vector3 moveDir = forward * input.y + right * input.x;
        bool moving = moveDir.sqrMagnitude > 0.01f;

        float turn = 0f;
        if (moving)
        {
            turningInPlace = false;
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, runTurnSpeed * Time.deltaTime);
        }
        else if (faceMouseWhenIdle && TryGetCursorPoint(out Vector3 cursor))
        {
            Vector3 toCursor = Vector3.ProjectOnPlane(cursor - transform.position, Vector3.up);
            if (toCursor.sqrMagnitude > 0.5f)
            {
                float angle = Vector3.SignedAngle(transform.forward, toCursor, Vector3.up);
                if (Mathf.Abs(angle) > idleTurnThreshold) turningInPlace = true;
                if (turningInPlace)
                {
                    float step = Mathf.Min(idleTurnSpeed * Time.deltaTime, Mathf.Abs(angle));
                    transform.Rotate(Vector3.up, Mathf.Sign(angle) * step, Space.World);
                    turn = Mathf.Sign(angle);
                    if (Mathf.Abs(angle) - step < 2f) turningInPlace = false;
                }
            }
        }

        bool jumped = UpdateGravity(Keyboard.current.spaceKey.wasPressedThisFrame);
        Vector3 motion = moveDir * moveSpeed + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        animator.SetFloat(SpeedHash, moving ? 1f : 0f);
        animator.SetFloat(TurnHash, moving ? 0f : turn, 0.05f, Time.deltaTime);
        if (turn == 0f && Mathf.Abs(animator.GetFloat(TurnHash)) < 0.01f) animator.SetFloat(TurnHash, 0f);
        UpdateJumpAnimation(jumped);
    }

    /// <summary>Applies gravity (and a jump if requested and allowed). Returns true if a jump started.</summary>
    bool UpdateGravity(bool jumpRequested)
    {
        bool grounded = controller.isGrounded;
        if (grounded) timeSinceGrounded = 0f; else timeSinceGrounded += Time.deltaTime;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            if (capeCloth != null) capeCloth.externalAcceleration = Vector3.zero;
        }

        bool jumped = false;
        bool canJump = timeSinceGrounded < groundedGraceTime && verticalVelocity <= 0.1f;
        if (jumpRequested && canJump)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumped = true;
            if (capeCloth != null) capeCloth.externalAcceleration = jumpWindForce;
        }

        verticalVelocity += gravity * Time.deltaTime;
        if (!jumped && (IsBusy || IsDead)) controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
        return jumped;
    }

    void UpdateJumpAnimation(bool jumped)
    {
        bool animGrounded = IsGroundedForActions();
        animator.SetBool(GroundedHash, animGrounded);
        if (jumped || (wasAnimGrounded && !animGrounded)) animator.SetTrigger(JumpHash);
        wasAnimGrounded = animGrounded;
    }

    bool IsGroundedForActions() => timeSinceGrounded < groundedGraceTime && verticalVelocity <= 0.1f;

    bool TryGetCursorPoint(out Vector3 point)
    {
        point = default;
        if (viewCamera == null || Mouse.current == null) return false;
        Vector2 mouse = Mouse.current.position.ReadValue();
        if (mouse.x < 0 || mouse.y < 0 || mouse.x > Screen.width || mouse.y > Screen.height) return false;
        Ray ray = viewCamera.ScreenPointToRay(mouse);
        Plane ground = new Plane(Vector3.up, transform.position);
        if (!ground.Raycast(ray, out float enter)) return false;
        point = ray.GetPoint(enter);
        return true;
    }

    void StopLocomotionAnimation()
    {
        animator.SetFloat(SpeedHash, 0f);
        animator.SetFloat(TurnHash, 0f);
        turningInPlace = false;
    }

    // ---------------------------------------------------------------- bite

    HumanAI FindVictim()
    {
        HumanAI best = null;
        float bestDistance = biteRange;
        foreach (HumanAI human in FindObjectsByType<HumanAI>(FindObjectsSortMode.None))
        {
            if (!human.CanBeBitten) continue;
            Vector3 to = Vector3.ProjectOnPlane(human.transform.position - transform.position, Vector3.up);
            float d = to.magnitude;
            if (d > bestDistance) continue;
            if (d > 0.2f && Vector3.Angle(transform.forward, to) > biteAngle * 0.5f) continue;
            best = human;
            bestDistance = d;
        }
        return best;
    }

    IEnumerator BiteRoutine(HumanAI victim)
    {
        IsBusy = true;
        StopLocomotionAnimation();

        if (victim != null)
        {
            Vector3 to = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up);
            if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
            victim.OnCaptured(transform);
            currentVictim = victim;
            Vector3 hold = transform.position + transform.forward * victimHoldDistance;
            hold.y = victim.transform.position.y;
            victim.transform.position = hold;
        }

        animator.SetTrigger(BiteHash);
        bool killed = false;
        yield return WaitForAction(BiteState, t =>
        {
            if (!killed && victim != null && t >= killAtNormalizedTime)
            {
                killed = true;
                victim.OnKilled();
            }
        });
        if (victim != null && !killed) victim.OnKilled();

        currentVictim = null;
        IsBusy = false;
        onBite.Invoke(victim != null ? victim.gameObject : null);
    }

    // ---------------------------------------------------------------- coffin

    CoffinCheckpoint FindCoffin()
    {
        CoffinCheckpoint best = null;
        float bestDistance = coffinRange;
        foreach (CoffinCheckpoint coffin in CoffinCheckpoint.All)
        {
            float d = Vector3.Distance(Vector3.ProjectOnPlane(coffin.transform.position, Vector3.up), Vector3.ProjectOnPlane(transform.position, Vector3.up));
            if (d < bestDistance) { best = coffin; bestDistance = d; }
        }
        return best;
    }

    IEnumerator CheckpointRoutine(CoffinCheckpoint coffin)
    {
        IsBusy = true;
        StopLocomotionAnimation();
        Vector3 to = Vector3.ProjectOnPlane(coffin.transform.position - transform.position, Vector3.up);
        if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);

        coffin.Activate();
        respawnPosition = coffin.RespawnPosition;
        respawnRotation = coffin.RespawnRotation;
        animator.SetTrigger(CheckpointHash);
        onCheckpoint.Invoke(coffin);

        yield return WaitForAction(CoffinState, null);
        IsBusy = false;
    }

    // ---------------------------------------------------------------- death

    /// <summary>Kill the vampire (call this from the health system or police damage).</summary>
    public void Die()
    {
        if (IsDead) return;
        StopAllCoroutines();
        if (currentVictim != null) currentVictim.OnReleased(); // a bite cut short lets the victim escape
        currentVictim = null;
        IsBusy = false;
        IsDead = true;
        StopLocomotionAnimation();
        animator.SetTrigger(DieHash);
        if (capeCloth != null) capeCloth.externalAcceleration = Vector3.zero;
        onDeath.Invoke();
    }

    /// <summary>Bring the vampire back at the last coffin he rested in (or where he started).</summary>
    public void Respawn()
    {
        if (!IsDead) return;
        controller.enabled = false;
        transform.SetPositionAndRotation(respawnPosition, respawnRotation);
        controller.enabled = true;
        if (capeCloth != null) capeCloth.ClearTransformMotion();
        if (viewCamera != null && viewCamera.TryGetComponent(out IsometricCamera iso)) iso.SnapToTarget();
        verticalVelocity = 0f;
        IsDead = false;
        animator.ResetTrigger(DieHash);
        animator.SetTrigger(RespawnHash);
        onRespawn.Invoke();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Waits until the given animator state has played (or a safety timeout), reporting its progress 0-1.</summary>
    IEnumerator WaitForAction(string stateName, System.Action<float> onProgress)
    {
        float waited = 0f;
        while (waited < 0.75f && !animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
        {
            waited += Time.deltaTime;
            yield return null;
        }

        float safety = 15f;
        while (safety > 0f)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(stateName)) break;
            onProgress?.Invoke(info.normalizedTime);
            if (info.normalizedTime >= 0.92f) break;
            safety -= Time.deltaTime;
            yield return null;
        }
        onProgress?.Invoke(1f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, biteRange);
        Gizmos.color = new Color(0.6f, 0f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, coffinRange);
    }
}
