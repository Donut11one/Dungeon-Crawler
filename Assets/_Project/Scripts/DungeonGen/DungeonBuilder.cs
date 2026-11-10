using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// Builds a static dungeon layout from Unity primitives at runtime.
/// Creates rooms, corridors, and spawns barrels and enemies.
/// Bakes NavMesh at runtime using NavMeshSurface.
/// </summary>
public class DungeonBuilder : NetworkBehaviour
{
    [Header("Layout")]
    [SerializeField] private float wallHeight = 4f;
    [SerializeField] private float wallThickness = 0.4f;

    [Header("Spawning")]
    [SerializeField] private int barrelCount = 10;
    [SerializeField] private int enemyCount = 2;
    [SerializeField] private GameObject barrelPrefab;
    [SerializeField] private GameObject enemyPrefab;

    [Header("Colors")]
    [SerializeField] private Color floorColor = new Color(0.25f, 0.25f, 0.28f);
    [SerializeField] private Color wallColor = new Color(0.35f, 0.32f, 0.30f);
    [SerializeField] private Color ceilingColor = new Color(0.2f, 0.2f, 0.22f);

    private GameObject dungeonRoot;
    private NavMeshSurface navSurface;

    // Spawn positions collected during room creation
    private readonly System.Collections.Generic.List<Vector3> barrelSpawnPoints = new();
    private readonly System.Collections.Generic.List<Vector3> enemySpawnPoints = new();
    private readonly System.Collections.Generic.List<Vector3> playerSpawnPoints = new();

    // Active spawned entities tracked on server
    private readonly System.Collections.Generic.List<GameObject> spawnedBarrels = new();
    private readonly System.Collections.Generic.List<GameObject> spawnedEnemies = new();

    /// <summary>
    /// Get a spawn position for a player by index.
    /// </summary>
    public Vector3 GetPlayerSpawnPoint(int index)
    {
        if (playerSpawnPoints.Count == 0)
        {
            return Vector3.up;
        }
        return playerSpawnPoints[index % playerSpawnPoints.Count];
    }

    public override void OnNetworkSpawn()
    {
        BuildDungeon();

        if (IsServer)
        {
            // Bake NavMesh then spawn entities
            if (navSurface != null)
            {
                navSurface.BuildNavMesh();
            }
            SpawnBarrels();
            SpawnEnemies();
        }
    }

    private void BuildDungeon()
    {
        dungeonRoot = new GameObject("Dungeon");

        // === MAIN HALL (16 x 16) — the central arena ===
        CreateRoom(Vector3.zero, 16f, 16f, "MainHall");
        playerSpawnPoints.Add(new Vector3(-3f, 1f, -3f));
        playerSpawnPoints.Add(new Vector3(3f, 1f, -3f));
        playerSpawnPoints.Add(new Vector3(0f, 1f, 3f));
        barrelSpawnPoints.Add(new Vector3(-5f, 0.6f, 5f));
        barrelSpawnPoints.Add(new Vector3(5f, 0.6f, 5f));
        barrelSpawnPoints.Add(new Vector3(0f, 0.6f, -5f));
        barrelSpawnPoints.Add(new Vector3(-6f, 0.6f, -2f));
        enemySpawnPoints.Add(new Vector3(0f, 0.5f, 6f));

        // === NORTH CORRIDOR (4 x 10) ===
        CreateRoom(new Vector3(0f, 0f, 13f), 4f, 10f, "NorthCorridor");
        barrelSpawnPoints.Add(new Vector3(1f, 0.6f, 14f));

        // === NORTH STORAGE ROOM (10 x 8) ===
        CreateRoom(new Vector3(0f, 0f, 26f), 10f, 8f, "NorthStorage");
        barrelSpawnPoints.Add(new Vector3(-3f, 0.6f, 25f));
        barrelSpawnPoints.Add(new Vector3(3f, 0.6f, 27f));
        barrelSpawnPoints.Add(new Vector3(0f, 0.6f, 28f));
        enemySpawnPoints.Add(new Vector3(0f, 0.5f, 26f));

        // === EAST CORRIDOR (10 x 4) ===
        CreateRoom(new Vector3(13f, 0f, 0f), 10f, 4f, "EastCorridor");
        barrelSpawnPoints.Add(new Vector3(14f, 0.6f, 0f));

        // === EAST CHAMBER (8 x 10) ===
        CreateRoom(new Vector3(26f, 0f, 0f), 8f, 10f, "EastChamber");
        barrelSpawnPoints.Add(new Vector3(25f, 0.6f, -3f));
        barrelSpawnPoints.Add(new Vector3(27f, 0.6f, 3f));

        // === WEST CORRIDOR (8 x 4) ===
        CreateRoom(new Vector3(-12f, 0f, 0f), 8f, 4f, "WestCorridor");

        // === WEST DEAD-END (6 x 6) ===
        CreateRoom(new Vector3(-23f, 0f, 0f), 6f, 6f, "WestDeadEnd");
        barrelSpawnPoints.Add(new Vector3(-23f, 0.6f, 0f));

        // Add NavMeshSurface for runtime baking
        navSurface = dungeonRoot.AddComponent<NavMeshSurface>();
        navSurface.collectObjects = CollectObjects.Children;
    }

