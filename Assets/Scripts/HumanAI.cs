using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class HumanAI : MonoBehaviour
{
    public enum State { Idle, Walking, Alerted, Fleeing, Captured, Dead }

    [Header("Current State")]
    public State currentState = State.Idle;

    [Header("Speed Settings (Difficulty)")]
    [Tooltip("Base walking speed for wandering around.")]
    public float walkSpeed = 2.0f;
    [Tooltip("Speed when running away from the vampire.")]
    public float fleeSpeed = 5.0f;

    [Header("Detection Settings")]
    public Transform vampirePlayer;
    public float detectionRadius = 10f;
    [Range(0, 360)] public float fieldOfViewAngle = 110f;
    [Tooltip("Layers that block this human's sight (buildings, walls, cars). Never include the Player layer.")]
    public LayerMask obstacleMask = 1; // Default layer
    [Tooltip("Short reaction time (seconds) between spotting the vampire and starting to run.")]
    public float detectionDelay = 0.25f;
    [Tooltip("Inside this distance the human senses the vampire even outside the field of view (behind/side), and reacts instantly.")]
    public float closeAwarenessRadius = 4f;
    [Tooltip("Height of the eyes, and of the point on the vampire they look at.")]
    public float eyeHeight = 1.6f;
    public float targetHeight = 1.0f;

    [Header("Health (melee fights)")]
    [Tooltip("The human's health. The vampire's fight attacks wear it down but never take it to 0 - only a bite kills him.")]
    public float maxHealth = 40f;
    [Tooltip("Seconds a human stays dazed in place after being hit, before he runs.")]
    public float hitStunTime = 1.2f;
    [Tooltip("How far a hit pushes the human back.")]
    public float knockbackDistance = 0.05f;
    [Tooltip("Minimum seconds between two hit-reaction animations, so a fast combo does not restart the reaction on every blow.")]
    public float hitRetriggerDelay = 1.0f;

    [Header("Wander/Idle Timing")]
    public float wanderRadius = 10f;
    public float walkDuration = 5f;
    public float idleDuration = 3f;

    private NavMeshAgent agent;
    private Animator animator;
    
    private float fleeRepathTimer;
private float stateTimer;
    private float health = -1f; // -1 = not set yet, becomes maxHealth on the first hit
    private static readonly int HitHash = Animator.StringToHash("Hit");
    private bool hasHitAnim;
    private float nextHitAnimTime;

    /// <summary>False once the vampire has killed this human.</summary>
    public bool IsAlive => currentState != State.Dead;
    /// <summary>True if the vampire is allowed to grab this human.</summary>
    /// <summary>True once the vampire has turned this human into his thrall (see ThrallAbility): he fights for the vampire and can no longer be hit or bitten by him.</summary>
    public bool IsThrall { get; set; }
    public bool CanBeBitten => currentState != State.Captured && currentState != State.Dead && !IsThrall;
    /// <summary>True if the vampire can hit this human in a fight.</summary>
    public bool CanBeHit => CanBeBitten;
    /// <summary>Health as a fraction of maxHealth (1 = untouched). Read by EntityHealthBar.</summary>
    public float HealthFraction => health < 0f ? 1f : Mathf.Clamp01(health / Mathf.Max(maxHealth, 0.01f));


    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        hasHitAnim = HasAnimatorParameter("Hit");
    }

    private void Start()
    {
        if (vampirePlayer == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) vampirePlayer = playerObj.transform;
        }

        SetIdle();
    }

    private void Update()
    {
        // Grabbed or dead humans are driven by the vampire, not by this state machine.
        if (currentState == State.Captured || currentState == State.Dead) return;

        // 1. Check line of sight to Vampire (only during Idle or Walking)
        if ((currentState == State.Idle || currentState == State.Walking) && CheckLineOfSight())
        {
            StartCoroutine(AlertAndFleeSequence());
        }

        // 2. State Machine behavior
        switch (currentState)
        {
            case State.Idle:
                HandleIdleState();
                break;

            case State.Walking:
                HandleWalkingState();
                break;

            case State.Alerted:
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                break;

            case State.Fleeing:
                HandleFleeing();
                break;
        }

        // 3. Update Animator Speed parameter continuously
        float currentSpeed = agent.velocity.magnitude;
        animator.SetFloat("Speed", currentSpeed);
    }

    private void SetIdle()
    {
        currentState = State.Idle;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        stateTimer = 0f;
    }

    private void SetNewWanderDestination()
    {
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            currentState = State.Walking;
            agent.speed = walkSpeed;
            agent.isStopped = false;
            agent.SetDestination(hit.position);
            stateTimer = 0f;
        }
    }

    private void HandleIdleState()
    {
        stateTimer += Time.deltaTime;
        if (stateTimer >= idleDuration)
        {
            SetNewWanderDestination();
        }
    }

    private void HandleWalkingState()
    {
        stateTimer += Time.deltaTime;

        // Give the NavMeshAgent a moment (0.2s) to compute the path before checking arrival
        if (stateTimer > 0.2f && !agent.pathPending)
        {
            bool reachedDestination = agent.remainingDistance <= agent.stoppingDistance;
            bool timeExpired = stateTimer >= walkDuration;

            if (reachedDestination || timeExpired)
            {
                SetIdle();
            }
        }
    }

