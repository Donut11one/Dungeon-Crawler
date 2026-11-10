using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All-in-one game HUD using OnGUI (no Canvas setup needed).
/// Displays: timer, health, crosshair, round state, connection info.
/// </summary>
public class GameHUD : MonoBehaviour
{
    private GUIStyle timerStyle;
    private GUIStyle healthStyle;
    private GUIStyle stateStyle;
    private GUIStyle infoStyle;
    private GUIStyle crosshairStyle;
    private bool stylesInitialized;

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

        stateStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };

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

        stylesInitialized = true;
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
        DrawTimer();
        DrawHealth();
        DrawRagdollStatus();
        DrawControlsHint();
        DrawConnectionInfo();
        DrawRoundState();
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
        GUI.Label(rect, "[Left Click / E] Pick Up / Drop   |   [Right Click] Throw Barrel", hintStyle);
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
        // Find local player health
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

        // Quick restart shortcut: press R when round is over
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.RoundOver)
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                GameManager.Instance.RestartRoundServerRpc();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
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

            stateStyle.normal.textColor = Color.red;
            Rect rect = new Rect(0f, Screen.height / 2f - 60f, Screen.width, 80f);
            GUI.Label(rect, "TIME'S UP!", stateStyle);

            if (GUI.Button(new Rect(Screen.width / 2f - 110f, Screen.height / 2f + 30f, 220f, 45f), "RESTART ROUND (R)"))
            {
                GameManager.Instance.RestartRoundServerRpc();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
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
}
