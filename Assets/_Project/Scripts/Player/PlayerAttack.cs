using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player attack combat states.
/// Synchronized across the network to drive visual color changes and animation hooks.
/// </summary>
public enum PlayerCombatState
{
    Ready = 0,
    Attacking = 1,
    Cooldown = 2
}

/// <summary>
/// Handles player melee attacks on Left-Click.
/// Enforces commitment: attacks have windup and recovery phases during which movement is heavily slowed
/// and weapon hotbar swapping is strictly locked to prevent swap-abuse / macro exploits.
/// Synchronizes visual state colors (Ready = standard suit, Attacking = strike orange/red, Cooldown = blue/cyan).
/// </summary>
public class PlayerAttack : NetworkBehaviour
{
    [Header("Fist Default Stats (Unarmed)")]
    [SerializeField] private int fistDamage = 15;
    [SerializeField] private float fistCooldown = 0.90f;
    [SerializeField] private float fistWindup = 0.18f;
    [SerializeField] private float fistRange = 2.0f;
    [SerializeField] private float fistKnockback = 5.5f;

    [Header("Hit Detection")]
    [SerializeField] private float hitSphereRadius = 0.35f;
    [SerializeField] private LayerMask attackMask = ~0;

    [Header("Combat State Visual Colors")]
    [SerializeField] private Color readyColor = new Color(0.28f, 0.32f, 0.36f);
    [SerializeField] private Color attackColor = new Color(1.0f, 0.25f, 0.10f);
    [SerializeField] private Color cooldownColor = new Color(0.20f, 0.60f, 0.95f);

    public NetworkVariable<PlayerCombatState> CurrentCombatState = new NetworkVariable<PlayerCombatState>(
        PlayerCombatState.Ready,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private PlayerInventory inventory;
    private RagdollController ragdollController;
    private GrabThrow grabThrow;
    private Camera playerCamera;
    private Renderer bodyRenderer;
    private MaterialPropertyBlock propBlock;

    private float attackTimer;
    private float totalCooldown;
    private float windupTimer;
    private bool hasExecutedHit;

    private int pendingDamage;
    private float pendingRange;
    private float pendingKnockback;
    private string pendingAttackName;

    /// <summary>Whether the player is committed to an active attack swing.</summary>
    public bool IsAttacking
    {
        get
        {
            return CurrentCombatState.Value == PlayerCombatState.Attacking;
        }
    }

    /// <summary>Whether the player is recovering on attack cooldown.</summary>
    public bool IsOnCooldown
    {
        get
        {
            return attackTimer > 0f;
        }
    }

    public float AttackTimer
    {
        get
        {
            return attackTimer;
        }
    }

    public float TotalCooldown
    {
        get
        {
            return totalCooldown;
        }
    }

    public float CooldownProgress
    {
        get
        {
            return totalCooldown > 0f ? Mathf.Clamp01(1f - (attackTimer / totalCooldown)) : 1f;
        }
    }

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        ragdollController = GetComponent<RagdollController>();
        grabThrow = GetComponent<GrabThrow>();
        playerCamera = GetComponentInChildren<Camera>();
        bodyRenderer = FindBodyRenderer();
    }

    public override void OnNetworkSpawn()
    {
        CurrentCombatState.OnValueChanged += OnCombatStateChanged;
        UpdateStateVisuals(CurrentCombatState.Value);
    }

    public override void OnNetworkDespawn()
    {
        CurrentCombatState.OnValueChanged -= OnCombatStateChanged;
    }

    private void OnCombatStateChanged(PlayerCombatState previous, PlayerCombatState current)
    {
        UpdateStateVisuals(current);
    }

    private void SetCombatState(PlayerCombatState state)
    {
        if (IsOwner && CurrentCombatState.Value != state)
        {
            CurrentCombatState.Value = state;
            UpdateStateVisuals(state);
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        // Interrupt attack if player is ragdolled/knocked down
        if (ragdollController != null && ragdollController.IsRagdolled.Value)
        {
            if (CurrentCombatState.Value != PlayerCombatState.Ready)
            {
                SetCombatState(PlayerCombatState.Ready);
            }
            attackTimer = 0.5f;
            windupTimer = 0f;
            return;
        }

        // Update active attack windup phase
        if (windupTimer > 0f)
        {
            windupTimer -= Time.deltaTime;
            if (windupTimer <= 0f && !hasExecutedHit)
            {
                hasExecutedHit = true;
                ExecuteHitDetection();
                SetCombatState(PlayerCombatState.Cooldown);
            }
        }

        // Update total attack cooldown
        if (attackTimer > 0f)
        {
            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                attackTimer = 0f;
                SetCombatState(PlayerCombatState.Ready);
            }
        }

        // Cannot initiate attack while holding objects or browsing menus
        if (grabThrow != null && grabThrow.IsHoldingObject)
        {
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (attackTimer <= 0f && CurrentCombatState.Value == PlayerCombatState.Ready)
            {
                StartAttack();
            }
        }
    }