private bool CheckLineOfSight()
    {
        if (vampirePlayer == null) return false;

        Vector3 directionToPlayer = vampirePlayer.position - transform.position;
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer <= detectionRadius)
        {
            // Very close: the human notices the vampire even if he is behind or beside them.
            bool inCloseRange = distanceToPlayer <= closeAwarenessRadius;
            float angle = Vector3.Angle(transform.forward, directionToPlayer);
            if (inCloseRange || angle <= fieldOfViewAngle / 2f)
            {
                // Eye to body: the ray must not end inside the floor, and walls on obstacleMask block it.
                Vector3 eye = transform.position + Vector3.up * eyeHeight;
                Vector3 body = vampirePlayer.position + Vector3.up * targetHeight;
                Vector3 toBody = body - eye;
                bool blocked = Physics.Raycast(eye, toBody.normalized, out RaycastHit hit, toBody.magnitude, obstacleMask, QueryTriggerInteraction.Ignore)
                               && !hit.transform.IsChildOf(vampirePlayer); // hitting the vampire himself is not a wall
                if (!blocked)
                {
                    return true;
                }
            }
        }
        return false;
    }

private IEnumerator AlertAndFleeSequence()
    {
        currentState = State.Alerted;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        // Brief reaction time. If the vampire is already close, run immediately.
        bool vampireClose = vampirePlayer != null &&
            Vector3.Distance(transform.position, vampirePlayer.position) <= closeAwarenessRadius;
        if (!vampireClose && detectionDelay > 0f)
        {
            yield return new WaitForSeconds(detectionDelay);
        }

        // Transition to Fleeing
        currentState = State.Fleeing;
        fleeRepathTimer = 0f; // pick a flee path right away
        agent.speed = fleeSpeed;
        agent.isStopped = false; // Ensure agent is explicitly un-paused
    }

private void HandleFleeing()
    {
        if (vampirePlayer == null) return;

        agent.isStopped = false;

        // Calling SetDestination every frame cancels the path that is still being computed, so the agent
        // never receives a path and stands still. Pick a flee point only every so often, or on arrival.
        fleeRepathTimer -= Time.deltaTime;
        bool arrived = agent.hasPath && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f;
        if (fleeRepathTimer > 0f && !arrived) return;
        fleeRepathTimer = 0.5f;

        // Flat direction away from the vampire (ignore height differences)
        Vector3 awayDir = transform.position - vampirePlayer.position;
        awayDir.y = 0f;
        if (awayDir.sqrMagnitude < 0.01f) awayDir = -transform.forward;
        awayDir.Normalize();

        float currentDist = Vector3.Distance(transform.position, vampirePlayer.position);
        float[] angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f };
        NavMeshPath path = new NavMeshPath();

        foreach (float angle in angles)
        {
            Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * awayDir * 15f;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas)) continue;

            // The point must take the human farther from the vampire and be fully reachable.
            if (Vector3.Distance(hit.position, vampirePlayer.position) < currentDist + 3f) continue;
            if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                agent.SetPath(path);
                return;
            }
        }

        // Nothing better found: keep moving away on whatever the NavMesh offers.
        if (NavMesh.SamplePosition(transform.position + awayDir * 15f, out NavMeshHit fallback, 15f, NavMesh.AllAreas))
        {
            agent.SetDestination(fallback.position);
        }
    }

    /// <summary>Called by the vampire when he grabs this human. Stops all movement and faces the biter.</summary>
    public void OnCaptured(Transform biter)
    {
        StopAllCoroutines();
        currentState = State.Captured;
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
        }
        agent.enabled = false;
        animator.SetFloat("Speed", 0f);

        Vector3 look = biter.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
    }

    /// <summary>Called if the vampire lets go before the bite finishes (for example he is killed). The human runs.</summary>
    public void OnReleased()
    {
        if (currentState != State.Captured) return;
        agent.enabled = true;
        if (agent.isOnNavMesh)
        {
            currentState = State.Fleeing;
            agent.speed = fleeSpeed;
            agent.isStopped = false;
        }
        else
        {
            SetIdle();
        }
    }

