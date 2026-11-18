using Unity.Netcode;
using UnityEngine;

public enum SocketType
{
    WallMount,
    Doorway,
    FloorPad,
    CeilingMount,
    Prop
}

/// <summary>
/// Modular socket / attachment point placed inside dungeon rooms.
/// Allows rooms to be built as modular prefabs or runtime layouts with sockets
/// for levers, doors, traps, chests, or puzzle mechanisms.
/// </summary>
public class RoomSocket : MonoBehaviour
{
    [Header("Socket Configuration")]
    [SerializeField] private string socketId = "Socket";
    [SerializeField] private SocketType socketType = SocketType.WallMount;
    [SerializeField] private GameObject contentPrefab;
    [SerializeField] private string mechanismChannel = "";
    [SerializeField] private bool autoSpawnOnServer = true;

    private GameObject spawnedInstance;

    public string SocketId
    {
        get
        {
            return socketId;
        }
    }

    public SocketType Type
    {
        get
        {
            return socketType;
        }
    }

    public GameObject ContentPrefab
    {
        get
        {
            return contentPrefab;
        }
        set
        {
            contentPrefab = value;
        }
    }

    public string MechanismChannel
    {
        get
        {
            return mechanismChannel;
        }
        set
        {
            mechanismChannel = value;
        }
    }

    public bool AutoSpawnOnServer
    {
        get
        {
            return autoSpawnOnServer;
        }
    }

    public GameObject SpawnedInstance
    {
        get
        {
            return spawnedInstance;
        }
    }

    public void Initialize(
        string id,
        SocketType type,
        GameObject prefab,
        string channel = "",
        bool autoSpawn = true
    )
    {
        socketId = id;
        socketType = type;
        contentPrefab = prefab;
        mechanismChannel = channel;
        autoSpawnOnServer = autoSpawn;
    }

    /// <summary>
    /// Instantiates and configures the socket's content prefab.
    /// Spawns the NetworkObject if server-authoritative.
    /// </summary>
    public GameObject SpawnContent(bool isServer)
    {
        if (contentPrefab == null)
        {
            return null;
        }

        if (spawnedInstance != null)
        {
            return spawnedInstance;
        }

        spawnedInstance = Instantiate(contentPrefab, transform.position, transform.rotation);
        spawnedInstance.name = $"{contentPrefab.name}_{socketId}";

        // If the spawned object has a DungeonLever, apply the socket's mechanism channel
        DungeonLever lever = spawnedInstance.GetComponent<DungeonLever>();
        if (lever != null && !string.IsNullOrEmpty(mechanismChannel))
        {
            lever.SetMechanismChannel(mechanismChannel);
        }

        // If the spawned object has a DungeonDoor, apply the socket's mechanism channel
        DungeonDoor door = spawnedInstance.GetComponent<DungeonDoor>();
        if (door != null && !string.IsNullOrEmpty(mechanismChannel))
        {
            door.SetMechanismChannel(mechanismChannel);
        }

        // Spawn across network if running on server
        if (isServer)
        {
            NetworkObject netObj = spawnedInstance.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsSpawned)
            {
                netObj.Spawn();
            }
        }

        return spawnedInstance;
    }

    /// <summary>
    /// Cleans up the spawned instance on round reset.
    /// </summary>
    public void DespawnContent(bool isServer)
    {
        if (spawnedInstance == null)
        {
            return;
        }

        if (isServer)
        {
            NetworkObject netObj = spawnedInstance.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
            else
            {
                Destroy(spawnedInstance);
            }
        }
        else
        {
            Destroy(spawnedInstance);
        }

        spawnedInstance = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = socketType switch
        {
            SocketType.WallMount => Color.yellow,
            SocketType.Doorway => Color.cyan,
            SocketType.FloorPad => Color.green,
            SocketType.CeilingMount => Color.magenta,
            _ => Color.white
        };

        Gizmos.DrawWireCube(transform.position, new Vector3(0.4f, 0.4f, 0.4f));
        Gizmos.DrawRay(transform.position, transform.forward * 0.7f);
    }
#endif
}
