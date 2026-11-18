using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Modular room component attached to all dungeon rooms.
/// Can be placed on procedurally generated room roots or authored room prefabs.
/// Manages modular sockets (levers, doors, traps, chests) and handles their lifecycle.
/// </summary>
public class DungeonRoom : MonoBehaviour
{
    [Header("Room Identity")]
    [SerializeField] private string roomId = "Room";
    [SerializeField] private string roomLabel = "Room";
    [SerializeField] private Vector3 roomDimensions = new Vector3(10f, 4f, 10f);

    [Header("Sockets")]
    [SerializeField] private List<RoomSocket> sockets = new List<RoomSocket>();

    [Header("Doorways")]
    [SerializeField] private List<DoorwayDefinition> doorways = new List<DoorwayDefinition>();

    private readonly List<GameObject> spawnedFeatures = new List<GameObject>();

    public List<DoorwayDefinition> Doorways
    {
        get
        {
            return doorways;
        }
    }

    public void SetDoorways(IEnumerable<DoorwayDefinition> defs)
    {
        doorways.Clear();
        if (defs != null)
        {
            doorways.AddRange(defs);
        }
    }

    public bool HasDoorway(WallSide side)
    {
        for (int i = 0; i < doorways.Count; i++)
        {
            if (doorways[i].side == side)
            {
                return true;
            }
        }

        return false;
    }

    public DoorwayDefinition GetDoorway(WallSide side)
    {
        for (int i = 0; i < doorways.Count; i++)
        {
            if (doorways[i].side == side)
            {
                return doorways[i];
            }
        }

        return null;
    }

    public void ToggleDoorway(WallSide side, float width = 3.2f)
    {
        for (int i = 0; i < doorways.Count; i++)
        {
            if (doorways[i].side == side)
            {
                doorways.RemoveAt(i);
                RebuildWalls();
                return;
            }
        }

        doorways.Add(new DoorwayDefinition(side, width, 0f, false, ""));
        RebuildWalls();
    }

    public void SetDoorwayState(WallSide side, bool hasDoorway, float width = 3.2f)
    {
        bool current = HasDoorway(side);
        if (current == hasDoorway)
        {
            return;
        }

        if (hasDoorway)
        {
            doorways.Add(new DoorwayDefinition(side, width, 0f, false, ""));
        }
        else
        {
            for (int i = doorways.Count - 1; i >= 0; i--)
            {
                if (doorways[i].side == side)
                {
                    doorways.RemoveAt(i);
                }
            }
        }

        RebuildWalls();
    }

    public void RebuildWalls(float wallHeight = 4f, float doorWidth = 3.2f, Color? wallColor = null)
    {
        Color col = wallColor ?? new Color(0.35f, 0.32f, 0.30f);

        List<GameObject> toDestroy = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("WallN") || child.name.StartsWith("WallS") ||
                child.name.StartsWith("WallE") || child.name.StartsWith("WallW"))
            {
                toDestroy.Add(child.gameObject);
            }
        }

        for (int i = 0; i < toDestroy.Count; i++)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(toDestroy[i]);
#else
            Destroy(toDestroy[i]);
