using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

/// <summary>
/// Enemy AI with aggro range detection, combat state transitions, and distinct visual states.
/// Server-authoritative: only the server runs AI decision logic.
/// Synchronizes CombatState across the network to drive visual color changes
/// (Idle, Chasing, Attacking, Cooldown), making future animation hooking seamless.
/// </summary>
public class BasicEnemyAI : NetworkBehaviour
{
    public enum CombatState
    {
        Idle = 0,
        Chasing = 1,
        Attacking = 2,
        Cooldown = 3
    }

    [Header("Behavior")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private bool canAttack = true;

    [Header("Aggro & Detection")]
    [SerializeField] private float aggroRange = 12f;
    [SerializeField] private float loseTargetRange = 18f;
    [SerializeField] private float directProximityRange = 4.5f;
    [SerializeField] private float chaseSpeed = 4.2f;
    [SerializeField] private float wanderSpeed = 2.2f;

    [Header("Attack & Cooldown")]
    [SerializeField] private float attackRange = 2.2f;
    [SerializeField] private float attackCooldown = 10.0f;
    [SerializeField] private float attackWindup = 0.35f;
    [SerializeField] private float attackRecovery = 1.2f;
    [SerializeField] private int attackDamage = 20;
    [SerializeField] private float attackKnockback = 8f;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 7f;
    [SerializeField] private float wanderInterval = 3.5f;

    [Header("State Visual Colors")]
    [SerializeField] private Color idleBodyColor = new Color(0.55f, 0.15f, 0.15f);
    [SerializeField] private Color idleEyeColor = Color.yellow;

    [SerializeField] private Color chaseBodyColor = new Color(0.95f, 0.35f, 0.05f);
    [SerializeField] private Color chaseEyeColor = new Color(1f, 0.05f, 0.05f);

    [SerializeField] private Color attackBodyColor = new Color(1f, 0.1f, 0.45f);
    [SerializeField] private Color attackEyeColor = new Color(1f, 1f, 0.3f);

    [SerializeField] private Color cooldownBodyColor = new Color(0.2f, 0.5f, 0.85f);
    [SerializeField] private Color cooldownEyeColor = new Color(0.2f, 0.85f, 1f);

    public NetworkVariable<CombatState> CurrentCombatState = new NetworkVariable<CombatState>(
        CombatState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsAggroed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Event fired whenever the combat state transitions.
    /// Hook future Animator triggers or sound effects here.
    /// </summary>
    public event Action<CombatState> OnCombatStateChangedEvent;

    private NavMeshAgent agent;
    private RagdollController ragdollController;
    private EnemyHealth enemyHealth;
    private float attackTimer;
    private float attackPauseTimer;
    private float windupTimer;
    private bool isAttacking;
    private float wanderTimer;
    private Transform currentTarget;

    private Renderer bodyRenderer;
    private readonly List<Renderer> eyeRenderers = new();
    private MaterialPropertyBlock propBlock;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ragdollController = GetComponent<RagdollController>();
        enemyHealth = GetComponent<EnemyHealth>();
        propBlock = new MaterialPropertyBlock();

        CacheRenderers();
    }

    private void CacheRenderers()
    {
        eyeRenderers.Clear();
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in allRenderers)
        {
            if (rend.gameObject.name.Contains("Eye"))
            {
                eyeRenderers.Add(rend);
            }
            else if (rend.gameObject.name.Contains("Body"))
            {
                bodyRenderer = rend;
            }
        }

        if (bodyRenderer == null)
        {
            bodyRenderer = GetComponent<Renderer>();
        }
    }

    public override void OnNetworkSpawn()
    {
        CurrentCombatState.OnValueChanged += OnCombatStateChanged;

        // Apply current visual state on spawn
        UpdateStateVisuals(CurrentCombatState.Value);
    }

    public override void OnNetworkDespawn()
    {
        CurrentCombatState.OnValueChanged -= OnCombatStateChanged;
    }

    private void OnCombatStateChanged(CombatState previous, CombatState current)
    {
        UpdateStateVisuals(current);
        OnCombatStateChangedEvent?.Invoke(current);
    }

    private void SetCombatState(CombatState newState)
    {
        if (CurrentCombatState.Value != newState)
        {
            CurrentCombatState.Value = newState;
            IsAggroed.Value = (newState != CombatState.Idle);
        }
    }

