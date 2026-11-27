using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Drives every LootSpawnPoint. Server-authoritative: it waits for the NetworkManager to become a server,
/// spawns treasure at each location, re-rolls everything when a round starts, and runs respawn timers.
/// Clients only need the component for prefab/treasure registration. Add it once to an empty object
/// (outside the generated 'Dungeon') and put spawn points beneath it.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Dungeon Crawler/Loot/Loot Spawner")]
public class LootSpawner : MonoBehaviour, ITreasureSpawnContext
{
    [Header("Required")]
    [Tooltip("Networked prefab with NetworkObject + TreasurePickup (TreasurePickupPrefab).")]
    [SerializeField] private GameObject treasurePrefab;

    [Header("Spawn Locations")]
    [Tooltip("Also use every LootSpawnPoint found under this object.")]
    [SerializeField] private bool autoCollectChildPoints = true;
    [Tooltip("Extra spawn points anywhere in the scene.")]
    [SerializeField] private List<LootSpawnPoint> spawnPoints = new List<LootSpawnPoint>();

    [Header("Timing")]
    [Tooltip("Roll loot as soon as the host / server starts.")]
    [SerializeField] private bool spawnOnServerStart = true;
    [Tooltip("Clear leftovers and roll fresh loot every time a new dive (round) begins.")]
    [SerializeField] private bool rerollOnRoundStart = true;

    [Header("Limits & Randomness")]
    [Tooltip("Global cap on simultaneously existing treasures. 0 = unlimited.")]
    [Min(0)]
    [SerializeField] private int maxActiveTreasures = 0;
    [Tooltip("Use a fixed seed for reproducible loot while testing.")]
    [SerializeField] private bool useFixedSeed;
    [SerializeField] private int seed = 12345;

    [Header("Registry")]
    [Tooltip("Treasures that are spawned by other code but never appear in a loot table here.")]
    [SerializeField] private List<TreasureData> extraRegisteredTreasures = new List<TreasureData>();

    [Header("Debug")]
    [SerializeField] private bool verboseLogging;

    private readonly List<LootSpawnPoint> resolvedPoints = new List<LootSpawnPoint>();
    private System.Random rng;
    private bool serverSessionActive;
    private bool prefabRegistered;
    private bool prefabValidated;
    private bool prefabUsable = true;
    private bool spawningAll;
    private GameManager subscribedGameManager;

    /// <summary>Raised on the server for every treasure that is spawned.</summary>
    public event System.Action<TreasurePickup> TreasureSpawned;

    public bool ServerSessionActive { get { return serverSessionActive; } }
    public GameObject TreasurePrefab { get { return treasurePrefab; } set { treasurePrefab = value; prefabValidated = false; prefabRegistered = false; } }

    public System.Random Rng
    {
        get
        {
            if (rng == null)
            {
                rng = useFixedSeed ? new System.Random(seed) : new System.Random();
            }
            return rng;
        }
    }

