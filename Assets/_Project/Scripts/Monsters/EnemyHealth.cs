using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Enemy health system. Server-authoritative.
/// Triggers ragdoll on hit, alerts AI to attacker, drops loot/coins on death,
/// and despawns after a delay.
/// </summary>
public class EnemyHealth : NetworkBehaviour, IDamageable
{
    public static readonly List<EnemyHealth> ActiveEnemies = new();

    [Header("Health")]
    [SerializeField] private int maxHealth = 50;
    [SerializeField] private float knockbackMultiplier = 3f;
    [SerializeField] private float deathDespawnDelay = 4f;

    [Header("Damage Settings")]
    [SerializeField] private float damageCooldown = 0.5f;

    public NetworkVariable<FixedString32Bytes> EnemyId = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public string DisplayName
    {
        get
        {
            string id = EnemyId.Value.ToString();
            if (!string.IsNullOrEmpty(id))
            {
                EnemyData data = EnemyRegistry.GetEnemy(id);
                if (data != null && !string.IsNullOrEmpty(data.displayName))
                {
                    return data.displayName;
                }
            }
            return "Containment Abnormality";
        }
    }

    public int MaxHealth
    {
        get
        {
            return maxHealth;
        }
    }

    [Header("Loot Drops")]
    [SerializeField] private GameObject[] dropPrefabs;
    [SerializeField] private int minDropCount = 2;
    [SerializeField] private int maxDropCount = 4;
    [SerializeField] private float dropScatterForce = 4.5f;

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
    private BasicEnemyAI enemyAI;
    private float despawnTimer;
    private float damageCooldownTimer;

    private void OnEnable()
    {
        if (!ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
    }

    private void Awake()
    {
        ragdollController = GetComponent<RagdollController>();
        enemyAI = GetComponent<BasicEnemyAI>();
    }

    public override void OnNetworkSpawn()
    {
        EnemyRegistry.Initialize();
        EnemyId.OnValueChanged += OnEnemyIdChanged;

        if (IsServer)
        {
            if (EnemyId.Value.IsEmpty)
            {
                EnemyData defaultData = EnemyRegistry.GetDefaultEnemy();
                ApplyEnemyData(defaultData);
            }
            else
            {
                CurrentHealth.Value = maxHealth;
            }
        }
        else
        {
            // Client side visual sync
            UpdateClientVisuals();
        }
    }

    public override void OnNetworkDespawn()
    {
        EnemyId.OnValueChanged -= OnEnemyIdChanged;
    }

    private void OnEnemyIdChanged(FixedString32Bytes previous, FixedString32Bytes current)
    {
        UpdateClientVisuals();
    }

    private void UpdateClientVisuals()
    {
        string id = EnemyId.Value.ToString();
        if (!string.IsNullOrEmpty(id))
        {
            EnemyData data = EnemyRegistry.GetEnemy(id);
            if (data != null)
            {
                if (enemyAI == null)
                {
                    enemyAI = GetComponent<BasicEnemyAI>();
                }
                if (enemyAI != null)
                {
                    enemyAI.ApplyEnemyData(data);
                }
            }
        }
    }

    /// <summary>
    /// Applies stats and visuals from the EnemyData parsed from Enemies.json.
    /// </summary>
    public void ApplyEnemyData(EnemyData data)
    {
        if (data == null)
        {
            return;
        }

        EnemyId.Value = new FixedString32Bytes(data.enemyId);
        maxHealth = data.maxHealth;
        CurrentHealth.Value = data.maxHealth;

        if (enemyAI == null)
        {
            enemyAI = GetComponent<BasicEnemyAI>();
        }

        if (enemyAI != null)
        {
            enemyAI.ApplyEnemyData(data);
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

        if (IsDead.Value)
        {
            despawnTimer -= Time.deltaTime;
            if (despawnTimer <= 0f)
            {
                if (NetworkObject != null && NetworkObject.IsSpawned)
                {
                    NetworkObject.Despawn(true);
                }
            }
        }
    }

    /// <summary>
    /// Server-side: Apply damage and knockback to this enemy.
    /// Alerts enemy AI to the attacker, stuns them, and resets their attack timer.
    /// Does NOT ragdoll living enemies on hit.
    /// </summary>
    public void TakeDamage(int damage, Vector3 hitDirection)
    {
        TakeDamage(damage, hitDirection, false);
    }

    /// <summary>
    /// Server-side: Apply damage and knockback to this enemy.
    /// Player attacks do NOT ragdoll living enemies; they stun them and reset their attack cooldown.
    /// Only death triggers ragdoll.
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

        damageCooldownTimer = damageCooldown;
        CurrentHealth.Value -= damage;

        // Alert enemy AI to attacker, STUN them, and RESET their attack timer
        if (enemyAI != null)
        {
            enemyAI.AlertToAttacker(hitDirection);
            enemyAI.Stun(0.65f);
        }

        if (CurrentHealth.Value <= 0)
        {
            CurrentHealth.Value = 0;
            HandleDeath();
        }
        else
        {
            // Apply a crisp physical flinch impulse without ragdolling
            ApplyFlinchClientRpc(hitDirection.normalized * 4.5f);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ApplyFlinchClientRpc(Vector3 flinch)
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            flinch.y = 0.2f;
            rb.AddForce(flinch, ForceMode.Impulse);
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
        despawnTimer = deathDespawnDelay;

        // Big death launch
        if (ragdollController != null)
        {
            Vector3 deathForce = Vector3.up * 12f + Random.insideUnitSphere * 5f;
            ragdollController.TriggerRagdoll(deathForce, deathDespawnDelay);
        }

        // Spawn loot drops
        DropLoot();
    }

    private void DropLoot()
    {
        if (!IsServer)
        {
            return;
        }

        string enemyType = EnemyId.Value.ToString();
        List<LootDropResult> drops = LootRegistry.RollLoot(enemyType);

        if (drops == null || drops.Count == 0)
        {
            return;
        }

        Vector3 origin = transform.position + Vector3.up * 0.6f;

        for (int i = 0; i < drops.Count; i++)
        {
            LootDropResult drop = drops[i];
            if (string.IsNullOrEmpty(drop.ItemId))
            {
                continue;
            }

            Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(0.5f, 1.3f);
            Vector3 spawnPos = origin + new Vector3(circle.x, 0.2f, circle.y);
            Vector3 scatterVelocity = new Vector3(circle.x, 2.4f, circle.y).normalized * dropScatterForce;

            WorldItemPickup.SpawnPickup(drop.ItemId, drop.Count, spawnPos, scatterVelocity);
        }

        Debug.Log($"[EnemyHealth] {DisplayName} ({enemyType}) dropped {drops.Count} salvage parts via LootTables.json.");
    }
}
