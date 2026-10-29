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
/// E / left click: bite a human or police officer within reach in front of him (does nothing if nobody is in reach)
/// Right click / Q: fight - a melee combo that hurts police officers and humans in front of him (it never kills; the bite does)
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

    [Header("Fight (Right click or Q)")]
    [Tooltip("How far from him (pivot to pivot) a blow still hits. His fists reach about 0.45 m in front of him, and an officer's collider keeps him about 0.74 m away, so keep this small or blows land on people he is visibly not touching.")]
    public float attackRange = 0.9f;
    [Tooltip("If an officer is this close (in the direction he is aiming), he runs up to him before swinging. 0 = swing on the spot.")]
    public float attackSeekRange = 6f;
    [Tooltip("How close (pivot to pivot) he gets to his target before the combo starts. His fists reach about 0.45 m, so 0.7 puts them on the target's body (officers' colliders stop him at about 0.74 anyway).")]
    public float fightDistance = 0.7f;
    [Tooltip("Speed of the dash toward the officer.")]
    public float lungeSpeed = 9f;
    [Tooltip("During the combo, if the target ends up further than fightDistance, he steps back in at this speed.")]
    public float attackStepSpeed = 3f;
    [Tooltip("How wide the swing is (degrees, centred on where he faces).")]
    [Range(0, 360)] public float attackAngle = 120f;
    [Tooltip("Damage per hit. Humans and police have 40 health by default. Hits wear health down but never kill; a bite does (police only once weakened).")]
    public float attackDamage = 20f;
    [Tooltip("Points in the attack animation (0-1) at which a blow lands, in order. The Vampire Fighting combo has strikes at about 0.17, 0.30, 0.47 and 0.58.")]
    public float[] hitTimes = { 0.17f, 0.30f, 0.47f, 0.58f };
    [Tooltip("Point in the attack animation (0-1) where he is free to move again. The full combo lasts 4.7 s; 0.68 ends it after the big punch.")]
    [Range(0, 1)] public float attackEndNormalizedTime = 0.68f;
    [Tooltip("Seconds he must wait after a swing before he can swing again.")]
    public float attackCooldown = 0.3f;
    [Tooltip("Only used while the Animator has no Attack animation yet: how long a swing lasts.")]
    public float fallbackAttackDuration = 0.6f;
    [Tooltip("Fires for every officer a swing hits. Hook hit sounds or effects here.")]
    public UnityEvent<GameObject> onHit = new UnityEvent<GameObject>();

    [Header("Getting hit")]
    [Tooltip("Minimum seconds between two flinches, so he is not stunned by every bullet.")]
    public float hitReactionCooldown = 2.5f;
    [Tooltip("Seconds he cannot move while flinching.")]
    public float hitStunTime = 0.8f;

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
    /// <summary>The human or officer he would bite if E were pressed now, or null.</summary>
    public GameObject BiteTargetInReach => biteTarget != null ? biteTarget.GameObject : null;
    /// <summary>The coffin he would rest in if F were pressed now, or null.</summary>
    public CoffinCheckpoint CoffinInReach { get; private set; }

    const string BiteState = "Bite", LieDownState = "Coffin Checkpoint", RiseState = "Coffin Rise", IdleState = "Idle", AttackState = "Attack";
    static readonly int AttackHash = Animator.StringToHash("Attack");
    static readonly int HitHash = Animator.StringToHash("Hit");
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
    BiteTarget biteTarget;
    BiteTarget currentVictim;
    float nextAttackTime;
    bool hasAttackAnim; // true once the Animator has an "Attack" trigger (and state) set up
    bool hasHitAnim;    // same for the "Hit" (getting hit) reaction
    float nextHitReactionTime;
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
        foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Trigger) continue;
                if (p.name == "Attack") hasAttackAnim = true;
                if (p.name == "Hit") hasHitAnim = true;
            }
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
            biteTarget = null;
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
            biteTarget = null;
            CoffinInReach = null;
            return;
        }

        bool grounded = IsGroundedForActions();
        biteTarget = grounded ? FindVictim() : null;
        CoffinInReach = grounded ? FindNearestCoffin(transform.position, coffinRange) : null;

        bool bitePressed = kb.eKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (bitePressed && biteTarget != null)
        {
            StartCoroutine(BiteRoutine(biteTarget));
            return;
        }

        bool attackPressed = kb.qKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
        if (attackPressed && grounded && Time.time >= nextAttackTime)
        {
            StartCoroutine(AttackRoutine());
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

    /// <summary>A human (HumanAI) or a police officer (PoliceAI) the vampire can bite.</summary>
    sealed class BiteTarget
    {
        readonly HumanAI human;
        readonly PoliceAI officer;
        public BiteTarget(HumanAI human) { this.human = human; }
        public BiteTarget(PoliceAI officer) { this.officer = officer; }
        public bool IsValid => human != null || officer != null;
        public Transform Transform => human != null ? human.transform : officer.transform;
        public GameObject GameObject => Transform.gameObject;
        public void Capture(Transform biter) { if (human != null) human.OnCaptured(biter); else if (officer != null) officer.OnCaptured(biter); }
        public void Release() { if (human != null) human.OnReleased(); else if (officer != null) officer.OnReleased(); }
        public void Kill() { if (human != null) human.OnKilled(); else if (officer != null) officer.Die(); }
    }

    BiteTarget FindVictim()
    {
        BiteTarget best = null;
        float bestDistance = biteRange;
        foreach (HumanAI human in FindObjectsByType<HumanAI>(FindObjectsSortMode.None))
            if (human.CanBeBitten && InBiteReach(human.transform, ref bestDistance)) best = new BiteTarget(human);
        foreach (PoliceAI officer in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
            if (officer.CanBeBitten && InBiteReach(officer.transform, ref bestDistance)) best = new BiteTarget(officer);
        return best;
    }

    bool InBiteReach(Transform target, ref float bestDistance)
    {
        Vector3 to = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        float d = to.magnitude;
        if (d > bestDistance) return false;
        if (d > 0.2f && Vector3.Angle(transform.forward, to) > biteAngle * 0.5f) return false;
        bestDistance = d;
        return true;
    }

    IEnumerator BiteRoutine(BiteTarget victim)
    {
        IsBusy = true;
        StopLocomotionAnimation();

        Transform victimTransform = victim.Transform;
        Vector3 to = Vector3.ProjectOnPlane(victimTransform.position - transform.position, Vector3.up);
        if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
        victim.Capture(transform);
        currentVictim = victim;
        Vector3 hold = transform.position + transform.forward * victimHoldDistance;
        hold.y = victimTransform.position.y;
        victimTransform.position = hold;

        animator.SetTrigger(BiteHash);
        if (sfx != null) sfx.PlayBite();
        bool killed = false;
        yield return WaitForAction(BiteState, t =>
        {
            if (!killed && t >= killAtNormalizedTime)
            {
                killed = true;
                victim.Kill();
                if (sfx != null) sfx.PlayFeed();
            }
        });
        if (!killed) victim.Kill();

        currentVictim = null;
        IsBusy = false;
        onBite.Invoke(victim.GameObject);
    }

// ---------------------------------------------------------------- fight

    /// <summary>A melee swing: turns toward the cursor, plays the attack animation, and hurts officers in front of him.</summary>
IEnumerator AttackRoutine()
    {
        IsBusy = true;
        StopLocomotionAnimation();

        // Where the player is pointing.
        Vector3 aimDir = transform.forward;
        if (TryGetCursorPoint(out Vector3 cursor))
        {
            Vector3 toCursor = Vector3.ProjectOnPlane(cursor - transform.position, Vector3.up);
            if (toCursor.sqrMagnitude > 0.25f)
            {
                aimDir = toCursor.normalized;
                transform.rotation = Quaternion.LookRotation(aimDir);
            }
        }

        // Close in on the nearest human or officer first, so the fight happens at arm's length.
        Component target = FindFightTarget(aimDir);
        HumanAI humanTarget = target as HumanAI;
        if (humanTarget != null) humanTarget.FreezeInFear(2.5f); // terrified: he does not run off while the vampire closes in
        PoliceAI officerTarget = target as PoliceAI;
        if (officerTarget != null)
        {
            officerTarget.HoldForFight(2.5f); // he stands his ground instead of backing off
            SetIgnoreOfficer(officerTarget, true); // his collider would otherwise keep the fists a body-width away
        }
        if (target != null) yield return ApproachTarget(target);

        if (hasAttackAnim)
        {
            // The fighting animation is a combo: every point in hitTimes lands a blow.
            int nextHit = 0;
            animator.ResetTrigger(AttackHash);
            animator.SetTrigger(AttackHash);
            yield return WaitForAction(AttackState, t =>
            {
                // Keep facing the target while he is pushed around by the blows.
                if (CanFight(target))
                {
                    FaceFlat(target.transform.position, runTurnSpeed);
                    StepCloser(target, attackStepSpeed);
                }
                while (hitTimes != null && nextHit < hitTimes.Length && t >= hitTimes[nextHit])
                {
                    nextHit++;
                    DealAttackDamage();
                }
            }, attackEndNormalizedTime);
        }
        else
        {
            // No animation set up: one blow in the middle of a short pause.
            yield return new WaitForSeconds(fallbackAttackDuration * 0.5f);
            DealAttackDamage();
            yield return new WaitForSeconds(fallbackAttackDuration * 0.5f);
        }

        SetIgnoreOfficer(null, false);
        nextAttackTime = Time.time + attackCooldown;
        IsBusy = false;
    }

    /// <summary>Hurts every living officer inside the swing's range and arc.</summary>
void DealAttackDamage()
    {
        foreach (PoliceAI officer in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
        {
            if (!officer.CanBeHit || !InSwing(officer.transform)) continue;
            officer.TakeHit(attackDamage, transform);
            onHit.Invoke(officer.gameObject);
        }
        foreach (HumanAI human in FindObjectsByType<HumanAI>(FindObjectsSortMode.None))
        {
            if (!human.CanBeHit || !InSwing(human.transform)) continue;
            human.TakeHit(attackDamage, transform);
            onHit.Invoke(human.gameObject);
        }
    }

    /// <summary>True if the thing is inside the swing's range and arc.</summary>
    bool InSwing(Transform t)
    {
        Vector3 to = Vector3.ProjectOnPlane(t.position - transform.position, Vector3.up);
        float d = to.magnitude;
        if (d > attackRange) return false;
        return d <= 0.2f || Vector3.Angle(transform.forward, to) <= attackAngle * 0.5f;
    }

    /// <summary>True while a human or officer can still be fought (not dead, not grabbed).</summary>
    static bool CanFight(Component c)
    {
        if (c == null) return false;
        PoliceAI officer = c as PoliceAI;
        if (officer != null) return officer.CanBeHit;
        HumanAI human = c as HumanAI;
        return human != null && human.CanBeHit;
    }

    void ConsiderTarget(Component c, Vector3 aimDir, ref Component best, ref float bestDist)
    {
        Vector3 to = Vector3.ProjectOnPlane(c.transform.position - transform.position, Vector3.up);
        float d = to.magnitude;
        if (d >= bestDist) return;
        if (d > 0.2f && Vector3.Angle(aimDir, to) > 100f) return;
        best = c;
        bestDist = d;
    }

/// <summary>The nearest living officer within attackSeekRange, in the direction he is aiming (or null).</summary>
Component FindFightTarget(Vector3 aimDir)
    {
        if (attackSeekRange <= 0f) return null;
        Component best = null;
        float bestDist = attackSeekRange;
        foreach (PoliceAI officer in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
            if (officer.CanBeHit) ConsiderTarget(officer, aimDir, ref best, ref bestDist);
        foreach (HumanAI human in FindObjectsByType<HumanAI>(FindObjectsSortMode.None))
            if (human.CanBeHit) ConsiderTarget(human, aimDir, ref best, ref bestDist);
        return best;
    }

    /// <summary>Runs up to the officer and stops fightDistance away from him, facing him.</summary>
IEnumerator ApproachTarget(Component target)
    {
        float timeout = 1.5f;
        float stalled = 0f;
        float lastDist = float.MaxValue;
        animator.SetFloat(SpeedHash, 1f); // run animation while closing in
        while (timeout > 0f && CanFight(target))
        {
            Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            float dist = to.magnitude;
            if (dist <= fightDistance + 0.05f) break;

            // Stop when he cannot get any closer (an officer's collider is in the way) instead of running on the spot.
            if (dist > lastDist - 0.003f) stalled += Time.deltaTime; else stalled = 0f;
            if (stalled > 0.15f) break;
            lastDist = dist;

            FaceFlat(target.transform.position, runTurnSpeed);
            float step = Mathf.Min(lungeSpeed * Time.deltaTime, dist - fightDistance);
            controller.Move((to / dist) * step);
            timeout -= Time.deltaTime;
            yield return null;
        }
        animator.SetFloat(SpeedHash, 0f);

        if (target != null)
        {
            Vector3 face = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            if (face.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(face);
        }
    }

    /// <summary>Steps toward the target if it has drifted further than fightDistance, so the blows keep landing at arm's length.</summary>
    void StepCloser(Component target, float speed)
    {
        Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
        float dist = to.magnitude;
        if (dist <= fightDistance + 0.1f || dist < 0.01f) return;
        float step = Mathf.Min(speed * Time.deltaTime, dist - fightDistance);
        controller.Move((to / dist) * step);
    }

    void FaceFlat(Vector3 worldPos, float degreesPerSecond)
    {
        Vector3 to = Vector3.ProjectOnPlane(worldPos - transform.position, Vector3.up);
        if (to.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
    }


/// <summary>Called when something hurts him (a police bullet). He flinches, but not more often than hitReactionCooldown.</summary>
    public void OnHurt()
    {
        if (!hasHitAnim || IsDead || IsBusy || inSequence || !IsGroundedForActions()) return;
        if (Time.time < nextHitReactionTime) return;
        StartCoroutine(HitReactionRoutine());
    }

    IEnumerator HitReactionRoutine()
    {
        nextHitReactionTime = Time.time + hitReactionCooldown;
        IsBusy = true;
        StopLocomotionAnimation();
        animator.ResetTrigger(HitHash);
        animator.SetTrigger(HitHash);
        yield return new WaitForSeconds(hitStunTime);
        IsBusy = false;
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
        // The controller stays enabled (it is simply not moved) so the police can still aim at his collider.
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
        SetIgnoreOfficer(null, false);
        if (currentVictim != null) currentVictim.Release(); // a bite cut short lets the victim escape
        currentVictim = null;
        if (inSequence) EndSequence();
        animator.speed = 1f;
        SetVisible(true);
        IsBusy = false;
        IsDead = true;
        StopLocomotionAnimation();
        if (hasAttackAnim) animator.ResetTrigger(AttackHash);
        if (hasHitAnim) animator.ResetTrigger(HitHash);
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
IEnumerator WaitForAction(string stateName, System.Action<float> onProgress, float endAt = 0.92f)
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
            if (info.normalizedTime >= endAt) break;
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
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }


PoliceAI ignoredOfficer;

    /// <summary>While fighting an officer, the vampire's body and the officer's colliders stop pushing each other so he can stand right in front of him.</summary>
    void SetIgnoreOfficer(PoliceAI officer, bool ignore)
    {
        if (ignore)
        {
            ignoredOfficer = officer;
            foreach (Collider c in officer.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(controller, c, true);
        }
        else if (ignoredOfficer != null)
        {
            foreach (Collider c in ignoredOfficer.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(controller, c, false);
            ignoredOfficer = null;
        }
    }
}
