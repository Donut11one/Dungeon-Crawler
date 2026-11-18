using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Networked security blast door.
/// Linked to levers via mechanism channels (e.g. "MainHall_NorthGate").
/// When the linked lever is pulled, the door slides open, enabling passages and NavMesh navigation.
/// When the lever is reset or door is closed, it slides shut and carves a NavMeshObstacle,
/// blocking players, monsters, and projectiles.
/// </summary>
public class DungeonDoor : NetworkBehaviour
{
    [Header("Door Settings")]
    [SerializeField] private string doorName = "Security Blast Door";
    [SerializeField] private string mechanismChannel = "";
    [SerializeField] private bool startsOpen = false;
    [SerializeField] private float slideSpeed = 3.6f;
    [SerializeField] private Vector3 openLocalOffset = new Vector3(0f, 3.4f, 0f);

    [Header("Visual Parts")]
    [SerializeField] private Transform doorPanel;
    [SerializeField] private Renderer statusLightRenderer;
    [SerializeField] private Color openColor = new Color(0.2f, 1f, 0.2f);
    [SerializeField] private Color closedColor = new Color(1f, 0.2f, 0.2f);

    [Header("Navigation & Physics")]
    [SerializeField] private NavMeshObstacle navObstacle;
    [SerializeField] private AudioSource audioSource;

    public string MechanismChannel
    {
        get
        {
            return mechanismChannel;
        }
    }

    public string DoorName
    {
        get
        {
            return doorName;
        }
    }

    public void SetMechanismChannel(string channel)
    {
        mechanismChannel = channel;
    }

    public void Configure(string name, string channel, bool startOpen)
    {
        doorName = name;
        mechanismChannel = channel;
        startsOpen = startOpen;
    }

    public NetworkVariable<bool> IsOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>The door currently targeted by the local player's crosshair.</summary>
    public static DungeonDoor FocusedDoor
    {
        get;
        private set;
    }

    private static readonly List<DungeonDoor> activeDoors = new List<DungeonDoor>();
    private static Texture2D promptTexture;
    private Vector3 targetLocalPosition;

    private void Awake()
    {
        EnsureComponentsAndVisuals();
    }

    public override void OnNetworkSpawn()
    {
        if (!activeDoors.Contains(this))
        {
            activeDoors.Add(this);
        }

        if (IsServer)
        {
            IsOpen.Value = startsOpen;
        }

        IsOpen.OnValueChanged += OnOpenStateChanged;
        DungeonLever.OnMechanismToggled += HandleMechanismToggled;

        UpdateVisualsAndObstacle(IsOpen.Value, false);
    }

