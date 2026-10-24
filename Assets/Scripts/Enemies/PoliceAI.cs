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
    /// <summary>True if the vampire is allowed to grab this officer.</summary>
    public bool CanBeBitten => currentState != State.Captured && currentState != State.Dead;

    NavMeshAgent agent;
    Collider[] ownColliders;
    VampireHealth vampireHealth;
    Collider vampireCollider;
    LineRenderer tracer;
    Light muzzleFlash;
    Animator animator;
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int AimingHash = Animator.StringToHash("Aiming");
    static readonly int ShootHash = Animator.StringToHash("Shoot");
    static readonly int DieHash = Animator.StringToHash("Die");
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

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        currentState = State.Idle; // always start calm, whatever was saved in the scene
        ownColliders = GetComponentsInChildren<Collider>(true);
        if (gun == null) gun = transform.Find("Gun");
        BuildEffects();
        animator = GetComponentInChildren<Animator>();
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
            vampireCollider = vampire.GetComponent<Collider>();
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

    void Fire(Vector3 aim)
    {
        nextFireTime = Time.time + fireInterval;
        Vector3 origin = GunPosition();
        bool hit = Random.value <= hitChance;
        Vector3 end = aim;

        if (hit)
        {
            if (vampireHealth != null) vampireHealth.TakeDamage(damage);
            else if (!warnedNoHealth)
            {
                warnedNoHealth = true;
                Debug.LogWarning("The vampire has no VampireHealth component, so shots do no damage.", this);
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
        if (shotSound != null) AudioSource.PlayClipAtPoint(shotSound, origin, shotVolume);
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
}