    /// <summary>
    /// Create a rectangular room with floor, ceiling, and four walls.
    /// Doorways are left open between connected rooms (handled by overlap).
    /// </summary>
    private void CreateRoom(Vector3 center, float width, float depth, string roomName)
    {
        GameObject room = new GameObject(roomName);
        room.transform.parent = dungeonRoot.transform;
        room.transform.position = center;

        // Floor
        CreatePrimitive(room.transform, "Floor",
            Vector3.zero,
            new Vector3(width, 0.2f, depth),
            floorColor);

        // Ceiling
        CreatePrimitive(room.transform, "Ceiling",
            new Vector3(0f, wallHeight, 0f),
            new Vector3(width, 0.2f, depth),
            ceilingColor);

        // North wall
        CreatePrimitive(room.transform, "WallNorth",
            new Vector3(0f, wallHeight / 2f, depth / 2f),
            new Vector3(width, wallHeight, wallThickness),
            wallColor);

        // South wall
        CreatePrimitive(room.transform, "WallSouth",
            new Vector3(0f, wallHeight / 2f, -depth / 2f),
            new Vector3(width, wallHeight, wallThickness),
            wallColor);

        // East wall
        CreatePrimitive(room.transform, "WallEast",
            new Vector3(width / 2f, wallHeight / 2f, 0f),
            new Vector3(wallThickness, wallHeight, depth),
            wallColor);

        // West wall
        CreatePrimitive(room.transform, "WallWest",
            new Vector3(-width / 2f, wallHeight / 2f, 0f),
            new Vector3(wallThickness, wallHeight, depth),
            wallColor);
    }

    private void CreatePrimitive(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.parent = parent;
        obj.transform.localPosition = localPos;
        obj.transform.localScale = scale;
        obj.isStatic = true;

        // Color
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            // Create a simple unlit-ish material
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            rend.material = mat;
        }
    }

    private void SpawnBarrels()
    {
        if (barrelPrefab == null)
        {
            Debug.LogWarning("[DungeonBuilder] No barrel prefab assigned. Skipping barrel spawns.");
            return;
        }

        int count = Mathf.Min(barrelCount, barrelSpawnPoints.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject barrel = Instantiate(barrelPrefab, barrelSpawnPoints[i], Quaternion.identity);
            barrel.GetComponent<NetworkObject>().Spawn();
            spawnedBarrels.Add(barrel);
        }
    }

    private void SpawnEnemies()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("[DungeonBuilder] No enemy prefab assigned. Skipping enemy spawns.");
            return;
        }

        int count = Mathf.Min(enemyCount, enemySpawnPoints.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject enemy = Instantiate(enemyPrefab, enemySpawnPoints[i], Quaternion.identity);
            enemy.GetComponent<NetworkObject>().Spawn();
            spawnedEnemies.Add(enemy);
        }
    }

    /// <summary>
    /// Server: Despawn all current barrels and enemies, and re-spawn them fresh at their original locations.
    /// </summary>
    public void ResetDungeonEntities()
    {
        if (!IsServer)
        {
            return;
        }

        // Clean up barrels
        for (int i = spawnedBarrels.Count - 1; i >= 0; i--)
        {
            if (spawnedBarrels[i] != null)
            {
                NetworkObject no = spawnedBarrels[i].GetComponent<NetworkObject>();
                if (no != null && no.IsSpawned)
                {
                    no.Despawn(true);
                }
                else
                {
                    Destroy(spawnedBarrels[i]);
                }
            }
        }
        spawnedBarrels.Clear();

        // Clean up enemies
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            if (spawnedEnemies[i] != null)
            {
                NetworkObject no = spawnedEnemies[i].GetComponent<NetworkObject>();
                if (no != null && no.IsSpawned)
                {
                    no.Despawn(true);
                }
                else
                {
                    Destroy(spawnedEnemies[i]);
                }
            }
        }
        spawnedEnemies.Clear();

        // Re-spawn everything fresh
        SpawnBarrels();
        SpawnEnemies();
    }
}
