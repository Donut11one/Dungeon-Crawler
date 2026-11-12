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

    public enum GameState
    {
        Lobby,
        Playing,
        RoundOver
    }

    // Tracks which clientIds have successfully extracted this round (server-side only)
    private readonly HashSet<ulong> extractedPlayers = new();

    private void Awake()
    {
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

    private void Update()
    {
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
                CurrentState.Value = GameState.RoundOver;
                OnRoundOverClientRpc();
            }
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
    /// Banks their run earnings into their persistent savings (no free bonus),
    /// and teleports them back safely to the Lobby Area!
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
        NetworkClient client;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out client))
        {
            if (client.PlayerObject != null)
            {
                PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                if (wallet != null)
                {
                    bankedAmount = wallet.BankRunMoney(bonusAmount);
                    totalBank = wallet.BankedMoney.Value;
                }

                // Teleport the extracted player back to the Lobby Area!
                Vector3 lobbySpawn = LobbyArea.Instance != null
                    ? LobbyArea.Instance.GetLobbySpawnPoint((int)clientId)
                    : new Vector3(0f, 1f, -80f);

                PlayerHealth ph = client.PlayerObject.GetComponent<PlayerHealth>();
                if (ph != null)
                {
                    ph.ResetPlayer(lobbySpawn);
                }

                PlayerController pc = client.PlayerObject.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.IsReadyToDive.Value = false;
                }
            }
        }

        Physics.SyncTransforms();

        // Notify all clients that this player escaped
        OnPlayerExtractedClientRpc(clientId, bankedAmount, totalBank);

        // Check if all connected alive players have extracted
        bool allExtracted = true;
        int validPlayers = 0;
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
            if (!ph.IsDead.Value && !extractedPlayers.Contains(nc.ClientId))
            {
                allExtracted = false;
                break;
            }
        }

        if (validPlayers > 0 && allExtracted)
        {
            RoundTimer.Value = 0f;
            CurrentState.Value = GameState.RoundOver;
            OnAllExtractedClientRpc();
        }
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

        extractedPlayers.Clear();
        RoundTimer.Value = 0f;

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
