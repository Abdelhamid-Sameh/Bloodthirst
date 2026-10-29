using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Police officer for the Enemies_pathfinding scene.
///
/// 1. Stands still until the vampire moves.
/// 2. Once the vampire moves, walks (NavMesh) to a spot close to him that has a clear line of fire.
/// 3. Stops there, turns to face him and shoots at him with its gun.
/// 4. If the vampire runs away, gets too close, or ducks behind something, it picks a new spot.
/// 5. Dies when the vampire bites it (see VampireBite).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class PoliceAI : MonoBehaviour
{
    public enum State { Idle, Moving, Shooting, Captured, Dead }

    [Header("Current State (read only)")]
    public State currentState = State.Idle;

    [Header("Target")]
    [Tooltip("The vampire. Empty = the object tagged Player.")]
    public Transform vampire;
    [Tooltip("Officers further away than this do not react to the vampire moving.")]
    public float reactionDistance = 60f;
    [Tooltip("How fast the vampire must move (units per second) to wake the officer up.")]
    public float movementThreshold = 0.3f;
    [Tooltip("Seconds the officer keeps fighting after the vampire stops moving. 0 = never stands down.")]
    public float standDownTime = 0f;

    [Header("Movement")]
    public float moveSpeed = 3.5f;
    [Tooltip("If the vampire is closer than this, the officer backs off to a better spot.")]
    public float tooCloseDistance = 2f;
    [Tooltip("Preferred distance range from the vampire when shooting.")]
    public float minCombatDistance = 4f;
    public float maxCombatDistance = 8f;
    [Tooltip("When the vampire moves this far away from the spot the officer chose, the officer picks a new spot.")]
    public float repositionDistance = 3f;

    [Header("Health (melee fights)")]
    [Tooltip("The officer's health. The vampire's fight attacks wear it down but never take it to 0 - only a bite kills him.")]
    public float maxHealth = 40f;
    [Tooltip("The vampire can only bite this officer once his health is at or below this fraction of maxHealth (0.4 = 40%). Weaken him with the fight first. 1 = can always be bitten.")]
    [Range(0f, 1f)] public float biteHealthFraction = 0.4f;
    [Tooltip("Seconds the officer cannot shoot after being hit.")]
    public float hitStaggerTime = 0.6f;
    [Tooltip("How far a hit pushes the officer back.")]
    public float knockbackDistance = 0.05f;
    [Tooltip("Minimum seconds between two hit-reaction animations, so a fast combo does not restart the reaction on every blow.")]
    public float hitRetriggerDelay = 1.0f;

    [Header("Shooting")]
    [Tooltip("Where shots come from. Empty = the child called Gun.")]
    public Transform gun;
    public float fireInterval = 1.2f;
    public float damage = 10f;
    [Tooltip("Chance (0-1) that a shot hits the vampire.")]
    [Range(0f, 1f)] public float hitChance = 0.6f;
    public float aimTurnSpeed = 360f;
    [Tooltip("Optional gunshot sound.")]
    public AudioClip shotSound;
    [Range(0f, 1f)] public float shotVolume = 0.8f;

    static readonly List<PoliceAI> all = new List<PoliceAI>();

    /// <summary>False once the officer is dead.</summary>
    public bool IsAlive => currentState != State.Dead;
    /// <summary>True if the vampire can hit this officer in a fight: not dead and not already grabbed.</summary>
    public bool CanBeHit => currentState != State.Captured && currentState != State.Dead;
    /// <summary>Health as a fraction of maxHealth (1 = untouched).</summary>
    public float HealthFraction => health < 0f ? 1f : Mathf.Clamp01(health / Mathf.Max(maxHealth, 0.01f));
    /// <summary>True once the fight has worn him down far enough for a bite to finish him.</summary>
    public bool IsWeakEnoughToBite => HealthFraction <= biteHealthFraction;
    /// <summary>True if the vampire is allowed to grab and bite this officer: not dead, not grabbed, and weakened enough.</summary>
    public bool CanBeBitten => CanBeHit && IsWeakEnoughToBite;

    NavMeshAgent agent;
    Collider[] ownColliders;
    VampireHealth vampireHealth;
    VampireBlood vampireBlood;
    Collider vampireCollider;
    LineRenderer tracer;
    Light muzzleFlash;
    Animator animator;
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int AimingHash = Animator.StringToHash("Aiming");
    static readonly int ShootHash = Animator.StringToHash("Shoot");
    static readonly int DieHash = Animator.StringToHash("Die");
    static readonly int HitHash = Animator.StringToHash("Hit");
    bool hasHitAnim;
    float nextHitAnimTime;
    float staggerUntil;
    bool wasStaggered;
    float tracerHideTime;

    bool engaged;
    Vector3 lastVampirePos;
    float lastMoveTime;
    float nextFireTime;
    bool hasSpot;
    Vector3 spot;
    Vector3 spotVampirePos;
    float repathTimer;
    bool warnedNoHealth;
    float health = -1f; // -1 = not set yet, becomes maxHealth on the first hit
    VampireController vampireController;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        currentState = State.Idle; // always start calm, whatever was saved in the scene
        ownColliders = GetComponentsInChildren<Collider>(true);
        if (gun == null) gun = transform.Find("Gun");
        BuildEffects();
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.name == "Hit" && p.type == AnimatorControllerParameterType.Trigger) hasHitAnim = true;
    }

    // Feeds the animator: how fast the officer walks, and whether he is aiming his gun.
    void LateUpdate()
    {
        if (animator == null || currentState == State.Dead) return;
        float speed = 0f;
        if (agent != null && agent.enabled && agent.isOnNavMesh) speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
        animator.SetBool(AimingHash, currentState == State.Shooting);
    }

    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    void Start()
    {
        if (vampire == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) vampire = player.transform;
        }
        if (vampire != null)
        {
            vampireHealth = vampire.GetComponent<VampireHealth>();
            vampireBlood = vampire.GetComponent<VampireBlood>();
            vampireCollider = vampire.GetComponent<Collider>();
            vampireController = vampire.GetComponent<VampireController>();
            lastVampirePos = vampire.position;
        }

        if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            agent.Warp(hit.position);
        if (!agent.isOnNavMesh)
            Debug.LogWarning(name + ": is not standing on the NavMesh, so it cannot walk. Bake the NavMesh again.", this);

        StopMoving();
    }

    void Update()
    {
        if (currentState == State.Dead || currentState == State.Captured || vampire == null) return;

        if (tracer.enabled && Time.time >= tracerHideTime)
        {
            tracer.enabled = false;
            muzzleFlash.enabled = false;
        }

        // Staggered by a hit: he stands still and cannot shoot until his hit reaction has finished.
        if (Time.time < staggerUntil || IsInHitReaction())
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
            wasStaggered = true;
            return;
        }
        if (wasStaggered)
        {
            wasStaggered = false;
            hasSpot = false; // pick a fresh spot now that he can move again
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
        }

        if (vampireHealth != null && vampireHealth.IsDead)
        {
            // The vampire is down: stop fighting.
            engaged = false;
            StopMoving();
            currentState = State.Idle;
            lastVampirePos = vampire.position;
            return;
        }

        WatchVampire();
        if (!engaged) return;

        Vector3 aim = AimPoint();
        float distance = FlatDistance(transform.position, vampire.position);
        bool goodDistance = distance >= tooCloseDistance && distance <= maxCombatDistance + 1f;
        bool canShoot = goodDistance && ClearShot(GunPosition(), aim);

        if (canShoot)
        {
            if (currentState != State.Shooting)
            {
                StopMoving();
                currentState = State.Shooting;
                hasSpot = false;
            }
            FaceVampire();
            if (Time.time >= nextFireTime) Fire(aim);
        }
        else
        {
            currentState = State.Moving;
            MoveToCombatSpot();
        }
    }

    // ------------------------------------------------------------ reacting to the vampire

    /// <summary>Wakes the officer up when the vampire moves.</summary>
    void WatchVampire()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 flatDelta = vampire.position - lastVampirePos;
        flatDelta.y = 0f;
        lastVampirePos = vampire.position;

        bool vampireMoving = flatDelta.magnitude / dt > movementThreshold;
        if (vampireMoving && FlatDistance(transform.position, vampire.position) <= reactionDistance)
        {
            engaged = true;
            lastMoveTime = Time.time;
        }

        if (engaged && standDownTime > 0f && Time.time - lastMoveTime > standDownTime)
        {
            engaged = false;
            StopMoving();
            currentState = State.Idle;
        }
    }

    // ------------------------------------------------------------ walking

    void MoveToCombatSpot()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;

        repathTimer -= Time.deltaTime;
        bool arrived = hasSpot && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;
        bool vampireMovedAway = hasSpot && FlatDistance(vampire.position, spotVampirePos) > repositionDistance;
        if (hasSpot && !arrived && !vampireMovedAway) return; // keep walking to the current spot
        if (repathTimer > 0f) return;
        repathTimer = 0.5f;

        agent.isStopped = false;
        agent.speed = moveSpeed;

        if (TryPickCombatSpot(out Vector3 newSpot, out NavMeshPath path))
        {
            spot = newSpot;
            spotVampirePos = vampire.position;
            hasSpot = true;
            agent.SetPath(path);
        }
        else if (NavMesh.SamplePosition(vampire.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            // No perfect spot found: just walk toward the vampire.
            spot = hit.position;
            spotVampirePos = vampire.position;
            hasSpot = true;
            agent.SetDestination(hit.position);
        }
    }

    /// <summary>Looks around the vampire for a reachable spot with a clear line of fire.</summary>
    bool TryPickCombatSpot(out Vector3 result, out NavMeshPath path)
    {
        result = default;
        path = new NavMeshPath();

        // Start on the side of the vampire the officer is already on, then fan out left and right.
        Vector3 fromVampire = transform.position - vampire.position;
        fromVampire.y = 0f;
        float baseAngle = Mathf.Atan2(fromVampire.x, fromVampire.z) * Mathf.Rad2Deg;

        for (int i = 0; i < 13; i++)
        {
            float step = ((i + 1) / 2) * 25f * (i % 2 == 0 ? 1f : -1f);
            float distance = Random.Range(minCombatDistance, maxCombatDistance);
            Vector3 candidate = vampire.position + Quaternion.Euler(0f, baseAngle + step, 0f) * Vector3.forward * distance;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            if (FlatDistance(hit.position, vampire.position) < tooCloseDistance) continue;
            if (FlatDistance(hit.position, vampire.position) > maxCombatDistance + 1f) continue;
            if (IsSpotTakenByOtherOfficer(hit.position)) continue;
            if (!ClearShot(hit.position + Vector3.up * 1.3f, AimPoint())) continue;
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;

            result = hit.position;
            return true;
        }
        return false;
    }

    bool IsSpotTakenByOtherOfficer(Vector3 position)
    {
        foreach (PoliceAI other in all)
        {
            if (other == this || !other.hasSpot) continue;
            if (FlatDistance(other.spot, position) < 2.5f) return true;
        }
        return false;
    }

    void StopMoving()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
        }
    }

    void FaceVampire()
    {
        Vector3 look = vampire.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), aimTurnSpeed * Time.deltaTime);
    }

    // ------------------------------------------------------------ shooting

    Vector3 GunPosition()
    {
        return gun != null ? gun.position : transform.position + Vector3.up * 1.3f;
    }

    Vector3 AimPoint()
    {
        return vampireCollider != null ? vampireCollider.bounds.center : vampire.position + Vector3.up * 0.5f;
    }

    /// <summary>True if nothing (walls, cars, other officers) is between the two points.</summary>
    bool ClearShot(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < 0.01f) return true;

        RaycastHit[] hits = Physics.RaycastAll(from, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(transform) || t.IsChildOf(vampire)) continue;
            return false;
        }
        return true;
    }

    AudioSource shotSource;

    // Gunshots must carry across the street; PlayClipAtPoint's default log rolloff made them near-silent beyond ~15 m.
    void PlayShot()
    {
        if (shotSource == null)
        {
            shotSource = gameObject.AddComponent<AudioSource>();
            shotSource.playOnAwake = false;
            shotSource.spatialBlend = 1f;
            shotSource.rolloffMode = AudioRolloffMode.Linear;
            shotSource.minDistance = 10f;
            shotSource.maxDistance = 120f;
        }
        shotSource.pitch = Random.Range(0.94f, 1.06f);
        shotSource.PlayOneShot(shotSound, shotVolume);
    }

    void Fire(Vector3 aim)
    {
        nextFireTime = Time.time + fireInterval;
        Vector3 origin = GunPosition();
        bool hit = Random.value <= hitChance;
        Vector3 end = aim;

        if (hit)
        {
            // The vampire flinches when a bullet lands (the controller decides if the reaction plays).
            if (vampireController != null) vampireController.OnHurt();
            if (vampireBlood != null) vampireBlood.TakeDamage(damage);
            else if (vampireHealth != null) vampireHealth.TakeDamage(damage);
            else if (!warnedNoHealth)
            {
                warnedNoHealth = true;
                Debug.LogWarning("The vampire has no VampireBlood or VampireHealth component, so shots do no damage.", this);
            }
        }
        else
        {
            // A miss flies past him.
            Vector3 missPoint = aim + Random.onUnitSphere * 0.8f;
            Vector3 dir = (missPoint - origin).normalized;
            end = origin + dir * (Vector3.Distance(origin, aim) + 3f);
        }

        ShowShot(origin, end);
        if (shotSound != null) PlayShot();
    }

    void BuildEffects()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

        GameObject tracerObject = new GameObject("Tracer");
        tracerObject.transform.SetParent(transform, false);
        tracer = tracerObject.AddComponent<LineRenderer>();
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.startWidth = 0.04f;
        tracer.endWidth = 0.02f;
        tracer.material = new Material(shader);
        tracer.startColor = new Color(1f, 0.9f, 0.5f, 1f);
        tracer.endColor = new Color(1f, 0.6f, 0.2f, 0.4f);
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tracer.receiveShadows = false;
        tracer.enabled = false;

        GameObject flashObject = new GameObject("MuzzleFlash");
        flashObject.transform.SetParent(gun != null ? gun : transform, false);
        muzzleFlash = flashObject.AddComponent<Light>();
        muzzleFlash.type = LightType.Point;
        muzzleFlash.color = new Color(1f, 0.8f, 0.4f);
        muzzleFlash.range = 5f;
        muzzleFlash.intensity = 4f;
        muzzleFlash.shadows = LightShadows.None;
        muzzleFlash.enabled = false;
    }

    void ShowShot(Vector3 from, Vector3 to)
    {
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;
        muzzleFlash.transform.position = from;
        muzzleFlash.enabled = true;
        tracerHideTime = Time.time + 0.07f;

        if (animator != null)
        {
            animator.ResetTrigger(ShootHash);
            animator.SetTrigger(ShootHash);
        }
    }

    // ------------------------------------------------------------ bitten / killed

    /// <summary>Called by the vampire when he grabs this officer. The officer stops, faces him and cannot shoot.</summary>
    public void OnCaptured(Transform biter)
    {
        StopAllCoroutines();
        currentState = State.Captured;
        StopMoving();
        if (agent.enabled) agent.enabled = false;
        SetCollidersEnabled(false);
        HideShot();

        Vector3 look = biter.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
    }

    /// <summary>Called if the vampire lets go before the bite finishes (for example he is shot dead).</summary>
    public void OnReleased()
    {
        if (currentState != State.Captured) return;
        SetCollidersEnabled(true);
        agent.enabled = true;
        engaged = true;
        currentState = agent.isOnNavMesh ? State.Moving : State.Idle;
        hasSpot = false;
    }

