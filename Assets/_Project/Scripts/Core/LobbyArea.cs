using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Independent staging lobby area.
/// Builds a dedicated 3D staging bunker/elevator room completely separate from the dungeon.
/// Manages player spawning in the lobby, mutual ready agreement before diving,
/// and returning players to the lobby upon extraction or round reset.
/// </summary>
public class LobbyArea : NetworkBehaviour
{
    public static LobbyArea Instance { get; private set; }

    [Header("Lobby Placement")]
    [SerializeField] private Vector3 lobbyCenter = new Vector3(0f, 0f, -80f);
    [SerializeField] private float roomWidth = 16f;
    [SerializeField] private float roomDepth = 12f;
    [SerializeField] private float wallHeight = 4f;
    [SerializeField] private float wallThickness = 0.3f;

    [Header("Colors")]
    [SerializeField] private Color floorColor = new Color(0.28f, 0.30f, 0.33f);
    [SerializeField] private Color wallColor = new Color(0.38f, 0.40f, 0.44f);
    [SerializeField] private Color ceilingColor = new Color(0.22f, 0.23f, 0.25f);
    [SerializeField] private Color elevatorColor = new Color(0.15f, 0.85f, 1.0f);

    [Header("Ready System")]
    public NetworkVariable<int> ReadyCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> TotalPlayers = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> DiveCountdown = new NetworkVariable<float>(
        -1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private GameObject lobbyRoot;
    private readonly List<Vector3> lobbySpawnPoints = new();
    private GameObject elevatorPad;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetupSpawnPoints();
    }

    private void SetupSpawnPoints()
    {
        lobbySpawnPoints.Clear();
        lobbySpawnPoints.Add(lobbyCenter + new Vector3(-3f, 1f, -2f));
        lobbySpawnPoints.Add(lobbyCenter + new Vector3( 3f, 1f, -2f));
        lobbySpawnPoints.Add(lobbyCenter + new Vector3(-1f, 1f, -4f));
        lobbySpawnPoints.Add(lobbyCenter + new Vector3( 1f, 1f, -4f));
    }

    public Vector3 GetLobbySpawnPoint(int index)
    {
        if (lobbySpawnPoints.Count == 0)
        {
            SetupSpawnPoints();
        }
        return lobbySpawnPoints[index % lobbySpawnPoints.Count];
    }

    public override void OnNetworkSpawn()
    {
        BuildLobbyGeometry();
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (GameManager.Instance == null || NetworkManager.Singleton == null)
        {
            return;
        }

        // Only manage ready countdown when actively in the Lobby
        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            if (DiveCountdown.Value >= 0f)
            {
                DiveCountdown.Value = -1f;
            }
            return;
        }

        int total = NetworkManager.Singleton.ConnectedClientsList.Count;
        int ready = 0;

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
            {
                continue;
            }

