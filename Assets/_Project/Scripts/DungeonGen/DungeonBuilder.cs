using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// Builds a static dungeon layout from Unity primitives at runtime.
/// Rooms are positioned so their edges share the same world coordinate,
/// meaning corridors exactly bridge every gap with no overlap and no void.
/// Walls at junctions have a doorway opening so players can walk through.
/// </summary>
public class DungeonBuilder : NetworkBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("Layout")]
    [SerializeField] private float wallHeight    = 4f;
    [SerializeField] private float wallThickness = 0.3f;
    [SerializeField] private float doorWidth     = 3.2f;   // opening at every junction

    [Header("Spawning")]
    [SerializeField] private int        coinCount   = 15;
    [SerializeField] private int        enemyCount  = 2;
    [SerializeField] private GameObject coinPrefab;
    [SerializeField] private GameObject enemyPrefab;

    [Header("Colors")]
    [SerializeField] private Color floorColor   = new Color(0.25f, 0.25f, 0.28f);
    [SerializeField] private Color wallColor    = new Color(0.35f, 0.32f, 0.30f);
    [SerializeField] private Color ceilingColor = new Color(0.20f, 0.20f, 0.22f);

    // ── State ────────────────────────────────────────────────────────────────
    private GameObject    dungeonRoot;
    private NavMeshSurface navSurface;
    private GameObject    extractionPointObj;

    private readonly System.Collections.Generic.List<Vector3>     coinSpawnPoints   = new();
    private readonly System.Collections.Generic.List<Vector3>     enemySpawnPoints  = new();
    private readonly System.Collections.Generic.List<Vector3>     playerSpawnPoints = new();
    private readonly System.Collections.Generic.List<GameObject>  spawnedCoins      = new();
    private readonly System.Collections.Generic.List<GameObject>  spawnedEnemies    = new();

    // ── Public API ───────────────────────────────────────────────────────────
    public Vector3 GetPlayerSpawnPoint(int index)
    {
        if (playerSpawnPoints.Count == 0)
        {
            return Vector3.up;
        }
        return playerSpawnPoints[index % playerSpawnPoints.Count];
    }

    // ── Unity / NGO lifecycle ────────────────────────────────────────────────
    private void Awake()
    {
        EnsureCoinPrefab();
        RegisterPrefabs();
    }

    public override void OnNetworkSpawn()
    {
        BuildDungeon();
        CreateExtractionPoint();

        if (IsServer)
        {
            if (navSurface != null)
            {
                navSurface.BuildNavMesh();
            }
            SpawnCoins();
            SpawnEnemies();
        }
    }

    private void EnsureCoinPrefab()
    {
        if (coinPrefab != null)
        {
            return;
        }

#if UNITY_EDITOR
        coinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Items/CoinPrefab.prefab");
#endif

        if (coinPrefab == null && NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
        {
            foreach (var netPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (netPrefab.Prefab != null && netPrefab.Prefab.GetComponent<Coin>() != null)
                {
                    coinPrefab = netPrefab.Prefab;
                    break;
                }
            }
        }
    }

    // ── Registration ─────────────────────────────────────────────────────────
    private void RegisterPrefabs()
    {
        EnsureCoinPrefab();

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        if (coinPrefab != null &&
            !NetworkManager.Singleton.NetworkConfig.Prefabs.Contains(coinPrefab))
        {
            NetworkManager.Singleton.AddNetworkPrefab(coinPrefab);
        }

        if (enemyPrefab != null &&
            !NetworkManager.Singleton.NetworkConfig.Prefabs.Contains(enemyPrefab))
        {
            NetworkManager.Singleton.AddNetworkPrefab(enemyPrefab);
        }
    }

    // ── Layout ───────────────────────────────────────────────────────────────
    //
    //  Room positions are chosen so edges align exactly:
    //
    //   WestDeadEnd(−23,0,0) w6  |WestCorridor(−14,0,0) w12|  MainHall(0,0,0) w16
    //   east edge x=−20          east −8 / west −20          east x=8 / west x=−8
    //
    //                                 NorthStorage(0,0,26) d8
    //                                 south edge z=22
    //                                 NorthCorridor(0,0,15) d14
    //                                 south z=8 / north z=22
    //                                 MainHall north edge z=8
    //
    //                              MainHall east edge x=8
    //                              EastCorridor(15,0,0) w14
    //                              west x=8 / east x=22
    //                              EastChamber(26,0,0) w8
    //                              west edge x=22
    //
    private void BuildDungeon()
    {
        dungeonRoot = new GameObject("Dungeon");

        float dw = doorWidth;

        // ── MAIN HALL (16 × 16, centre 0,0,0) ───────────────────────────────
        //   north edge z=8, east edge x=8, west edge x=-8
        Room(Vector3.zero, 16f, 16f, "MainHall",
            Door.N(dw),
            Door.E(dw),
            Door.W(dw));

        playerSpawnPoints.Add(new Vector3(-3f, 1f, -3f));
        playerSpawnPoints.Add(new Vector3( 3f, 1f, -3f));
        playerSpawnPoints.Add(new Vector3( 0f, 1f,  3f));
        Coins( new Vector3(-5f, 0.8f,  5f),
               new Vector3( 5f, 0.8f,  5f),
               new Vector3( 0f, 0.8f, -5f),
               new Vector3(-5f, 0.8f, -5f),
               new Vector3( 4f, 0.8f, -4f));
        enemySpawnPoints.Add(new Vector3(0f, 0.5f, 6f));

        // ── NORTH CORRIDOR (4 × 14, centre 0,0,15) ──────────────────────────
        //   south edge z=8  →  meets MainHall north  (depth/2=7, centre.z=15, z=15-7=8 ✓)
        //   north edge z=22 →  meets NorthStorage south
        Room(new Vector3(0f, 0f, 15f), 4f, 14f, "NorthCorridor",
            Door.S(dw),
            Door.N(dw));

        Coins( new Vector3( 1f, 0.8f, 13f),
               new Vector3(-1f, 0.8f, 17f));

        // ── NORTH STORAGE (10 × 8, centre 0,0,26) ───────────────────────────
        //   south edge z=22  (depth/2=4, centre.z=26, z=26-4=22 ✓)
        Room(new Vector3(0f, 0f, 26f), 10f, 8f, "NorthStorage",
            Door.S(dw));

        Coins( new Vector3(-3f, 0.8f, 25f),
               new Vector3( 3f, 0.8f, 27f),
               new Vector3( 0f, 0.8f, 29f),
               new Vector3(-4f, 0.8f, 27f));
        enemySpawnPoints.Add(new Vector3(0f, 0.5f, 26f));

        // ── EAST CORRIDOR (14 × 4, centre 15,0,0) ───────────────────────────
        //   west edge x=8  →  meets MainHall east  (width/2=7, centre.x=15, x=15-7=8 ✓)
        //   east edge x=22 →  meets EastChamber west
        Room(new Vector3(15f, 0f, 0f), 14f, 4f, "EastCorridor",
            Door.W(dw),
            Door.E(dw));

        Coins( new Vector3(13f, 0.8f,  0f),
               new Vector3(17f, 0.8f,  1f));

        // ── EAST CHAMBER (8 × 10, centre 26,0,0) ────────────────────────────
        //   west edge x=22  (width/2=4, centre.x=26, x=26-4=22 ✓)
        //   EXTRACTION POINT IS HERE
        Room(new Vector3(26f, 0f, 0f), 8f, 10f, "EastChamber",
            Door.W(dw));

        Coins( new Vector3(25f, 0.8f, -3f),
               new Vector3(27f, 0.8f,  3f));

        // ── WEST CORRIDOR (12 × 4, centre -14,0,0) ──────────────────────────
        //   east edge x=-8  (width/2=6, centre.x=-14, x=-14+6=-8 ✓)
        //   west edge x=-20 →  meets WestDeadEnd east
        Room(new Vector3(-14f, 0f, 0f), 12f, 4f, "WestCorridor",
            Door.E(dw),
            Door.W(dw));

        // ── WEST DEAD-END (6 × 6, centre -23,0,0) ───────────────────────────
        //   east edge x=-20  (width/2=3, centre.x=-23, x=-23+3=-20 ✓)
        Room(new Vector3(-23f, 0f, 0f), 6f, 6f, "WestDeadEnd",
            Door.E(dw));

        Coins( new Vector3(-23f, 0.8f,  0f),
               new Vector3(-21f, 0.8f,  2f),
               new Vector3(-24f, 0.8f, -2f));

        // NavMesh surface
        navSurface = dungeonRoot.AddComponent<NavMeshSurface>();
        navSurface.collectObjects = CollectObjects.Children;
    }

    // ── Doorway descriptor ───────────────────────────────────────────────────

    private struct Door
    {
        public enum Side { N, S, E, W }
        public Side  side;
        public float width;
        public float offset; // shift along wall (0 = centred)

        private Door(Side s, float w, float o) { side = s; width = w; offset = o; }

        public static Door N(float w, float o = 0f) { return new Door(Side.N, w, o); }
        public static Door S(float w, float o = 0f) { return new Door(Side.S, w, o); }
        public static Door E(float w, float o = 0f) { return new Door(Side.E, w, o); }
        public static Door W(float w, float o = 0f) { return new Door(Side.W, w, o); }
    }

    // ── Room construction ────────────────────────────────────────────────────

    private void Room(Vector3 centre, float width, float depth,
                      string label, params Door[] doors)
    {
        GameObject go = new GameObject(label);
        go.transform.parent   = dungeonRoot.transform;
        go.transform.position = centre;

        // Floor
        Prim(go.transform, "Floor",
            Vector3.zero,
            new Vector3(width, 0.2f, depth),
            floorColor);

        // Ceiling
        Prim(go.transform, "Ceiling",
            new Vector3(0f, wallHeight, 0f),
            new Vector3(width, 0.2f, depth),
            ceilingColor);

        // Four walls (with optional doorways)
        BuildWall(go.transform, Door.Side.N, width, depth, doors);
        BuildWall(go.transform, Door.Side.S, width, depth, doors);
        BuildWall(go.transform, Door.Side.E, width, depth, doors);
        BuildWall(go.transform, Door.Side.W, width, depth, doors);
    }

    private void BuildWall(Transform parent, Door.Side side,
                            float roomW, float roomD, Door[] doors)
    {
        // Find matching doorway (if any)
        Door? door = null;
        foreach (Door d in doors)
        {
            if (d.side == side)
            {
                door = d;
                break;
            }
        }

        bool nsWall = side == Door.Side.N || side == Door.Side.S;
        float span  = nsWall ? roomW : roomD;  // wall runs along this length

        // Wall centre in room-local space
        Vector3 wallCentre = side switch
        {
            Door.Side.N => new Vector3(0f,          wallHeight * 0.5f,  roomD * 0.5f),
            Door.Side.S => new Vector3(0f,          wallHeight * 0.5f, -roomD * 0.5f),
            Door.Side.E => new Vector3( roomW * 0.5f, wallHeight * 0.5f, 0f),
            _           => new Vector3(-roomW * 0.5f, wallHeight * 0.5f, 0f),
        };

        // Span axis (direction along which the wall extends)
        Vector3 spanAxis = nsWall ? Vector3.right : Vector3.forward;

        if (door == null)
        {
            // Solid wall
            Vector3 scale = nsWall
                ? new Vector3(span,          wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, span);
            Prim(parent, $"Wall{side}", wallCentre, scale, wallColor);
            return;
        }

        // ── Wall with doorway ────────────────────────────────────────────────
        // Door occupies: [doorCentre - halfDoor .. doorCentre + halfDoor]
        // Everything outside that range becomes a wall segment.
        float halfSpan = span * 0.5f;
        float halfDoor = door.Value.width * 0.5f;
        float dc       = door.Value.offset;          // door centre offset from wall mid

        float gapL = dc - halfDoor;    // gap left edge  (relative to wall centre)
        float gapR = dc + halfDoor;    // gap right edge

        // Clamp to wall extents
        gapL = Mathf.Clamp(gapL, -halfSpan, halfSpan);
        gapR = Mathf.Clamp(gapR, -halfSpan, halfSpan);

        // Left segment: from -halfSpan to gapL
        float leftLen = gapL + halfSpan;
        if (leftLen > 0.05f)
        {
            float centreOfs = -halfSpan + leftLen * 0.5f;
            Vector3 pos     = wallCentre + spanAxis * centreOfs;
            Vector3 scale   = nsWall
                ? new Vector3(leftLen,       wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, leftLen);
            Prim(parent, $"Wall{side}_L", pos, scale, wallColor);
        }

        // Right segment: from gapR to +halfSpan
        float rightLen = halfSpan - gapR;
        if (rightLen > 0.05f)
        {
            float centreOfs = halfSpan - rightLen * 0.5f;
            Vector3 pos     = wallCentre + spanAxis * centreOfs;
            Vector3 scale   = nsWall
                ? new Vector3(rightLen,      wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, rightLen);
            Prim(parent, $"Wall{side}_R", pos, scale, wallColor);
        }

        // Note: no lintel needed — full-height door opening lets tall players through
    }

    // ── Primitive factory ────────────────────────────────────────────────────

    private void Prim(Transform parent, string label,
                       Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject obj          = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name                = label;
        obj.transform.parent    = parent;
        obj.transform.localPosition = localPos;
        obj.transform.localScale    = scale;
        obj.isStatic            = true;

        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color    = color;
            rend.material = mat;
        }
    }

    // ── Coin helper ──────────────────────────────────────────────────────────

    private void Coins(params Vector3[] points)
    {
        foreach (Vector3 p in points) { coinSpawnPoints.Add(p); }
    }

    // ── Entity spawning ──────────────────────────────────────────────────────

    private void SpawnCoins()
    {
        EnsureCoinPrefab();

        if (coinPrefab == null)
        {
            Debug.LogError("[DungeonBuilder] No coin prefab assigned and could not find one!");
            return;
        }

        int count = Mathf.Min(coinCount, coinSpawnPoints.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject c = Instantiate(coinPrefab, coinSpawnPoints[i], Quaternion.identity);
            NetworkObject no = c.GetComponent<NetworkObject>();
            if (no != null)
            {
                no.Spawn();
            }
            spawnedCoins.Add(c);
        }

        Debug.Log($"[DungeonBuilder] Spawned {spawnedCoins.Count} coins across the dungeon.");
    }

    private void SpawnEnemies()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("[DungeonBuilder] No enemy prefab assigned.");
            return;
        }

        int count = Mathf.Min(enemyCount, enemySpawnPoints.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject e = Instantiate(enemyPrefab, enemySpawnPoints[i], Quaternion.identity);
            NetworkObject no = e.GetComponent<NetworkObject>();
            if (no != null)
            {
                no.Spawn();
            }
            spawnedEnemies.Add(e);
        }
    }

    private void CreateExtractionPoint()
    {
        if (extractionPointObj != null)
        {
            return;
        }

        // EastChamber centre is (26, 0, 0)
        extractionPointObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        extractionPointObj.name = "ExtractionPoint";
        extractionPointObj.transform.position   = new Vector3(26f, 0.1f, 0f);
        extractionPointObj.transform.localScale = new Vector3(3f, 0.1f, 3f);
        if (dungeonRoot != null)
        {
            extractionPointObj.transform.parent = dungeonRoot.transform;
        }

        Renderer rend = extractionPointObj.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color    = new Color(0.1f, 1f, 0.2f);
            rend.material = mat;
        }

        Destroy(extractionPointObj.GetComponent<CapsuleCollider>());
        BoxCollider trigger = extractionPointObj.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size      = new Vector3(1f, 20f, 1f);

        extractionPointObj.AddComponent<ExtractionPoint>();
    }

    // ── Round reset ──────────────────────────────────────────────────────────

    public void ResetDungeonEntities()
    {
        if (!IsServer)
        {
            return;
        }

        for (int i = spawnedCoins.Count - 1; i >= 0; i--)
        {
            if (spawnedCoins[i] == null)
            {
                continue;
            }
            NetworkObject no = spawnedCoins[i].GetComponent<NetworkObject>();
            if (no != null && no.IsSpawned)
            {
                no.Despawn(true);
            }
            else
            {
                Destroy(spawnedCoins[i]);
            }
        }
        spawnedCoins.Clear();

        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            if (spawnedEnemies[i] == null)
            {
                continue;
            }
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
        spawnedEnemies.Clear();

        SpawnCoins();
        SpawnEnemies();
    }
}
