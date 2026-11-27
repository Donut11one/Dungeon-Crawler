using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Services a LootSpawnPoint needs from whatever manages it (implemented by LootSpawner).</summary>
public interface ITreasureSpawnContext
{
    System.Random Rng { get; }
    bool CanSpawnMore { get; }
    TreasurePickup SpawnTreasure(TreasureData treasure, Vector3 position, Quaternion rotation);
}

public enum LootRespawnMode
{
    [InspectorName("Once per round")]
    OncePerRound = 0,

    [InspectorName("Respawn after cleared")]
    AfterCleared = 1
}

/// <summary>
/// One spawn location with its own, fully independent loot configuration: loot table (or shared preset),
/// number of rolls, scatter, and respawn behaviour. Place these anywhere in the scene (do NOT parent them under
/// the generated 'Dungeon' object, which is rebuilt at runtime) and let a LootSpawner drive them.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Dungeon Crawler/Loot/Loot Spawn Point")]
public class LootSpawnPoint : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Optional friendly name shown in the scene view and in the LootSpawner overview.")]
    [SerializeField] private string label;
    [Tooltip("Disabled locations never spawn anything.")]
    [SerializeField] private bool spawnEnabled = true;

    [Header("Loot Table")]
    [Tooltip("Optional shared preset. When assigned it replaces this location's own table below.")]
    [SerializeField] private LootTable presetTable;
    [SerializeField] private LootTableDefinition customTable = new LootTableDefinition();

    [Header("Spawn Count")]
    [Tooltip("Minimum number of rolls per spawn. Every roll independently produces nothing or one treasure.")]
    [Min(0)]
    [SerializeField] private int minSpawnCount = 1;
    [Tooltip("Maximum number of rolls per spawn.")]
    [Min(0)]
    [SerializeField] private int maxSpawnCount = 1;

    [Header("Placement")]
    [Tooltip("Treasures are scattered randomly inside this radius around the point.")]
    [Min(0f)]
    [SerializeField] private float scatterRadius = 0.8f;
    [Tooltip("Minimum horizontal distance between treasures spawned together.")]
    [Min(0f)]
    [SerializeField] private float minSeparation = 0.7f;
    [Tooltip("Drop each treasure onto the floor below its scatter position.")]
    [SerializeField] private bool snapToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [Min(0f)]
    [SerializeField] private float groundProbeHeight = 1f;
    [Min(0.1f)]
    [SerializeField] private float groundProbeDistance = 4f;
    [Tooltip("Extra height above the detected floor.")]
    [SerializeField] private float heightOffset = 0.02f;
    [SerializeField] private bool randomYaw = true;

    [Header("Respawn")]
    [SerializeField] private LootRespawnMode respawnMode = LootRespawnMode.OncePerRound;
    [Tooltip("Seconds to wait after every treasure here is gone before rolling again (random between min and max).")]
    [Min(0f)]
    [SerializeField] private float respawnDelayMin = 30f;
    [Min(0f)]
    [SerializeField] private float respawnDelayMax = 60f;
    [Tooltip("How many times this location may respawn per round. -1 = unlimited.")]
    [Min(-1)]
    [SerializeField] private int maxRespawns = -1;

    // ── Server-side runtime state ────────────────────────────────────────────
    private readonly List<TreasurePickup> active = new List<TreasurePickup>();
    private readonly List<Vector3> spawnedPositions = new List<Vector3>();
    private ITreasureSpawnContext context;
    private float nextRespawnTime = -1f;
    private int respawnsDone;
    private bool isClearing;
    private bool rolledThisCycle;

    // ── Public read access ───────────────────────────────────────────────────
    public string DisplayName { get { return string.IsNullOrWhiteSpace(label) ? name : label; } }
    public bool SpawnEnabled { get { return spawnEnabled; } set { spawnEnabled = value; } }
    public LootTable PresetTable { get { return presetTable; } }
    public bool UsesPreset { get { return presetTable != null; } }
    public LootTableDefinition ActiveTable { get { return presetTable != null ? presetTable.Definition : customTable; } }
    public int MinSpawnCount { get { return Mathf.Min(minSpawnCount, maxSpawnCount); } }
    public int MaxSpawnCount { get { return Mathf.Max(minSpawnCount, maxSpawnCount); } }
    public LootRespawnMode RespawnMode { get { return respawnMode; } }
    public float ScatterRadius { get { return scatterRadius; } }
    public int ActiveCount { get { return active.Count; } }
    public int RespawnsDone { get { return respawnsDone; } }
    public float SecondsUntilRespawn { get { return nextRespawnTime < 0f ? -1f : Mathf.Max(0f, nextRespawnTime - Time.time); } }

    // ── Server API (called by LootSpawner) ───────────────────────────────────
    public void ServerInitialize(ITreasureSpawnContext spawnContext)
    {
        context = spawnContext;
    }

    /// <summary>
    /// Rolls and spawns one batch. Does nothing if this location still has live treasure, so a batch can
    /// never be stacked on top of another. Returns the number of treasures spawned.
    /// </summary>
    public int ServerSpawnBatch(bool isRespawn = false)
    {
        if (context == null)
        {
            Debug.LogError($"[LootSpawnPoint] '{DisplayName}' is not attached to a LootSpawner session.", this);
            return 0;
        }
        if (!spawnEnabled || !isActiveAndEnabled)
        {
            return 0;
        }

        PruneDeadReferences();
        if (active.Count > 0)
        {
            return 0;
        }

        // Once rolled this cycle (even if the roll produced nothing), only a timed respawn may roll again.
        if (rolledThisCycle && !isRespawn)
        {
            return 0;
        }

        rolledThisCycle = true;
        nextRespawnTime = -1f;

        LootTableDefinition table = ActiveTable;
        if (table == null || table.entries == null)
        {
            Debug.LogWarning($"[LootSpawnPoint] '{DisplayName}' has no loot table.", this);
            return 0;
        }

        System.Random rng = context.Rng;
        int low = Mathf.Max(0, MinSpawnCount);
        int high = Mathf.Max(low, MaxSpawnCount);
        int rolls = low == high ? low : rng.Next(low, high + 1);

        int[] taken = new int[table.entries.Count];
        spawnedPositions.Clear();
        int spawned = 0;

        for (int i = 0; i < rolls; i++)
        {
            if (!context.CanSpawnMore)
            {
                break;
            }
            if (!LootRoller.TryRoll(table, rng, taken, out LootEntry entry))
            {
                continue;
            }

            Vector3 position = PickPosition(rng);
            Quaternion rotation = randomYaw
                ? Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f)
                : transform.rotation;

            TreasurePickup pickup = context.SpawnTreasure(entry.treasure, position, rotation);
            if (pickup == null)
            {
                continue;
            }

            pickup.ServerDespawned += HandleTreasureDespawned;
            active.Add(pickup);
            spawnedPositions.Add(position);
            spawned++;
        }

        if (isRespawn)
        {
            respawnsDone++;
        }

        // An all-empty roll counts as 'cleared' so respawning locations try again later.
        if (spawned == 0)
        {
            ScheduleRespawn();
        }
        return spawned;
    }

    /// <summary>Despawns everything this location currently owns and cancels any pending respawn.</summary>
    public void ServerClear()
    {
        isClearing = true;
        TreasurePickup[] snapshot = active.ToArray();
        active.Clear();

        for (int i = 0; i < snapshot.Length; i++)
        {
            TreasurePickup pickup = snapshot[i];
            if (pickup == null)
            {
                continue;
            }

            pickup.ServerDespawned -= HandleTreasureDespawned;
            NetworkObjectDespawn(pickup);
        }

        nextRespawnTime = -1f;
        rolledThisCycle = false;
        isClearing = false;
    }

    /// <summary>Starts a fresh cycle (new round): resets the respawn counter and timer.</summary>
    public void ServerResetCycle()
    {
        respawnsDone = 0;
        nextRespawnTime = -1f;
        rolledThisCycle = false;
    }

    /// <summary>Drops all references without despawning (used when the network session has already ended).</summary>
    public void ServerForget()
    {
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i] != null)
            {
                active[i].ServerDespawned -= HandleTreasureDespawned;
            }
        }
        active.Clear();
        spawnedPositions.Clear();
        nextRespawnTime = -1f;
        respawnsDone = 0;
        rolledThisCycle = false;
        context = null;
    }

    /// <summary>Per-frame server update: detects externally removed treasure and fires due respawns.</summary>
    public void ServerTick(float now)
    {
        if (active.Count > 0)
        {
            PruneDeadReferences();
            if (active.Count == 0)
            {
                ScheduleRespawn();
            }
        }

        if (nextRespawnTime >= 0f && now >= nextRespawnTime)
        {
            nextRespawnTime = -1f;
            if (active.Count == 0)
            {
                ServerSpawnBatch(true);
            }
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────
    private static void NetworkObjectDespawn(TreasurePickup pickup)
    {
        Unity.Netcode.NetworkObject networkObject = pickup.NetworkObject;
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn(true);
        }
        else
        {
            Destroy(pickup.gameObject);
        }
    }

    private void HandleTreasureDespawned(TreasurePickup pickup)
    {
        if (pickup != null)
        {
            pickup.ServerDespawned -= HandleTreasureDespawned;
        }

        active.Remove(pickup);
        if (!isClearing && active.Count == 0)
        {
            ScheduleRespawn();
        }
    }

    private void PruneDeadReferences()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            TreasurePickup pickup = active[i];
            bool dead = pickup == null || pickup.NetworkObject == null || !pickup.NetworkObject.IsSpawned;
            if (dead)
            {
                if (pickup != null)
                {
                    pickup.ServerDespawned -= HandleTreasureDespawned;
                }
                active.RemoveAt(i);
            }
        }
    }

    private void ScheduleRespawn()
    {
        if (respawnMode != LootRespawnMode.AfterCleared || context == null || nextRespawnTime >= 0f)
        {
            return;
        }
        if (maxRespawns >= 0 && respawnsDone >= maxRespawns)
        {
            return;
        }

        float low = Mathf.Min(respawnDelayMin, respawnDelayMax);
        float high = Mathf.Max(respawnDelayMin, respawnDelayMax);
        float delay = low + (float)context.Rng.NextDouble() * (high - low);
        nextRespawnTime = Time.time + Mathf.Max(0f, delay);
    }

    private static bool IsStaticGeometry(Collider collider)
    {
        return collider != null && collider.GetComponentInParent<Unity.Netcode.NetworkObject>() == null;
    }

    private Vector3 PickPosition(System.Random rng)
    {
        Vector3 origin = transform.position;
        bool scatter = scatterRadius > 0.01f;
        int attempts = scatter ? 14 : 1;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector3 candidate = origin;
            if (scatter)
            {
                double angle = rng.NextDouble() * System.Math.PI * 2.0;
                double radius = System.Math.Sqrt(rng.NextDouble()) * scatterRadius;
                candidate += new Vector3(
                    (float)(System.Math.Cos(angle) * radius),
                    0f,
                    (float)(System.Math.Sin(angle) * radius));
            }

            candidate = SnapToGround(candidate);
            if (scatter && !IsPlacementValid(origin, candidate))
            {
                continue;
            }
            return candidate;
        }

        return SnapToGround(origin);
    }

    private Vector3 SnapToGround(Vector3 position)
    {
        if (!snapToGround)
        {
            return position + Vector3.up * heightOffset;
        }

        Vector3 start = position + Vector3.up * groundProbeHeight;
        RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, groundProbeHeight + groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        float bestDistance = float.MaxValue;
        Vector3 bestPoint = position;
        for (int i = 0; i < hits.Length; i++)
        {
            if (!IsStaticGeometry(hits[i].collider) || hits[i].distance >= bestDistance)
            {
                continue;
            }
            bestDistance = hits[i].distance;
            bestPoint = hits[i].point;
            found = true;
        }

        return (found ? bestPoint : position) + Vector3.up * heightOffset;
    }

    private bool IsPlacementValid(Vector3 origin, Vector3 candidate)
    {
        float minSqr = minSeparation * minSeparation;
        for (int i = 0; i < spawnedPositions.Count; i++)
        {
            Vector3 delta = spawnedPositions[i] - candidate;
            delta.y = 0f;
            if (delta.sqrMagnitude < minSqr)
            {
                return false;
            }
        }

        Vector3 lift = Vector3.up * 0.6f;
        if (Physics.Linecast(origin + lift, candidate + lift, out RaycastHit hit, groundMask, QueryTriggerInteraction.Ignore)
            && IsStaticGeometry(hit.collider))
        {
            return false;
        }

        Collider[] overlaps = Physics.OverlapSphere(candidate + Vector3.up * 0.45f, 0.28f, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (IsStaticGeometry(overlaps[i]))
            {
                return false;
            }
        }
        return true;
    }

    // ── Validation ───────────────────────────────────────────────────────────
    public void GetValidationIssues(List<string> issues)
    {
        LootRoller.Validate(ActiveTable, issues);

        if (MaxSpawnCount <= 0)
        {
            issues.Add("Spawn count is 0, so this location never spawns anything.");
        }
        if (MaxSpawnCount > 1 && scatterRadius <= 0.01f)
        {
            issues.Add("Several rolls with a scatter radius of 0 will stack treasures on one spot.");
        }
        if (respawnMode == LootRespawnMode.AfterCleared && maxRespawns == 0)
        {
            issues.Add("Respawn after cleared is selected but Max Respawns is 0.");
        }
    }

    private void OnValidate()
    {
        if (maxSpawnCount < minSpawnCount)
        {
            maxSpawnCount = minSpawnCount;
        }
        if (respawnDelayMax < respawnDelayMin)
        {
            respawnDelayMax = respawnDelayMin;
        }
        if (customTable == null)
        {
            customTable = new LootTableDefinition();
        }
        if (customTable.entries == null)
        {
            customTable.entries = new List<LootEntry>();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Color color = spawnEnabled ? new Color(1f, 0.82f, 0.2f, 0.95f) : new Color(0.55f, 0.55f, 0.55f, 0.9f);
        Vector3 p = transform.position;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(p + Vector3.up * 0.25f, 0.18f);
        Gizmos.DrawLine(p, p + Vector3.up * 0.25f);

        if (scatterRadius > 0.01f)
        {
            Handles.color = new Color(color.r, color.g, color.b, 0.6f);
            Handles.DrawWireDisc(p, Vector3.up, scatterRadius);
        }

        LootTableDefinition table = ActiveTable;
        string summary = table != null
            ? $"{DisplayName}\n{MinSpawnCount}-{MaxSpawnCount} rolls, {table.emptyChancePercent:0}% empty"
            : DisplayName;
        Handles.Label(p + Vector3.up * 0.75f, summary);
    }
#endif
}
