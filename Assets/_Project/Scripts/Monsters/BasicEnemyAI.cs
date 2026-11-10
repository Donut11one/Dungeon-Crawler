using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Simple enemy AI with stationary dummy or Wander/Chase states.
/// Server-authoritative: only the host runs AI logic.
/// Disables when ragdolled.
/// </summary>
public class BasicEnemyAI : NetworkBehaviour
{
    [Header("Behavior")]
    [SerializeField] private bool canMove = false;
    [SerializeField] private bool canAttack = false;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 12f;
    [SerializeField] private float loseTargetRange = 18f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 2.5f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private int attackDamage = 20;
    [SerializeField] private float attackKnockback = 8f;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 8f;
    [SerializeField] private float wanderInterval = 4f;

    private NavMeshAgent agent;
    private RagdollController ragdollController;
    private float attackTimer;
    private float wanderTimer;
    private Transform currentTarget;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ragdollController = GetComponent<RagdollController>();
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (!canMove && !canAttack)
        {
            return;
        }

        // Don't do anything while ragdolled
        if (ragdollController != null && ragdollController.IsRagdolled.Value)
        {
            return;
        }

        attackTimer -= Time.deltaTime;

        // Find closest player
        Transform closestPlayer = FindClosestPlayer();
        float distToClosest = closestPlayer != null
            ? Vector3.Distance(transform.position, closestPlayer.position)
            : float.MaxValue;

        // State logic
        if (currentTarget != null)
        {
            float distToTarget = Vector3.Distance(transform.position, currentTarget.position);

            // Lose target if too far
            if (distToTarget > loseTargetRange)
            {
                currentTarget = null;
            }
        }

        // Acquire target
        if (currentTarget == null && closestPlayer != null && distToClosest <= detectionRange)
        {
            currentTarget = closestPlayer;
        }

        if (currentTarget != null)
        {
            Chase();

            float dist = Vector3.Distance(transform.position, currentTarget.position);
            if (dist <= attackRange && attackTimer <= 0f)
            {
                Attack();
            }
        }
        else
        {
            Wander();
        }
    }

    private Transform FindClosestPlayer()
    {
        Transform closest = null;
        float closestDist = float.MaxValue;

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
            {
                continue;
            }

            // Skip dead players
            PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead.Value)
            {
                continue;
            }

            float dist = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = client.PlayerObject.transform;
            }
        }

        return closest;
    }

    private void Chase()
    {
        if (!canMove || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (currentTarget != null)
        {
            agent.SetDestination(currentTarget.position);
        }
    }

    private void Attack()
    {
        if (!canAttack)
        {
            return;
        }

        attackTimer = attackCooldown;

        if (currentTarget == null)
        {
            return;
        }

        IDamageable damageable = currentTarget.GetComponent<IDamageable>();
        if (damageable != null)
        {
            Vector3 hitDir = (currentTarget.position - transform.position).normalized;
            hitDir.y = 0.3f; // Slight upward hit
            damageable.TakeDamage(attackDamage, hitDir * attackKnockback);
        }
    }

    private void Wander()
    {
        if (!canMove || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f)
        {
            wanderTimer = wanderInterval;

            Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
            randomDir += transform.position;

            if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }
    }
}