    public override void OnNetworkDespawn()
    {
        IsOpen.OnValueChanged -= OnOpenStateChanged;
        DungeonLever.OnMechanismToggled -= HandleMechanismToggled;

        if (activeDoors.Contains(this))
        {
            activeDoors.Remove(this);
        }

        if (FocusedDoor == this)
        {
            FocusedDoor = null;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();

        DungeonLever.OnMechanismToggled -= HandleMechanismToggled;

        if (activeDoors.Contains(this))
        {
            activeDoors.Remove(this);
        }

        if (FocusedDoor == this)
        {
            FocusedDoor = null;
        }
    }

    private void HandleMechanismToggled(string channel, bool isPulled)
    {
        if (!IsServer)
        {
            return;
        }

        if (string.IsNullOrEmpty(mechanismChannel) || string.IsNullOrEmpty(channel))
        {
            return;
        }

        if (string.Equals(mechanismChannel, channel, System.StringComparison.OrdinalIgnoreCase))
        {
            IsOpen.Value = isPulled;
            Debug.Log($"[DungeonDoor] Door '{doorName}' (Channel: {mechanismChannel}) set to Open={isPulled} by lever.");
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ToggleDoorServerRpc()
    {
        if (!IsServer)
        {
            return;
        }

        IsOpen.Value = !IsOpen.Value;
    }

    public void SetDoorOpenServer(bool open)
    {
        if (IsServer)
        {
            IsOpen.Value = open;
        }
    }

    private void OnOpenStateChanged(bool previous, bool current)
    {
        UpdateVisualsAndObstacle(current, true);
    }

    private void UpdateVisualsAndObstacle(bool open, bool playSound)
    {
        targetLocalPosition = open ? openLocalOffset : Vector3.zero;

        if (statusLightRenderer != null)
        {
            MaterialHelper.ApplyMaterial(statusLightRenderer, open ? openColor : closedColor);
        }

        if (navObstacle != null)
        {
            // When door is open, disable obstacle so AI paths freely
            // When door is closed, enable obstacle carving to block pathfinding
            navObstacle.carving = !open;
            navObstacle.enabled = !open;
        }

        if (playSound)
        {
            PlayDoorSound(open);
        }
    }

    private void Update()
    {
        // Smoothly slide the door panel between closed and open positions
        if (doorPanel != null)
        {
            doorPanel.localPosition = Vector3.MoveTowards(
                doorPanel.localPosition,
                targetLocalPosition,
                slideSpeed * Time.deltaTime
            );
        }

        // Crosshair focus detection for HUD interaction prompt
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            UpdateFocusedDoor();
        }
    }

    private static void UpdateFocusedDoor()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            FocusedDoor = null;
            return;
        }

        GameObject localPlayer = GetLocalPlayerObject();
        if (localPlayer == null)
        {
            FocusedDoor = null;
            return;
        }

        Camera cam = GetActiveCamera();
        if (cam == null)
        {
            FocusedDoor = null;
            return;
        }

        Ray aimRay = new Ray(cam.transform.position, cam.transform.forward);
        float maxDistance = 4.2f;
        Transform playerTransform = localPlayer.transform;

        RaycastHit[] hits = Physics.RaycastAll(aimRay, maxDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) =>
        {
            return a.distance.CompareTo(b.distance);
        });

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(playerTransform))
            {
                continue;
            }

            DungeonDoor door = hit.collider.GetComponentInParent<DungeonDoor>();
            if (door != null && activeDoors.Contains(door))
            {
                FocusedDoor = door;
                return;
            }

            // If we hit an opaque obstacle that isn't a door, stop line of sight
            if (!hit.collider.isTrigger)
            {
                break;
            }
        }

        // Proximity cone fallback
        DungeonDoor bestDoor = null;
        float bestDot = 0.65f;

        for (int i = 0; i < activeDoors.Count; i++)
        {
            DungeonDoor candidate = activeDoors[i];
            if (candidate == null)
            {
                continue;
            }

            Vector3 toDoor = candidate.transform.position - cam.transform.position;
            float dist = toDoor.magnitude;
            if (dist > maxDistance)
            {
                continue;
            }

            float dot = Vector3.Dot(cam.transform.forward, toDoor.normalized);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestDoor = candidate;
            }
        }

        FocusedDoor = bestDoor;
    }

    private void OnGUI()
    {
        if (FocusedDoor != this)
        {
            return;
        }

        if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
        {
            return;
        }

        DrawInteractionPrompt();
    }

    private void DrawInteractionPrompt()
    {
        string statusText;
        Color textColor;

        if (IsOpen.Value)
        {
            statusText = $"{doorName.ToUpper()} [UNLOCKED / OPEN]";
            textColor = new Color(0.3f, 1f, 0.4f);
        }
        else
        {
            if (!string.IsNullOrEmpty(mechanismChannel))
            {
                statusText = $"[LOCKED] LINKED TO LEVER: {mechanismChannel.ToUpper()}";
                textColor = new Color(1f, 0.4f, 0.3f);
            }
            else
            {
                statusText = $"{doorName.ToUpper()} [CLOSED / LOCKED]";
                textColor = new Color(1f, 0.3f, 0.3f);
            }
        }

        float promptWidth = Mathf.Max(320f, statusText.Length * 9.5f);
        float promptHeight = 36f;
        float promptX = (Screen.width - promptWidth) * 0.5f;
        float promptY = Screen.height * 0.5f + 55f;

        DrawPromptRect(new Rect(promptX, promptY, promptWidth, promptHeight), new Color(0.05f, 0.05f, 0.08f, 0.92f));
        DrawPromptRect(new Rect(promptX - 1f, promptY - 1f, promptWidth + 2f, 2f), textColor);

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = textColor;

        GUI.Label(new Rect(promptX, promptY, promptWidth, promptHeight), statusText, style);
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

    private void PlayDoorSound(bool open)
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource != null)
        {
            audioSource.pitch = open ? 1.05f : 0.85f;
            audioSource.Play();
        }
    }

    /// <summary>
    /// Builds and verifies all physical sub-objects (posts, frame lintel, sliding door slab,
    /// status light, colliders, and NavMeshObstacle).
    /// </summary>
    public void EnsureComponentsAndVisuals()
    {
        // 1. AudioSource
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 3f;
                audioSource.maxDistance = 22f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        // 2. NavMeshObstacle on root
        if (navObstacle == null)
        {
            navObstacle = GetComponent<NavMeshObstacle>();
            if (navObstacle == null)
            {
                navObstacle = gameObject.AddComponent<NavMeshObstacle>();
            }
            navObstacle.shape = NavMeshObstacleShape.Box;
            navObstacle.size = new Vector3(3.4f, 3.8f, 0.6f);
            navObstacle.center = new Vector3(0f, 1.9f, 0f);
            navObstacle.carving = !startsOpen;
            navObstacle.enabled = !startsOpen;
        }

        // 3. Frame Posts & Lintel
        Transform frame = transform.Find("DoorFrame");
        if (frame == null)
        {
            GameObject frameObj = new GameObject("DoorFrame");
            frameObj.transform.parent = transform;
            frameObj.transform.localPosition = Vector3.zero;
            frameObj.transform.localRotation = Quaternion.identity;
            frame = frameObj.transform;

            Color frameCol = new Color(0.22f, 0.22f, 0.25f);

            // Left Post
            CreateCubePrimitive(frame, "Post_Left", new Vector3(-1.7f, 1.9f, 0f), new Vector3(0.35f, 3.8f, 0.5f), frameCol);
            // Right Post
            CreateCubePrimitive(frame, "Post_Right", new Vector3(1.7f, 1.9f, 0f), new Vector3(0.35f, 3.8f, 0.5f), frameCol);
            // Top Lintel
            CreateCubePrimitive(frame, "Lintel_Top", new Vector3(0f, 3.75f, 0f), new Vector3(3.75f, 0.45f, 0.55f), frameCol);
        }

        // 4. Sliding Door Panel
        if (doorPanel == null)
        {
            Transform existingPanel = transform.Find("SlidingPanel");
            if (existingPanel != null)
            {
                doorPanel = existingPanel;
            }
            else
            {
                GameObject panelObj = new GameObject("SlidingPanel");
                panelObj.transform.parent = transform;
                panelObj.transform.localPosition = Vector3.zero;
                panelObj.transform.localRotation = Quaternion.identity;
                doorPanel = panelObj.transform;

                // Main heavy door slab
                Color doorMetalCol = new Color(0.35f, 0.34f, 0.36f);
                GameObject slab = CreateCubePrimitive(
                    doorPanel,
                    "DoorSlab",
                    new Vector3(0f, 1.7f, 0f),
                    new Vector3(3.1f, 3.4f, 0.24f),
                    doorMetalCol
                );

                // Add BoxCollider for physical bullet/player/monster collision
                BoxCollider slabCol = slab.GetComponent<BoxCollider>();
                if (slabCol == null)
                {
                    slabCol = slab.AddComponent<BoxCollider>();
                }
                slabCol.isTrigger = false;

                // Center hazard band decoration
                Color hazardCol = new Color(0.85f, 0.65f, 0.12f);
                CreateCubePrimitive(
                    doorPanel,
                    "HazardBand",
                    new Vector3(0f, 1.7f, 0f),
                    new Vector3(2.8f, 0.35f, 0.27f),
                    hazardCol
                );
            }
        }

        // 5. Status Light
        if (statusLightRenderer == null)
        {
            Transform existingLight = transform.Find("StatusLight");
            if (existingLight != null)
            {
                statusLightRenderer = existingLight.GetComponent<Renderer>();
            }
            else
            {
                GameObject lightObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lightObj.name = "StatusLight";
                lightObj.transform.parent = transform;
                lightObj.transform.localPosition = new Vector3(0f, 3.5f, -0.28f);
                lightObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                lightObj.transform.localScale = new Vector3(0.25f, 0.06f, 0.25f);

                Collider col = lightObj.GetComponent<Collider>();
                if (col != null)
                {
                    DestroyImmediate(col);
                }

                statusLightRenderer = lightObj.GetComponent<Renderer>();
                if (statusLightRenderer != null)
                {
                    MaterialHelper.ApplyMaterial(statusLightRenderer, startsOpen ? openColor : closedColor);
                }
            }
        }
    }

    private static GameObject CreateCubePrimitive(Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.parent = parent;
        cube.transform.localPosition = pos;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = scale;

        Renderer rend = cube.GetComponent<Renderer>();
        if (rend != null)
        {
            MaterialHelper.ApplyMaterial(rend, color);
        }

        return cube;
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

    private static Camera GetActiveCamera()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.isActiveAndEnabled)
        {
            return mainCam;
        }

        Camera[] cams = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
        for (int i = 0; i < cams.Length; i++)
        {
            if (cams[i] != null && cams[i].isActiveAndEnabled)
            {
                return cams[i];
            }
        }

        return null;
    }
}
