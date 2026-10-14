using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class HumanAI : MonoBehaviour
{
    public enum State { Idle, Walking, Alerted, Fleeing }

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
    public LayerMask obstacleMask;
    public float detectionDelay = 5.0f;

    [Header("Wander/Idle Timing")]
    public float wanderRadius = 10f;
    public float walkDuration = 5f;
    public float idleDuration = 3f;

    private NavMeshAgent agent;
    private Animator animator;
    private float stateTimer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
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
            float angle = Vector3.Angle(transform.forward, directionToPlayer);
            if (angle <= fieldOfViewAngle / 2f)
            {
                if (!Physics.Raycast(transform.position + Vector3.up, directionToPlayer.normalized, distanceToPlayer, obstacleMask))
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

        // Wait 5 seconds
        yield return new WaitForSeconds(detectionDelay);

        // Transition to Fleeing
        currentState = State.Fleeing;
        agent.speed = fleeSpeed;
        agent.isStopped = false; // Ensure agent is explicitly un-paused
    }

    private void HandleFleeing()
    {
        if (vampirePlayer == null) return;

        // Ensure agent is unstopped during fleeing updates
        agent.isStopped = false;

        // Calculate flee direction away from vampire
        Vector3 runDirection = (transform.position - vampirePlayer.position).normalized;
        Vector3 fleeTarget = transform.position + runDirection * 15f;

        // Check if fleeTarget is valid on the NavMesh
        if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, 15f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            // Fallback: If running straight back hits an obstacle/edge, pick a random spot far away
            Vector3 randomFlee = transform.position + (Quaternion.Euler(0, Random.Range(-45, 45), 0) * runDirection) * 15f;
            if (NavMesh.SamplePosition(randomFlee, out NavMeshHit fallbackHit, 15f, NavMesh.AllAreas))
            {
                agent.SetDestination(fallbackHit.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}