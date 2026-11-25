using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked player health. Server-authoritative damage and death.
/// Triggers ragdoll on hit via RagdollController.
/// </summary>
public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float knockbackMultiplier = 2f;
    [SerializeField] private float respawnDelay = 4f;

    [Header("Damage Settings")]
    [SerializeField] private float damageCooldown = 1.2f;

    public int MaxHealth
    {
        get
        {
            return maxHealth;
        }
    }

    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private RagdollController ragdollController;
    private Vector3 spawnPosition;
    private Vector3 lastSafeGroundedPosition;
    private float respawnTimer;
    private float damageCooldownTimer;

    private void Awake()
    {
        ragdollController = GetComponent<RagdollController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;

            // Spawn player into the staging LobbyArea by default
            if (LobbyArea.Instance != null &&
                (GameManager.Instance == null || GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby))
            {
                int index = (int)OwnerClientId;
                spawnPosition = LobbyArea.Instance.GetLobbySpawnPoint(index);
                transform.position = spawnPosition;
                Physics.SyncTransforms();
                ResetPlayerClientRpc(spawnPosition);
            }
            else
            {
                spawnPosition = transform.position;
            }
        }
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (damageCooldownTimer > 0f)
        {
            damageCooldownTimer -= Time.deltaTime;
        }

        if (!IsDead.Value)
        {
            // Record last safe grounded position when player is on solid floor
            if (transform.position.y > -2f)
            {
                if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit groundHit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    lastSafeGroundedPosition = groundHit.point + Vector3.up * 0.25f;
                }
            }

            // Fall into the void / suicide check
            if (transform.position.y < -25f)
            {
                TakeDamage(9999, Vector3.up, true);
            }
        }

        if (IsDead.Value)
        {
            // Only auto-respawn if in the Lobby (for lobby ragdoll/PVP fun)
            // In the dungeon, players stay dead until the expedition ends and the team returns to the bunker
            bool inLobby = GameManager.Instance == null || GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby;
            if (inLobby)
            {
                respawnTimer -= Time.deltaTime;
                if (respawnTimer <= 0f)
                {
                    Respawn();
                }
            }
        }
    }

    /// <summary>
    /// Grants a temporary invulnerability grace period (e.g. after standing up from ragdoll)
    /// to prevent perma-stun locking.
    /// </summary>
    public void GrantGetUpGracePeriod(float duration = 1.5f)
    {
        damageCooldownTimer = Mathf.Max(damageCooldownTimer, duration);
    }

    /// <summary>
    /// Server-side: Apply damage and knockback to this player.
    /// Default overload: treats hits >= 30 damage as heavy knockdown hits.
    /// </summary>
    public void TakeDamage(int damage, Vector3 hitDirection)
    {
        TakeDamage(damage, hitDirection, damage >= 30);
    }

    /// <summary>
    /// Server-side: Apply damage and knockback to this player.
    /// Only heavy hits with big windup trigger ragdoll knockdown.
    /// Normal hits deal damage and apply physical pushback without knocking the player down.
    /// </summary>
    public void TakeDamage(int damage, Vector3 hitDirection, bool isHeavyHit)
    {
        if (!IsServer)
        {
            return;
        }
        if (IsDead.Value)
        {
            return;
        }
        if (damageCooldownTimer > 0f)
        {
            return;
        }

        // Extracted players waiting safely cannot take damage
        if (GameManager.Instance != null && GameManager.Instance.IsPlayerExtracted(OwnerClientId))
        {
            return;
        }

        damageCooldownTimer = damageCooldown;
        CurrentHealth.Value -= damage;

        if (CurrentHealth.Value <= 0)
        {
            CurrentHealth.Value = 0;
            HandleDeath();
            return;
        }

        // Only big hits with big windups trigger ragdoll knockdown!
        if (isHeavyHit && ragdollController != null)
        {
            Vector3 knockback = hitDirection.normalized * damage * knockbackMultiplier;
            knockback.y = Mathf.Abs(knockback.y) + 3.5f; // Always pop up
            ragdollController.TriggerRagdoll(knockback);
        }
        else
        {
            // Normal hit: apply physical impulse pushback to Rigidbody without knockdown
            ApplyFlinchPushClientRpc(hitDirection.normalized * (damage * 0.35f));
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ApplyFlinchPushClientRpc(Vector3 push)
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            push.y = Mathf.Max(0.5f, push.y);
            rb.AddForce(push, ForceMode.Impulse);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TakeDamageServerRpc(int damage, Vector3 hitDirection)
    {
        TakeDamage(damage, hitDirection);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TakeDamageWithHeavyServerRpc(int damage, Vector3 hitDirection, bool isHeavyHit)
    {
        TakeDamage(damage, hitDirection, isHeavyHit);
    }

    private void HandleDeath()
    {
        IsDead.Value = true;
        respawnTimer = respawnDelay;

        // Resolve drop origin: if falling off edge, drop at the last solid ledge
        Vector3 dropOrigin = transform.position.y < -5f && lastSafeGroundedPosition.sqrMagnitude > 0.01f
            ? lastSafeGroundedPosition
            : transform.position + Vector3.up * 0.7f;

        // 1. Drop all carried hotbar items onto the ground
        PlayerInventory inv = GetComponent<PlayerInventory>();
        if (inv != null)
        {
            inv.DropAllItems(dropOrigin);
        }

        // 2. Drop all gold collected on this run onto the ground
        PlayerWallet wallet = GetComponent<PlayerWallet>();
        if (wallet != null)
        {
            wallet.DropAllRunMoney(dropOrigin);
        }

        // 3. Drop any grabbed physics object (barrel, crate)
        GrabThrow grabThrow = GetComponent<GrabThrow>();
        if (grabThrow != null)
        {
            grabThrow.ForceDrop();
        }

        // Big death ragdoll
        if (ragdollController != null)
        {
            ragdollController.TriggerRagdoll(Vector3.up * 8f, respawnDelay - 0.5f);
        }

        // Notify GameManager to check if all players are now dead or extracted
        if (GameManager.Instance != null)
        {
            GameManager.Instance.CheckRoundEndConditions();
        }
    }

    private void Respawn()
    {
        IsDead.Value = false;
        CurrentHealth.Value = maxHealth;
        transform.position = spawnPosition;

        if (ragdollController != null)
        {
            ragdollController.RecoverImmediately();
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Server: Restores health up to maxHealth.
    /// Returns true if health was restored, false if already at full health.
    /// </summary>
    public bool Heal(int amount)
    {
        if (!IsServer || IsDead.Value || amount <= 0)
        {
            return false;
        }

        if (CurrentHealth.Value >= maxHealth)
        {
            return false;
        }

        int previous = CurrentHealth.Value;
        CurrentHealth.Value = Mathf.Min(maxHealth, CurrentHealth.Value + amount);
        int healedAmount = CurrentHealth.Value - previous;

        OnHealedClientRpc(healedAmount, CurrentHealth.Value);
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void HealServerRpc(int amount)
    {
        Heal(amount);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnHealedClientRpc(int healedAmount, int currentHealth)
    {
        if (IsOwner)
        {
            Debug.Log($"[PlayerHealth] Healed +{healedAmount} HP! (Health: {currentHealth}/{maxHealth})");
        }
    }

    /// <summary>
    /// Server: Reset player health, death state, and position for a round restart.
    /// </summary>
    public void ResetPlayer(Vector3 newSpawnPosition)
    {
        if (!IsServer)
        {
            return;
        }

        spawnPosition = newSpawnPosition;
        IsDead.Value = false;
        CurrentHealth.Value = maxHealth;
        damageCooldownTimer = 0f;
        transform.position = newSpawnPosition;

        if (ragdollController != null)
        {
            ragdollController.RecoverImmediately();
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = newSpawnPosition;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.freezeRotation = true;
        }

        Physics.SyncTransforms();

        ResetPlayerClientRpc(newSpawnPosition);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ResetPlayerClientRpc(Vector3 newPosition)
    {
        OwnerNetworkTransform ont = GetComponent<OwnerNetworkTransform>();
        if (ont != null && IsOwner)
        {
            ont.Teleport(newPosition, transform.rotation, transform.localScale);
        }
        else
        {
            transform.position = newPosition;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = newPosition;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.freezeRotation = true;
        }

        Physics.SyncTransforms();

        GrabThrow grabThrow = GetComponent<GrabThrow>();
        if (grabThrow != null)
        {
            grabThrow.ForceDrop();
        }

        PlayerController controller = GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.ResetAfterRagdoll(transform.eulerAngles.y);
        }
    }
}
