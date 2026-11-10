using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central game state manager. Singleton, server-authoritative.
/// Manages game states (Lobby → Playing → RoundOver) and the countdown timer.
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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
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
    /// Server: Start the round with the configured duration.
    /// </summary>
    public void StartRound()
    {
        if (!IsServer)
        {
            return;
        }

        RoundTimer.Value = roundDuration;
        CurrentState.Value = GameState.Playing;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartRoundServerRpc()
    {
        StartRound();
    }

    /// <summary>
    /// Server: Reset everything for a fresh round:
    /// - Timer and game state
    /// - All barrels and enemies re-spawned at original positions
    /// - All players healed, uprighted, dropped items, and teleported to spawn
    /// </summary>
    public void RestartRound()
    {
        if (!IsServer)
        {
            return;
        }

        RoundTimer.Value = roundDuration;
        CurrentState.Value = GameState.Playing;

        // 1. Reset dungeon entities (barrels, enemies)
        DungeonBuilder dungeonBuilder = GetComponent<DungeonBuilder>();
        if (dungeonBuilder != null)
        {
            dungeonBuilder.ResetDungeonEntities();
        }

        // 2. Reset all connected players
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

                clientIndex++;
            }
        }

        OnRoundRestartedClientRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RestartRoundServerRpc()
    {
        RestartRound();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnRoundRestartedClientRpc()
    {
        Debug.Log("[GameManager] Round Restarted - Everything Reset!");
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnRoundOverClientRpc()
    {
        Debug.Log("[GameManager] Round Over!");
    }
}
