using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Enemy health system. Server-authoritative.
/// Triggers ragdoll on hit, alerts AI to attacker, drops loot/coins on death,
/// and despawns after a delay.
/// </summary>
public class EnemyHealth : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 50;
    [SerializeField] private float knockbackMultiplier = 3f;
    [SerializeField] private float deathDespawnDelay = 4f;

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

    private void Awake()
    {
        ragdollController = GetComponent<RagdollController>();
        enemyAI = GetComponent<BasicEnemyAI>();
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
                if (NetworkObject != null && NetworkObject.IsSpawned)
                {
                    NetworkObject.Despawn(true);
                }
            }
        }
    }

    /// <summary>
    /// Server-side: Apply damage and knockback to this enemy.
    /// Alerts enemy AI to the attacker.
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

        // Alert enemy AI to attacker
        if (enemyAI != null)
        {
            enemyAI.AlertToAttacker(hitDirection);
        }

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

        // Spawn loot drops
        DropLoot();
    }

    private void DropLoot()
    {
        if (!IsServer)
        {
            return;
        }

        List<GameObject> candidates = new List<GameObject>();
        if (dropPrefabs != null)
        {
            for (int i = 0; i < dropPrefabs.Length; i++)
            {
                if (dropPrefabs[i] != null)
                {
                    candidates.Add(dropPrefabs[i]);
                }
            }
        }

        // Fallback: If no inspector drops assigned, grab CoinPrefab from DungeonBuilder
        DungeonBuilder dungeonBuilder = FindAnyObjectByType<DungeonBuilder>();
        if (candidates.Count == 0 && dungeonBuilder != null && dungeonBuilder.CoinPrefab != null)
        {
            candidates.Add(dungeonBuilder.CoinPrefab);
        }

        if (candidates.Count == 0)
        {
            return;
        }

        int count = Random.Range(minDropCount, maxDropCount + 1);
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = candidates[Random.Range(0, candidates.Count)];
            Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(0.4f, 1.2f);
            Vector3 spawnPos = transform.position + new Vector3(circle.x, 0.4f, circle.y);

            GameObject droppedObj = Instantiate(prefab, spawnPos, Quaternion.identity);
            NetworkObject netObj = droppedObj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }

            // Scatter impulse if it has physics
            Rigidbody rb = droppedObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 impulse = new Vector3(circle.x, 1.5f, circle.y).normalized * dropScatterForce;
                rb.AddForce(impulse, ForceMode.Impulse);
            }

            // If coin, register with DungeonBuilder for round lifecycle cleanup
            if (dungeonBuilder != null && droppedObj.GetComponent<Coin>() != null)
            {
                dungeonBuilder.TrackSpawnedCoin(droppedObj);
            }
        }
    }
}