    public int ActiveTreasureCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < resolvedPoints.Count; i++)
            {
                if (resolvedPoints[i] != null)
                {
                    total += resolvedPoints[i].ActiveCount;
                }
            }
            return total;
        }
    }

    public bool CanSpawnMore
    {
        get { return maxActiveTreasures <= 0 || ActiveTreasureCount < maxActiveTreasures; }
    }

    // ── Setup (runs on every peer) ───────────────────────────────────────────
    private void Awake()
    {
        ResolvePoints();
        RegisterTreasures();
        TryRegisterPrefab();
    }

    private void Start()
    {
        TryRegisterPrefab();
    }

    private void OnDisable()
    {
        UnsubscribeFromGameManager();
    }

    /// <summary>Builds the final, de-duplicated list of spawn points (explicit list first, then children).</summary>
    public IReadOnlyList<LootSpawnPoint> ResolvePoints()
    {
        resolvedPoints.Clear();
        HashSet<LootSpawnPoint> seen = new HashSet<LootSpawnPoint>();

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            LootSpawnPoint point = spawnPoints[i];
            if (point != null && seen.Add(point))
            {
                resolvedPoints.Add(point);
            }
        }

        if (autoCollectChildPoints)
        {
            LootSpawnPoint[] children = GetComponentsInChildren<LootSpawnPoint>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (seen.Add(children[i]))
                {
                    resolvedPoints.Add(children[i]);
                }
            }
        }
        return resolvedPoints;
    }

    private void RegisterTreasures()
    {
        for (int i = 0; i < extraRegisteredTreasures.Count; i++)
        {
            TreasureRegistry.Register(extraRegisteredTreasures[i]);
        }

        for (int p = 0; p < resolvedPoints.Count; p++)
        {
            LootTableDefinition table = resolvedPoints[p].ActiveTable;
            if (table == null || table.entries == null)
            {
                continue;
            }

            for (int e = 0; e < table.entries.Count; e++)
            {
                LootEntry entry = table.entries[e];
                if (entry != null && entry.treasure != null)
                {
                    TreasureRegistry.Register(entry.treasure);
                }
            }
        }
    }

    /// <summary>
    /// Registers the treasure prefab with the NetworkManager (same approach DungeonBuilder uses for its prefabs).
    /// Must happen before the NetworkManager starts so host and clients agree on the prefab list.
    /// </summary>
    private void TryRegisterPrefab()
    {
        if (prefabRegistered || treasurePrefab == null)
        {
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.NetworkConfig == null)
        {
            return;
        }

        try
        {
            if (!manager.NetworkConfig.Prefabs.Contains(treasurePrefab))
            {
                manager.AddNetworkPrefab(treasurePrefab);
            }
            prefabRegistered = true;
        }
        catch (System.Exception ex)
        {
            prefabRegistered = true;
            Debug.LogWarning($"[LootSpawner] Could not register '{treasurePrefab.name}' with the NetworkManager ({ex.Message}). Add it to the NetworkManager's prefab list manually.", this);
        }
    }

    private bool ValidatePrefab()
    {
        if (prefabValidated)
        {
            return prefabUsable;
        }

        prefabValidated = true;
        prefabUsable = false;

        if (treasurePrefab == null)
        {
            Debug.LogError("[LootSpawner] No Treasure Prefab assigned. Assign TreasurePickupPrefab in the inspector.", this);
        }
        else if (treasurePrefab.GetComponent<NetworkObject>() == null || treasurePrefab.GetComponent<TreasurePickup>() == null)
        {
            Debug.LogError($"[LootSpawner] '{treasurePrefab.name}' needs both a NetworkObject and a TreasurePickup component.", this);
        }
        else
        {
            prefabUsable = true;
        }
        return prefabUsable;
    }

    // ── Server session lifecycle ─────────────────────────────────────────────
    private void Update()
    {
        TryRegisterPrefab();

        NetworkManager manager = NetworkManager.Singleton;
        bool isServer = manager != null && manager.IsListening && manager.IsServer;

        if (isServer && !serverSessionActive)
        {
            BeginServerSession();
        }
        else if (!isServer && serverSessionActive)
        {
            EndServerSession();
        }

        if (!serverSessionActive)
        {
            return;
        }

        SubscribeToGameManager();

        float now = Time.time;
        for (int i = 0; i < resolvedPoints.Count; i++)
        {
            if (resolvedPoints[i] != null)
            {
                resolvedPoints[i].ServerTick(now);
            }
        }
    }

    private void BeginServerSession()
    {
        serverSessionActive = true;
        rng = null;

        ResolvePoints();
        RegisterTreasures();

        List<string> issues = new List<string>();
        for (int i = 0; i < resolvedPoints.Count; i++)
        {
            resolvedPoints[i].ServerInitialize(this);
            resolvedPoints[i].ServerResetCycle();

            issues.Clear();
            resolvedPoints[i].GetValidationIssues(issues);
            for (int j = 0; j < issues.Count; j++)
            {
                Debug.LogWarning($"[LootSpawner] '{resolvedPoints[i].DisplayName}': {issues[j]}", resolvedPoints[i]);
            }
        }

        Log($"Server session started with {resolvedPoints.Count} spawn point(s).");

        if (spawnOnServerStart)
        {
            SpawnAll();
        }
    }

    private void EndServerSession()
    {
        serverSessionActive = false;
        UnsubscribeFromGameManager();

        for (int i = 0; i < resolvedPoints.Count; i++)
        {
            if (resolvedPoints[i] != null)
            {
                resolvedPoints[i].ServerForget();
            }
        }
        Log("Server session ended.");
    }

    private void SubscribeToGameManager()
    {
        if (subscribedGameManager != null || GameManager.Instance == null)
        {
            return;
        }

        subscribedGameManager = GameManager.Instance;
        subscribedGameManager.CurrentState.OnValueChanged += OnGameStateChanged;
    }

    private void UnsubscribeFromGameManager()
    {
        if (subscribedGameManager != null)
        {
            subscribedGameManager.CurrentState.OnValueChanged -= OnGameStateChanged;
            subscribedGameManager = null;
        }
    }

    private void OnGameStateChanged(GameManager.GameState previous, GameManager.GameState current)
    {
        if (rerollOnRoundStart && serverSessionActive && current == GameManager.GameState.Playing)
        {
            Log("Round started - rolling fresh loot.");
            RerollAll();
        }
    }

    // ── Public server API ────────────────────────────────────────────────────
    /// <summary>Rolls every location that has no live treasure. Returns the number of treasures spawned.</summary>
    public int SpawnAll()
    {
        if (!RequireServer("SpawnAll") || spawningAll)
        {
            return 0;
        }

        spawningAll = true;
        int total = 0;
        try
        {
            for (int i = 0; i < resolvedPoints.Count; i++)
            {
                if (resolvedPoints[i] != null)
                {
                    total += resolvedPoints[i].ServerSpawnBatch(false);
                }
            }
        }
        finally
        {
            spawningAll = false;
        }

        Log($"Spawned {total} treasure(s) across {resolvedPoints.Count} location(s).");
        return total;
    }

    public void ClearAll()
    {
        if (!RequireServer("ClearAll"))
        {
            return;
        }

        for (int i = 0; i < resolvedPoints.Count; i++)
        {
            if (resolvedPoints[i] != null)
            {
                resolvedPoints[i].ServerClear();
            }
        }
    }

    /// <summary>Removes all treasure, resets respawn counters and rolls every location again.</summary>
    public int RerollAll()
    {
        if (!RequireServer("RerollAll"))
        {
            return 0;
        }

        ClearAll();
        for (int i = 0; i < resolvedPoints.Count; i++)
        {
            if (resolvedPoints[i] != null)
            {
                resolvedPoints[i].ServerResetCycle();
            }
        }
        return SpawnAll();
    }

    public int RerollPoint(LootSpawnPoint point)
    {
        if (!RequireServer("RerollPoint") || point == null)
        {
            return 0;
        }

        point.ServerClear();
        point.ServerResetCycle();
        return point.ServerSpawnBatch(false);
    }

    // ── ITreasureSpawnContext ────────────────────────────────────────────────
    public TreasurePickup SpawnTreasure(TreasureData treasure, Vector3 position, Quaternion rotation)
    {
        if (!serverSessionActive || !ValidatePrefab())
        {
            return null;
        }

        TryRegisterPrefab();
        TreasurePickup pickup = TreasurePickup.SpawnServer(treasurePrefab, treasure, position, rotation);
        if (pickup != null)
        {
            if (TreasureSpawned != null)
            {
                TreasureSpawned.Invoke(pickup);
            }
            if (verboseLogging)
            {
                Log($"Spawned '{treasure.DisplayName}' at {position}.");
            }
        }
        return pickup;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private bool RequireServer(string action)
    {
        if (serverSessionActive)
        {
            return true;
        }

        Debug.LogWarning($"[LootSpawner] {action} is only available while running as the server / host.", this);
        return false;
    }

    private void Log(string message)
    {
        if (verboseLogging)
        {
            Debug.Log($"[LootSpawner] {message}", this);
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        treasurePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TreasurePickup.DefaultPrefabPath);
    }

    /// <summary>Editor helper: stores every child LootSpawnPoint in the explicit list.</summary>
    public void EditorCollectChildPoints()
    {
        LootSpawnPoint[] children = GetComponentsInChildren<LootSpawnPoint>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (!spawnPoints.Contains(children[i]))
            {
                spawnPoints.Add(children[i]);
            }
        }
        spawnPoints.RemoveAll(p => p == null);
    }
#endif
}
