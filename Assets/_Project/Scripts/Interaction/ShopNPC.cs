using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// NPC vendor standing at the Facility Armory shop counter in the staging lobby.
/// Provides interaction prompt [E] to browse weapons and equipment,
/// and draws overhead nameplate for the Quartermaster.
/// </summary>
public class ShopNPC : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionDistance = 5.2f;
    [SerializeField] private float autoCloseDistance = 7.0f;
    [SerializeField] private string npcName = "Quartermaster Knox";
    [SerializeField] private string npcTitle = "< Weapons & Munitions >";

    private Texture2D promptTexture;

    private void OnDestroy()
    {
        if (promptTexture != null)
        {
            Destroy(promptTexture);
        }
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        Transform localPlayer = GetLocalPlayerTransform();
        if (localPlayer == null)
        {
            return;
        }

        // Distance from player to the shop counter front
        Vector3 counterFront = transform.position + Vector3.forward * 0.75f;
        float dist = Vector3.Distance(counterFront, localPlayer.position);

        // Auto-close if player walked away while shop was open
        if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
        {
            if (dist > autoCloseDistance)
            {
                ArmoryShopUI.Instance.CloseShop();
            }
            return;
        }

        // Open shop on [E] when within interaction range
        if (dist <= interactionDistance)
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                ArmoryShopUI.Instance.OpenShop();
            }
        }
    }

    private void OnGUI()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        // If the Armory Shop is open, don't draw prompt or overhead tags
        if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
        {
            return;
        }

        Camera cam = GetActiveCamera();
        if (cam == null)
        {
            return;
        }

        Transform localPlayer = GetLocalPlayerTransform();
        Vector3 counterFront = transform.position + Vector3.forward * 0.75f;
        float playerDist = localPlayer != null ? Vector3.Distance(counterFront, localPlayer.position) : 999f;

        // 1. Draw overhead world nameplate when within 14 units
        Vector3 headPos = transform.position + Vector3.up * 2.2f;
        Vector3 camPos = cam.transform.position;
        Vector3 toNpc = headPos - camPos;
        float camDist = toNpc.magnitude;

        if (camDist < 14f && camDist > 0.5f)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(headPos);
            if (screenPos.z > 0.2f)
            {
                float guiX = screenPos.x;
                float guiY = Screen.height - screenPos.y;

                float alpha = Mathf.Clamp01((14f - camDist) / 4f);

                GUIStyle nameStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                nameStyle.normal.textColor = new Color(1f, 0.85f, 0.2f, alpha);

                GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter
                };
                titleStyle.normal.textColor = new Color(0.2f, 0.85f, 1f, alpha);

                GUI.Label(new Rect(guiX - 120f, guiY - 18f, 240f, 18f), npcName, nameStyle);
                GUI.Label(new Rect(guiX - 120f, guiY - 2f, 240f, 16f), npcTitle, titleStyle);
            }
        }

        // 2. Interaction prompt positioned below crosshair (never overlaps hotbar!)
        if (playerDist <= interactionDistance)
        {
            float promptW = 380f;
            float promptH = 36f;
            float promptX = (Screen.width - promptW) * 0.5f;
            float promptY = Screen.height * 0.5f + 45f;

            DrawPromptRect(new Rect(promptX, promptY, promptW, promptH), new Color(0.08f, 0.10f, 0.14f, 0.92f));
            DrawPromptRect(new Rect(promptX - 1f, promptY - 1f, promptW + 2f, 1.5f), new Color(0.15f, 0.85f, 1f, 0.9f));

            GUIStyle promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            promptStyle.normal.textColor = new Color(1f, 0.92f, 0.4f);

            // Clickable button or keyboard [E]
            if (GUI.Button(new Rect(promptX, promptY, promptW, promptH), "[E] TALK TO QUARTERMASTER (ARMORY SHOP)", promptStyle))
            {
                ArmoryShopUI.Instance.OpenShop();
            }
        }
    }

    private void DrawPromptRect(Rect rect, Color color)
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

    private Camera GetActiveCamera()
    {
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc != null && pc.IsOwner)
        {
            Camera playerCam = pc.GetComponentInChildren<Camera>();
            if (playerCam != null && playerCam.isActiveAndEnabled)
            {
                return playerCam;
            }
        }

        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
        {
            return main;
        }

        return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
    }

    private Transform GetLocalPlayerTransform()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObj != null)
            {
                return playerObj.transform;
            }
        }

        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc != null && pc.IsOwner)
        {
            return pc.transform;
        }

        return null;
    }
}