using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Gameplay controls for the vampire in the isometric main scene.
///
/// WASD / arrows : run, relative to the camera (W = up the screen)
/// Mouse         : when standing still he turns on the spot to face the cursor
/// Space         : jump
/// E / left click: bite a human within reach in front of him (does nothing if nobody is in reach)
/// F             : rest in a nearby coffin (saves the checkpoint)
/// K / R         : debug only - die / rise again from the last coffin
///
/// The level starts with him rising out of the nearest closed coffin, and he rises from his last
/// coffin after dying.
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
    [Range(0, 360)] public float biteAngle = 140f;
    [Tooltip("Distance in front of him where the victim is held during the bite.")]
    public float victimHoldDistance = 0.75f;
    [Tooltip("Point in the bite animation (0-1) at which the victim dies.")]
    [Range(0, 1)] public float killAtNormalizedTime = 0.7f;

    [Header("Coffins")]
    public float coffinRange = 2.8f;
    [Tooltip("Start the level lying in the nearest coffin and rise out of it.")]
    public bool startInCoffin = true;
    [Tooltip("Coffin to rise from at the start. Empty = the nearest one.")]
    public CoffinCheckpoint startCoffin;
    [Tooltip("Seconds before he starts rising when the level loads.")]
    public float introDelay = 0.8f;
    [Tooltip("Smoke burst played when he vanishes from the coffin and reappears in front of it.")]
    public GameObject vanishEffect;

    [Header("Cape")]
    public Cloth capeCloth;
    public Vector3 jumpWindForce = new Vector3(0f, 5f, -2f);

    [Header("Debug")]
    [Tooltip("K kills the vampire and R makes him rise again from his last coffin.")]
    public bool debugKeys = true;

    [Header("Events - hook health, UI and sound here")]
    [Tooltip("Fires when a bite finishes. The argument is the victim.")]
    public UnityEvent<GameObject> onBite = new UnityEvent<GameObject>();
    public UnityEvent<CoffinCheckpoint> onCheckpoint = new UnityEvent<CoffinCheckpoint>();
    [Tooltip("Fires when he has climbed out of a coffin (level start and after every respawn).")]
    public UnityEvent onEmerged = new UnityEvent();
    public UnityEvent onDeath = new UnityEvent();
    public UnityEvent onRespawn = new UnityEvent();

    public bool IsDead { get; private set; }
    /// <summary>True while an action or coffin sequence is playing and movement is locked.</summary>
    public bool IsBusy { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsGrounded => IsGroundedForActions();
    /// <summary>The human he would bite if E were pressed now, or null.</summary>
    public HumanAI BiteTargetInReach { get; private set; }
    /// <summary>The coffin he would rest in if F were pressed now, or null.</summary>
    public CoffinCheckpoint CoffinInReach { get; private set; }

    const string BiteState = "Bite", LieDownState = "Coffin Checkpoint", RiseState = "Coffin Rise", IdleState = "Idle";
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
    VampireAudio sfx;
    float verticalVelocity;
    float timeSinceGrounded;
    float airTime;
    bool wasAnimGrounded = true;
    bool turningInPlace;
    bool inSequence;
    HumanAI currentVictim;
    CoffinCheckpoint respawnCoffin;
    Vector3 fallbackRespawnPosition;
    Quaternion fallbackRespawnRotation;
    readonly List<Renderer> visibleRenderers = new List<Renderer>();

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        sfx = GetComponent<VampireAudio>();
        animator.applyRootMotion = false;
        if (viewCamera == null) viewCamera = Camera.main;
        fallbackRespawnPosition = transform.position;
        fallbackRespawnRotation = transform.rotation;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) visibleRenderers.Add(r);
    }

    void Start()
    {
        if (!startInCoffin) return;
        CoffinCheckpoint coffin = startCoffin != null ? startCoffin : FindNearestCoffin(transform.position, 8f);
        if (coffin == null) return;
        coffin.Activate();
        respawnCoffin = coffin;
        StartCoroutine(EmergeRoutine(coffin, introDelay));
    }

    void Update()
    {
        if (GameState.Paused) return;
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (inSequence) return; // coffin sequences move him directly

        if (IsDead)
        {
            UpdateGravity(false);
            BiteTargetInReach = null;
            CoffinInReach = null;
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
            BiteTargetInReach = null;
            CoffinInReach = null;
            return;
        }

        bool grounded = IsGroundedForActions();
        BiteTargetInReach = grounded ? FindVictim() : null;
        CoffinInReach = grounded ? FindNearestCoffin(transform.position, coffinRange) : null;

        bool bitePressed = kb.eKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (bitePressed && BiteTargetInReach != null)
        {
            StartCoroutine(BiteRoutine(BiteTargetInReach));
            return;
        }

        if (kb.fKey.wasPressedThisFrame && CoffinInReach != null)
        {
            StartCoroutine(RestInCoffinRoutine(CoffinInReach));
            return;
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
        IsRunning = moving;

        float turn = 0f;
        if (moving)
        {
            turningInPlace = false;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(moveDir), runTurnSpeed * Time.deltaTime);
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

        bool jumped = UpdateGravity(kb.spaceKey.wasPressedThisFrame);
        controller.Move((moveDir * moveSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);

        animator.SetFloat(SpeedHash, moving ? 1f : 0f);
        animator.SetFloat(TurnHash, moving ? 0f : turn, 0.05f, Time.deltaTime);
        if (turn == 0f && Mathf.Abs(animator.GetFloat(TurnHash)) < 0.01f) animator.SetFloat(TurnHash, 0f);
        UpdateJumpAnimation(jumped);
    }

    /// <summary>Applies gravity (and a jump if requested and allowed). Returns true if a jump started.</summary>
    bool UpdateGravity(bool jumpRequested)
    {
        if (!controller.enabled) return false;
        bool grounded = controller.isGrounded;
        if (grounded)
        {
            if (airTime > 0.3f && sfx != null) sfx.PlayLand();
            airTime = 0f;
            timeSinceGrounded = 0f;
        }
        else
        {
            airTime += Time.deltaTime;
            timeSinceGrounded += Time.deltaTime;
        }

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
            if (sfx != null) sfx.PlayJump();
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
        IsRunning = false;
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

        Vector3 to = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up);
        if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
        victim.OnCaptured(transform);
        currentVictim = victim;
        Vector3 hold = transform.position + transform.forward * victimHoldDistance;
        hold.y = victim.transform.position.y;
        victim.transform.position = hold;

        animator.SetTrigger(BiteHash);
        if (sfx != null) sfx.PlayBite();
        bool killed = false;
        yield return WaitForAction(BiteState, t =>
        {
            if (!killed && t >= killAtNormalizedTime)
            {
                killed = true;
                victim.OnKilled();
                if (sfx != null) sfx.PlayFeed();
            }
        });
        if (!killed) victim.OnKilled();

        currentVictim = null;
        IsBusy = false;
        onBite.Invoke(victim.gameObject);
    }

    // ---------------------------------------------------------------- coffins

    CoffinCheckpoint FindNearestCoffin(Vector3 from, float range)
    {
        CoffinCheckpoint best = null;
        float bestDistance = range;
        foreach (CoffinCheckpoint coffin in CoffinCheckpoint.All)
        {
            float d = Vector3.Distance(Vector3.ProjectOnPlane(coffin.transform.position, Vector3.up), Vector3.ProjectOnPlane(from, Vector3.up));
            if (d < bestDistance) { best = coffin; bestDistance = d; }
        }
        return best;
    }

    /// <summary>Lid opens, he floats in and lies down, the lid closes, and he reappears in front in a burst of smoke.</summary>
    IEnumerator RestInCoffinRoutine(CoffinCheckpoint coffin)
    {
        BeginSequence();
        coffin.Activate();
        respawnCoffin = coffin;
        onCheckpoint.Invoke(coffin);

        Vector3 toCoffin = Vector3.ProjectOnPlane(coffin.transform.position - transform.position, Vector3.up);
        if (toCoffin.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(toCoffin);
        yield return coffin.MoveLid(CoffinCheckpoint.LidState.Open, 0.5f);

        yield return FloatTo(coffin.InsidePosition, coffin.InsideRotation, 0.45f, 0.7f);

        animator.Play(LieDownState, 0, 0f);
        if (sfx != null) sfx.PlayLieDown();
        yield return WaitForState(LieDownState, 0.98f);

        yield return coffin.MoveLid(CoffinCheckpoint.LidState.Closed, 0.6f);
        yield return new WaitForSeconds(0.35f);

        SpawnVanishEffect(coffin.transform.position + Vector3.up * 0.5f);
        if (sfx != null) sfx.PlayVanish();
        SetVisible(false);
        yield return new WaitForSeconds(0.25f);

        PlaceAt(coffin.RespawnPosition, coffin.RespawnRotation);
        animator.Play(IdleState, 0, 0f);
        SpawnVanishEffect(coffin.RespawnPosition + Vector3.up * 0.9f);
        if (sfx != null) sfx.PlayAppear();
        SetVisible(true);

        EndSequence();
    }

    /// <summary>He lies in the closed coffin, the lid opens, he rises stiffly and floats out in front of it.</summary>
    IEnumerator EmergeRoutine(CoffinCheckpoint coffin, float delay)
    {
        BeginSequence();
        SetVisible(true);
        coffin.SetLidImmediate(CoffinCheckpoint.LidState.Closed);
        PlaceAt(coffin.InsidePosition, coffin.InsideRotation);
        animator.Play(LieDownState, 0, 1f); // the lying pose
        animator.speed = 0f;
        animator.Update(0f);
        if (viewCamera != null && viewCamera.TryGetComponent(out IsometricCamera iso)) iso.SnapToTarget();

        yield return new WaitForSeconds(delay);
        yield return coffin.MoveLid(CoffinCheckpoint.LidState.Open, 0.7f);

        animator.speed = 1f;
        animator.Play(RiseState, 0, 1f); // the lie-down clip played backwards
        if (sfx != null) sfx.PlayRise();
        yield return WaitForRise();

        animator.Play(IdleState, 0, 0f);
        yield return FloatTo(coffin.RespawnPosition, coffin.RespawnRotation, 0.5f, 0.7f);

        EndSequence();
        onEmerged.Invoke();
    }

    void BeginSequence()
    {
        inSequence = true;
        IsBusy = true;
        StopLocomotionAnimation();
        controller.enabled = false;
        verticalVelocity = 0f;
    }

    void EndSequence()
    {
        controller.enabled = true;
        verticalVelocity = -2f;
        timeSinceGrounded = 0f;
        airTime = 0f;
        inSequence = false;
        IsBusy = false;
    }

    IEnumerator FloatTo(Vector3 position, Quaternion rotation, float duration, float arcHeight)
    {
        Vector3 fromPos = transform.position;
        Quaternion fromRot = transform.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.SetPositionAndRotation(Vector3.Lerp(fromPos, position, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * arcHeight,
                                             Quaternion.Slerp(fromRot, rotation, k));
            yield return null;
        }
        transform.SetPositionAndRotation(position, rotation);
    }

    void PlaceAt(Vector3 position, Quaternion rotation)
    {
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = wasEnabled;
        if (capeCloth != null) capeCloth.ClearTransformMotion();
    }

    void SetVisible(bool visible)
    {
        foreach (Renderer r in visibleRenderers)
            if (r != null) r.enabled = visible;
        if (visible && capeCloth != null) capeCloth.ClearTransformMotion();
    }

    void SpawnVanishEffect(Vector3 position)
    {
        if (vanishEffect != null) Instantiate(vanishEffect, position, Quaternion.identity);
    }

    // ---------------------------------------------------------------- death

    /// <summary>Kill the vampire (call this from the health system or police damage).</summary>
    public void Die()
    {
        if (IsDead) return;
        StopAllCoroutines();
        if (currentVictim != null) currentVictim.OnReleased(); // a bite cut short lets the victim escape
        currentVictim = null;
        if (inSequence) EndSequence();
        animator.speed = 1f;
        SetVisible(true);
        IsBusy = false;
        IsDead = true;
        StopLocomotionAnimation();
        animator.SetTrigger(DieHash);
        if (capeCloth != null) capeCloth.externalAcceleration = Vector3.zero;
        if (sfx != null) sfx.PlayDeath();
        onDeath.Invoke();
    }

    /// <summary>Bring the vampire back: he rises from the last coffin he rested in.</summary>
    public void Respawn()
    {
        if (!IsDead) return;
        IsDead = false;
        animator.ResetTrigger(DieHash);
        animator.SetTrigger(RespawnHash);
        onRespawn.Invoke();

        CoffinCheckpoint coffin = respawnCoffin != null ? respawnCoffin : CoffinCheckpoint.Current;
        if (coffin != null)
        {
            StartCoroutine(EmergeRoutine(coffin, 0.4f));
        }
        else
        {
            PlaceAt(fallbackRespawnPosition, fallbackRespawnRotation);
            verticalVelocity = 0f;
            if (viewCamera != null && viewCamera.TryGetComponent(out IsometricCamera iso)) iso.SnapToTarget();
        }
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

    /// <summary>Waits until a state started with Animator.Play reaches the given normalized time.</summary>
    IEnumerator WaitForState(string stateName, float untilNormalizedTime)
    {
        yield return null;
        float safety = 15f;
        while (safety > 0f)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(stateName) || info.normalizedTime >= untilNormalizedTime) break;
            safety -= Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// The rise plays the lie-down clip backwards. Unity still counts normalized time upward for reversed
    /// states, so wait for the state's duration instead.
    /// </summary>
    IEnumerator WaitForRise()
    {
        yield return null;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        float duration = info.IsName(RiseState) && info.length > 0f && info.length < 30f ? info.length : 3f;
        float elapsed = 0f;
        while (elapsed < duration * 0.97f && animator.GetCurrentAnimatorStateInfo(0).IsName(RiseState))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, biteRange);
        Gizmos.color = new Color(0.6f, 0f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, coffinRange);
    }
}
