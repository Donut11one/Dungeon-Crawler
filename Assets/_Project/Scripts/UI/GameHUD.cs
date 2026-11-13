using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All-in-one game HUD using OnGUI (no Canvas setup needed).
/// Displays: lobby staging status & mutual ready system, countdown timer, health,
/// run earnings vs persistent bank savings, crosshair, extraction status, and all-player scoreboards.
/// </summary>
public class GameHUD : MonoBehaviour
{
    private struct ExtractedRecord
    {
        public int bankedAmount;
        public int totalBank;
    }

    private GUIStyle timerStyle;
    private GUIStyle healthStyle;
    private GUIStyle moneyStyle;
    private GUIStyle stateStyle;
    private GUIStyle subStateStyle;
    private GUIStyle infoStyle;
    private GUIStyle crosshairStyle;
    private GUIStyle scoreBoardStyle;
    private GUIStyle extractedBannerStyle;
    private bool stylesInitialized;

    // Tracks which players escaped and their banked money (populated via RPC from GameManager)
    private readonly Dictionary<ulong, ExtractedRecord> extractedThisRound = new();

    private void InitStyles()
    {
        if (stylesInitialized)
        {
            return;
        }

        timerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold
        };
        timerStyle.normal.textColor = Color.white;

        healthStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.LowerLeft,
            fontStyle = FontStyle.Bold
        };
        healthStyle.normal.textColor = Color.green;

        moneyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            alignment = TextAnchor.LowerLeft,
            fontStyle = FontStyle.Bold
        };
        moneyStyle.normal.textColor = new Color(1f, 0.85f, 0f); // Gold

        stateStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };

        subStateStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        subStateStyle.normal.textColor = new Color(1f, 0.85f, 0f);

        infoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.LowerRight
        };
        infoStyle.normal.textColor = Color.gray;

        crosshairStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter
        };
        crosshairStyle.normal.textColor = Color.white;

        scoreBoardStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
        scoreBoardStyle.normal.textColor = Color.white;

        extractedBannerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        extractedBannerStyle.normal.textColor = new Color(0.2f, 1f, 0.2f);

        stylesInitialized = true;
    }

    /// <summary>
    /// Called across network via GameManager ClientRpc when round starts, restarts, or returns to lobby.
    /// Wipes all stale round-over states, extraction statuses, and locks cursor for gameplay.
    /// </summary>
    public void ClearRoundState()
    {
        extractedThisRound.Clear();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>
    /// Called by GameManager RPC when a player successfully extracts.
    /// </summary>
    public void RegisterExtraction(ulong clientId, int bankedAmount, int totalBank)
    {
        extractedThisRound[clientId] = new ExtractedRecord
        {
            bankedAmount = bankedAmount,
            totalBank = totalBank
        };
    }

    private void OnGUI()
    {
        InitStyles();

        if (NetworkManager.Singleton == null)
        {
            return;
        }
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        DrawCrosshair();
        DrawLobbyHUD();
        DrawTimer();
        DrawHealth();
        DrawMoney();
        DrawControlsHint();
        DrawConnectionInfo();
        DrawRoundState();
        DrawExtractionBanner();
        DrawScoreboard();
    }

    private void DrawLobbyHUD()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        GUIStyle lobbyTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold
        };
        lobbyTitleStyle.normal.textColor = new Color(0.15f, 0.85f, 1f);

        Rect titleRect = new Rect(0f, 20f, Screen.width, 32f);
        GUI.Label(titleRect, "STAGING BUNKER // CONTRACTOR BASECAMP", lobbyTitleStyle);

        int ready = 0;
        int total = 1;
        float countdown = -1f;

        if (LobbyArea.Instance != null)
        {
            ready = LobbyArea.Instance.ReadyCount.Value;
            total = LobbyArea.Instance.TotalPlayers.Value;
            countdown = LobbyArea.Instance.DiveCountdown.Value;
        }
        else if (NetworkManager.Singleton != null)
        {
            total = NetworkManager.Singleton.ConnectedClientsList.Count;
        }

        PlayerController localPc = FindLocalPlayerController();
        bool isLocalReady = localPc != null && localPc.IsReadyToDive.Value;

        if (countdown > 0f)
        {
            GUIStyle countdownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold
            };
            countdownStyle.normal.textColor = new Color(0.2f, 1f, 0.3f);

            int sec = Mathf.CeilToInt(countdown);
            Rect countRect = new Rect(0f, 55f, Screen.width, 35f);
            GUI.Label(countRect, $"ALL SQUAD MEMBERS READY! DIVING IN {sec}s...", countdownStyle);
        }
        else
        {
            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold
            };
            subStyle.normal.textColor = (ready >= total && total > 0) ? Color.green : new Color(1f, 0.85f, 0.2f);

            Rect statusRect = new Rect(0f, 55f, Screen.width, 28f);
            GUI.Label(statusRect, $"EXPEDITION READY: {ready} / {total} CONTRACTORS AGREED", subStyle);

            GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperCenter
            };
            hintStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);

            Rect hintRect = new Rect(0f, 85f, Screen.width, 22f);
            GUI.Label(hintRect, "Both contractors must step on the ELEVATOR PLATFORM or press [F] to agree", hintStyle);
        }

        // Ready toggle button
        if (localPc != null)
        {
            string btnText = isLocalReady ? "YOU ARE READY ✓  (Press F to Cancel)" : "AGREE TO DIVE  (Press F or Step on Pad)";
            Color originalBg = GUI.backgroundColor;
            GUI.backgroundColor = isLocalReady ? new Color(0.2f, 0.8f, 0.3f) : new Color(0.9f, 0.7f, 0.1f);

            if (GUI.Button(new Rect(Screen.width / 2f - 180f, 115f, 360f, 38f), btnText))
            {
                localPc.SetReadyServerRpc(!isLocalReady);
            }

            GUI.backgroundColor = originalBg;
        }
    }


    private void DrawControlsHint()
    {
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.LowerCenter
        };
        hintStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 0.7f);

        Rect rect = new Rect(0f, Screen.height - 35f, Screen.width, 30f);

        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby)
        {
            GUI.Label(rect, "[WASD] Move   |   [Space] Jump   |   [F] Agree to Dive / Cancel   |   Step on ELEVATOR PLATFORM to Ready Up", hintStyle);
        }
        else
        {
            GUI.Label(rect, "[WASD] Move   |   [Space] Jump   |   [E / Left Click] Pick Up/Drop   |   [Right Click] Throw   |   GREEN ZONE to extract!", hintStyle);
        }
    }

    private void DrawCrosshair()
    {
        Rect rect = new Rect(Screen.width / 2f - 15f, Screen.height / 2f - 15f, 30f, 30f);
        GUI.Label(rect, "+", crosshairStyle);
    }

    private void DrawTimer()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        float timer = GameManager.Instance.RoundTimer.Value;
        GameManager.GameState state = GameManager.Instance.CurrentState.Value;

        if (state != GameManager.GameState.Playing)
        {
            return;
        }

        int minutes = Mathf.FloorToInt(timer / 60f);
        int seconds = Mathf.FloorToInt(timer % 60f);

        timerStyle.normal.textColor = timer <= 30f ? Color.red : Color.white;

        Rect rect = new Rect(Screen.width / 2f - 100f, 15f, 200f, 50f);
        GUI.Label(rect, $"{minutes:00}:{seconds:00}", timerStyle);
    }

    private void DrawHealth()
    {
        PlayerHealth localHealth = FindLocalPlayerHealth();
        if (localHealth == null)
        {
            return;
        }

        int hp = localHealth.CurrentHealth.Value;
        healthStyle.normal.textColor = hp > 50 ? Color.green : (hp > 25 ? Color.yellow : Color.red);

        Rect rect = new Rect(20f, Screen.height - 60f, 300f, 40f);
        GUI.Label(rect, $"HP: {hp}", healthStyle);

        if (localHealth.IsDead.Value)
        {
            healthStyle.normal.textColor = Color.red;
            GUI.Label(new Rect(20f, Screen.height - 90f, 300f, 30f), "DEAD - Respawning...", healthStyle);
        }
    }

    private void DrawMoney()
    {
        PlayerWallet localWallet = FindLocalPlayerWallet();
        if (localWallet == null)
        {
            return;
        }

        bool inLobby = GameManager.Instance != null &&
                       GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby;

        // In lobby: show total accumulated money; in run: show money collected this run (starts at 0)
        int displayAmount = inLobby ? localWallet.BankedMoney.Value : localWallet.RunMoney.Value;

        Rect rect = new Rect(20f, Screen.height - 95f, 300f, 40f);
        GUI.Label(rect, $"MONEY: ${displayAmount}", moneyStyle);
    }

    private void DrawConnectionInfo()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        string role = NetworkManager.Singleton.IsHost ? "Host" : "Client";
        int playerCount = NetworkManager.Singleton.ConnectedClientsList.Count;

        Rect rect = new Rect(Screen.width - 250f, Screen.height - 35f, 240f, 25f);
        GUI.Label(rect, $"{role} | Players: {playerCount}", infoStyle);
    }

    private void Update()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        // In Lobby: Any player can press F to toggle their ready agreement
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby)
        {
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                PlayerController pc = FindLocalPlayerController();
                if (pc != null)
                {
                    pc.SetReadyServerRpc(!pc.IsReadyToDive.Value);
                }
            }
        }

        // When round is over: Press L or Space to return squad to lobby
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.RoundOver)
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.lKey.wasPressedThisFrame ||
                    Keyboard.current.rKey.wasPressedThisFrame ||
                    Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    GameManager.Instance.ReturnToLobbyServerRpc();
                }
            }
        }
    }

    private void DrawRoundState()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.GameState state = GameManager.Instance.CurrentState.Value;

        if (state == GameManager.GameState.RoundOver)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            bool anyoneEscaped = extractedThisRound.Count > 0;

            // Header
            stateStyle.normal.textColor = anyoneEscaped ? new Color(0.2f, 1f, 0.2f) : Color.red;
            Rect headerRect = new Rect(0f, Screen.height / 2f - 115f, Screen.width, 70f);
            GUI.Label(headerRect, anyoneEscaped ? "EXPEDITION COMPLETE!" : "TIME'S UP!", stateStyle);

            // Per-player results
            float startY = Screen.height / 2f - 35f;
            int i = 0;
            if (NetworkManager.Singleton != null)
            {
                foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client.PlayerObject == null)
                    {
                        continue;
                    }

                    ulong cid = client.ClientId;
                    bool escaped = extractedThisRound.ContainsKey(cid);

                    PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                    int bank = wallet != null ? wallet.BankedMoney.Value : 0;

                    string label = cid == NetworkManager.Singleton.LocalClientId ? " (You)" : "";
                    string status = escaped ? "ESCAPED" : "CAPTURED";
                    Color statusColor = escaped ? new Color(0.2f, 1f, 0.2f) : Color.red;

                    scoreBoardStyle.normal.textColor = statusColor;
                    Rect row = new Rect(Screen.width / 2f - 210f, startY + i * 32f, 420f, 28f);

                    if (escaped)
                    {
                        int bankedThisRun = extractedThisRound[cid].bankedAmount;
                        GUI.Label(row, $"Player {cid + 1}{label}: {status} (+${bankedThisRun})  |  Total: ${bank}", scoreBoardStyle);
                    }
                    else
                    {
                        GUI.Label(row, $"Player {cid + 1}{label}: {status} ($0)  |  Total: ${bank}", scoreBoardStyle);
                    }
                    i++;
                }
            }

            // Return to Bunker button (returns all contractors to lobby to ready up together)
            if (GUI.Button(new Rect(Screen.width / 2f - 160f, Screen.height / 2f + 95f, 320f, 45f), "RETURN TO BUNKER (SPACE / L)"))
            {
                GameManager.Instance.ReturnToLobbyServerRpc();
            }
        }
    }

    /// <summary>
    /// Shows a small banner in the center when the local player has extracted.
    /// </summary>
    private void DrawExtractionBanner()
    {
        if (!IsLocalPlayerExtracted())
        {
            return;
        }

        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        int bankedAmount = extractedThisRound.ContainsKey(localId) ? extractedThisRound[localId].bankedAmount : 0;

        PlayerWallet wallet = FindLocalPlayerWallet();
        int totalBank = wallet != null ? wallet.BankedMoney.Value : 0;

        Rect rect = new Rect(0f, Screen.height / 2f - 50f, Screen.width, 60f);
        GUI.Label(rect, $"ESCAPED TO LOBBY!  \ud83d\udcb0  +${bankedAmount}", extractedBannerStyle);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter
        };
        subStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, Screen.height / 2f + 15f, Screen.width, 30f),
            $"Total Money: ${totalBank}  |  Waiting in Bunker for squad...", subStyle);
    }

    /// <summary>
    /// Shows a compact scoreboard in the top-right during a live round.
    /// </summary>
    private void DrawScoreboard()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        float x = Screen.width - 200f;
        float y = 15f;
        int i = 0;

        GUIStyle sbStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.UpperRight,
            fontStyle = FontStyle.Bold
        };

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
            {
                continue;
            }

            PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
            int runMoney = wallet != null ? wallet.RunMoney.Value : 0;

            bool escaped = extractedThisRound.ContainsKey(client.ClientId);
            string escapedTag = escaped ? " ✓" : "";
            bool isLocal = client.ClientId == NetworkManager.Singleton.LocalClientId;

            sbStyle.normal.textColor = escaped
                ? new Color(0.2f, 1f, 0.2f)
                : (isLocal ? new Color(1f, 0.85f, 0f) : Color.white);

            GUI.Label(new Rect(x, y + i * 24f, 180f, 22f),
                $"P{client.ClientId + 1}: ${runMoney}{escapedTag}", sbStyle);
            i++;
        }
    }

    private bool IsLocalPlayerExtracted()
    {
        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        return extractedThisRound.ContainsKey(NetworkManager.Singleton.LocalClientId);
    }

    private PlayerController FindLocalPlayerController()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
        {
            return null;
        }

        NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null)
        {
            return null;
        }

        return playerObj.GetComponent<PlayerController>();
    }

    private PlayerHealth FindLocalPlayerHealth()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
        {
            return null;
        }

        NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null)
        {
            return null;
        }

        return playerObj.GetComponent<PlayerHealth>();
    }

    private PlayerWallet FindLocalPlayerWallet()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
        {
            return null;
        }

        NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null)
        {
            return null;
        }

        return playerObj.GetComponent<PlayerWallet>();
    }
}
