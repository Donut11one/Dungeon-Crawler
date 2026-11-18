using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central game state manager. Singleton, server-authoritative.
/// Manages game states (Lobby → Playing → RoundOver) and the countdown timer.
/// Coordinates between LobbyArea (staging & extraction return) and DungeonBuilder (dungeon run).
/// Handles economy rules: run earnings start from $0 per dive; extracted money is banked permanently.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Round Settings")]
    [SerializeField] private float roundDuration = 120f;

    public NetworkVariable<float> RoundTimer = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<GameState> CurrentState = new NetworkVariable<GameState>(
        GameState.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Round Flow")]
    [SerializeField] private float autoReturnDelay = 5.0f;

    public NetworkVariable<float> AutoReturnTimer = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public enum GameState
    {
        Lobby,
        Playing,
        RoundOver
    }

    // Tracks which clientIds have successfully extracted this round (server-side only)
    private readonly HashSet<ulong> extractedPlayers = new();
    private int lastScreenWidth;
    private int lastScreenHeight;

    private void Awake()
    {
        // Enforce frame rate cap and VSync to prevent standalone builds from running at 2,000+ FPS and pegging GPU/CPU at 100%
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 60;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (GetComponent<LobbyArea>() == null && FindAnyObjectByType<LobbyArea>() == null)
        {
            gameObject.AddComponent<LobbyArea>();
        }
    }

    private void OnApplicationQuit()
    {
        ShutdownNetwork();
    }

    public override void OnDestroy()
    {
        ShutdownNetwork();
        base.OnDestroy();
    }

    private void ShutdownNetwork()
    {
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    private void Update()
    {
        // Re-enforce frame rate cap and VSync if the resolution or window size changes
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            if (QualitySettings.vSyncCount == 0)
            {
                QualitySettings.vSyncCount = 1;
            }
            if (Application.targetFrameRate != 60)
            {
                Application.targetFrameRate = 60;
            }
        }

        if (!IsServer)
        {
            return;
        }

        if (CurrentState.Value == GameState.Playing)
        {
            RoundTimer.Value -= Time.deltaTime;

            if (RoundTimer.Value <= 0f)
            {
                RoundTimer.Value = 0f;
                TriggerRoundOver();
            }
        }
        else if (CurrentState.Value == GameState.RoundOver)
        {
            if (AutoReturnTimer.Value > 0f)
            {
                AutoReturnTimer.Value -= Time.deltaTime;
                if (AutoReturnTimer.Value <= 0f)
                {
                    AutoReturnTimer.Value = 0f;
                    ReturnToLobby();
                }
            }
        }
    }

    /// <summary>
    /// Transitions from Playing to RoundOver, drops failed contractors' carried loot,
    /// and begins the countdown to automatically respawn all players in the bunker.
    /// </summary>
    public void TriggerRoundOver()
    {
        if (!IsServer || CurrentState.Value == GameState.RoundOver)
        {
            return;
        }

        CurrentState.Value = GameState.RoundOver;
        AutoReturnTimer.Value = autoReturnDelay;

        // Timeout / Round Over: contractors who failed to extract lose their inventory
        if (NetworkManager.Singleton != null)
        {
            foreach (NetworkClient nc in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (nc.PlayerObject != null && !extractedPlayers.Contains(nc.ClientId))
                {
                    PlayerInventory inv = nc.PlayerObject.GetComponent<PlayerInventory>();
                    if (inv != null)
                    {
                        inv.ClearInventory();
                    }
                }
            }
        }

        OnRoundOverClientRpc();
    }

    /// <summary>
    /// Checks if all connected players have either extracted or died.
    /// If yes, automatically ends the expedition and triggers the return countdown.
    /// </summary>
    public void CheckRoundEndConditions()
    {
        if (!IsServer || CurrentState.Value != GameState.Playing)
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        int validPlayers = 0;
        bool allFinished = true;

        foreach (NetworkClient nc in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (nc.PlayerObject == null)
            {
                continue;
            }

            PlayerHealth ph = nc.PlayerObject.GetComponent<PlayerHealth>();
            if (ph == null)
            {
                continue;
            }

            validPlayers++;
            // Active if neither dead nor extracted
            if (!ph.IsDead.Value && !extractedPlayers.Contains(nc.ClientId))
            {
                allFinished = false;
                break;
            }
        }

        if (validPlayers > 0 && allFinished)
        {
            TriggerRoundOver();
        }
    }

    /// <summary>
    /// Server: Start the expedition dive.
    /// Resets dungeon entities, resets run money to $0 (banked savings stay),
    /// revives all players, resets ready states, and teleports all players from Lobby into Dungeon.
    /// </summary>
    public void StartRound()
    {
        if (!IsServer)
        {
            return;
        }

        extractedPlayers.Clear();
        RoundTimer.Value = roundDuration;
        AutoReturnTimer.Value = 0f;

        // Reset dungeon entities (coins, traps, enemies)
        DungeonBuilder dungeonBuilder = FindAnyObjectByType<DungeonBuilder>();
        if (dungeonBuilder != null)
        {
            dungeonBuilder.ResetDungeonEntities();
        }

        // Teleport all connected players into the dungeon, reset ready flags, and reset run money to $0
        if (NetworkManager.Singleton != null)
        {
            int clientIndex = 0;
            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                {
                    continue;
                }

                // Reset readiness flag
                PlayerController pc = client.PlayerObject.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.IsReadyToDive.Value = false;
                }

                // Reset run money to 0 for this new dive; persistent bank is preserved!
                PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                if (wallet != null)
                {
                    wallet.StartNewRun();
                }

                Vector3 dungeonSpawn = dungeonBuilder != null
                    ? dungeonBuilder.GetPlayerSpawnPoint(clientIndex)
                    : new Vector3(0f, 1f, 0f);

                // Fully revive and position player
                PlayerHealth ph = client.PlayerObject.GetComponent<PlayerHealth>();
                if (ph != null)
                {
                    ph.ResetPlayer(dungeonSpawn);
                }

                clientIndex++;
            }
        }

        extractedPlayers.Clear();
        Physics.SyncTransforms();

        // Transition to Playing state
        CurrentState.Value = GameState.Playing;

        OnRoundStartedClientRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartRoundServerRpc()
    {
        StartRound();
    }

    /// <summary>
    /// Server: Mark a player as extracted.
    /// Banks their run earnings into their persistent savings, removes control while waiting on the pad,
    /// and triggers the round end check.
    /// </summary>
    public void ExtractPlayer(ulong clientId, int bonusAmount = 0)
    {
        if (!IsServer)
        {
            return;
        }

        if (extractedPlayers.Contains(clientId))
        {
            return;
        }

        extractedPlayers.Add(clientId);

        int bankedAmount = 0;
        int totalBank = 0;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            if (client.PlayerObject != null)
            {
                PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                if (wallet != null)
                {
                    bankedAmount = wallet.BankRunMoney(bonusAmount);
                    totalBank = wallet.BankedMoney.Value;
                }

                PlayerController pc = client.PlayerObject.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.IsReadyToDive.Value = false;
                }
            }
        }

        // Notify all clients that this player escaped
        OnPlayerExtractedClientRpc(clientId, bankedAmount, totalBank);

        // Check if all players have now either extracted or died
        CheckRoundEndConditions();
    }

    /// <summary>
    /// Client-initiated RPC: Request extraction when standing on the pad and pressing [E].
    /// Server validates distance and state before authorizing extraction.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestExtractionServerRpc(ulong requestingClientId)
    {
        if (!IsServer || CurrentState.Value != GameState.Playing)
        {
            return;
        }

        if (extractedPlayers.Contains(requestingClientId))
        {
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requestingClientId, out NetworkClient client))
        {
            return;
        }

        if (client.PlayerObject == null)
        {
            return;
        }

        PlayerHealth ph = client.PlayerObject.GetComponent<PlayerHealth>();
        if (ph != null && ph.IsDead.Value)
        {
            return;
        }

        RagdollController ragdoll = client.PlayerObject.GetComponent<RagdollController>();
        if (ragdoll != null && ragdoll.IsRagdolled.Value)
        {
            return;
        }

        ExtractionPoint ep = FindAnyObjectByType<ExtractionPoint>();
        if (ep != null)
        {
            if (!ep.IsPlayerOnPad(client.PlayerObject.gameObject))
            {
                return;
            }
        }

        ExtractPlayer(requestingClientId);
    }

    /// <summary>
    /// Server: Return all players to the staging lobby.
    /// Resets timer, clears extracted status, resets ready states, teleports all players back to the LobbyArea.
    /// Resets run money to $0 while keeping all banked savings intact!
    /// </summary>
    public void ReturnToLobby()
    {
        if (!IsServer)
        {
            return;
        }

        RoundTimer.Value = 0f;
        AutoReturnTimer.Value = 0f;

        DungeonBuilder dungeonBuilder = FindAnyObjectByType<DungeonBuilder>();
        if (dungeonBuilder != null)
        {
            dungeonBuilder.ResetDungeonEntities();
        }

        if (NetworkManager.Singleton != null)
        {
            int clientIndex = 0;
            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                {
                    continue;
                }

                // If contractor died or failed to extract in time, their items disappear!
                // If they extracted in time, their items stay with them!
                if (!extractedPlayers.Contains(client.ClientId))
                {
                    PlayerInventory inv = client.PlayerObject.GetComponent<PlayerInventory>();
                    if (inv != null)
                    {
                        inv.ClearInventory();
                    }
                }

                Vector3 lobbySpawn = LobbyArea.Instance != null
                    ? LobbyArea.Instance.GetLobbySpawnPoint(clientIndex)
                    : new Vector3(0f, 1f, -80f);

                PlayerHealth ph = client.PlayerObject.GetComponent<PlayerHealth>();
                if (ph != null)
                {
                    ph.ResetPlayer(lobbySpawn);
                }

                PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                if (wallet != null)
                {
                    wallet.StartNewRun(); // Clear run earnings; keep all banked savings!
                }

                PlayerController pc = client.PlayerObject.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.IsReadyToDive.Value = false;
                }

                clientIndex++;
            }
        }

        extractedPlayers.Clear();
        Physics.SyncTransforms();

        CurrentState.Value = GameState.Lobby;

        OnReturnedToLobbyClientRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReturnToLobbyServerRpc()
    {
        ReturnToLobby();
    }

    /// <summary>
    /// Server: Restart dive into the dungeon.
    /// Run earnings start at $0 while all banked savings remain intact.
    /// </summary>
    public void RestartRound()
    {
        if (!IsServer)
        {
            return;
        }

        extractedPlayers.Clear();
        RoundTimer.Value = roundDuration;
        AutoReturnTimer.Value = 0f;

        DungeonBuilder dungeonBuilder = FindAnyObjectByType<DungeonBuilder>();
        if (dungeonBuilder != null)
        {
            dungeonBuilder.ResetDungeonEntities();
        }

        if (NetworkManager.Singleton != null)
        {
            int clientIndex = 0;
            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                {
                    continue;
                }

                Vector3 spawnPoint = dungeonBuilder != null
                    ? dungeonBuilder.GetPlayerSpawnPoint(clientIndex)
                    : new Vector3(0f, 1f, 0f);

                PlayerHealth playerHealth = client.PlayerObject.GetComponent<PlayerHealth>();
                if (playerHealth != null)
                {
                    playerHealth.ResetPlayer(spawnPoint);
                }

                PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                if (wallet != null)
                {
                    wallet.StartNewRun(); // Run starts from $0; bank stays!
                }

                PlayerController pc = client.PlayerObject.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.IsReadyToDive.Value = false;
                }

                clientIndex++;
            }
        }

        extractedPlayers.Clear();
        Physics.SyncTransforms();

        CurrentState.Value = GameState.Playing;

        OnRoundRestartedClientRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RestartRoundServerRpc()
    {
        RestartRound();
    }

    public bool IsPlayerExtracted(ulong clientId)
    {
        return extractedPlayers.Contains(clientId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnRoundStartedClientRpc()
    {
        Debug.Log("[GameManager] Expedition Started! Dive into the dungeon!");
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.ClearRoundState();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnReturnedToLobbyClientRpc()
    {
        Debug.Log("[GameManager] Returned to Staging Lobby.");
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.ClearRoundState();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnRoundRestartedClientRpc()
    {
        Debug.Log("[GameManager] Round Restarted!");
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.ClearRoundState();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnRoundOverClientRpc()
    {
        Debug.Log("[GameManager] Round Over - Time's Up!");
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnPlayerExtractedClientRpc(ulong clientId, int bankedAmount, int totalBank)
    {
        Debug.Log($"[GameManager] Player {clientId} ESCAPED to Lobby! Banked +${bankedAmount} (Total Bank: ${totalBank})");
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.RegisterExtraction(clientId, bankedAmount, totalBank);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnAllExtractedClientRpc()
    {
        Debug.Log("[GameManager] ALL PLAYERS ESCAPED! Round Won!");
    }
}
