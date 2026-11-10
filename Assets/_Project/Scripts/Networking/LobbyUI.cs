using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple lobby UI for hosting and joining games.
/// Uses OnGUI for zero-setup display. Shows host/join buttons
/// when disconnected, connection info when connected.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private string joinAddress = "127.0.0.1";

    private void Update()
    {
        // Toggle UI visibility
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (NetworkManager.Singleton != null &&
                (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                // Already connected — unlock/lock cursor
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None
                    : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }
        }
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
        {
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
        float boxWidth = 320f;
        float boxHeight = 220f;
        float x = (Screen.width - boxWidth) / 2f;
        float y = (Screen.height - boxHeight) / 2f;

        GUI.Box(new Rect(x, y, boxWidth, boxHeight), "");

        // Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y + 10f, boxWidth, 35f), "DUNGEON CRAWLER", titleStyle);

        // Host button
        if (GUI.Button(new Rect(x + 60f, y + 55f, 200f, 35f), "HOST GAME"))
        {
            NetworkManager.Singleton.StartHost();

            // Auto-start the round
            if (GameManager.Instance != null)
            {
                // Small delay for network spawn
                Invoke(nameof(DelayedStartRound), 0.5f);
            }
        }

        // Separator
        GUI.Label(new Rect(x + 60f, y + 95f, 200f, 20f), "— or join —");

        // Join section
        GUI.Label(new Rect(x + 60f, y + 120f, 200f, 20f), "Server Address:");
        joinAddress = GUI.TextField(new Rect(x + 60f, y + 140f, 200f, 25f), joinAddress);

        if (GUI.Button(new Rect(x + 60f, y + 172f, 200f, 35f), "JOIN GAME"))
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = joinAddress;
            }
            NetworkManager.Singleton.StartClient();
        }
    }

    private void DelayedStartRound()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartRound();
        }
    }
}
