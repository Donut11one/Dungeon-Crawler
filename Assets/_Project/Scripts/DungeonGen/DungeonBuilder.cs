using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// Source options for determining how the dungeon layout is selected at play time.
/// </summary>
public enum DungeonMapSource
{
    [Tooltip("Uses Active Layout if assigned; falls back to Built-In Code Map if empty.")]
    AutoDetect = 0,

    [Tooltip("Forces using the designed ScriptableObject asset assigned to Active Layout.")]
    DesignedAsset = 1,

    [Tooltip("Forces using the default procedural built-in code map.")]
    BuiltInCodeMap = 2,

    [Tooltip("Picks a random map from the Layout Pool each time a new round starts.")]
    RandomFromPool = 3
}

/// <summary>
/// Modular dungeon builder supporting both runtime procedural assembly and
/// data-driven layout assets (DungeonLayoutData) authored by level designers.
/// Assembles rooms, doorway openings, physical security blast doors, interactive levers,
/// monster spawns, and extraction pads.
/// Synchronizes the selected layout across all connected players.
/// </summary>
public class DungeonBuilder : NetworkBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("Level Design Data")]
    [Tooltip("How the dungeon map is chosen when starting a run.")]
    [SerializeField] private DungeonMapSource mapSource = DungeonMapSource.AutoDetect;

    [Tooltip("Primary layout asset used when Map Source is AutoDetect or DesignedAsset.")]
    [SerializeField] private DungeonLayoutData activeLayout;

    [Tooltip("Pool of layout assets randomly chosen from when Map Source is RandomFromPool.")]
    [SerializeField] private List<DungeonLayoutData> layoutPool = new List<DungeonLayoutData>();

    [Header("Layout Settings")]
    [SerializeField] private float wallHeight = 4f;
    [SerializeField] private float wallThickness = 0.3f;
    [SerializeField] private float doorWidth = 3.2f;

    [Header("Prefabs")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject leverPrefab;
    [SerializeField] private GameObject doorPrefab;

    [Header("Default Fallback Quantities")]
    [SerializeField] private int enemyCount = 2;

    [Header("Colors")]
    [SerializeField] private Color floorColor = new Color(0.25f, 0.25f, 0.28f);
    [SerializeField] private Color wallColor = new Color(0.35f, 0.32f, 0.30f);
    [SerializeField] private Color ceilingColor = new Color(0.20f, 0.20f, 0.22f);

    // ── Network State ────────────────────────────────────────────────────────
    public NetworkVariable<FixedString64Bytes> SynchronizedLayoutName = new NetworkVariable<FixedString64Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── State ────────────────────────────────────────────────────────────────
    private GameObject dungeonRoot;
    private NavMeshSurface navSurface;
    private GameObject extractionPointObj;
    private DungeonLayoutData currentResolvedLayout;

    private readonly List<Vector3> enemySpawnPoints = new();
    private readonly List<string> enemySpawnArchetypes = new();
    private readonly List<Vector3> playerSpawnPoints = new();
    private readonly List<GameObject> spawnedEnemies = new();
    private readonly List<DungeonRoom> dungeonRooms = new();

    // ── Public API ───────────────────────────────────────────────────────────
    public DungeonMapSource MapSource
    {
        get
        {
            return mapSource;
        }
        set
        {
            mapSource = value;
        }
    }

    public DungeonLayoutData ActiveLayout
    {
        get
        {
            return activeLayout;
        }
        set
        {
            activeLayout = value;
        }
    }

    public List<DungeonLayoutData> LayoutPool
    {
        get
        {
            return layoutPool;
        }
    }

    public DungeonLayoutData CurrentResolvedLayout
    {
        get
        {
            return currentResolvedLayout != null ? currentResolvedLayout : GetResolvedLayout();
        }
    }

    public GameObject LeverPrefab
    {
        get
        {
            return leverPrefab;
        }
    }

    public GameObject DoorPrefab
    {
        get
        {
            return doorPrefab;
        }
    }

    public IReadOnlyList<DungeonRoom> Rooms
    {
        get
        {
            return dungeonRooms;
        }
    }

    public Vector3 GetPlayerSpawnPoint(int index)
    {
        if (playerSpawnPoints.Count == 0)
        {
            return Vector3.up;
        }
        return playerSpawnPoints[index % playerSpawnPoints.Count];
    }

    /// <summary>
    /// Resolves which layout data asset should be used based on mapSource configuration.
    /// Returns null if the built-in procedural code map should be used.
    /// </summary>
    public DungeonLayoutData GetResolvedLayout()
    {
        switch (mapSource)
        {
            case DungeonMapSource.BuiltInCodeMap:
                return null;

            case DungeonMapSource.DesignedAsset:
                if (activeLayout != null && activeLayout.rooms.Count > 0)
                {
                    return activeLayout;
                }
                return LoadDefaultLayoutAsset();

            case DungeonMapSource.RandomFromPool:
                if (layoutPool != null && layoutPool.Count > 0)
                {
                    int randomIndex = Random.Range(0, layoutPool.Count);
                    return layoutPool[randomIndex];
                }
                return activeLayout;

            case DungeonMapSource.AutoDetect:
            default:
                if (activeLayout != null && activeLayout.rooms.Count > 0)
                {
                    return activeLayout;
                }
                DungeonLayoutData fallback = LoadDefaultLayoutAsset();
                if (fallback != null && fallback.rooms.Count > 0)
                {
                    return fallback;
                }
                return null;
        }
    }

    private DungeonLayoutData LoadDefaultLayoutAsset()
    {
#if UNITY_EDITOR
        DungeonLayoutData asset = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonLayoutData>(
            "Assets/_Project/ScriptableObjects/Dungeon/DefaultDungeonLayout.asset"
        );
        if (asset != null)
        {
            return asset;
        }
#endif
        return null;
    }

    // ── Unity / NGO lifecycle ────────────────────────────────────────────────
    private void Awake()
    {
        EnsureLeverPrefab();
        EnsureDoorPrefab();
        EnsureEnemyPrefab();
        RegisterPrefabs();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            currentResolvedLayout = GetResolvedLayout();
            if (currentResolvedLayout != null)
            {
                SynchronizedLayoutName.Value = currentResolvedLayout.layoutName;
            }
            else
            {
                SynchronizedLayoutName.Value = "";
            }

            BuildDungeon();
            CreateExtractionPoint();

            if (navSurface != null)
            {
                navSurface.BuildNavMesh();
            }
            SpawnEnemies();
            SpawnModularFeatures();
        }
        else
        {
            SynchronizedLayoutName.OnValueChanged += OnSynchronizedLayoutNameChanged;
            ApplySynchronizedLayout(SynchronizedLayoutName.Value.ToString());
        }
    }

    public override void OnNetworkDespawn()
    {
        SynchronizedLayoutName.OnValueChanged -= OnSynchronizedLayoutNameChanged;
    }

    private void OnSynchronizedLayoutNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        ApplySynchronizedLayout(current.ToString());
    }

    private void ApplySynchronizedLayout(string layoutName)
    {
        if (string.IsNullOrEmpty(layoutName))
        {
            currentResolvedLayout = null;
        }
        else if (activeLayout != null && string.Equals(activeLayout.layoutName, layoutName, System.StringComparison.OrdinalIgnoreCase))
        {
            currentResolvedLayout = activeLayout;
        }
        else
        {
            currentResolvedLayout = null;
            if (layoutPool != null)
            {
                for (int i = 0; i < layoutPool.Count; i++)
                {
                    if (layoutPool[i] != null && string.Equals(layoutPool[i].layoutName, layoutName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        currentResolvedLayout = layoutPool[i];
                        break;
                    }
                }
            }
        }

        BuildDungeon();
        CreateExtractionPoint();
    }

    private void EnsureLeverPrefab()
    {
        if (leverPrefab != null)
        {
            return;
        }

#if UNITY_EDITOR
        leverPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Dungeon/LeverPrefab.prefab");
#endif

        if (leverPrefab == null && NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
        {
            foreach (var netPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (netPrefab.Prefab != null && netPrefab.Prefab.GetComponent<DungeonLever>() != null)
                {
                    leverPrefab = netPrefab.Prefab;
                    break;
                }
            }
        }
    }

    private void EnsureDoorPrefab()
    {
        if (doorPrefab != null)
        {
            return;
        }

#if UNITY_EDITOR
        doorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Dungeon/DoorPrefab.prefab");
#endif

        if (doorPrefab == null && NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
        {
            foreach (var netPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (netPrefab.Prefab != null && netPrefab.Prefab.GetComponent<DungeonDoor>() != null)
                {
                    doorPrefab = netPrefab.Prefab;
                    break;
                }
            }
        }
    }

    private void EnsureEnemyPrefab()
    {
        if (enemyPrefab != null)
        {
            return;
        }

#if UNITY_EDITOR
        enemyPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Entities/EnemyPrefab.prefab");
#endif

        if (enemyPrefab == null && NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
        {
            foreach (var netPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (netPrefab.Prefab != null && netPrefab.Prefab.GetComponent<EnemyHealth>() != null)
                {
                    enemyPrefab = netPrefab.Prefab;
                    break;
                }
            }
        }
    }

    // ── Registration ─────────────────────────────────────────────────────────
    private void RegisterPrefabs()
    {
        EnsureLeverPrefab();
        EnsureDoorPrefab();
        EnsureEnemyPrefab();

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        if (enemyPrefab != null && !NetworkManager.Singleton.NetworkConfig.Prefabs.Contains(enemyPrefab))
        {
            NetworkManager.Singleton.AddNetworkPrefab(enemyPrefab);
        }

        if (leverPrefab != null && !NetworkManager.Singleton.NetworkConfig.Prefabs.Contains(leverPrefab))
        {
            NetworkManager.Singleton.AddNetworkPrefab(leverPrefab);
        }

        if (doorPrefab != null && !NetworkManager.Singleton.NetworkConfig.Prefabs.Contains(doorPrefab))
        {
            NetworkManager.Singleton.AddNetworkPrefab(doorPrefab);
        }
    }

    // ── Layout Assembly ──────────────────────────────────────────────────────
    public void BuildDungeon()
    {
        ClearDungeonHierarchy();

        dungeonRoot = new GameObject("Dungeon");
        dungeonRooms.Clear();
        enemySpawnPoints.Clear();
        enemySpawnArchetypes.Clear();
        playerSpawnPoints.Clear();

        DungeonLayoutData layout = currentResolvedLayout != null ? currentResolvedLayout : GetResolvedLayout();

        if (layout != null && layout.rooms.Count > 0)
        {
            BuildFromLayoutData(layout);
        }
        else
        {
            BuildDefaultLayout();
        }

        // NavMesh surface on dungeon root
        navSurface = dungeonRoot.AddComponent<NavMeshSurface>();
        navSurface.collectObjects = CollectObjects.Children;
    }

    private void ClearDungeonHierarchy()
    {
        if (dungeonRoot != null)
        {
            if (Application.isPlaying)
            {
                Destroy(dungeonRoot);
            }
            else
            {
                DestroyImmediate(dungeonRoot);
            }
            dungeonRoot = null;
        }

        GameObject existing = GameObject.Find("Dungeon");
        if (existing != null)
        {
            if (Application.isPlaying)
            {
                Destroy(existing);
            }
            else
            {
                DestroyImmediate(existing);
            }
        }
    }

    /// <summary>
    /// Builds the layout using an authored DungeonLayoutData asset.
    /// </summary>
    private void BuildFromLayoutData(DungeonLayoutData layout)
    {
        float defaultH = layout.defaultWallHeight > 0f ? layout.defaultWallHeight : wallHeight;

        for (int r = 0; r < layout.rooms.Count; r++)
        {
            RoomLayoutData roomData = layout.rooms[r];
            float h = roomData.dimensions.y > 0f ? roomData.dimensions.y : defaultH;

            // Convert doorway definitions for wall cutouts
            List<Door> doors = new List<Door>();
            for (int d = 0; d < roomData.doorways.Count; d++)
            {
                DoorwayDefinition def = roomData.doorways[d];
                Door.Side s = def.side switch
                {
                    WallSide.North => Door.Side.N,
                    WallSide.South => Door.Side.S,
                    WallSide.East => Door.Side.E,
                    _ => Door.Side.W
                };
                // Avoid duplicating doors if roomData.doors explicitly contains authored doors
                bool spawnDoorwayDoor = def.hasDoor && (roomData.doors == null || roomData.doors.Count == 0);
                doors.Add(new Door(s, def.width, def.offset, spawnDoorwayDoor, def.doorChannel, def.doorStartsOpen));
            }

            DungeonRoom room = Room(
                roomData.position,
                roomData.rotation,
                roomData.dimensions.x,
                roomData.dimensions.z,
                roomData.roomLabel,
                roomData.floorColor,
                roomData.wallColor,
                roomData.ceilingColor,
                doors.ToArray()
            );

            room.SetDoorways(roomData.doorways);

            // Sockets for levers
            for (int l = 0; l < roomData.levers.Count; l++)
            {
                LeverPlacementData lever = roomData.levers[l];
                room.AddSocket(
                    $"{roomData.roomId}_Lever_{l}",
                    SocketType.WallMount,
                    lever.localPosition,
                    Quaternion.Euler(lever.localEulerAngles),
                    leverPrefab,
                    lever.mechanismChannel
                );
            }

            // Sockets for standalone doors
            for (int d = 0; d < roomData.doors.Count; d++)
            {
                DoorPlacementData door = roomData.doors[d];
                room.AddSocket(
                    $"{roomData.roomId}_CustomDoor_{d}",
                    SocketType.Doorway,
                    door.localPosition,
                    Quaternion.Euler(door.localEulerAngles),
                    doorPrefab,
                    door.mechanismChannel
                );
            }

            // Props (pillars, obstacles, decorative cubes)
            if (roomData.props != null)
            {
                for (int pr = 0; pr < roomData.props.Count; pr++)
                {
                    PropPlacementData prop = roomData.props[pr];
                    Prim(room.transform, prop.propName, prop.localPosition, prop.localScale, prop.color, prop.localEulerAngles);
                }
            }

            // Enemy spawns transformed by room transform
            for (int e = 0; e < roomData.enemySpawns.Count; e++)
            {
                enemySpawnPoints.Add(room.transform.TransformPoint(roomData.enemySpawns[e].localPosition));
                enemySpawnArchetypes.Add(roomData.enemySpawns[e].enemyId);
            }

            // Player spawns transformed by room transform
            for (int p = 0; p < roomData.playerSpawns.Count; p++)
            {
                playerSpawnPoints.Add(room.transform.TransformPoint(roomData.playerSpawns[p]));
            }

            // Extraction point transformed by room transform
            if (roomData.hasExtractionPoint)
            {
                Vector3 extPos = room.transform.TransformPoint(roomData.extractionLocalPosition);
                CreateCustomExtractionPoint(extPos);
            }
        }
    }

    /// <summary>
    /// Builds the default facility dungeon with connected corridors,
    /// a security blast door on the North Doorway linked to the Main Hall lever,
    /// enemies, and an extraction point.
    /// </summary>
    private void BuildDefaultLayout()
    {
        float dw = doorWidth;

        // ── MAIN HALL (16 × 16, centre 0,0,0) ───────────────────────────────
        // North Doorway has an unlockable blast door linked to "MainHall_NorthGate"
        DungeonRoom mainHall = Room(
            Vector3.zero,
            16f,
            16f,
            "MainHall",
            floorColor,
            wallColor,
            ceilingColor,
            Door.N(dw, 0f, true, "MainHall_NorthGate", false),
            Door.E(dw),
            Door.W(dw)
        );

        // Security lever on North wall that unlocks the North Blast Door!
        mainHall.AddSocket(
            "MainHall_NorthLever",
            SocketType.WallMount,
            new Vector3(3.0f, 1.4f, 7.85f),
            Quaternion.Euler(0f, 180f, 0f),
            leverPrefab,
            "MainHall_NorthGate"
        );

        playerSpawnPoints.Add(new Vector3(-3f, 1f, -3f));
        playerSpawnPoints.Add(new Vector3(3f, 1f, -3f));
        playerSpawnPoints.Add(new Vector3(0f, 1f, 3f));

        // ── NORTH CORRIDOR (4 × 14, centre 0,0,15) ──────────────────────────
        Room(
            new Vector3(0f, 0f, 15f),
            4f,
            14f,
            "NorthCorridor",
            floorColor,
            wallColor,
            ceilingColor,
            Door.S(dw),
            Door.N(dw)
        );

        enemySpawnPoints.Add(new Vector3(0f, 0.5f, 15f));
        enemySpawnArchetypes.Add("");

        // ── NORTH STORAGE (10 × 8, centre 0,0,26) ───────────────────────────
        Room(
            new Vector3(0f, 0f, 26f),
            10f,
            8f,
            "NorthStorage",
            floorColor,
            wallColor,
            ceilingColor,
            Door.S(dw)
        );

        enemySpawnPoints.Add(new Vector3(0f, 0.5f, 26f));
        enemySpawnArchetypes.Add("");

        // ── EAST CORRIDOR (14 × 4, centre 15,0,0) ───────────────────────────
        Room(
            new Vector3(15f, 0f, 0f),
            14f,
            4f,
            "EastCorridor",
            floorColor,
            wallColor,
            ceilingColor,
            Door.W(dw),
            Door.E(dw)
        );

        // ── EAST CHAMBER (8 × 10, centre 26,0,0) ────────────────────────────
        Room(
            new Vector3(26f, 0f, 0f),
            8f,
            10f,
            "EastChamber",
            floorColor,
            wallColor,
            ceilingColor,
            Door.W(dw)
        );

        enemySpawnPoints.Add(new Vector3(26f, 0.5f, 3f));
        enemySpawnArchetypes.Add("");

        // ── WEST CORRIDOR (12 × 4, centre -14,0,0) ──────────────────────────
        Room(
            new Vector3(-14f, 0f, 0f),
            12f,
            4f,
            "WestCorridor",
            floorColor,
            wallColor,
            ceilingColor,
            Door.E(dw),
            Door.W(dw)
        );

        // ── WEST DEAD-END (6 × 6, centre -23,0,0) ───────────────────────────
        Room(
            new Vector3(-23f, 0f, 0f),
            6f,
            6f,
            "WestDeadEnd",
            floorColor,
            wallColor,
            ceilingColor,
            Door.E(dw)
        );

        enemySpawnPoints.Add(new Vector3(-23f, 0.5f, 0f));
        enemySpawnArchetypes.Add("");
    }

    // ── Doorway descriptor ───────────────────────────────────────────────────
    public struct Door
    {
        public enum Side
        {
            N,
            S,
            E,
            W
        }

        public Side side;
        public float width;
        public float offset;
        public bool hasDoor;
        public string doorChannel;
        public bool doorStartsOpen;

        public Door(Side s, float w, float o, bool hasDoor = false, string channel = "", bool startsOpen = false)
        {
            side = s;
            width = w;
            offset = o;
            this.hasDoor = hasDoor;
            this.doorChannel = channel;
            this.doorStartsOpen = startsOpen;
        }

        public static Door N(float w, float o = 0f, bool hasDoor = false, string channel = "", bool startsOpen = false)
        {
            return new Door(Side.N, w, o, hasDoor, channel, startsOpen);
        }

        public static Door S(float w, float o = 0f, bool hasDoor = false, string channel = "", bool startsOpen = false)
        {
            return new Door(Side.S, w, o, hasDoor, channel, startsOpen);
        }

        public static Door E(float w, float o = 0f, bool hasDoor = false, string channel = "", bool startsOpen = false)
        {
            return new Door(Side.E, w, o, hasDoor, channel, startsOpen);
        }

        public static Door W(float w, float o = 0f, bool hasDoor = false, string channel = "", bool startsOpen = false)
        {
            return new Door(Side.W, w, o, hasDoor, channel, startsOpen);
        }
    }

    // ── Room construction ────────────────────────────────────────────────────
    private DungeonRoom Room(
        Vector3 centre,
        Vector3 rotationEuler,
        float width,
        float depth,
        string label,
        Color floorCol,
        Color wallCol,
        Color ceilingCol,
        params Door[] doors
    )
    {
        GameObject go = new GameObject(label);
        go.transform.parent = dungeonRoot.transform;
        go.transform.position = centre;
        go.transform.rotation = Quaternion.Euler(rotationEuler);

        DungeonRoom room = go.AddComponent<DungeonRoom>();
        room.Configure(label, label, new Vector3(width, wallHeight, depth));
        dungeonRooms.Add(room);

        // Floor
        Prim(go.transform, "Floor", Vector3.zero, new Vector3(width, 0.2f, depth), floorCol);

        // Ceiling
        Prim(go.transform, "Ceiling", new Vector3(0f, wallHeight, 0f), new Vector3(width, 0.2f, depth), ceilingCol);

        // Four walls (with doorway cuts)
        BuildWall(go.transform, Door.Side.N, width, depth, wallCol, doors);
        BuildWall(go.transform, Door.Side.S, width, depth, wallCol, doors);
        BuildWall(go.transform, Door.Side.E, width, depth, wallCol, doors);
        BuildWall(go.transform, Door.Side.W, width, depth, wallCol, doors);

        // For any doorway with hasDoor = true, add a modular door socket!
        for (int i = 0; i < doors.Length; i++)
        {
            Door d = doors[i];
            if (!d.hasDoor)
            {
                continue;
            }

            Vector3 doorLocalPos = d.side switch
            {
                Door.Side.N => new Vector3(d.offset, 0f, depth * 0.5f),
                Door.Side.S => new Vector3(d.offset, 0f, -depth * 0.5f),
                Door.Side.E => new Vector3(width * 0.5f, 0f, d.offset),
                _ => new Vector3(-width * 0.5f, 0f, d.offset)
            };

            Quaternion doorLocalRot = d.side switch
            {
                Door.Side.N => Quaternion.identity,
                Door.Side.S => Quaternion.Euler(0f, 180f, 0f),
                Door.Side.E => Quaternion.Euler(0f, 90f, 0f),
                _ => Quaternion.Euler(0f, -90f, 0f)
            };

            room.AddSocket(
                $"{label}_Door_{d.side}",
                SocketType.Doorway,
                doorLocalPos,
                doorLocalRot,
                doorPrefab,
                d.doorChannel,
                true
            );
        }

        return room;
    }

    private DungeonRoom Room(
        Vector3 centre,
        float width,
        float depth,
        string label,
        Color floorCol,
        Color wallCol,
        Color ceilingCol,
        params Door[] doors
    )
    {
        return Room(centre, Vector3.zero, width, depth, label, floorCol, wallCol, ceilingCol, doors);
    }

    private void BuildWall(
        Transform parent,
        Door.Side side,
        float roomW,
        float roomD,
        Color wallCol,
        Door[] doors
    )
    {
        Door? door = null;
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i].side == side)
            {
                door = doors[i];
                break;
            }
        }

        bool nsWall = side == Door.Side.N || side == Door.Side.S;
        float span = nsWall ? roomW : roomD;

        Vector3 wallCentre = side switch
        {
            Door.Side.N => new Vector3(0f, wallHeight * 0.5f, roomD * 0.5f),
            Door.Side.S => new Vector3(0f, wallHeight * 0.5f, -roomD * 0.5f),
            Door.Side.E => new Vector3(roomW * 0.5f, wallHeight * 0.5f, 0f),
            _ => new Vector3(-roomW * 0.5f, wallHeight * 0.5f, 0f)
        };

        Vector3 spanAxis = nsWall ? Vector3.right : Vector3.forward;

        if (door == null)
        {
            Vector3 scale = nsWall
                ? new Vector3(span, wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, span);
            Prim(parent, $"Wall{side}", wallCentre, scale, wallCol);
            return;
        }

        float halfSpan = span * 0.5f;
        float halfDoor = door.Value.width * 0.5f;
        float dc = door.Value.offset;

        float gapL = dc - halfDoor;
        float gapR = dc + halfDoor;

        gapL = Mathf.Clamp(gapL, -halfSpan, halfSpan);
        gapR = Mathf.Clamp(gapR, -halfSpan, halfSpan);

        float leftLen = gapL + halfSpan;
        if (leftLen > 0.05f)
        {
            float centreOfs = -halfSpan + leftLen * 0.5f;
            Vector3 pos = wallCentre + spanAxis * centreOfs;
            Vector3 scale = nsWall
                ? new Vector3(leftLen, wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, leftLen);
            Prim(parent, $"Wall{side}_L", pos, scale, wallCol);
        }

        float rightLen = halfSpan - gapR;
        if (rightLen > 0.05f)
        {
            float centreOfs = halfSpan - rightLen * 0.5f;
            Vector3 pos = wallCentre + spanAxis * centreOfs;
            Vector3 scale = nsWall
                ? new Vector3(rightLen, wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, rightLen);
            Prim(parent, $"Wall{side}_R", pos, scale, wallCol);
        }
    }

    private void Prim(Transform parent, string label, Vector3 localPos, Vector3 scale, Color color, Vector3? localEuler = null)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = label;
        if (parent != null)
        {
            obj.transform.SetParent(parent, false);
        }
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = localEuler.HasValue ? Quaternion.Euler(localEuler.Value) : Quaternion.identity;
        obj.transform.localScale = scale;
        obj.isStatic = true;

        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            MaterialHelper.ApplyMaterial(rend, color);
        }
    }

    // ── Entity Spawning ──────────────────────────────────────────────────────
    private void SpawnEnemies()
    {
        EnsureEnemyPrefab();

        if (enemyPrefab == null)
        {
            Debug.LogWarning("[DungeonBuilder] No enemy prefab assigned.");
            return;
        }

        DungeonLayoutData layout = currentResolvedLayout != null ? currentResolvedLayout : GetResolvedLayout();
        bool isAuthored = layout != null && layout.rooms.Count > 0;
        int count = isAuthored ? enemySpawnPoints.Count : Mathf.Min(enemyCount, enemySpawnPoints.Count);
        List<EnemyData> allEnemies = EnemyRegistry.GetAllEnemies();

        for (int i = 0; i < count; i++)
        {
            string targetId = i < enemySpawnArchetypes.Count ? enemySpawnArchetypes[i] : null;
            EnemyData chosenData = null;

            if (!string.IsNullOrEmpty(targetId))
            {
                chosenData = EnemyRegistry.GetEnemy(targetId);
            }

            if (chosenData == null && allEnemies != null && allEnemies.Count > 0)
            {
                chosenData = allEnemies[i % allEnemies.Count];
            }

            int spawnPackSize = (chosenData != null && chosenData.packSpawnCount > 1) ? chosenData.packSpawnCount : 1;

            for (int p = 0; p < spawnPackSize; p++)
            {
                Vector3 spawnPos = enemySpawnPoints[i];
                if (p > 0)
                {
                    Vector2 packOffset = Random.insideUnitCircle * (1.1f + p * 0.4f);
                    Vector3 testPos = spawnPos + new Vector3(packOffset.x, 0f, packOffset.y);
                    if (NavMesh.SamplePosition(testPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    {
                        spawnPos = hit.position;
                    }
                    else
                    {
                        spawnPos = testPos;
                    }
                }

                GameObject e = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
                NetworkObject no = e.GetComponent<NetworkObject>();
                if (no != null)
                {
                    no.Spawn();
                }

                EnemyHealth eh = e.GetComponent<EnemyHealth>();
                if (eh != null && chosenData != null)
                {
                    eh.ApplyEnemyData(chosenData);
                }

                spawnedEnemies.Add(e);
            }
        }

        Debug.Log($"[DungeonBuilder] Spawned {spawnedEnemies.Count} enemies across the dungeon.");
    }

    private void SpawnModularFeatures()
    {
        for (int i = 0; i < dungeonRooms.Count; i++)
        {
            if (dungeonRooms[i] != null)
            {
                dungeonRooms[i].SpawnRoomFeatures(IsServer);
            }
        }
    }

    private void CreateExtractionPoint()
    {
        if (extractionPointObj != null)
        {
            return;
        }

        CreateCustomExtractionPoint(new Vector3(26f, 0.1f, 0f));
    }

    private void CreateCustomExtractionPoint(Vector3 pos)
    {
        if (extractionPointObj != null)
        {
            extractionPointObj.transform.position = pos;
            return;
        }

        extractionPointObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        extractionPointObj.name = "ExtractionPoint";
        if (dungeonRoot != null)
        {
            extractionPointObj.transform.SetParent(dungeonRoot.transform, false);
        }
        extractionPointObj.transform.position = pos;
        extractionPointObj.transform.localRotation = Quaternion.identity;
        extractionPointObj.transform.localScale = new Vector3(3f, 0.1f, 3f);

        Renderer rend = extractionPointObj.GetComponent<Renderer>();
        if (rend != null)
        {
            Color padColor = new Color(0.1f, 0.85f, 0.4f, 0.6f);
            MaterialHelper.ApplyMaterial(rend, padColor);
        }

        Collider col = extractionPointObj.GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        extractionPointObj.AddComponent<ExtractionPoint>();
    }

    // ── Round Reset ──────────────────────────────────────────────────────────
    public void ResetDungeonEntities()
    {
        if (!IsServer)
        {
            return;
        }

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

        for (int i = 0; i < dungeonRooms.Count; i++)
        {
            if (dungeonRooms[i] != null)
            {
                dungeonRooms[i].ResetRoomFeatures(IsServer);
            }
        }

        // If map source is RandomFromPool, re-pick a fresh map for the new dive!
        if (mapSource == DungeonMapSource.RandomFromPool && layoutPool != null && layoutPool.Count > 0)
        {
            currentResolvedLayout = GetResolvedLayout();
            SynchronizedLayoutName.Value = currentResolvedLayout != null ? currentResolvedLayout.layoutName : "";
            BuildDungeon();
            CreateExtractionPoint();
            if (navSurface != null)
            {
                navSurface.BuildNavMesh();
            }
        }

        SpawnEnemies();
        SpawnModularFeatures();
    }

    // ── Editor Helpers ───────────────────────────────────────────────────────
#if UNITY_EDITOR
    public void BuildDungeonInEditor()
    {
        EnsureLeverPrefab();
        EnsureDoorPrefab();
        currentResolvedLayout = GetResolvedLayout();
        BuildDungeon();
        CreateExtractionPoint();

        // Instantiate socket contents for visual preview in editor
        for (int i = 0; i < dungeonRooms.Count; i++)
        {
            if (dungeonRooms[i] != null)
            {
                dungeonRooms[i].SpawnRoomFeatures(false);
            }
        }

        if (navSurface != null)
        {
            navSurface.BuildNavMesh();
        }
    }

    public void ClearDungeonInEditor()
    {
        ClearDungeonHierarchy();
    }
#endif
}
