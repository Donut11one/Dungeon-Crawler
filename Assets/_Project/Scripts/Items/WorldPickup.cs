using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract base class for all modular in-world pickups (Coins, Weapons, Tools, Scrap).
/// Provides unified crosshair aim-detection, line-of-sight verification, HUD action prompts,
/// and server-authoritative collection RPCs.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public abstract class WorldPickup : NetworkBehaviour
{
    [Header("Pickup Settings")]
    [SerializeField] protected float pickupDistance = 3.2f;

    public NetworkVariable<bool> IsCollected = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>The pickup currently targeted for [E] interaction by the local player.</summary>
    public static WorldPickup FocusedPickup
    {
        get;
        private set;
    }

    private static readonly List<WorldPickup> activePickups = new List<WorldPickup>();
    private static Texture2D promptTexture;

    public override void OnNetworkSpawn()
    {
        if (!activePickups.Contains(this))
        {
            activePickups.Add(this);
        }

        IsCollected.OnValueChanged += OnCollectedStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        IsCollected.OnValueChanged -= OnCollectedStateChanged;

        if (activePickups.Contains(this))
        {
            activePickups.Remove(this);
        }

        if (FocusedPickup == this)
        {
            FocusedPickup = null;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();

        if (activePickups.Contains(this))
        {
            activePickups.Remove(this);
        }

        if (FocusedPickup == this)
        {
            FocusedPickup = null;
        }
    }

    protected virtual void Update()
    {
        if (IsCollected.Value)
        {
            return;
        }

        // Only evaluate targeting on the local client
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            UpdateTargeting();
        }
    }

    private void UpdateTargeting()
    {
        UpdateFocusedPickup();

        if (FocusedPickup == this)
        {
            if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
            {
                return;
            }

            GameObject localPlayer = GetLocalPlayerObject();
            if (localPlayer == null)
            {
                return;
            }

            RagdollController ragdoll = localPlayer.GetComponent<RagdollController>();
            if (ragdoll != null && ragdoll.IsRagdolled.Value)
            {
                return;
            }

            PlayerHealth health = localPlayer.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead.Value)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                if (CanPickup(localPlayer))
                {
                    PickupServerRpc(NetworkManager.Singleton.LocalClientId);
                    FocusedPickup = null;
                }
            }
        }
    }

    private static void UpdateFocusedPickup()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            FocusedPickup = null;
            return;
        }

        GameObject localPlayer = GetLocalPlayerObject();
        if (localPlayer == null)
        {
            FocusedPickup = null;
            return;
        }

        Camera cam = GetActiveCamera();
        if (cam == null)
        {
            FocusedPickup = null;
            return;
        }

        Ray aimRay = new Ray(cam.transform.position, cam.transform.forward);
        float maxDistance = 3.8f;
        Transform playerTransform = localPlayer.transform;

        WorldPickup targetedPickup = null;

        // 1. Direct line-of-sight raycast from center of crosshair
        RaycastHit[] rayHits = Physics.RaycastAll(aimRay, maxDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < rayHits.Length; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider == null || hit.transform.IsChildOf(playerTransform))
            {
                continue;
            }

            WorldPickup hitPickup = hit.collider.GetComponentInParent<WorldPickup>();
            if (hitPickup != null && !hitPickup.IsCollected.Value)
            {
                targetedPickup = hitPickup;
                break;
            }
            else if (!hit.collider.isTrigger)
            {
                // Solid obstacle (wall, obstacle) in front of pickup
                break;
            }
        }

        // 2. If direct ray didn't hit a pickup, try a spherecast (generous cone around crosshair)
        if (targetedPickup == null)
        {
            RaycastHit[] sphereHits = Physics.SphereCastAll(aimRay, 0.25f, maxDistance, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(sphereHits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < sphereHits.Length; i++)
            {
                RaycastHit hit = sphereHits[i];
                if (hit.collider == null || hit.transform.IsChildOf(playerTransform))
                {
                    continue;
                }

                WorldPickup hitPickup = hit.collider.GetComponentInParent<WorldPickup>();
                if (hitPickup != null && !hitPickup.IsCollected.Value)
                {
                    Vector3 toPickup = hitPickup.transform.position - cam.transform.position;
                    float dot = Vector3.Dot(cam.transform.forward, toPickup.normalized);

                    // Forgiving cone (~49 deg) so looking down at coins on the floor works smoothly
                    if (dot > 0.65f)
                    {
                        targetedPickup = hitPickup;
                        break;
                    }
                }
                else if (!hit.collider.isTrigger)
                {
                    break;
                }
            }
        }

        FocusedPickup = targetedPickup;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PickupServerRpc(ulong collectorClientId)
    {
        if (IsCollected.Value)
        {
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(collectorClientId, out NetworkClient client))
        {
            return;
        }

        if (client.PlayerObject == null)
        {
            return;
        }

        PlayerHealth playerHealth = client.PlayerObject.GetComponent<PlayerHealth>();
        if (playerHealth != null && playerHealth.IsDead.Value)
        {
            return;
        }

        float dist = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
        if (dist > pickupDistance + 3.0f)
        {
            return;
        }

        OnPickupServer(collectorClientId, client.PlayerObject.gameObject);
    }

    protected void CompletePickup()
    {
        IsCollected.Value = true;

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnCollectedStateChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            gameObject.SetActive(false);
        }
    }

    protected virtual void OnGUI()
    {
        if (FocusedPickup != this || IsCollected.Value)
        {
            return;
        }

        if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
        {
            return;
        }

        Camera cam = GetActiveCamera();
        if (cam == null)
        {
            return;
        }

        GameObject localPlayer = GetLocalPlayerObject();
        if (localPlayer == null)
        {
            return;
        }

        bool canTake = CanPickup(localPlayer);
        Color accentColor = canTake ? GetAccentColor() : new Color(0.9f, 0.3f, 0.3f);

        // 1. Floating 3D tag above object
        Vector3 worldPoint = transform.position + Vector3.up * 0.38f;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPoint);
        if (screenPos.z > 0f)
        {
            float tagX = screenPos.x;
            float tagY = Screen.height - screenPos.y;

            GUIStyle tagStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            tagStyle.normal.textColor = accentColor;

            GUI.Label(new Rect(tagX - 90f, tagY - 12f, 180f, 24f), GetFloatingTagText(), tagStyle);
        }

        // 2. Centered HUD action prompt below crosshair
        string promptText = GetPromptText(localPlayer);
        float promptW = Mathf.Max(260f, promptText.Length * 9.5f);
        float promptH = 34f;
        float promptX = (Screen.width - promptW) * 0.5f;
        float promptY = Screen.height * 0.5f + 48f;

        // Dark background box
        DrawPromptRect(new Rect(promptX, promptY, promptW, promptH), new Color(0.06f, 0.08f, 0.12f, 0.92f));
        // Accent top border
        DrawPromptRect(new Rect(promptX - 1f, promptY - 1f, promptW + 2f, 2f), accentColor);

        GUIStyle promptStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        promptStyle.normal.textColor = canTake ? new Color(1f, 0.95f, 0.75f) : new Color(1f, 0.4f, 0.4f);

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

    protected static Camera GetActiveCamera()
    {
        PlayerController[] pcs = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < pcs.Length; i++)
        {
            if (pcs[i] != null && pcs[i].IsOwner)
            {
                Camera playerCam = pcs[i].GetComponentInChildren<Camera>();
                if (playerCam != null && playerCam.isActiveAndEnabled)
                {
                    return playerCam;
                }
            }
        }

        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
        {
            return main;
        }

        return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
    }

    protected static GameObject GetLocalPlayerObject()
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

    // ── Abstract Methods for Subclasses ───────────────────────────────────────
    public abstract string GetPromptText(GameObject localPlayer);
    public abstract string GetFloatingTagText();
    public abstract Color GetAccentColor();
    public abstract bool CanPickup(GameObject localPlayer);
    protected abstract void OnPickupServer(ulong collectorClientId, GameObject playerObject);
}
