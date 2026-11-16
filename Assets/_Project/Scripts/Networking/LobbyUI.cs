using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Lobby connection UI.
/// Supports both:
/// 1) Unity Relay (Online over the Internet with 6-letter Join Codes, no port forwarding required).
/// 2) Local LAN / Direct IP (Same Wi-Fi or Radmin VPN).
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private enum ConnectionMode
    {
        RelayOnline = 0,
        LocalLan = 1
    }

    private ConnectionMode currentMode = ConnectionMode.RelayOnline;
    private string relayJoinCode = string.Empty;
    private string lanAddress = "127.0.0.1";
    private Texture2D boxTexture;

    private void Awake()
    {
        // Enforce VSync and frame cap as soon as the lobby UI loads to protect GPU/CPU
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 60;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDestroy()
    {
        if (boxTexture != null)
        {
            Destroy(boxTexture);
        }
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
        {
            GUIStyle waitStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            waitStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(0, Screen.height * 0.45f, Screen.width, 30f), "Initializing Network Session...", waitStyle);
            return;
        }

        bool isConnected = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;
        if (!isConnected)
        {
            DrawLobbyUI();
        }
    }

    private void DrawLobbyUI()
    {
        InitTexture();

        float boxWidth = 440f;
        float boxHeight = 370f;
        float x = (Screen.width - boxWidth) * 0.5f;
        float y = (Screen.height - boxHeight) * 0.5f;

        // Background box
        DrawRect(new Rect(x - 2f, y - 2f, boxWidth + 4f, boxHeight + 4f), new Color(0.15f, 0.75f, 0.95f, 0.9f));
        DrawRect(new Rect(x, y, boxWidth, boxHeight), new Color(0.08f, 0.10f, 0.13f, 0.97f));

        // Header Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = new Color(0.2f, 0.85f, 1f);
        GUI.Label(new Rect(x, y + 10f, boxWidth, 30f), "DUNGEON CRAWLER // LOBBY", titleStyle);

        // Mode Selection Tabs
        float tabW = (boxWidth - 40f) * 0.5f;
        float tabY = y + 46f;

        bool isRelay = currentMode == ConnectionMode.RelayOnline;
        GUI.backgroundColor = isRelay ? new Color(0.15f, 0.75f, 0.95f) : new Color(0.25f, 0.28f, 0.32f);
        if (GUI.Button(new Rect(x + 18f, tabY, tabW, 28f), "ONLINE (UNITY RELAY)"))
        {
            currentMode = ConnectionMode.RelayOnline;
        }

        GUI.backgroundColor = !isRelay ? new Color(0.15f, 0.75f, 0.95f) : new Color(0.25f, 0.28f, 0.32f);
        if (GUI.Button(new Rect(x + 22f + tabW, tabY, tabW, 28f), "LOCAL (LAN / VPN)"))
        {
            currentMode = ConnectionMode.LocalLan;
        }
        GUI.backgroundColor = Color.white;

        // Mode Content
        if (currentMode == ConnectionMode.RelayOnline)
        {
            DrawRelayMode(x, y + 84f, boxWidth);
        }
        else
        {
            DrawLanMode(x, y + 84f, boxWidth);
        }

        // Status Message bar at bottom
        DrawStatusMessage(x, y + boxHeight - 34f, boxWidth);
    }

    private void DrawRelayMode(float x, float y, float width)
    {
        GUIStyle descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter
        };
        descStyle.normal.textColor = new Color(0.75f, 0.85f, 0.9f);
        GUI.Label(new Rect(x + 20f, y, width - 40f, 18f), "Play across different Wi-Fi networks using a 6-letter Join Code", descStyle);

        bool busy = RelayManager.IsConnecting;
        GUI.enabled = !busy;

        // --- HOST SECTION ---
        float btnW = width - 80f;
        if (GUI.Button(new Rect(x + 40f, y + 26f, btnW, 36f), busy ? "CONNECTING..." : "HOST ONLINE GAME (GET JOIN CODE)"))
        {
            _ = RelayManager.StartHostWithRelayAsync(4);
        }

        // --- DIVIDER ---
        DrawRect(new Rect(x + 30f, y + 74f, width - 60f, 1f), new Color(0.25f, 0.35f, 0.45f, 0.6f));

        // --- JOIN SECTION ---
        GUIStyle joinLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        joinLabelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(x + 40f, y + 84f, btnW, 20f), "Enter 6-Letter Join Code:", joinLabelStyle);

        GUIStyle codeFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };

        relayJoinCode = GUI.TextField(new Rect(x + 100f, y + 108f, width - 200f, 32f), relayJoinCode, 8, codeFieldStyle);
        if (!string.IsNullOrEmpty(relayJoinCode))
        {
            relayJoinCode = relayJoinCode.Trim().ToUpperInvariant();
        }

        if (GUI.Button(new Rect(x + 40f, y + 150f, btnW, 34f), busy ? "JOINING..." : "JOIN ONLINE GAME"))
        {
            _ = RelayManager.StartClientWithRelayAsync(relayJoinCode);
        }

        // Encryption protocol toggle
        GUIStyle toggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 10
        };
        toggleStyle.normal.textColor = new Color(0.65f, 0.75f, 0.85f);
        RelayManager.UseDtlsEncryption = GUI.Toggle(
            new Rect(x + 40f, y + 188f, btnW, 18f),
            RelayManager.UseDtlsEncryption,
            " Use DTLS Encryption (keep OFF for max compatibility)",
            toggleStyle
        );

        GUI.enabled = true;
    }

    private void DrawLanMode(float x, float y, float width)
    {
        GUIStyle descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter
        };
        descStyle.normal.textColor = new Color(0.75f, 0.85f, 0.9f);
        GUI.Label(new Rect(x + 20f, y, width - 40f, 18f), "Direct IP connection for Same Wi-Fi LAN or Radmin VPN", descStyle);

        float btnW = width - 80f;

        // --- LOCAL HOST ---
        if (GUI.Button(new Rect(x + 40f, y + 26f, btnW, 36f), "HOST LOCAL GAME"))
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            }

            NetworkManager.Singleton.StartHost();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // --- DIVIDER ---
        DrawRect(new Rect(x + 30f, y + 74f, width - 60f, 1f), new Color(0.25f, 0.35f, 0.45f, 0.6f));

        // --- LOCAL JOIN ---
        GUIStyle joinLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        joinLabelStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(x + 40f, y + 84f, btnW, 20f), "Host IP Address (e.g. 192.168.1.X or Radmin IP):", joinLabelStyle);

        lanAddress = GUI.TextField(new Rect(x + 70f, y + 108f, width - 140f, 26f), lanAddress);

        if (GUI.Button(new Rect(x + 40f, y + 144f, btnW, 34f), "JOIN LOCAL GAME"))
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData(lanAddress.Trim(), 7777);
            }

            NetworkManager.Singleton.StartClient();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void DrawStatusMessage(float x, float y, float width)
    {
        string status = RelayManager.StatusMessage;
        if (string.IsNullOrEmpty(status))
        {
            return;
        }

        GUIStyle statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        statusStyle.normal.textColor = RelayManager.HasError
            ? new Color(1f, 0.35f, 0.35f)
            : new Color(0.35f, 0.95f, 0.45f);

        GUI.Label(new Rect(x + 10f, y, width - 20f, 24f), status, statusStyle);
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
