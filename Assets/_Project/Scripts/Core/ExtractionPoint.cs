using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Networked extraction zone.
/// Players stand on the pad and press [E] to initiate extraction.
/// Stepping on the pad alone will NOT automatically extract.
/// </summary>
public class ExtractionPoint : MonoBehaviour
{
    [Header("Visual Animation")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseScale = 0.15f;
    [SerializeField] private float padRadius = 2.4f;

    private Vector3 baseScale;
    private Renderer zoneRenderer;
    private static Texture2D promptTexture;

    private void Awake()
    {
        baseScale = transform.localScale;
        zoneRenderer = GetComponent<Renderer>();
    }

    private void Update()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        // Pulse visual animation
        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseScale;
        transform.localScale = baseScale * pulse;

        // Check if local player is standing on the pad and wants to extract
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            UpdateLocalPlayerInteraction();
        }
    }

    private void UpdateLocalPlayerInteraction()
    {
        GameObject localPlayer = GetLocalPlayer();
        if (localPlayer == null)
        {
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (GameManager.Instance.IsPlayerExtracted(localId))
        {
            return;
        }

        PlayerHealth ph = localPlayer.GetComponent<PlayerHealth>();
        if (ph != null && ph.IsDead.Value)
        {
            return;
        }

        RagdollController ragdoll = localPlayer.GetComponent<RagdollController>();
        if (ragdoll != null && ragdoll.IsRagdolled.Value)
        {
            return;
        }

        if (IsPlayerOnPad(localPlayer))
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                GameManager.Instance.RequestExtractionServerRpc(localId);
            }
        }
    }

    public bool IsPlayerOnPad(GameObject player)
    {
        if (player == null)
        {
            return false;
        }

        Vector3 diff = player.transform.position - transform.position;
        float horizontalDist = new Vector2(diff.x, diff.z).magnitude;
        float verticalDist = Mathf.Abs(diff.y);

        return horizontalDist <= padRadius && verticalDist <= 2.5f;
    }

    private void OnGUI()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (GameManager.Instance.IsPlayerExtracted(localId))
        {
            return;
        }

        GameObject localPlayer = GetLocalPlayer();
        if (localPlayer == null || !IsPlayerOnPad(localPlayer))
        {
            return;
        }

        PlayerHealth ph = localPlayer.GetComponent<PlayerHealth>();
        if (ph != null && ph.IsDead.Value)
        {
            return;
        }

        RagdollController ragdoll = localPlayer.GetComponent<RagdollController>();
        if (ragdoll != null && ragdoll.IsRagdolled.Value)
        {
            return;
        }

        if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
        {
            return;
        }

        // Draw HUD prompt
        string promptText = "[E] INITIATE EXTRACTION (SECURE LOOT)";
        float promptW = Mathf.Max(320f, promptText.Length * 9.5f);
        float promptH = 36f;
        float promptX = (Screen.width - promptW) * 0.5f;
        float promptY = Screen.height * 0.5f + 85f;

        DrawPromptRect(new Rect(promptX, promptY, promptW, promptH), new Color(0.04f, 0.12f, 0.06f, 0.94f));
        DrawPromptRect(new Rect(promptX - 1f, promptY - 1f, promptW + 2f, 2f), new Color(0.2f, 1f, 0.35f));

        GUIStyle promptStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        promptStyle.normal.textColor = new Color(0.4f, 1f, 0.5f);

        GUI.Label(new Rect(promptX, promptY, promptW, promptH), promptText, promptStyle);
    }

    private static void DrawPromptRect(Rect rect, Color color)
    {
        if (promptTexture == null)
        {
            promptTexture = new Texture2D(1, 1);
            promptTexture.SetPixel(0, 0, Color.white);
            promptTexture.Apply();
        }

        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, promptTexture);
        GUI.color = prev;
    }

    private static GameObject GetLocalPlayer()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            NetworkObject netObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (netObj != null)
            {
                return netObj.gameObject;
            }
        }

        PlayerController[] pcs = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < pcs.Length; i++)
        {
            if (pcs[i] != null && pcs[i].IsOwner)
            {
                return pcs[i].gameObject;
            }
        }

        return null;
    }
}
