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
    private float respawnTimer;

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

        if (IsDead.Value)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f)
            {
                Respawn();
            }
        }
    }

    /// <summary>
    /// Server-side: Apply damage and knockback to this player.
    /// </summary>
    public void TakeDamage(int damage, Vector3 hitDirection)
    {
        if (!IsServer)
        {
            return;
        }
        if (IsDead.Value)
        {
            return;
        }

        CurrentHealth.Value -= damage;

        // Ragdoll on any hit
        if (ragdollController != null)
        {
            Vector3 knockback = hitDirection.normalized * damage * knockbackMultiplier;
            knockback.y = Mathf.Abs(knockback.y) + 3f; // Always pop up a bit
            ragdollController.TriggerRagdoll(knockback);
        }

        if (CurrentHealth.Value <= 0)
        {
            CurrentHealth.Value = 0;
            HandleDeath();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TakeDamageServerRpc(int damage, Vector3 hitDirection)
    {
        TakeDamage(damage, hitDirection);
    }

    private void HandleDeath()
    {
        IsDead.Value = true;
        respawnTimer = respawnDelay;

        // Big death ragdoll
        if (ragdollController != null)
        {
            ragdollController.TriggerRagdoll(Vector3.up * 8f, respawnDelay - 0.5f);
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

        ResetPlayerClientRpc(newSpawnPosition);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ResetPlayerClientRpc(Vector3 newPosition)
    {
        transform.position = newPosition;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = newPosition;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.freezeRotation = true;
        }

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