            PlayerController pc = client.PlayerObject.GetComponent<PlayerController>();
            if (pc != null && pc.IsReadyToDive.Value)
            {
                ready++;
            }
        }

        TotalPlayers.Value = total;
        ReadyCount.Value = ready;

        // Both/all players must agree before diving!
        if (ready >= total && total > 0)
        {
            if (DiveCountdown.Value < 0f)
            {
                DiveCountdown.Value = 3.5f;
            }

            DiveCountdown.Value -= Time.deltaTime;

            if (DiveCountdown.Value <= 0f)
            {
                DiveCountdown.Value = -1f;

                // Reset all ready states before launching
                foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client.PlayerObject != null)
                    {
                        PlayerController pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null)
                        {
                            pc.IsReadyToDive.Value = false;
                        }
                    }
                }

                LaunchExpedition();
            }
        }
        else
        {
            // If someone is not ready or cancels/steps off, abort the countdown
            if (DiveCountdown.Value >= 0f)
            {
                DiveCountdown.Value = -1f;
            }
        }
    }

    /// <summary>
    /// Builds the 3D staging bunker environment with walls, floor, props, and elevator pad.
    /// Runs on both Host and Clients so geometry is visible to all players.
    /// </summary>
    private void BuildLobbyGeometry()
    {
        if (lobbyRoot != null)
        {
            return;
        }

        lobbyRoot = new GameObject("LobbyArea");
        Transform root = lobbyRoot.transform;

        // 1. Floor
        CreatePrimitive(root, "LobbyFloor",
            lobbyCenter,
            new Vector3(roomWidth, 0.2f, roomDepth),
            floorColor);

        // 2. Ceiling
        CreatePrimitive(root, "LobbyCeiling",
            lobbyCenter + new Vector3(0f, wallHeight, 0f),
            new Vector3(roomWidth, 0.2f, roomDepth),
            ceilingColor);

        // 3. Walls
        float halfW = roomWidth * 0.5f;
        float halfD = roomDepth * 0.5f;
        float halfH = wallHeight * 0.5f;

        // North wall (solid)
        CreatePrimitive(root, "LobbyWall_N",
            lobbyCenter + new Vector3(0f, halfH, halfD),
            new Vector3(roomWidth, wallHeight, wallThickness),
            wallColor);

        // South wall (solid)
        CreatePrimitive(root, "LobbyWall_S",
            lobbyCenter + new Vector3(0f, halfH, -halfD),
            new Vector3(roomWidth, wallHeight, wallThickness),
            wallColor);

        // East wall (solid)
        CreatePrimitive(root, "LobbyWall_E",
            lobbyCenter + new Vector3(halfW, halfH, 0f),
            new Vector3(wallThickness, wallHeight, roomDepth),
            wallColor);

        // West wall (solid)
        CreatePrimitive(root, "LobbyWall_W",
            lobbyCenter + new Vector3(-halfW, halfH, 0f),
            new Vector3(wallThickness, wallHeight, roomDepth),
            wallColor);

        // 4. Props & Décor
        BuildProps(root);

        // 5. Dive Elevator Platform (Trigger zone to enter dungeon)
        BuildDiveElevator(root);
    }

    private void BuildProps(Transform root)
    {
        // Supply lockers along west wall
        for (int i = -1; i <= 1; i++)
        {
            Vector3 lockerPos = lobbyCenter + new Vector3(-roomWidth * 0.5f + 0.6f, 1.5f, i * 2.2f);
            CreatePrimitive(root, $"Locker_{i}",
                lockerPos,
                new Vector3(0.6f, 3f, 1.2f),
                new Color(0.2f, 0.25f, 0.28f));
        }

        // Cargo crates along east wall
        Vector3 cratePos1 = lobbyCenter + new Vector3(roomWidth * 0.5f - 1.2f, 0.6f, -2f);
        CreatePrimitive(root, "Crate_1", cratePos1, new Vector3(1.2f, 1.2f, 1.2f), new Color(0.5f, 0.35f, 0.2f));

        Vector3 cratePos2 = lobbyCenter + new Vector3(roomWidth * 0.5f - 1.2f, 0.6f, -0.6f);
        CreatePrimitive(root, "Crate_2", cratePos2, new Vector3(1.2f, 1.2f, 1.2f), new Color(0.45f, 0.3f, 0.18f));

        Vector3 cratePos3 = lobbyCenter + new Vector3(roomWidth * 0.5f - 1.2f, 1.6f, -1.3f);
        CreatePrimitive(root, "Crate_Top", cratePos3, new Vector3(1f, 0.8f, 1f), new Color(0.55f, 0.38f, 0.22f));

        // Overhead staging sign
        Vector3 signPos = lobbyCenter + new Vector3(0f, wallHeight - 0.5f, -roomDepth * 0.5f + 0.3f);
        GameObject sign = CreatePrimitive(root, "FacilitySign",
            signPos,
            new Vector3(6f, 0.8f, 0.1f),
            new Color(0.1f, 0.6f, 0.9f));
        sign.GetComponent<Collider>().enabled = false;
    }

    private void BuildDiveElevator(Transform root)
    {
        Vector3 elevatorPos = lobbyCenter + new Vector3(0f, 0.1f, roomDepth * 0.5f - 2.5f);

        // Glowing platform base
        elevatorPad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        elevatorPad.name = "DungeonElevatorPad";
        elevatorPad.transform.parent = root;
        elevatorPad.transform.position = elevatorPos;
        elevatorPad.transform.localScale = new Vector3(4f, 0.15f, 4f);

        Renderer rend = elevatorPad.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = elevatorColor;
            rend.material = mat;
        }

        // Replace default capsule collider with a tall trigger volume
        Destroy(elevatorPad.GetComponent<CapsuleCollider>());
        BoxCollider trigger = elevatorPad.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1f, 20f, 1f);

        // Attach the elevator trigger logic
        elevatorPad.AddComponent<LobbyDiveTrigger>();

        // Overhead elevator sign
        Vector3 signPos = elevatorPos + new Vector3(0f, wallHeight - 0.6f, 0f);
        GameObject signObj = CreatePrimitive(root, "ElevatorSign",
            signPos,
            new Vector3(5f, 0.6f, 0.1f),
            new Color(1f, 0.7f, 0.1f));
        signObj.GetComponent<Collider>().enabled = false;
    }

    private GameObject CreatePrimitive(Transform parent, string label,
                                       Vector3 pos, Vector3 scale, Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = label;
        obj.transform.parent = parent;
        obj.transform.position = pos;
        obj.transform.localScale = scale;
        obj.isStatic = true;

        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            rend.material = mat;
        }

        return obj;
    }

    /// <summary>
    /// Server: Initiates the dive by teleporting all connected players into the dungeon
    /// and starting the game round.
    /// </summary>
    public void LaunchExpedition()
    {
        if (!IsServer)
        {
            return;
        }

        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        Debug.Log("[LobbyArea] All contractors ready! Launching expedition dive into dungeon.");
        GameManager.Instance.StartRound();
    }
}