    /// <summary>
    /// Begins the committed attack animation and windup phase.
    /// Locks movement speed and locks weapon hotbar swapping.
    /// </summary>
    private void StartAttack()
    {
        ItemData equipped = inventory != null ? inventory.GetSelectedItem() : null;

        int damage = equipped != null ? equipped.Damage : fistDamage;
        float cooldown = equipped != null ? equipped.AttackCooldown : fistCooldown;
        float windup = equipped != null ? equipped.AttackWindup : fistWindup;
        float range = equipped != null ? equipped.AttackRange : fistRange;
        float knockback = equipped != null ? equipped.KnockbackForce : fistKnockback;
        string attackName = equipped != null ? equipped.DisplayName : "Fist";

        attackTimer = cooldown;
        totalCooldown = cooldown;
        windupTimer = windup;
        hasExecutedHit = false;

        pendingDamage = damage;
        pendingRange = range;
        pendingKnockback = knockback;
        pendingAttackName = attackName;

        SetCombatState(PlayerCombatState.Attacking);
        PlayAttackFeedback(attackName, hit: false);
    }

    /// <summary>
    /// Checks whether the player is permitted to swap hotbar items.
    /// Blocked during the committed attack swing!
    /// </summary>
    public bool CanSwapItem()
    {
        return CurrentCombatState.Value != PlayerCombatState.Attacking;
    }

    /// <summary>
    /// Called when the player selects a new hotbar item.
    /// Enforces equip delay and preserves ongoing cooldown to eliminate hot-swapping burst exploits.
    /// </summary>
    public void OnItemSwapped()
    {
        float equipDelay = 0.50f;
        if (attackTimer < equipDelay)
        {
            attackTimer = equipDelay;
            totalCooldown = equipDelay;
            SetCombatState(PlayerCombatState.Cooldown);
        }
    }

    /// <summary>
    /// Fires the physical hit raycast/spherecast at the exact moment the weapon swing connects.
    /// </summary>
    private void ExecuteHitDetection()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null)
            {
                return;
            }
        }

        Ray aimRay = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        float castDist = pendingRange + 4.5f;

        RaycastHit[] hits = Physics.SphereCastAll(aimRay, hitSphereRadius, castDist, attackMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        Vector3 playerPos = transform.position + Vector3.up * 1.2f;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            float distToPlayer = Vector3.Distance(playerPos, hit.point);
            if (distToPlayer > pendingRange + 0.9f)
            {
                continue;
            }

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            NetworkObject netObj = hit.collider.GetComponentInParent<NetworkObject>();

            Vector3 hitDirection = (hit.point - playerPos).normalized;
            if (hitDirection.sqrMagnitude < 0.01f)
            {
                hitDirection = transform.forward;
            }
            hitDirection.y = Mathf.Max(0.25f, hitDirection.y);

            if (netObj != null)
            {
                PlayAttackFeedback(pendingAttackName, hit: true);
                AttackServerRpc(netObj.NetworkObjectId, pendingDamage, hitDirection * pendingKnockback);
                return;
            }

            Rigidbody targetRb = hit.collider.GetComponentInParent<Rigidbody>();
            if (targetRb != null)
            {
                PlayAttackFeedback(pendingAttackName, hit: true);
                targetRb.AddForce(hitDirection * pendingKnockback * 18f, ForceMode.Impulse);
                return;
            }

            if (!hit.collider.isTrigger)
            {
                break;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AttackServerRpc(ulong targetNetId, int damage, Vector3 knockbackImpulse)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out NetworkObject targetNetObj))
        {
            float dist = Vector3.Distance(transform.position, targetNetObj.transform.position);
            if (dist > 7.5f)
            {
                return;
            }

            IDamageable damageable = targetNetObj.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, knockbackImpulse);
            }

            Rigidbody rb = targetNetObj.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(knockbackImpulse * 14f, ForceMode.Impulse);
            }
        }
    }

    private void PlayAttackFeedback(string attackName, bool hit)
    {
        if (hit)
        {
            Debug.Log($"[PlayerAttack] {attackName} connected with target!");
        }
    }

    private void UpdateStateVisuals(PlayerCombatState state)
    {
        if (bodyRenderer == null)
        {
            bodyRenderer = FindBodyRenderer();
            if (bodyRenderer == null)
            {
                return;
            }
        }

        if (propBlock == null)
        {
            propBlock = new MaterialPropertyBlock();
        }

        Color targetColor;
        switch (state)
        {
            case PlayerCombatState.Attacking:
                targetColor = attackColor;
                break;
            case PlayerCombatState.Cooldown:
                targetColor = cooldownColor;
                break;
            case PlayerCombatState.Ready:
            default:
                targetColor = readyColor;
                break;
        }

        bodyRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor("_BaseColor", targetColor);
        propBlock.SetColor("_Color", targetColor);
        bodyRenderer.SetPropertyBlock(propBlock);
    }

    private Renderer FindBodyRenderer()
    {
        Transform bodyTr = transform.Find("Body");
        if (bodyTr != null)
        {
            Renderer rend = bodyTr.GetComponent<Renderer>();
            if (rend != null)
            {
                return rend;
            }
        }

        return GetComponentInChildren<Renderer>();
    }
}