#endif
        }

        float roomW = roomDimensions.x;
        float roomD = roomDimensions.z;

        BuildWallSide(WallSide.North, roomW, roomD, wallHeight, col);
        BuildWallSide(WallSide.South, roomW, roomD, wallHeight, col);
        BuildWallSide(WallSide.East, roomW, roomD, wallHeight, col);
        BuildWallSide(WallSide.West, roomW, roomD, wallHeight, col);
    }

    private void BuildWallSide(WallSide side, float roomW, float roomD, float wallHeight, Color wallCol)
    {
        DoorwayDefinition door = GetDoorway(side);
        bool nsWall = side == WallSide.North || side == WallSide.South;
        float span = nsWall ? roomW : roomD;

        Vector3 wallCentre = side switch
        {
            WallSide.North => new Vector3(0f, wallHeight * 0.5f, roomD * 0.5f),
            WallSide.South => new Vector3(0f, wallHeight * 0.5f, -roomD * 0.5f),
            WallSide.East => new Vector3(roomW * 0.5f, wallHeight * 0.5f, 0f),
            _ => new Vector3(-roomW * 0.5f, wallHeight * 0.5f, 0f)
        };

        Vector3 spanAxis = nsWall ? Vector3.right : Vector3.forward;

        if (door == null)
        {
            Vector3 scale = nsWall
                ? new Vector3(span, wallHeight, 0.3f)
                : new Vector3(0.3f, wallHeight, span);
            CreatePrimitiveWall($"Wall{side}", wallCentre, scale, wallCol);
            return;
        }

        float halfSpan = span * 0.5f;
        float halfDoor = door.width * 0.5f;
        float dc = door.offset;

        float gapL = Mathf.Clamp(dc - halfDoor, -halfSpan, halfSpan);
        float gapR = Mathf.Clamp(dc + halfDoor, -halfSpan, halfSpan);

        float leftLen = gapL + halfSpan;
        if (leftLen > 0.05f)
        {
            float centreOfs = -halfSpan + leftLen * 0.5f;
            Vector3 pos = wallCentre + spanAxis * centreOfs;
            Vector3 scale = nsWall
                ? new Vector3(leftLen, wallHeight, 0.3f)
                : new Vector3(0.3f, wallHeight, leftLen);
            CreatePrimitiveWall($"Wall{side}_L", pos, scale, wallCol);
        }

        float rightLen = halfSpan - gapR;
        if (rightLen > 0.05f)
        {
            float centreOfs = halfSpan - rightLen * 0.5f;
            Vector3 pos = wallCentre + spanAxis * centreOfs;
            Vector3 scale = nsWall
                ? new Vector3(rightLen, wallHeight, 0.3f)
                : new Vector3(0.3f, wallHeight, rightLen);
            CreatePrimitiveWall($"Wall{side}_R", pos, scale, wallCol);
        }
    }

    private GameObject CreatePrimitiveWall(string wallName, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = wallName;
        cube.transform.parent = transform;
        cube.transform.localPosition = localPos;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = localScale;
        cube.isStatic = true;

        Renderer rend = cube.GetComponent<Renderer>();
        if (rend != null)
        {
            MaterialHelper.ApplyMaterial(rend, color);
        }

#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCreatedObjectUndo(cube, "Create Wall");
#endif
        return cube;
    }

    public Vector3 GetWallCenterLocal(WallSide side, float wallHeight = 4f)
    {
        return side switch
        {
            WallSide.North => new Vector3(0f, 0f, roomDimensions.z * 0.5f),
            WallSide.South => new Vector3(0f, 0f, -roomDimensions.z * 0.5f),
            WallSide.East => new Vector3(roomDimensions.x * 0.5f, 0f, 0f),
            _ => new Vector3(-roomDimensions.x * 0.5f, 0f, 0f)
        };
    }

    public Vector3 GetWallCenterWorld(WallSide side, float wallHeight = 4f)
    {
        return transform.position + transform.rotation * GetWallCenterLocal(side, wallHeight);
    }

    public string RoomId
    {
        get
        {
            return roomId;
        }
    }

    public string RoomLabel
    {
        get
        {
            return roomLabel;
        }
    }

    public Vector3 Dimensions
    {
        get
        {
            return roomDimensions;
        }
    }

    public IReadOnlyList<RoomSocket> Sockets
    {
        get
        {
            return sockets;
        }
    }

    public void Configure(string id, string label, Vector3 dimensions)
    {
        roomId = id;
        roomLabel = label;
        roomDimensions = dimensions;
        name = label;
    }

    /// <summary>
    /// Modularly adds a socket attachment point to this room at runtime.
    /// Useful for procedurally configured rooms or runtime room builder scripts.
    /// </summary>
    public RoomSocket AddSocket(
        string socketId,
        SocketType type,
        Vector3 localPosition,
        Quaternion localRotation,
        GameObject contentPrefab = null,
        string channel = "",
        bool autoSpawn = true
    )
    {
        GameObject socketObj = new GameObject($"Socket_{socketId}");
        socketObj.transform.parent = transform;
        socketObj.transform.localPosition = localPosition;
        socketObj.transform.localRotation = localRotation;

        RoomSocket socket = socketObj.AddComponent<RoomSocket>();
        socket.Initialize(socketId, type, contentPrefab, channel, autoSpawn);

        if (!sockets.Contains(socket))
        {
            sockets.Add(socket);
        }

        return socket;
    }

    /// <summary>
    /// Spawns all modular features registered in this room's sockets.
    /// Supports both code-generated sockets and sockets authored inside room prefabs.
    /// </summary>
    public void SpawnRoomFeatures(bool isServer)
    {
        // Discover any pre-authored sockets placed in children (e.g., from room prefabs)
        RoomSocket[] childSockets = GetComponentsInChildren<RoomSocket>(true);
        for (int i = 0; i < childSockets.Length; i++)
        {
            if (!sockets.Contains(childSockets[i]))
            {
                sockets.Add(childSockets[i]);
            }
        }

        for (int i = 0; i < sockets.Count; i++)
        {
            RoomSocket socket = sockets[i];
            if (socket == null || !socket.AutoSpawnOnServer)
            {
                continue;
            }

            GameObject instance = socket.SpawnContent(isServer);
            if (instance != null && !spawnedFeatures.Contains(instance))
            {
                spawnedFeatures.Add(instance);
            }
        }
    }

    /// <summary>
    /// Despawns and cleans up all spawned socket features on round reset.
    /// </summary>
    public void ResetRoomFeatures(bool isServer)
    {
        for (int i = 0; i < sockets.Count; i++)
        {
            if (sockets[i] != null)
            {
                sockets[i].DespawnContent(isServer);
            }
        }

        spawnedFeatures.Clear();
    }

    /// <summary>
    /// Retrieves a socket by its unique ID.
    /// </summary>
    public RoomSocket GetSocket(string targetSocketId)
    {
        for (int i = 0; i < sockets.Count; i++)
        {
            if (sockets[i] != null && sockets[i].SocketId == targetSocketId)
            {
                return sockets[i];
            }
        }

        return null;
    }
}
