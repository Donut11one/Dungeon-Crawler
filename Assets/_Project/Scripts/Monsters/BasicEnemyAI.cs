using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy AI with aggro range detection, chase, wander, and attack behaviors.
/// Server-authoritative: only the server runs AI decision logic.
/// Syncs aggro state across the network for visual feedback (e.g. glowing red eyes).
/// Pauses when ragdolled or dead.
/// </summary>
public class BasicEnemyAI : NetworkBehaviour
{
    [Header("Behavior")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private bool canAttack = true;

    [Header("Aggro & Detection")]
    [SerializeField] private float aggroRange = 12f;
    [SerializeField] private float loseTargetRange = 18f;
    [SerializeField] private float directProximityRange = 4.5f;
    [SerializeField] private float chaseSpeed = 4.2f;
    [SerializeField] private float wanderSpeed = 2.2f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1.8f;
    [SerializeField] private int attackDamage = 20;
    [SerializeField] private float attackKnockback = 8f;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 7f;
    [SerializeField] private float wanderInterval = 3.5f;

    public NetworkVariable<bool> IsAggroed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NavMeshAgent agent;
    private RagdollController ragdollController;
    private EnemyHealth enemyHealth;
    private float attackTimer;
    private float wanderTimer;
    private Transform currentTarget;

    private readonly List<Renderer> eyeRenderers = new();
    private Color originalEyeColor = Color.yellow;
    private readonly Color aggroEyeColor = new Color(1f, 0.1f, 0.1f);

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ragdollController = GetComponent<RagdollController>();
        enemyHealth = GetComponent<EnemyHealth>();

        CacheEyeRenderers();
    }

    private void CacheEyeRenderers()
    {
        eyeRenderers.Clear();
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in allRenderers)
        {
            if (rend.gameObject.name.Contains("Eye"))
            {
                eyeRenderers.Add(rend);
                if (rend.material != null)
                {
                    originalEyeColor = rend.material.color;
                }
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        IsAggroed.OnValueChanged += OnAggroStateChanged;

        // Apply current visual state on spawn
        UpdateEyeVisuals(IsAggroed.Value);
    }

    public override void OnNetworkDespawn()
    {
        IsAggroed.OnValueChanged -= OnAggroStateChanged;
    }

    private void OnAggroStateChanged(bool previous, bool current)
    {
        UpdateEyeVisuals(current);
    }

    private void UpdateEyeVisuals(bool aggroed)
    {
        Color targetColor = aggroed ? aggroEyeColor : originalEyeColor;
        foreach (Renderer rend in eyeRenderers)
        {
            if (rend != null && rend.material != null)
            {
                rend.material.color = targetColor;
            }
        }
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        // Don't act while dead
        if (enemyHealth != null && enemyHealth.IsDead.Value)
        {
            if (agent != null && agent.enabled)
            {
                agent.enabled = false;
            }
            if (IsAggroed.Value)
            {
                IsAggroed.Value = false;
            }
            return;
        }

        // Don't act while ragdolled
        if (ragdollController != null && ragdollController.IsRagdolled.Value)
        {
            return;
        }

        if (!canMove && !canAttack)
        {
            return;
        }

        attackTimer -= Time.deltaTime;

        // Re-evaluate target
        EvaluateTarget();

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

    private void EvaluateTarget()
    {
        // 1. If currently aggroed on a target, check if we should lose them
        if (currentTarget != null)
        {
            PlayerHealth targetHealth = currentTarget.GetComponent<PlayerHealth>();
            if (targetHealth == null || targetHealth.IsDead.Value)
            {
                // Target died or disappeared
                currentTarget = null;
                IsAggroed.Value = false;
            }
            else
            {
                float distToTarget = Vector3.Distance(transform.position, currentTarget.position);
                if (distToTarget > loseTargetRange)
                {
                    // Target escaped beyond aggro leash
                    currentTarget = null;
                    IsAggroed.Value = false;
                }
            }
        }

        // 2. If no target, scan for nearby living players within aggro range
        if (currentTarget == null)
        {
            Transform closestPlayer = FindAggroablePlayer();
            if (closestPlayer != null)
            {
                currentTarget = closestPlayer;
                IsAggroed.Value = true;
            }
            else
            {
                if (IsAggroed.Value)
                {
                    IsAggroed.Value = false;
                }
            }
        }
    }

    private Transform FindAggroablePlayer()
    {
        if (NetworkManager.Singleton == null)
        {
            return null;
        }

        Transform closest = null;
        float closestDist = float.MaxValue;
        Vector3 eyePos = transform.position + Vector3.up * 1.2f;

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

            Transform playerT = client.PlayerObject.transform;
            float dist = Vector3.Distance(transform.position, playerT.position);

            // Outside aggro range
            if (dist > aggroRange)
            {
                continue;
            }

            // Direct proximity hearing: instantly detect if within directProximityRange
            bool detected = dist <= directProximityRange;

            // Otherwise, require line of sight (no solid walls blocking vision)
            if (!detected)
            {
                Vector3 playerTargetPos = playerT.position + Vector3.up * 1.0f;
                Vector3 toPlayer = playerTargetPos - eyePos;
                if (!Physics.Raycast(eyePos, toPlayer.normalized, dist, ~0, QueryTriggerInteraction.Ignore))
                {
                    detected = true;
                }
                else
                {
                    // If raycast hit something, verify if it was the player or a child collider
                    if (Physics.Raycast(eyePos, toPlayer.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.collider != null && hit.collider.transform.IsChildOf(playerT))
                        {
                            detected = true;
                        }
                    }
                }
            }

            if (detected && dist < closestDist)
            {
                closestDist = dist;
                closest = playerT;
            }
        }

        return closest;
    }

    /// <summary>
    /// Called when the enemy takes damage. Immediately alerts and aggros onto the nearest player.
    /// </summary>
    public void AlertToAttacker(Vector3 hitDirection)
    {
        if (!IsServer)
        {
            return;
        }

        Transform closest = FindAggroablePlayer();
        if (closest != null)
        {
            currentTarget = closest;
            IsAggroed.Value = true;
        }
    }

    private void Chase()
    {
        if (!canMove || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (currentTarget != null)
        {
            agent.speed = chaseSpeed;
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
            hitDir.y = 0.35f; // Upward knockback pop
            damageable.TakeDamage(attackDamage, hitDir * attackKnockback);
        }
    }

    private void Wander()
    {
        if (!canMove || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.speed = wanderSpeed;

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
