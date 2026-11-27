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

    [Header("Crew Economy (Lethal Company Shared Funds & Quota)")]
    [SerializeField] private int startingCrewCredits = 150;
    [SerializeField] private int baseQuotaTarget = 250;
    [SerializeField] private int quotaIncrement = 125;
    [SerializeField] private int totalDaysPerCycle = 3;

    public NetworkVariable<int> GroupCredits = new NetworkVariable<int>(
        150,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> QuotaProgress = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> QuotaTarget = new NetworkVariable<int>(
        250,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> QuotaLevel = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> QuotaPoints = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> DaysRemaining = new NetworkVariable<int>(
        3,
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

        if (GetComponent<DungeonLightingManager>() == null && FindAnyObjectByType<DungeonLightingManager>() == null)
        {
            gameObject.AddComponent<DungeonLightingManager>();
        }

        if (GetComponent<LobbyArea>() == null && FindAnyObjectByType<LobbyArea>() == null)
        {
            gameObject.AddComponent<LobbyArea>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CurrentState.OnValueChanged += OnGameStateNetworkChanged;

        if (IsServer)
        {
            if (GroupCredits.Value <= 0)
            {
                GroupCredits.Value = startingCrewCredits;
            }
            if (QuotaTarget.Value <= 0)
            {
                QuotaTarget.Value = baseQuotaTarget;
            }
            if (DaysRemaining.Value <= 0)
            {
                DaysRemaining.Value = totalDaysPerCycle;
            }
            SyncPlayerWallets();
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentState.OnValueChanged -= OnGameStateNetworkChanged;
        base.OnNetworkDespawn();
    }

    private void OnGameStateNetworkChanged(GameState previous, GameState current)
    {
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (current == GameState.Lobby || current == GameState.Playing)
        {
            if (hud != null)
            {
                hud.ClearRoundState();
            }
            if (InGameMenuUI.Instance == null || !InGameMenuUI.Instance.IsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                PlayerController pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.OnGameStateChanged(current);
                }
            }
        }
        else if (current == GameState.RoundOver)
        {
            if (hud != null)
            {
                hud.ShowRoundOver();
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
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

        // Lethal Company deadline countdown: each dive advances the contract day
        if (DaysRemaining.Value <= 0)
        {
            HandleDeadlineFailure();
        }
        else
        {
            DaysRemaining.Value--;
        }
        SyncPlayerWallets();

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
                    totalBank = GroupCredits.Value;
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

        SyncPlayerWallets();

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
        extractedPlayers.Clear();
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.ClearRoundState();
        }
        if (InGameMenuUI.Instance == null || !InGameMenuUI.Instance.IsOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            PlayerController pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.OnReturnedToLobby();
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnReturnedToLobbyClientRpc()
    {
        Debug.Log("[GameManager] Returned to Staging Lobby.");
        extractedPlayers.Clear();
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.ClearRoundState();
        }
        if (InGameMenuUI.Instance == null || !InGameMenuUI.Instance.IsOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            PlayerController pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.OnReturnedToLobby();
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnRoundRestartedClientRpc()
    {
        Debug.Log("[GameManager] Round Restarted!");
        extractedPlayers.Clear();
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.ClearRoundState();
        }
        if (InGameMenuUI.Instance == null || !InGameMenuUI.Instance.IsOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            PlayerController pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.OnReturnedToLobby();
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnRoundOverClientRpc()
    {
        Debug.Log("[GameManager] Round Over - Time's Up!");
        GameHUD hud = FindAnyObjectByType<GameHUD>();
        if (hud != null)
        {
            hud.ShowRoundOver();
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OnPlayerExtractedClientRpc(ulong clientId, int bankedAmount, int totalBank)
    {
        Debug.Log($"[GameManager] Player {clientId} ESCAPED to Lobby! Banked +${bankedAmount} (Total Bank: ${totalBank})");
        extractedPlayers.Add(clientId);
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

    /// <summary>
    /// Server: Adds money to the shared crew bank.
    /// If countsTowardsQuota is true, also contributes towards the combined profit quota.
    /// </summary>
    public void AddCrewCredits(int amount, bool countsTowardsQuota = true)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        GroupCredits.Value += amount;
        Debug.Log($"[CrewEconomy] +${amount} added to Company Funds. New Total: ${GroupCredits.Value}");

        if (countsTowardsQuota)
        {
            AddQuotaContribution(amount);
        }

        SyncPlayerWallets();
    }

    /// <summary>
    /// Server: Attempts to spend from the shared crew bank.
    /// Deducts from the shared pool so every player's balance decreases.
    /// </summary>
    public bool TrySpendCrewCredits(int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return false;
        }

        if (GroupCredits.Value >= amount)
        {
            GroupCredits.Value -= amount;
            Debug.Log($"[CrewEconomy] -${amount} spent from Company Funds. Remaining: ${GroupCredits.Value}");
            SyncPlayerWallets();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Server: Contributes towards the crew's combined profit quota.
    /// If target met or exceeded: advances tier, resets deadline, carries over surplus, and triggers celebration.
    /// </summary>
    public void AddQuotaContribution(int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        QuotaProgress.Value += amount;
        int pointsEarned = 0;

        while (QuotaTarget.Value > 0 && QuotaProgress.Value >= QuotaTarget.Value)
        {
            QuotaProgress.Value -= QuotaTarget.Value;
            QuotaPoints.Value++;
            QuotaLevel.Value++;
            pointsEarned++;

            QuotaTarget.Value = CalculateQuotaTarget(QuotaLevel.Value);
            DaysRemaining.Value = totalDaysPerCycle;

            Debug.Log($"[CrewEconomy] CREW MET QUOTA! Earned +1 Point (Total: {QuotaPoints.Value}). Next Quota Target: ${QuotaTarget.Value} (Tier {QuotaLevel.Value}, {DaysRemaining.Value} Days)");
        }

        if (pointsEarned > 0)
        {
            QuotaFulfilledClientRpc(pointsEarned, QuotaPoints.Value, QuotaTarget.Value, DaysRemaining.Value);
        }

        SyncPlayerWallets();
    }

    public int CalculateQuotaTarget(int level)
    {
        int target = baseQuotaTarget + Mathf.Max(0, level - 1) * quotaIncrement;
        return Mathf.Max(50, target);
    }

    /// <summary>
    /// Server: Disciplinary penalty when the deadline expires without meeting the profit quota.
    /// </summary>
    private void HandleDeadlineFailure()
    {
        int penalty = Mathf.RoundToInt(GroupCredits.Value * 0.25f);
        GroupCredits.Value = Mathf.Max(50, GroupCredits.Value - penalty);
        QuotaProgress.Value = 0;
        QuotaLevel.Value = 1;
        QuotaTarget.Value = baseQuotaTarget;
        DaysRemaining.Value = totalDaysPerCycle;

        Debug.Log($"[CrewEconomy] QUOTA DEADLINE EXPIRED! Docked ${penalty}. Reset to Tier 1 Quota (${baseQuotaTarget}), {DaysRemaining.Value} Days.");
        QuotaDeadlineFailedClientRpc(penalty, GroupCredits.Value, QuotaTarget.Value);
        SyncPlayerWallets();
    }

    /// <summary>
    /// Server: Resets crew economy to initial state if needed.
    /// </summary>
    public void ResetCrewEconomy()
    {
        if (!IsServer)
        {
            return;
        }

        GroupCredits.Value = startingCrewCredits;
        QuotaProgress.Value = 0;
        QuotaPoints.Value = 0;
        QuotaLevel.Value = 1;
        QuotaTarget.Value = baseQuotaTarget;
        DaysRemaining.Value = totalDaysPerCycle;
        SyncPlayerWallets();
    }

    /// <summary>
    /// Server: Synchronizes all connected player wallets with the shared crew economy.
    /// </summary>
    public void SyncPlayerWallets()
    {
        if (!IsServer || NetworkManager.Singleton == null)
        {
            return;
        }

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
            {
                continue;
            }

            PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
            if (wallet != null)
            {
                wallet.BankedMoney.Value = GroupCredits.Value;
                wallet.QuotaProgress.Value = QuotaProgress.Value;
                wallet.QuotaTarget.Value = QuotaTarget.Value;
                wallet.QuotaLevel.Value = QuotaLevel.Value;
                wallet.QuotaPoints.Value = QuotaPoints.Value;
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void QuotaFulfilledClientRpc(int pointsAdded, int totalPoints, int nextTarget, int days)
    {
        GameHUD.TriggerCrewQuotaCelebration(pointsAdded, totalPoints, nextTarget, days);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void QuotaDeadlineFailedClientRpc(int penaltyDeducted, int remainingCredits, int nextTarget)
    {
        GameHUD.TriggerQuotaDeadlineFailed(penaltyDeducted, remainingCredits, nextTarget);
    }

    [Rpc(SendTo.NotOwner)]
    public void NotifyHostShutdownClientRpc(string reason)
    {
        Debug.Log($"[GameManager] Host is ending the session: {reason}");
        InGameMenuUI.BootBackToMenu(reason);
    }
}
