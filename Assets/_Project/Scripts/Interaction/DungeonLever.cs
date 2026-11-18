using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Networked interactive lever / switch.
/// Mounted on walls or consoles in the dungeon.
/// Players aim at the lever and press [E] to pull or toggle it.
/// Supports toggle mode, timed auto-reset (for 2-player co-op puzzles),
/// and fires events when state changes to open doors or trigger mechanisms.
/// </summary>
public class DungeonLever : NetworkBehaviour
{
    [Header("Lever Settings")]
    [SerializeField] private string leverName = "Security Lever";
    [SerializeField] private string mechanismChannel = "";
    [SerializeField] private bool toggleable = true;
    [SerializeField] private float resetDelay = 0f; // 0 = permanent until toggled; > 0 = timed auto-reset
    [SerializeField] private float interactionDistance = 3.2f;

    [Header("Visual Parts")]
    [SerializeField] private Transform handlePivot;
    [SerializeField] private Renderer statusLightRenderer;
    [SerializeField] private Color activeColor = new Color(0.2f, 1f, 0.2f); // Green
    [SerializeField] private Color inactiveColor = new Color(1f, 0.2f, 0.2f); // Red

    [Header("Events")]
    public UnityEvent<bool> OnLeverToggled;
    public static event System.Action<string, bool> OnMechanismToggled;

    public string MechanismChannel
    {
        get
        {
            return mechanismChannel;
        }
    }

    public void SetMechanismChannel(string channel)
    {
        mechanismChannel = channel;
    }

    public NetworkVariable<bool> IsPulled = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>The lever currently targeted by the local player's crosshair.</summary>
    public static DungeonLever FocusedLever
    {
        get;
        private set;
    }

    private static readonly List<DungeonLever> activeLevers = new List<DungeonLever>();
    private static Texture2D promptTexture;

    private float autoResetTimer;
    private Quaternion targetHandleRotation;

    private void Awake()
    {
        EnsureComponentsAndVisuals();
    }

    public override void OnNetworkSpawn()
    {
        if (!activeLevers.Contains(this))
        {
            activeLevers.Add(this);
        }

        IsPulled.OnValueChanged += OnPulledStateChanged;
        UpdateVisuals(IsPulled.Value, false);
    }

    public override void OnNetworkDespawn()
    {
        IsPulled.OnValueChanged -= OnPulledStateChanged;

        if (activeLevers.Contains(this))
        {
            activeLevers.Remove(this);
        }

        if (FocusedLever == this)
        {
            FocusedLever = null;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();

        if (activeLevers.Contains(this))
        {
            activeLevers.Remove(this);
        }

        if (FocusedLever == this)
        {
            FocusedLever = null;
        }
    }

    private void Update()
    {
        // Smoothly animate handle rotation towards target
        if (handlePivot != null)
        {
            handlePivot.localRotation = Quaternion.Slerp(
                handlePivot.localRotation,
                targetHandleRotation,
                Time.deltaTime * 14f
            );
        }

        // Server-side timed auto-reset (for co-op coordination puzzles)
        if (IsServer && resetDelay > 0f && IsPulled.Value)
        {
            autoResetTimer -= Time.deltaTime;
            if (autoResetTimer <= 0f)
            {
                IsPulled.Value = false;
            }
        }

        // Client crosshair aim-detection and [E] interaction
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            UpdateTargeting();
        }
    }

