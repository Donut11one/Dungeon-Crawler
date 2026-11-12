using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Simple lobby UI for hosting and joining games.
/// Uses OnGUI for zero-setup display. Shows host/join buttons
/// when disconnected, connection info when connected.
/// Players connect directly into the 3D Staging Lobby area.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private string joinAddress = "127.0.0.1";

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
        float boxWidth  = 320f;
        float boxHeight = 220f;
        float x = (Screen.width  - boxWidth)  / 2f;
        float y = (Screen.height - boxHeight) / 2f;

        GUI.Box(new Rect(x, y, boxWidth, boxHeight), "");

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize   = 22,
            alignment  = TextAnchor.MiddleCenter,
            fontStyle  = FontStyle.Bold
        };
        titleStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y + 10f, boxWidth, 35f), "DUNGEON CRAWLER", titleStyle);

        // HOST
        if (GUI.Button(new Rect(x + 60f, y + 55f, 200f, 35f), "HOST GAME"))
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            }

            NetworkManager.Singleton.StartHost();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        GUI.Label(new Rect(x + 60f, y + 95f,  200f, 20f), "— or join —");
        GUI.Label(new Rect(x + 60f, y + 120f, 200f, 20f), "Server Address:");
        joinAddress = GUI.TextField(new Rect(x + 60f, y + 140f, 200f, 25f), joinAddress);

        // JOIN
        if (GUI.Button(new Rect(x + 60f, y + 172f, 200f, 35f), "JOIN GAME"))
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData(joinAddress.Trim(), 7777);
            }

            NetworkManager.Singleton.StartClient();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }
}
