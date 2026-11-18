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

    private readonly List<GameObject> spawnedFeatures = new List<GameObject>();

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