/// <summary>Called when the vampire hits this human in a fight. Wears his health down (never to 0 - only a bite kills him), shoves him back and leaves him dazed.</summary>
public void TakeHit(float amount, Transform attacker)
    {
        if (currentState == State.Dead || currentState == State.Captured) return;
        if (health < 0f) health = maxHealth;

        // A hit never kills: it only wears him down, and always leaves a little health.
        health = Mathf.Max(health - amount, Mathf.Min(1f, maxHealth));

        Vector3 away = Vector3.zero;
        if (attacker != null)
        {
            away = transform.position - attacker.position;
            away.y = 0f;
        }

        // Dazed in place while the hit reaction plays (so the rest of the combo can land), then he runs.
        FreezeInFear(hitStunTime);
        PlayHitAnimation();
        if (away.sqrMagnitude > 0.001f && knockbackDistance > 0f) StartCoroutine(Knockback(away.normalized * knockbackDistance));
    }

    /// <summary>Holds the human in place, terrified, for a few seconds before he runs. Used while the vampire closes in for a fight.</summary>
    public void FreezeInFear(float seconds)
    {
        if (currentState == State.Dead || currentState == State.Captured) return;
        StopAllCoroutines();
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
        currentState = State.Alerted;
        StartCoroutine(FleeAfter(seconds));
    }

private IEnumerator FleeAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // Do not run off in the middle of the hit reaction (it is a stooped, staggering pose).
        float guard = 3f;
        while (guard > 0f && IsInHitReaction())
        {
            guard -= Time.deltaTime;
            yield return null;
        }

        if (currentState != State.Alerted || !agent.enabled || !agent.isOnNavMesh) yield break;
        currentState = State.Fleeing;
        fleeRepathTimer = 0f; // pick a flee path right away
        agent.speed = fleeSpeed;
        agent.isStopped = false;
    }

    private IEnumerator Knockback(Vector3 total)
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

/// <summary>Plays this character's own "getting hit" animation (punch or uppercut), not more often than hitRetriggerDelay.</summary>
    private void PlayHitAnimation()
    {
        if (!hasHitAnim || Time.time < nextHitAnimTime) return;
        nextHitAnimTime = Time.time + hitRetriggerDelay;
        animator.ResetTrigger(HitHash);
        animator.SetTrigger(HitHash);
    }

    /// <summary>True while the hit reaction is playing (or about to).</summary>
    private bool IsInHitReaction()
    {
        if (!hasHitAnim) return false;
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Hit")) return true;
        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Hit");
    }



    /// <summary>Called by the vampire when the bite kills this human.</summary>
public void OnKilled()
    {
        if (currentState == State.Dead) return;
        StopAllCoroutines();
        currentState = State.Dead;
        agent.enabled = false;

        if (HasAnimatorParameter("Die"))
        {
            // Play the character's real death animation (it stays on its last frame, lying on the ground).
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.SetFloat("Speed", 0f);
            if (hasHitAnim) animator.ResetTrigger(HitHash); // a stale hit reaction must never play over the corpse
            animator.ResetTrigger("Die");
            animator.SetTrigger("Die");
        }
        else
        {
            // Controller has no death state: fall back to the old tip-over.
            StartCoroutine(Collapse());
        }
    }

private bool HasAnimatorParameter(string paramName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger) return true;
        }
        return false;
    }


    // Placeholder until the humans get a death animation: freeze the pose and tip the body over backwards.
    private IEnumerator Collapse()
    {
        animator.enabled = false;
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(-90f, 0f, 0f);
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * 0.12f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.6f;
            float k = t * t; // accelerate like a fall
            transform.SetPositionAndRotation(Vector3.Lerp(startPos, endPos, k), Quaternion.Slerp(start, end, k));
            yield return null;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
