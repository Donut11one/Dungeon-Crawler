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

    [Header("Stun & Heavy Attack Visuals")]
    [SerializeField] private Color stunBodyColor = new Color(0.85f, 0.75f, 0.15f);
    [SerializeField] private Color stunEyeColor = Color.white;
    [SerializeField] private Color heavyWindupBodyColor = new Color(1f, 0.05f, 0.25f);
    [SerializeField] private Color heavyWindupEyeColor = new Color(1f, 0.95f, 0.1f);

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

    public NetworkVariable<bool> IsStunned = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsHeavyWindup = new NetworkVariable<bool>(
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
    private float stunTimer;
    private bool isAttacking;
    private bool isCurrentAttackHeavy;
    private int attackCount;
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
        IsStunned.OnValueChanged += OnStunStateChanged;
        IsHeavyWindup.OnValueChanged += OnHeavyWindupChanged;

        // Apply current visual state on spawn
        UpdateStateVisuals(CurrentCombatState.Value);
    }

    public override void OnNetworkDespawn()
    {
        CurrentCombatState.OnValueChanged -= OnCombatStateChanged;
        IsStunned.OnValueChanged -= OnStunStateChanged;
        IsHeavyWindup.OnValueChanged -= OnHeavyWindupChanged;
    }

    private void OnStunStateChanged(bool previous, bool current)
    {
        UpdateStateVisuals(CurrentCombatState.Value);
    }

    private void OnHeavyWindupChanged(bool previous, bool current)
    {
        UpdateStateVisuals(CurrentCombatState.Value);
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

        if (IsStunned.Value)
        {
            bodyColor = stunBodyColor;
            eyeColor = stunEyeColor;
        }
        else if (IsHeavyWindup.Value)
        {
            bodyColor = heavyWindupBodyColor;
            eyeColor = heavyWindupEyeColor;
        }
        else
        {
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

    /// <summary>
    /// Applies movement speeds, ranges, attack stats, and base colors from JSON-loaded EnemyData.
    /// </summary>
    public void ApplyEnemyData(EnemyData data)
    {
        if (data == null)
        {
            return;
        }

        chaseSpeed = data.chaseSpeed;
        wanderSpeed = data.wanderSpeed;
        aggroRange = data.aggroRange;
        loseTargetRange = data.loseTargetRange;
        directProximityRange = data.directProximityRange;
        attackRange = data.attackRange;
        attackDamage = data.attackDamage;
        attackCooldown = data.attackCooldown;
        attackWindup = data.attackWindup;
        attackRecovery = data.attackRecovery;
        attackKnockback = data.attackKnockback;

        idleBodyColor = data.GetBodyColor();
        idleEyeColor = data.GetEyeColor();

        if (agent != null && agent.enabled)
        {
            agent.speed = wanderSpeed;
        }

        UpdateStateVisuals(CurrentCombatState.Value);
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

        // Handle stun timer: halts movement, prevents attacking, stays dazed
        if (stunTimer > 0f)
        {
            stunTimer -= Time.deltaTime;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            if (stunTimer <= 0f)
            {
                stunTimer = 0f;
                IsStunned.Value = false;
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                }
            }
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

            // Skip extracted players
            if (GameManager.Instance != null && GameManager.Instance.IsPlayerExtracted(client.ClientId))
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

    /// <summary>
    /// Stuns the enemy when hit by the player. Interrupts active attacks,
    /// resets the attack cooldown timer, and halts movement for the duration.
    /// Living enemies do not ragdoll on player hits.
    /// </summary>
    public void Stun(float duration = 0.65f)
    {
        if (!IsServer)
        {
            return;
        }

        if (enemyHealth != null && enemyHealth.IsDead.Value)
        {
            return;
        }

        CancelAttack();

        // Reset attack timer so the enemy is delayed before their next strike
        attackTimer = Mathf.Max(attackTimer, attackCooldown);

        stunTimer = Mathf.Max(stunTimer, duration);
        IsStunned.Value = true;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
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
            // Maintain at least a 2.5s window after the player stands up before another attack can start,
            // and never overwrite a longer attackCooldown.
            attackTimer = Mathf.Max(attackTimer, 2.5f);
            return;
        }

        attackCount++;
        // Heavy attacks with big windups happen periodically (e.g. every 3rd attack, or 50% for high knockback brutes)
        bool doHeavy = (attackCount % 3 == 0) || (attackKnockback >= 12f && attackCount % 2 == 1);
        isCurrentAttackHeavy = doHeavy;
        IsHeavyWindup.Value = doHeavy;

        isAttacking = true;
        windupTimer = doHeavy ? Mathf.Max(1.15f, attackWindup * 2.8f) : Mathf.Max(0.35f, attackWindup);
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

        // Rotate towards target during windup. If charging a big heavy hit, rotate slowly so player can dodge!
        Vector3 lookDir = currentTarget.position - transform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            float turnSpeed = isCurrentAttackHeavy ? 2.2f : 10f;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * turnSpeed);
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
        bool wasHeavy = isCurrentAttackHeavy;
        isCurrentAttackHeavy = false;
        IsHeavyWindup.Value = false;

        attackTimer = attackCooldown;
        attackPauseTimer = wasHeavy ? attackRecovery * 1.35f : attackRecovery;
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
        float reach = wasHeavy ? attackRange + 0.8f : attackRange + 0.4f;
        if (dist <= reach)
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
                if (wasHeavy)
                {
                    // Big Heavy Hit with big windup: knocks down!
                    hitDir.y = 0.5f;
                    int heavyDmg = Mathf.RoundToInt(attackDamage * 1.6f);
                    float heavyKb = Mathf.Max(14f, attackKnockback * 1.5f);
                    damageable.TakeDamage(heavyDmg, hitDir * heavyKb, true);
                }
                else
                {
                    // Normal Hit: does NOT knock down!
                    hitDir.y = 0.2f;
                    damageable.TakeDamage(attackDamage, hitDir * 5f, false);
                }
            }
        }
    }

    private void CancelAttack()
    {
        isAttacking = false;
        isCurrentAttackHeavy = false;
        IsHeavyWindup.Value = false;
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