    private void UpdateTargeting()
    {
        UpdateFocusedLever();

        if (FocusedLever == this)
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
                PullServerRpc(NetworkManager.Singleton.LocalClientId);
            }
        }
    }

    private static void UpdateFocusedLever()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            FocusedLever = null;
            return;
        }

        GameObject localPlayer = GetLocalPlayerObject();
        if (localPlayer == null)
        {
            FocusedLever = null;
            return;
        }

        Camera cam = GetActiveCamera();
        if (cam == null)
        {
            FocusedLever = null;
            return;
        }

        Ray aimRay = new Ray(cam.transform.position, cam.transform.forward);
        float maxDistance = 3.8f;
        Transform playerTransform = localPlayer.transform;

        DungeonLever targetedLever = null;

        // 1. Direct crosshair raycast
        RaycastHit[] rayHits = Physics.RaycastAll(aimRay, maxDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < rayHits.Length; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider == null || hit.transform.IsChildOf(playerTransform))
            {
                continue;
            }

            DungeonLever hitLever = hit.collider.GetComponentInParent<DungeonLever>();
            if (hitLever != null)
            {
                targetedLever = hitLever;
                break;
            }
            else if (!hit.collider.isTrigger)
            {
                break;
            }
        }

        // 2. Fallback spherecast for forgiving aim near lever
        if (targetedLever == null)
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

                DungeonLever hitLever = hit.collider.GetComponentInParent<DungeonLever>();
                if (hitLever != null)
                {
                    Vector3 toLever = hitLever.transform.position - cam.transform.position;
                    float dot = Vector3.Dot(cam.transform.forward, toLever.normalized);

                    if (dot > 0.65f)
                    {
                        targetedLever = hitLever;
                        break;
                    }
                }
                else if (!hit.collider.isTrigger)
                {
                    break;
                }
            }
        }

        FocusedLever = targetedLever;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PullServerRpc(ulong collectorClientId)
    {
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
        if (dist > interactionDistance + 2.5f)
        {
            return;
        }

        if (toggleable)
        {
            IsPulled.Value = !IsPulled.Value;
        }
        else
        {
            IsPulled.Value = true;
        }

        if (resetDelay > 0f && IsPulled.Value)
        {
            autoResetTimer = resetDelay;
        }

        Debug.Log($"[DungeonLever] Lever '{leverName}' pulled by Player {collectorClientId}. State: {IsPulled.Value}");
    }

    private void OnPulledStateChanged(bool oldValue, bool newValue)
    {
        UpdateVisuals(newValue, true);
        OnLeverToggled?.Invoke(newValue);

        if (!string.IsNullOrEmpty(mechanismChannel))
        {
            OnMechanismToggled?.Invoke(mechanismChannel, newValue);
        }
    }

    private void UpdateVisuals(bool pulled, bool playSound)
    {
        // Target handle rotation: Up (-40 deg) when off, Down (+40 deg) when pulled
        targetHandleRotation = pulled ? Quaternion.Euler(40f, 0f, 0f) : Quaternion.Euler(-40f, 0f, 0f);

        // Update status light color
        if (statusLightRenderer != null)
        {
            Color lightCol = pulled ? activeColor : inactiveColor;
            MaterialHelper.ApplyMaterial(statusLightRenderer, lightCol);
        }
    }

    private void OnGUI()
    {
        if (FocusedLever != this)
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

        Color accentColor = IsPulled.Value ? activeColor : new Color(0.2f, 0.85f, 1f);

        // 1. Floating 3D world tag
        Vector3 worldPoint = transform.position + Vector3.up * 0.42f;
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

            string statusTag = IsPulled.Value ? "ACTIVE" : "READY";
            GUI.Label(new Rect(tagX - 80f, tagY - 12f, 160f, 24f), $"{leverName} [{statusTag}]", tagStyle);
        }

        // 2. Centered HUD action prompt
        string actionText = IsPulled.Value ? $"[E] RESET {leverName.ToUpper()}" : $"[E] PULL {leverName.ToUpper()}";
        float promptW = Mathf.Max(260f, actionText.Length * 9.5f);
        float promptH = 34f;
        float promptX = (Screen.width - promptW) * 0.5f;
        float promptY = Screen.height * 0.5f + 48f;

        DrawPromptRect(new Rect(promptX, promptY, promptW, promptH), new Color(0.06f, 0.08f, 0.12f, 0.92f));
        DrawPromptRect(new Rect(promptX - 1f, promptY - 1f, promptW + 2f, 2f), accentColor);

        GUIStyle promptStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        promptStyle.normal.textColor = new Color(1f, 0.95f, 0.75f);

        GUI.Label(new Rect(promptX, promptY, promptW, promptH), actionText, promptStyle);
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

    private static Camera GetActiveCamera()
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

    private static GameObject GetLocalPlayerObject()
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

    /// <summary>
    /// Constructs procedural industrial lever meshes if not already assigned.
    /// Allows creating functional levers directly from code without manual modeling.
    /// </summary>
    public void EnsureComponentsAndVisuals()
    {
        // Collider for raycast targeting
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
        {
            box = gameObject.AddComponent<BoxCollider>();
        }
        box.size = new Vector3(0.5f, 0.65f, 0.45f);
        box.center = new Vector3(0f, 0f, 0.15f);

        // 1. Base Plate
        Transform baseTr = transform.Find("BasePlate");
        if (baseTr == null)
        {
            GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseObj.name = "BasePlate";
            baseObj.transform.parent = transform;
            baseObj.transform.localPosition = Vector3.zero;
            baseObj.transform.localRotation = Quaternion.identity;
            baseObj.transform.localScale = new Vector3(0.4f, 0.55f, 0.12f);

            Destroy(baseObj.GetComponent<Collider>());
            Renderer rend = baseObj.GetComponent<Renderer>();
            if (rend != null)
            {
                MaterialHelper.ApplyMaterial(rend, new Color(0.22f, 0.22f, 0.25f));
            }
        }

        // 2. Status Light
        Transform lightTr = transform.Find("StatusLight");
        if (lightTr == null)
        {
            GameObject lightObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lightObj.name = "StatusLight";
            lightObj.transform.parent = transform;
            lightObj.transform.localPosition = new Vector3(0f, 0.2f, 0.07f);
            lightObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lightObj.transform.localScale = new Vector3(0.08f, 0.02f, 0.08f);

            Destroy(lightObj.GetComponent<Collider>());
            statusLightRenderer = lightObj.GetComponent<Renderer>();
            if (statusLightRenderer != null)
            {
                MaterialHelper.ApplyMaterial(statusLightRenderer, inactiveColor);
            }
        }
        else
        {
            statusLightRenderer = lightTr.GetComponent<Renderer>();
        }

        // 3. Handle Pivot & Stick
        Transform pivotTr = transform.Find("HandlePivot");
        if (pivotTr == null)
        {
            GameObject pivotObj = new GameObject("HandlePivot");
            pivotObj.transform.parent = transform;
            pivotObj.transform.localPosition = new Vector3(0f, -0.05f, 0.07f);
            pivotObj.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);
            handlePivot = pivotObj.transform;

            // Lever Arm Rod
            GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rod.name = "Rod";
            rod.transform.parent = handlePivot;
            rod.transform.localPosition = new Vector3(0f, 0.14f, 0f);
            rod.transform.localRotation = Quaternion.identity;
            rod.transform.localScale = new Vector3(0.045f, 0.14f, 0.045f);

            Destroy(rod.GetComponent<Collider>());
            Renderer rodRend = rod.GetComponent<Renderer>();
            if (rodRend != null)
            {
                MaterialHelper.ApplyMaterial(rodRend, new Color(0.6f, 0.62f, 0.65f));
            }

            // Knob / Grip Ball
            GameObject knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = "Knob";
            knob.transform.parent = handlePivot;
            knob.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            knob.transform.localRotation = Quaternion.identity;
            knob.transform.localScale = new Vector3(0.11f, 0.11f, 0.11f);

            Destroy(knob.GetComponent<Collider>());
            Renderer knobRend = knob.GetComponent<Renderer>();
            if (knobRend != null)
            {
                MaterialHelper.ApplyMaterial(knobRend, new Color(1f, 0.75f, 0.1f)); // Industrial Yellow
            }
        }
        else
        {
            handlePivot = pivotTr;
        }

        targetHandleRotation = Quaternion.Euler(-40f, 0f, 0f);
    }
}