/// <summary>Called when the vampire hits this officer in a fight. Wears his health down (never to 0 - only a bite kills him), staggers him and shoves him back.</summary>
public void TakeHit(float amount, Transform attacker)
    {
        if (currentState == State.Dead || currentState == State.Captured) return;
        if (health < 0f) health = maxHealth;

        // A hit never kills: it only wears him down, and always leaves a little health.
        health = Mathf.Max(health - amount, Mathf.Min(1f, maxHealth));

        // Being hit makes the officer notice the vampire even if he was standing still.
        engaged = true;
        lastMoveTime = Time.time;

        // Stagger: no shooting or walking while the hit reaction plays, and a small push away from the attacker.
        nextFireTime = Mathf.Max(nextFireTime, Time.time + hitStaggerTime);
        staggerUntil = Time.time + 0.3f; // bridges the frames before the reaction state begins
        PlayHitAnimation();
        if (attacker != null && knockbackDistance > 0f && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            Vector3 push = transform.position - attacker.position;
            push.y = 0f;
            if (push.sqrMagnitude > 0.001f) StartCoroutine(Knockback(push.normalized * knockbackDistance));
        }
    }

    IEnumerator Knockback(Vector3 total)
    {
        const float duration = 0.15f;
        float t = 0f;
        while (t < duration && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(total * (Time.deltaTime / duration));
            t += Time.deltaTime;
            yield return null;
        }
    }

