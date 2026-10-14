using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class HumanAI : MonoBehaviour
{
    public enum State { Idle, Alerted, Fleeing }

    [Header("Current State")]
    public State currentState = State.Idle;

    [Header("Speed Settings (Difficulty)")]
    [Tooltip("Speed when running away from the vampire.")]
    public float fleeSpeed = 5.0f;

    [Header("Detection Settings")]
    public Transform vampirePlayer;
    public float detectionRadius = 10f;
    [Range(0, 360)] public float fieldOfViewAngle = 110f;
    public LayerMask obstacleMask;
    public float detectionDelay = 5.0f;

    private NavMeshAgent agent;
    private Animator animator;

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

        // Start in complete Idle
        SetIdle();
    }

    private void Update()
    {
        // 1. Check line of sight to Vampire if not already fleeing or alerted
        if (currentState == State.Idle && CheckLineOfSight())
        {
            StartCoroutine(AlertAndFleeSequence());
        }

        // 2. Handle state behavior
        switch (currentState)
        {
            case State.Idle:
            case State.Alerted:
                // Ensure zero physical movement and clear pathing to stop sliding
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                break;

            case State.Fleeing:
                HandleFleeing();
                break;
        }

        // 3. Drive Animator with physical velocity magnitude
        float currentSpeed = agent.velocity.magnitude;
        animator.SetFloat("Speed", currentSpeed);
    }

    private void SetIdle()
    {
        currentState = State.Idle;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
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

        // 5-second frozen interval before fleeing
        yield return new WaitForSeconds(detectionDelay);

        // Transition to Fleeing
        currentState = State.Fleeing;
        agent.isStopped = false;
        agent.speed = fleeSpeed;
    }

    private void HandleFleeing()
    {
        if (vampirePlayer == null) return;

        // Run directly away from vampire
        Vector3 runDirection = transform.position - vampirePlayer.position;
        Vector3 fleeTarget = transform.position + runDirection.normalized * 10f;

        if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Vision cone visualization in Scene view
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}