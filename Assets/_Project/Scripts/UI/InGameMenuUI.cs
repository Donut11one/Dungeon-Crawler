using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// In-game Escape/Pause menu.
/// Allows players to leave the lobby, copy the Relay join code, view crew stats, or quit.
/// Manages graceful disconnects: when the Host ends the session or disconnects,
/// connected clients are cleanly shut down and booted back to the main menu without crashing.
/// </summary>
public class InGameMenuUI : MonoBehaviour
{
    private static InGameMenuUI instance;
    public static InGameMenuUI Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<InGameMenuUI>();
                if (instance == null)
                {
                    GameObject go = new GameObject("InGameMenuUI");
                    instance = go.AddComponent<InGameMenuUI>();
                }
            }
            return instance;
        }
    }

    public bool IsOpen { get; private set; } = false;

    private static bool isBootingBack = false;
    private float copyFeedbackTimer = 0f;
    private Texture2D boxTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        isBootingBack = false;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        isBootingBack = false;
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    /// <summary>
    /// Catches unexpected disconnects (host shutdown, timeout, lost connection).
    /// Prevents client game crash and cleanly boots client back to main menu.
    /// </summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // On clients: if our client or the host disconnected, return to menu
        if (clientId == NetworkManager.Singleton.LocalClientId || clientId == NetworkManager.ServerClientId)
        {
            Debug.LogWarning($"[InGameMenuUI] Disconnected from server (clientId: {clientId}). Booting back to menu...");
            BootBackToMenu("Lost connection to host. The host may have quit or disconnected.");
        }
    }

    private void Update()
    {
        if (isBootingBack)
        {
            return;
        }

        bool isConnected = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);
        if (!isConnected)
        {
            if (IsOpen)
            {
                IsOpen = false;
            }
            return;
        }

        // Handle copy button feedback timer
        if (copyFeedbackTimer > 0f)
        {
            copyFeedbackTimer -= Time.deltaTime;
        }

        // Toggle pause menu with Escape key
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // If the armory shop is open, let the shop handle closing itself first
            if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
            {
                return;
            }

            ToggleMenu();
        }
    }

    public void ToggleMenu()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void CloseMenu()
    {
        if (IsOpen)
        {
            IsOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnGUI()
    {
        if (!IsOpen || isBootingBack)
        {
            return;
        }

        bool isConnected = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);
        if (!isConnected)
        {
            return;
        }

        InitTexture();

        // 1. Dim background
        DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.72f));

        // 2. Menu Card
        float cardW = 460f;
        float cardH = 400f;
        float x = (Screen.width - cardW) * 0.5f;
        float y = (Screen.height - cardH) * 0.5f;

        // Outer border & card background
        DrawRect(new Rect(x - 2f, y - 2f, cardW + 4f, cardH + 4f), new Color(0.2f, 0.75f, 0.95f, 0.9f));
        DrawRect(new Rect(x, y, cardW, cardH), new Color(0.08f, 0.10f, 0.14f, 0.98f));

        // Top Accent stripe
        DrawRect(new Rect(x, y, cardW, 4f), new Color(0.2f, 0.85f, 1f));

        // Title Header
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = new Color(0.25f, 0.88f, 1f);
        GUI.Label(new Rect(x, y + 12f, cardW, 26f), "EXPEDITION PAUSED", titleStyle);

        // Role Subtitle
        bool isHost = NetworkManager.Singleton.IsHost;
        string roleText = isHost ? "[HOST] EXPEDITION COMMANDER" : "[CLIENT] SQUAD CONTRACTOR";
        Color roleColor = isHost ? new Color(1f, 0.82f, 0.2f) : new Color(0.4f, 0.85f, 1f);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        subStyle.normal.textColor = roleColor;
        GUI.Label(new Rect(x, y + 38f, cardW, 18f), roleText, subStyle);

        // Divider
        DrawRect(new Rect(x + 20f, y + 60f, cardW - 40f, 1.5f), new Color(0.25f, 0.35f, 0.45f, 0.8f));

        // --- SESSION STATS CARD ---
        float statsY = y + 68f;
        float statsH = 100f;
        DrawRect(new Rect(x + 20f, statsY, cardW - 40f, statsH), new Color(0.12f, 0.15f, 0.20f, 0.75f));

        // Online Relay Join Code section
        if (RelayManager.IsRelayActive && !string.IsNullOrEmpty(RelayManager.CurrentJoinCode))
        {
            GUIStyle codeLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
            codeLabel.normal.textColor = Color.white;
            GUI.Label(new Rect(x + 32f, statsY + 8f, 170f, 24f), "RELAY JOIN CODE:", codeLabel);

            GUIStyle codeVal = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            codeVal.normal.textColor = new Color(0.3f, 1f, 0.6f);
            DrawRect(new Rect(x + 195f, statsY + 7f, 110f, 26f), new Color(0.06f, 0.08f, 0.10f, 0.9f));
            GUI.Label(new Rect(x + 195f, statsY + 7f, 110f, 26f), RelayManager.CurrentJoinCode, codeVal);

            string copyLabel = copyFeedbackTimer > 0f ? "COPIED!" : "COPY";
            if (GUI.Button(new Rect(x + 315f, statsY + 7f, 75f, 26f), copyLabel))
            {
                GUIUtility.systemCopyBuffer = RelayManager.CurrentJoinCode;
                copyFeedbackTimer = 1.5f;
            }
        }
        else
        {
            GUIStyle lanLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            lanLabel.normal.textColor = new Color(0.7f, 0.85f, 0.95f);
            GUI.Label(new Rect(x + 30f, statsY + 8f, cardW - 60f, 24f), "SESSION MODE: DIRECT LAN / LOCAL", lanLabel);
        }

        // Crew stats
        int playerCount = (NetworkManager.Singleton.ConnectedClientsList != null) ? NetworkManager.Singleton.ConnectedClientsList.Count : 1;
        int credits = GameManager.Instance != null ? GameManager.Instance.GroupCredits.Value : 150;
        int qProg = GameManager.Instance != null ? GameManager.Instance.QuotaProgress.Value : 0;
        int qTarg = (GameManager.Instance != null && GameManager.Instance.QuotaTarget.Value > 0) ? GameManager.Instance.QuotaTarget.Value : 250;
        int days = GameManager.Instance != null ? GameManager.Instance.DaysRemaining.Value : 3;

        GUIStyle statStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };
        statStyle.normal.textColor = new Color(0.85f, 0.90f, 0.95f);
        GUI.Label(new Rect(x + 25f, statsY + 40f, cardW - 50f, 22f), $"SQUAD SIZE: {playerCount}/4 CONTRACTORS", statStyle);

        GUIStyle econStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        econStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
        GUI.Label(new Rect(x + 25f, statsY + 66f, cardW - 50f, 22f), $"FUNDS: ${credits}  |  QUOTA: ${qProg}/${qTarg} ({days}D)", econStyle);

        // Divider
        DrawRect(new Rect(x + 20f, y + 180f, cardW - 40f, 1.5f), new Color(0.25f, 0.35f, 0.45f, 0.8f));

        // --- BUTTONS ---
        float btnW = cardW - 60f;
        float btnX = x + 30f;
        float btnY = y + 196f;

        // Button 1: Resume
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.9f);
        if (GUI.Button(new Rect(btnX, btnY, btnW, 36f), "RESUME EXPEDITION [ESC]"))
        {
            ToggleMenu();
        }

        // Button 2: Leave Lobby / Disconnect
        btnY += 46f;
        if (isHost)
        {
            GUI.backgroundColor = new Color(0.95f, 0.55f, 0.15f);
            if (GUI.Button(new Rect(btnX, btnY, btnW, 36f), "END SESSION & RETURN TO MENU"))
            {
                LeaveOrEndSession();
            }
        }
        else
        {
            GUI.backgroundColor = new Color(0.9f, 0.45f, 0.15f);
            if (GUI.Button(new Rect(btnX, btnY, btnW, 36f), "LEAVE LOBBY & RETURN TO MENU"))
            {
                LeaveOrEndSession();
            }
        }

        // Button 3: Quit to Desktop
        btnY += 46f;
        GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
        if (GUI.Button(new Rect(btnX, btnY, btnW, 36f), "QUIT TO DESKTOP"))
        {
            Application.Quit();
        }

        GUI.backgroundColor = Color.white;

        // Hint at bottom
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter
        };
        hintStyle.normal.textColor = new Color(0.55f, 0.65f, 0.75f);
        string hintText = isHost
            ? "Ending session as Host will safely boot all contractors back to the main menu."
            : "Leaving will disconnect you from the current squad and return you to the main menu.";
        GUI.Label(new Rect(x + 20f, y + cardH - 32f, cardW - 40f, 24f), hintText, hintStyle);
    }

    private void LeaveOrEndSession()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        if (isHost)
        {
            StartCoroutine(HostEndSessionRoutine());
        }
        else
        {
            BootBackToMenu("You left the lobby.");
        }
    }

    private IEnumerator HostEndSessionRoutine()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsServer)
        {
            GameManager.Instance.NotifyHostShutdownClientRpc("The Host ended the lobby session.");
        }

        // Wait a brief moment to allow the RPC to reach connected clients before closing the socket
        yield return new WaitForSecondsRealtime(0.15f);

        BootBackToMenu("Session ended by Host.");
    }

    public static void BootBackToMenu(string reason)
    {
        if (isBootingBack)
        {
            return;
        }
        isBootingBack = true;

        Debug.Log($"[InGameMenuUI] Booting back to main menu. Reason: {reason}");
        LobbyUI.DisconnectNotice = reason;

        // Unlock and restore cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Reset Relay state
        RelayManager.ResetStaticState();

        // Shut down NGO cleanly if still active
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.Shutdown();
            }
            Destroy(NetworkManager.Singleton.gameObject);
        }

        // Reload DemoScene to restore pristine state
        Scene active = SceneManager.GetActiveScene();
        string sceneName = !string.IsNullOrEmpty(active.name) ? active.name : "DemoScene";
        SceneManager.LoadScene(sceneName);
    }

    private void InitTexture()
    {
        if (boxTexture == null)
        {
            boxTexture = new Texture2D(1, 1);
            boxTexture.SetPixel(0, 0, Color.white);
            boxTexture.Apply();
        }
    }

    private void DrawRect(Rect rect, Color color)
    {
        InitTexture();
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, boxTexture);
        GUI.color = prev;
    }
}