/// <summary>Plays the officer's "taking a punch" animation, not more often than hitRetriggerDelay.</summary>
    void PlayHitAnimation()
    {
        if (animator == null || !hasHitAnim || Time.time < nextHitAnimTime) return;
        nextHitAnimTime = Time.time + hitRetriggerDelay;
        animator.ResetTrigger(HitHash);
        animator.SetTrigger(HitHash);
    }

    /// <summary>True while the hit reaction is playing (or about to).</summary>
    bool IsInHitReaction()
    {
        if (animator == null || !hasHitAnim) return false;
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Hit")) return true;
        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Hit");
    }



    /// <summary>Called by the vampire when the bite kills this officer.</summary>
    public void Die()
    {
        if (currentState == State.Dead) return;
        StopAllCoroutines();
        currentState = State.Dead;
        StopMoving();
        if (agent.enabled) agent.enabled = false;
        SetCollidersEnabled(false);
        HideShot();
        if (animator != null && hasHitAnim) animator.ResetTrigger(HitHash); // a stale hit reaction must never play over the corpse
        if (animator != null) animator.SetTrigger(DieHash);
        else StartCoroutine(Collapse());
    }

    // Placeholder until the officers get a death animation: tip the body over backwards.
    IEnumerator Collapse()
    {
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(-90f, 0f, 0f);
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * 0.12f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.6f;
            float k = Mathf.Clamp01(t);
            k *= k; // accelerate like a fall
            transform.SetPositionAndRotation(Vector3.Lerp(startPos, endPos, k), Quaternion.Slerp(start, end, k));
            yield return null;
        }
    }

    void SetCollidersEnabled(bool value)
    {
        foreach (Collider c in ownColliders)
            if (c != null) c.enabled = value;
    }

    void HideShot()
    {
        if (tracer != null) tracer.enabled = false;
        if (muzzleFlash != null) muzzleFlash.enabled = false;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }


/// <summary>The vampire is closing in to fight: stand still (no walking, no shooting) for a moment so he can reach you.</summary>
    public void HoldForFight(float seconds)
    {
        staggerUntil = Mathf.Max(staggerUntil, Time.time + seconds);
    }
}