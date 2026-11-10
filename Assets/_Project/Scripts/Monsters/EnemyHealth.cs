using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Enemy health system. Server-authoritative.
/// Triggers ragdoll on hit, despawns on death after a delay.
/// </summary>
public class EnemyHealth : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 50;
    [SerializeField] private float knockbackMultiplier = 3f;
    [SerializeField] private float deathDespawnDelay = 4f;

    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        50,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private RagdollController ragdollController;
    private float despawnTimer;

    private void Awake()
    {
        ragdollController = GetComponent<RagdollController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
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
            despawnTimer -= Time.deltaTime;
            if (despawnTimer <= 0f)
            {
                NetworkObject.Despawn(true);
            }
        }
    }

    /// <summary>
    /// Server-side: Apply damage and knockback to this enemy.
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
            knockback.y = Mathf.Abs(knockback.y) + 5f; // Pop up on hit
            ragdollController.TriggerRagdoll(knockback);
        }

        if (CurrentHealth.Value <= 0)
        {
            CurrentHealth.Value = 0;
            HandleDeath();
        }
    }

    private void HandleDeath()
    {
        IsDead.Value = true;
        despawnTimer = deathDespawnDelay;

        // Big death launch
        if (ragdollController != null)
        {
            Vector3 deathForce = Vector3.up * 12f + Random.insideUnitSphere * 5f;
            ragdollController.TriggerRagdoll(deathForce, deathDespawnDelay);
        }
    }
}