    private void UpdateStateVisuals(CombatState state)
    {
        Color bodyColor;
        Color eyeColor;

        switch (state)
        {
            case CombatState.Attacking:
                bodyColor = attackBodyColor;
                eyeColor = attackEyeColor;
                break;
            case CombatState.Cooldown:
                bodyColor = cooldownBodyColor;
                eyeColor = cooldownEyeColor;
                break;
            case CombatState.Chasing:
                bodyColor = chaseBodyColor;
                eyeColor = chaseEyeColor;
                break;
            case CombatState.Idle:
            default:
                bodyColor = idleBodyColor;
                eyeColor = idleEyeColor;
                break;
        }

        if (bodyRenderer != null)
        {
            ApplyColor(bodyRenderer, bodyColor);
        }

        foreach (Renderer eyeRend in eyeRenderers)
        {
            if (eyeRend != null)
            {
                ApplyColor(eyeRend, eyeColor);
            }
        }
    }

    private void ApplyColor(Renderer rend, Color color)
    {
        if (rend == null)
        {
            return;
        }

        if (propBlock == null)
        {
            propBlock = new MaterialPropertyBlock();
        }

        rend.GetPropertyBlock(propBlock);
        propBlock.SetColor("_BaseColor", color);
        propBlock.SetColor("_Color", color);
        rend.SetPropertyBlock(propBlock);

        if (rend.material != null)
        {
            rend.material.color = color;
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
            CancelAttack();
            SetCombatState(CombatState.Idle);
            if (agent != null && agent.enabled)
            {
                agent.enabled = false;
            }
            return;
        }

        // Don't act while ragdolled
        if (ragdollController != null && ragdollController.IsRagdolled.Value)
        {
            CancelAttack();
            return;
        }

        if (!canMove && !canAttack)
        {
            return;
        }

        // Decrement attack cooldown timer
        if (attackTimer > 0f)
        {
            attackTimer -= Time.deltaTime;
        }

        // If in post-attack recovery pause: remain stationary until recovery expires
        if (attackPauseTimer > 0f)
        {
            SetCombatState(CombatState.Cooldown);
            attackPauseTimer -= Time.deltaTime;

            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            if (attackPauseTimer <= 0f && agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
            return;
        }

        // If in attack wind-up: telegraph strike and face target
        if (isAttacking)
        {
            SetCombatState(CombatState.Attacking);
            HandleAttackWindup();
            return;
        }

        // Re-evaluate target
        EvaluateTarget();

        if (currentTarget != null)
        {
            float dist = Vector3.Distance(transform.position, currentTarget.position);

            if (dist <= attackRange && attackTimer <= 0f)
            {
                StartAttack();
            }
            else
            {
                SetCombatState(CombatState.Chasing);
                Chase();
            }
        }
        else
        {
            SetCombatState(CombatState.Idle);
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
                currentTarget = null;
            }
            else
            {
                float distToTarget = Vector3.Distance(transform.position, currentTarget.position);
                if (distToTarget > loseTargetRange)
                {
                    currentTarget = null;
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
            SetCombatState(CombatState.Chasing);
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

    private void StartAttack()
    {
        if (!canAttack || currentTarget == null)
        {
            return;
        }

        // Do not attack a player who is already knocked down in ragdoll
        RagdollController targetRagdoll = currentTarget.GetComponent<RagdollController>();
        if (targetRagdoll != null && targetRagdoll.IsRagdolled.Value)
        {
            attackTimer = 1.5f; // Wait for player to stand up
            return;
        }

        isAttacking = true;
        windupTimer = attackWindup;
        SetCombatState(CombatState.Attacking);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
    }

    private void HandleAttackWindup()
    {
        if (currentTarget == null)
        {
            CancelAttack();
            return;
        }

        // Rotate smoothly towards target during windup
        Vector3 lookDir = currentTarget.position - transform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 10f);
        }

        windupTimer -= Time.deltaTime;
        if (windupTimer <= 0f)
        {
            ExecuteAttackStrike();
        }
    }

    private void ExecuteAttackStrike()
    {
        isAttacking = false;
        attackTimer = attackCooldown;
        attackPauseTimer = attackRecovery;
        SetCombatState(CombatState.Cooldown);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        if (currentTarget == null)
        {
            return;
        }

        // Verify target is still within strike range and not downed
        float dist = Vector3.Distance(transform.position, currentTarget.position);
        if (dist <= attackRange + 0.8f)
        {
            RagdollController targetRagdoll = currentTarget.GetComponent<RagdollController>();
            if (targetRagdoll != null && targetRagdoll.IsRagdolled.Value)
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
    }

    private void CancelAttack()
    {
        isAttacking = false;
        windupTimer = 0f;
        attackPauseTimer = 0f;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
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
