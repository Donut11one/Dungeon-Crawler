using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All-in-one game HUD using OnGUI (no Canvas setup needed).
/// Displays: lobby status, timer, health, run earnings vs persistent bank savings,
/// crosshair, extraction status, and all-player scoreboards.
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
        DrawRagdollStatus();
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
            fontSize = 28,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold
        };
        lobbyTitleStyle.normal.textColor = new Color(0.15f, 0.85f, 1f);

        Rect titleRect = new Rect(0f, 25f, Screen.width, 35f);
        GUI.Label(titleRect, "STAGING LOBBY // CONTRACTOR BASECAMP", lobbyTitleStyle);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.UpperCenter
        };
        subStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 0.85f);

        bool isHost = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer);

        string hint = isHost
            ? "Step into the ELEVATOR PLATFORM or press [F] to dive into the dungeon"
            : "Waiting for expedition launch. Step onto elevator or wait for host";

        Rect hintRect = new Rect(0f, 60f, Screen.width, 25f);
        GUI.Label(hintRect, hint, subStyle);

        if (isHost)
        {
            if (GUI.Button(new Rect(Screen.width / 2f - 110f, 90f, 220f, 35f), "ENTER DUNGEON (F)"))
            {
                extractedThisRound.Clear();
                GameManager.Instance.StartRoundServerRpc();
            }
        }
    }

    private void DrawRagdollStatus()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
        {
            return;
        }

        NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null)
        {
            return;
        }

        RagdollController ragdoll = playerObj.GetComponent<RagdollController>();
        if (ragdoll != null && ragdoll.IsRagdolled.Value)
        {
            PlayerHealth health = playerObj.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead.Value)
            {
                return;
            }

            float timeLeft = ragdoll.RemainingRecoveryTime.Value;
            GUIStyle ragdollStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            ragdollStyle.normal.textColor = Color.yellow;

            Rect rect = new Rect(0f, Screen.height / 2f + 40f, Screen.width, 35f);
            GUI.Label(rect, $"KNOCKED DOWN! Standing up in {timeLeft:0.0}s...", ragdollStyle);
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
            GUI.Label(rect, "[WASD] Move   |   Step into ELEVATOR pad or press [F] to enter dungeon", hintStyle);
        }
        else
        {
            GUI.Label(rect, "[E / Left Click] Pick Up/Drop   |   [Right Click] Throw   |   Reach GREEN ZONE to extract!", hintStyle);
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

        // In Lobby: Host can press F to start the dive
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby)
        {
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer))
                {
                    extractedThisRound.Clear();
                    GameManager.Instance.StartRoundServerRpc();
                }
            }
        }

        // Quick restart shortcut: press R when round is over, or L to return to lobby
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.RoundOver)
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    extractedThisRound.Clear();
                    GameManager.Instance.RestartRoundServerRpc();
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                else if (Keyboard.current.lKey.wasPressedThisFrame)
                {
                    extractedThisRound.Clear();
                    GameManager.Instance.ReturnToLobbyServerRpc();
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
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

            // Buttons
            if (GUI.Button(new Rect(Screen.width / 2f - 225f, Screen.height / 2f + 95f, 210f, 45f), "RETURN TO LOBBY (L)"))
            {
                extractedThisRound.Clear();
                GameManager.Instance.ReturnToLobbyServerRpc();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (GUI.Button(new Rect(Screen.width / 2f + 15f, Screen.height / 2f + 95f, 210f, 45f), "PLAY AGAIN (R)"))
            {
                extractedThisRound.Clear();
                GameManager.Instance.RestartRoundServerRpc();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
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
