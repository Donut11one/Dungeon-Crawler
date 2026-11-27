using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Modular player inventory and Minecraft-style hotbar system.
/// Server-authoritative slot storage using NGO NetworkList.
/// Owner-authoritative slot selection for zero-latency active item switching.
/// </summary>
public class PlayerInventory : NetworkBehaviour
{
    public const int HotbarSlotCount = 5;

    public NetworkList<InventorySlot> Slots;

    public NetworkVariable<int> SelectedSlotIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public NetworkVariable<bool> FlashlightActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private PlayerAttack playerAttack;
    private PlayerController playerController;
    private FirstPersonViewmodel viewmodel;
    private Camera ownerCamera;
    private Light flashlightLight;
    private Light flashlightSoftFill;
    private GameObject thirdPersonFlashlight;
    private Light thirdPersonFlashlightSpot;
    private Light thirdPersonFlashlightGlow;
    private Material thirdPersonFlashlightLensMat;
    private readonly RaycastHit[] flashlightWallHits = new RaycastHit[8];

    private void Awake()
    {
        playerAttack = GetComponent<PlayerAttack>();
        playerController = GetComponent<PlayerController>();
        viewmodel = GetComponent<FirstPersonViewmodel>();
        ownerCamera = GetComponentInChildren<Camera>(true);

        Slots = new NetworkList<InventorySlot>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    }

    private Transform thirdPersonHolder;
    private GameObject currentThirdPersonVisual;

    public override void OnNetworkSpawn()
    {
        ItemRegistry.Initialize();

        SelectedSlotIndex.OnValueChanged += OnSelectedSlotChanged;
        FlashlightActive.OnValueChanged += OnFlashlightActiveChanged;
        Slots.OnListChanged += OnSlotsListChanged;

        if (IsServer)
        {
            // Initialize 5 empty hotbar slots if empty
            if (Slots.Count == 0)
            {
                for (int i = 0; i < HotbarSlotCount; i++)
                {
                    Slots.Add(new InventorySlot());
                }
            }

            // Contractor starter gear: Tactical Flashlight in hotbar
            if (IsEmpty())
            {
                AddItem("flashlight", 1);
            }
        }

        UpdateThirdPersonWeaponVisual(SelectedSlotIndex.Value);
        RefreshFlashlightState();
    }

    public override void OnNetworkDespawn()
    {
        SelectedSlotIndex.OnValueChanged -= OnSelectedSlotChanged;
        FlashlightActive.OnValueChanged -= OnFlashlightActiveChanged;
        Slots.OnListChanged -= OnSlotsListChanged;
    }

    private void OnSlotsListChanged(NetworkListEvent<InventorySlot> changeEvent)
    {
        UpdateThirdPersonWeaponVisual(SelectedSlotIndex.Value);
        RefreshFlashlightState();
    }

    private void OnSelectedSlotChanged(int previous, int current)
    {
        UpdateThirdPersonWeaponVisual(current);
        RefreshFlashlightState();
    }

    private void UpdateThirdPersonWeaponVisual(int slotIndex)
    {
        // Local owner uses FirstPersonViewmodel; never attach blocking third-person cubes into owner's body/camera
        if (IsOwner)
        {
            if (currentThirdPersonVisual != null)
            {
                Destroy(currentThirdPersonVisual);
                currentThirdPersonVisual = null;
            }
            thirdPersonFlashlightLensMat = null;
            return;
        }

        if (thirdPersonHolder == null)
        {
            Transform handR = FindHandBone(transform);
            if (handR != null)
            {
                GameObject holder = new GameObject("ThirdPersonWeaponHolder");
                holder.transform.parent = handR;
                holder.transform.localPosition = new Vector3(0.04f, 0.08f, 0.05f);
                holder.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
                thirdPersonHolder = holder.transform;
            }
            else
            {
                Transform camHolder = transform.Find("CameraHolder") ?? transform;
                GameObject holder = new GameObject("ThirdPersonWeaponHolder");
                holder.transform.SetParent(camHolder, false);
                holder.transform.localPosition = camHolder == transform
                    ? new Vector3(0.28f, 1.05f, 0.34f)
                    : new Vector3(0.28f, -0.15f, 0.34f);
                holder.transform.localRotation = Quaternion.identity;
                thirdPersonHolder = holder.transform;
            }
        }

        if (thirdPersonHolder == null)
        {
            return;
        }

        if (currentThirdPersonVisual != null)
        {
            Destroy(currentThirdPersonVisual);
            currentThirdPersonVisual = null;
        }
        thirdPersonFlashlightLensMat = null;

        ItemData active = GetSelectedItem();
        if (active == null)
        {
            return;
        }

        currentThirdPersonVisual = new GameObject($"TP_{active.ItemId}");
        currentThirdPersonVisual.transform.parent = thirdPersonHolder;
        currentThirdPersonVisual.transform.localPosition = Vector3.zero;
        currentThirdPersonVisual.transform.localRotation = Quaternion.identity;

        if (active.ItemId == "flashlight")
        {
            BuildThirdPersonFlashlightVisual(currentThirdPersonVisual.transform);
            return;
        }

        // Create compact third-person item mesh
        GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prop.transform.parent = currentThirdPersonVisual.transform;
        prop.transform.localPosition = new Vector3(0f, 0f, 0.12f);
        prop.transform.localScale = active.Type == ItemType.RangedWeapon
            ? new Vector3(0.06f, 0.10f, 0.32f)
            : new Vector3(0.04f, 0.04f, 0.38f);
        prop.transform.localRotation = Quaternion.identity;

        Collider col = prop.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer rend = prop.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = MaterialHelper.CreateLitMaterial(rend, active.SlotColor);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void BuildThirdPersonFlashlightVisual(Transform parent)
    {
        CreateThirdPersonPart(parent, "TP_Handle", new Vector3(0f, 0f, 0.08f), new Vector3(0.05f, 0.05f, 0.22f), new Color(0.18f, 0.20f, 0.22f));
        CreateThirdPersonPart(parent, "TP_Bezel", new Vector3(0f, 0f, 0.20f), new Vector3(0.085f, 0.085f, 0.07f), new Color(0.95f, 0.85f, 0.20f));

        GameObject lens = CreateThirdPersonPart(parent, "TP_Lens", new Vector3(0f, 0f, 0.238f), new Vector3(0.068f, 0.068f, 0.01f), new Color(0.35f, 0.35f, 0.32f));
        Renderer lensRend = lens.GetComponent<Renderer>();
        if (lensRend != null)
        {
            thirdPersonFlashlightLensMat = lensRend.material;
            UpdateThirdPersonLensEmission(IsFlashlightShining);
        }
    }

    private GameObject CreateThirdPersonPart(Transform parent, string partName, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        part.transform.localRotation = Quaternion.identity;

        Collider col = part.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer rend = part.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = MaterialHelper.CreateLitMaterial(rend, color);
            // Held third-person meshes must never cast shadows into the remote flashlight spotlight
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        return part;
    }

    private void UpdateThirdPersonLensEmission(bool shining)
    {
        if (thirdPersonFlashlightLensMat == null)
        {
            return;
        }

        if (shining)
        {
            Color bright = new Color(1f, 0.96f, 0.85f);
            thirdPersonFlashlightLensMat.color = bright;
            thirdPersonFlashlightLensMat.EnableKeyword("_EMISSION");
            thirdPersonFlashlightLensMat.SetColor("_EmissionColor", bright * 4.0f);
        }
        else
        {
            thirdPersonFlashlightLensMat.color = new Color(0.35f, 0.35f, 0.32f);
            thirdPersonFlashlightLensMat.DisableKeyword("_EMISSION");
            thirdPersonFlashlightLensMat.SetColor("_EmissionColor", Color.black);
        }
    }

    private Transform FindHandBone(Transform current)
    {
        string lower = current.name.ToLowerInvariant();
        if (lower == "hand.r" || lower == "wrist.r" || lower == "righthand" ||
            lower == "hand_r" || lower == "r_hand" || lower.EndsWith(":righthand") || lower.EndsWith("_righthand"))
        {
            return current;
        }

        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindHandBone(current.GetChild(i));
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        HandleHotbarSelectionInput();
    }

    private void LateUpdate()
    {
        if (IsOwner)
        {
            if (flashlightLight != null && flashlightLight.enabled)
            {
                UpdateLocalFlashlightPose();
            }
        }
        else
        {
            if (thirdPersonFlashlight != null && thirdPersonFlashlight.activeSelf)
            {
                UpdateRemoteFlashlightPose();
            }
        }
    }

    private void HandleHotbarSelectionInput()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (playerController != null && !playerController.CanControl)
        {
            return;
        }

        // Cannot switch weapons or drop item while committed to an active attack swing
        if (playerAttack != null && !playerAttack.CanSwapItem())
        {
            return;
        }

        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            SelectSlot(0);
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            SelectSlot(1);
        }
        else if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            SelectSlot(2);
        }
        else if (Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            SelectSlot(3);
        }
        else if (Keyboard.current.digit5Key.wasPressedThisFrame)
        {
            SelectSlot(4);
        }

        // Drop current item key [Q]
        if (Keyboard.current.qKey.wasPressedThisFrame)
        {
            DropSelectedItemServerRpc();
        }

        // Toggle Tactical Flashlight key [F]
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            ToggleFlashlight();
        }
    }

    public void SelectSlot(int index)
    {
        if (index >= 0 && index < HotbarSlotCount)
        {
            if (index == SelectedSlotIndex.Value)
            {
                return;
            }

            if (playerAttack == null)
            {
                playerAttack = GetComponent<PlayerAttack>();
            }

            // Cannot swap while mid-swing!
            if (playerAttack != null && !playerAttack.CanSwapItem())
            {
                return;
            }

            SelectedSlotIndex.Value = index;

            // Enforce weapon swap equip delay and preserve cooldown to prevent hot-swap exploit
            if (playerAttack != null)
            {
                playerAttack.OnItemSwapped();
            }

            // Immediately extinguish flashlight when swapping away from the flashlight slot
            if (!IsHoldingFlashlight())
            {
                ApplyFlashlightVisuals(false);
                if (FlashlightActive.Value)
                {
                    SetFlashlightActiveServerRpc(false, index);
                }
            }
        }
    }

    /// <summary>
    /// Gets the ItemData for the currently selected hotbar slot.
    /// Returns null if the slot is empty (which signifies using Fists!).
    /// </summary>
    public ItemData GetSelectedItem()
    {
        int index = SelectedSlotIndex.Value;
        if (Slots != null && index >= 0 && index < Slots.Count)
        {
            InventorySlot slot = Slots[index];
            if (!slot.IsEmpty)
            {
                return ItemRegistry.GetItem(slot.itemId.ToString());
            }
        }
        return null; // Empty slot = Fists!
    }

    /// <summary>
    /// Gets the slot data at the given index.
    /// </summary>
    public InventorySlot GetSlot(int index)
    {
        if (Slots != null && index >= 0 && index < Slots.Count)
        {
            return Slots[index];
        }
        return default;
    }

    /// <summary>
    /// Server-authoritative: Clears all inventory slots (used on death or timeout in dungeon).
    /// </summary>
    public void ClearInventory()
    {
        if (!IsServer || Slots == null)
        {
            return;
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            Slots[i] = default;
        }

        if (FlashlightActive.Value)
        {
            FlashlightActive.Value = false;
        }

        Debug.Log($"[PlayerInventory] Cleared inventory for client {OwnerClientId}");
    }

    /// <summary>
    /// Server-authoritative: Drops all carried items in the hotbar onto the ground around origin.
    /// Called when the player dies (whether from mobs, friendly fire, or hazards).
    /// </summary>
    public void DropAllItems(Vector3 origin)
    {
        if (!IsServer || Slots == null)
        {
            return;
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            InventorySlot slot = Slots[i];
            if (slot.IsEmpty)
            {
                continue;
            }

            Vector3 scatterDir = Random.insideUnitSphere;
            scatterDir.y = Mathf.Abs(scatterDir.y) * 0.5f + 0.5f;
            Vector3 dropPos = origin + new Vector3(Random.Range(-0.35f, 0.35f), 0.2f, Random.Range(-0.35f, 0.35f));
            Vector3 dropVel = scatterDir.normalized * Random.Range(2.0f, 4.0f) + Vector3.up * 2.0f;

            WorldItemPickup.SpawnPickup(slot.itemId.ToString(), slot.count, dropPos, dropVel);
            Slots[i] = default;
        }

        if (FlashlightActive.Value)
        {
            FlashlightActive.Value = false;
        }

        Debug.Log($"[PlayerInventory] Player {OwnerClientId} dropped all carried items on death.");
    }

    /// <summary>
    /// Checks if there is at least one empty hotbar slot.
    /// </summary>
    public bool HasFreeSlot()
    {
        if (Slots == null)
        {
            return false;
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether the inventory has capacity for the specified item and count.
    /// Can be queried client-side or server-side.
    /// </summary>
    public bool CanAddItem(string itemId, int count = 1)
    {
        if (string.IsNullOrEmpty(itemId) || count <= 0)
        {
            return false;
        }

        ItemData data = ItemRegistry.GetItem(itemId);
        int maxStack = data != null ? data.MaxStack : 1;

        int remaining = count;

        // 1. Check existing slots with same item
        for (int i = 0; i < Slots.Count; i++)
        {
            InventorySlot slot = Slots[i];
            if (!slot.IsEmpty && slot.itemId.ToString() == itemId && slot.count < maxStack)
            {
                int space = maxStack - slot.count;
                remaining -= space;
                if (remaining <= 0)
                {
                    return true;
                }
            }
        }

        // 2. Check empty slots
        for (int i = 0; i < Slots.Count; i++)
        {
            InventorySlot slot = Slots[i];
            if (slot.IsEmpty)
            {
                remaining -= maxStack;
                if (remaining <= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Server-authoritative: Adds an item into the hotbar.
    /// Returns true if successfully added, false if inventory full.
    /// </summary>
    public bool AddItem(string itemId, int count = 1)
    {
        if (!IsServer || string.IsNullOrEmpty(itemId) || count <= 0)
        {
            return false;
        }

        ItemData data = ItemRegistry.GetItem(itemId);
        int maxStack = data != null ? data.MaxStack : 1;

        // 1. Try stacking into existing non-full slot with same ID
        for (int i = 0; i < Slots.Count; i++)
        {
            InventorySlot slot = Slots[i];
            if (!slot.IsEmpty && slot.itemId.ToString() == itemId && slot.count < maxStack)
            {
                int space = maxStack - slot.count;
                int toAdd = Mathf.Min(space, count);
                slot.count += toAdd;
                Slots[i] = slot;
                count -= toAdd;

                if (count <= 0)
                {
                    return true;
                }
            }
        }

        // 2. Put remainder into first empty slot
        for (int i = 0; i < Slots.Count; i++)
        {
            InventorySlot slot = Slots[i];
            if (slot.IsEmpty)
            {
                int toAdd = Mathf.Min(maxStack, count);
                Slots[i] = new InventorySlot(itemId, toAdd);
                count -= toAdd;

                if (count <= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddItemServerRpc(string itemId, int count)
    {
        AddItem(itemId, count);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DropSelectedItemServerRpc()
    {
        int index = SelectedSlotIndex.Value;
        if (index < 0 || index >= Slots.Count)
        {
            return;
        }

        InventorySlot slot = Slots[index];
        if (slot.IsEmpty)
        {
            return;
        }

        // Spawn world pickup in front of player
        Vector3 spawnPos = transform.position + Vector3.up * 1.0f + transform.forward * 1.2f;
        WorldItemPickup.SpawnPickup(slot.itemId.ToString(), slot.count, spawnPos, transform.forward * 3.5f + Vector3.up * 1.5f);

        Slots[index] = default;

        if (!IsHoldingFlashlight() && FlashlightActive.Value)
        {
            FlashlightActive.Value = false;
        }
    }

    /// <summary>
    /// Returns true if the currently selected hotbar slot holds the flashlight.
    /// </summary>
    public bool IsHoldingFlashlight()
    {
        ItemData selected = GetSelectedItem();
        return selected != null && selected.ItemId == "flashlight";
    }

    /// <summary>
    /// True only when the flashlight is toggled on AND actively held in the selected slot.
    /// </summary>
    public bool IsFlashlightShining
    {
        get
        {
            if (!IsHoldingFlashlight())
            {
                return false;
            }

            if (IsOwner && flashlightLight != null)
            {
                return flashlightLight.enabled;
            }

            return FlashlightActive.Value;
        }
    }

    /// <summary>
    /// Checks whether the inventory contains at least one item with the specified itemId.
    /// </summary>
    public bool HasItem(string itemId)
    {
        if (Slots == null)
        {
            return false;
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            if (!Slots[i].IsEmpty && Slots[i].itemId.ToString() == itemId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks whether all inventory slots are empty.
    /// </summary>
    public bool IsEmpty()
    {
        if (Slots == null)
        {
            return true;
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            if (!Slots[i].IsEmpty)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Toggles the tactical flashlight beam mounted to the player's camera.
    /// Synchronized across all clients via NetworkVariable.
    /// </summary>
    public void ToggleFlashlight()
    {
        if (!IsOwner || !IsHoldingFlashlight())
        {
            return;
        }

        bool currentlyLit = flashlightLight != null ? flashlightLight.enabled : FlashlightActive.Value;
        bool nextState = !currentlyLit;
        ApplyFlashlightVisuals(nextState);
        SetFlashlightActiveServerRpc(nextState, SelectedSlotIndex.Value);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetFlashlightActiveServerRpc(bool desiredState, int slotIndex)
    {
        if (!desiredState || Slots == null || slotIndex < 0 || slotIndex >= Slots.Count)
        {
            FlashlightActive.Value = false;
            return;
        }

        InventorySlot slot = Slots[slotIndex];
        bool slotHasFlashlight = !slot.IsEmpty && slot.itemId.ToString() == "flashlight";
        FlashlightActive.Value = slotHasFlashlight;
    }

    private void OnFlashlightActiveChanged(bool previous, bool current)
    {
        ApplyFlashlightVisuals(current);
    }

    private void RefreshFlashlightState()
    {
        if (!IsHoldingFlashlight())
        {
            if (IsServer && FlashlightActive.Value)
            {
                FlashlightActive.Value = false;
            }
            ApplyFlashlightVisuals(false);
        }
        else
        {
            ApplyFlashlightVisuals(FlashlightActive.Value);
        }
    }

    private void ApplyFlashlightVisuals(bool active)
    {
        bool shouldShine = active && IsHoldingFlashlight();

        if (IsOwner)
        {
            if (shouldShine || flashlightLight != null)
            {
                EnsureLocalFlashlight();
            }

            if (flashlightLight != null)
            {
                flashlightLight.enabled = shouldShine;
            }
            if (flashlightSoftFill != null)
            {
                flashlightSoftFill.enabled = shouldShine;
            }
            if (shouldShine)
            {
                UpdateLocalFlashlightPose();
            }
        }
        else
        {
            if (shouldShine || thirdPersonFlashlight != null)
            {
                EnsureRemoteFlashlight();
            }

            if (thirdPersonFlashlight != null)
            {
                thirdPersonFlashlight.SetActive(shouldShine);
                if (thirdPersonFlashlightSpot != null)
                {
                    thirdPersonFlashlightSpot.enabled = shouldShine;
                }
                if (thirdPersonFlashlightGlow != null)
                {
                    thirdPersonFlashlightGlow.enabled = shouldShine;
                }
                if (shouldShine)
                {
                    UpdateRemoteFlashlightPose();
                }
            }

            UpdateThirdPersonLensEmission(shouldShine);
        }
    }

    private void EnsureLocalFlashlight()
    {
        if (flashlightLight == null)
        {
            if (ownerCamera == null)
            {
                ownerCamera = GetComponentInChildren<Camera>(true);
            }

            if (ownerCamera != null)
            {
                Transform existing = ownerCamera.transform.Find("FlashlightBeam");
                if (existing != null)
                {
                    flashlightLight = existing.GetComponent<Light>();
                    Transform fill = existing.Find("FlashlightFill");
                    if (fill != null)
                    {
                        flashlightSoftFill = fill.GetComponent<Light>();
                    }
                }
                else
                {
                    GameObject lightGo = new GameObject("FlashlightBeam");
                    lightGo.transform.SetParent(ownerCamera.transform, false);
                    lightGo.transform.localPosition = new Vector3(0.28f, -0.265f, 0.775f);
                    lightGo.transform.localRotation = Quaternion.identity;

                    flashlightLight = lightGo.AddComponent<Light>();
                    flashlightLight.type = LightType.Spot;
                    flashlightLight.range = 34f;
                    flashlightLight.spotAngle = 56f;
                    flashlightLight.innerSpotAngle = 22f;
                    flashlightLight.intensity = 14f;
                    flashlightLight.color = new Color(1f, 0.96f, 0.88f);
                    flashlightLight.shadows = LightShadows.Hard;

                    GameObject fillGo = new GameObject("FlashlightFill");
                    fillGo.transform.SetParent(lightGo.transform, false);
                    fillGo.transform.localPosition = Vector3.zero;
                    flashlightSoftFill = fillGo.AddComponent<Light>();
                    flashlightSoftFill.type = LightType.Point;
                    flashlightSoftFill.range = 4.0f;
                    flashlightSoftFill.intensity = 0.8f;
                    flashlightSoftFill.color = new Color(1f, 0.95f, 0.85f);
                    flashlightSoftFill.shadows = LightShadows.None;
                }
            }
        }
    }

    private void EnsureRemoteFlashlight()
    {
        if (thirdPersonFlashlight == null)
        {
            Transform holder = transform.Find("CameraHolder");
            bool hasCameraHolder = holder != null;
            if (!hasCameraHolder)
            {
                holder = transform;
            }

            Transform existing = holder.Find("RemoteFlashlightBeam");
            if (existing != null)
            {
                thirdPersonFlashlight = existing.gameObject;
                thirdPersonFlashlightSpot = existing.GetComponent<Light>();
                Transform glow = existing.Find("RemoteFlashlightGlow");
                if (glow != null)
                {
                    thirdPersonFlashlightGlow = glow.GetComponent<Light>();
                }
            }
            else
            {
                thirdPersonFlashlight = new GameObject("RemoteFlashlightBeam");
                thirdPersonFlashlight.transform.SetParent(holder, false);
                thirdPersonFlashlight.transform.localPosition = hasCameraHolder
                    ? new Vector3(0.28f, -0.10f, 0.58f)
                    : new Vector3(0.28f, 1.10f, 0.58f);
                thirdPersonFlashlight.transform.localRotation = Quaternion.identity;

                thirdPersonFlashlightSpot = thirdPersonFlashlight.AddComponent<Light>();
                thirdPersonFlashlightSpot.type = LightType.Spot;
                thirdPersonFlashlightSpot.range = 34f;
                thirdPersonFlashlightSpot.spotAngle = 56f;
                thirdPersonFlashlightSpot.innerSpotAngle = 22f;
                thirdPersonFlashlightSpot.intensity = 14f;
                thirdPersonFlashlightSpot.color = new Color(1f, 0.96f, 0.88f);
                thirdPersonFlashlightSpot.shadows = LightShadows.Hard;

                // Subtle local glow so teammates visibly radiate light around their flashlight head
                GameObject glowGo = new GameObject("RemoteFlashlightGlow");
                glowGo.transform.SetParent(thirdPersonFlashlight.transform, false);
                glowGo.transform.localPosition = new Vector3(0f, 0f, -0.08f);
                thirdPersonFlashlightGlow = glowGo.AddComponent<Light>();
                thirdPersonFlashlightGlow.type = LightType.Point;
                thirdPersonFlashlightGlow.range = 5.0f;
                thirdPersonFlashlightGlow.intensity = 1.6f;
                thirdPersonFlashlightGlow.color = new Color(1f, 0.96f, 0.88f);
                thirdPersonFlashlightGlow.shadows = LightShadows.None;
            }
        }
    }

    private void UpdateLocalFlashlightPose()
    {
        if (flashlightLight == null)
        {
            return;
        }

        if (ownerCamera == null)
        {
            ownerCamera = GetComponentInChildren<Camera>(true);
            if (ownerCamera == null)
            {
                return;
            }
        }

        Vector3 origin = ownerCamera.transform.position;
        Vector3 desiredTip;
        if (viewmodel != null && viewmodel.TryGetFlashlightTipWorldPose(out Vector3 vmTip, out _))
        {
            desiredTip = vmTip;
        }
        else
        {
            desiredTip = ownerCamera.transform.TransformPoint(new Vector3(0.28f, -0.265f, 0.775f));
        }

        Vector3 clampedPos = ClampPositionBeforeWalls(origin, desiredTip);
        Vector3 aimTarget = origin + ownerCamera.transform.forward * 16f;
        Vector3 aimDir = aimTarget - clampedPos;
        if (aimDir.sqrMagnitude < 0.01f)
        {
            aimDir = ownerCamera.transform.forward;
        }

        flashlightLight.transform.position = clampedPos;
        flashlightLight.transform.rotation = Quaternion.LookRotation(aimDir.normalized, ownerCamera.transform.up);
    }

    private void UpdateRemoteFlashlightPose()
    {
        if (thirdPersonFlashlight == null)
        {
            return;
        }

        Transform holder = thirdPersonFlashlight.transform.parent;
        if (holder == null)
        {
            return;
        }

        bool isCamHolder = holder != transform;
        Vector3 localDefault = isCamHolder
            ? new Vector3(0.28f, -0.10f, 0.58f)
            : new Vector3(0.28f, 1.10f, 0.58f);

        Vector3 origin = holder.position;
        Vector3 desiredTip = holder.TransformPoint(localDefault);
        thirdPersonFlashlight.transform.position = ClampPositionBeforeWalls(origin, desiredTip);
        thirdPersonFlashlight.transform.rotation = holder.rotation;
    }

    private Vector3 ClampPositionBeforeWalls(Vector3 origin, Vector3 desiredPos)
    {
        Vector3 delta = desiredPos - origin;
        float dist = delta.magnitude;
        if (dist < 0.001f)
        {
            return desiredPos;
        }

        Vector3 dir = delta / dist;
        int hitCount = Physics.SphereCastNonAlloc(origin, 0.06f, dir, flashlightWallHits, dist, ~0, QueryTriggerInteraction.Ignore);
        float closestDist = dist;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = flashlightWallHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < closestDist)
            {
                closestDist = hit.distance;
            }
        }

        if (closestDist < dist)
        {
            return origin + dir * Mathf.Max(0.12f, closestDist - 0.05f);
        }

        return desiredPos;
    }

    /// <summary>
    /// Uses the currently equipped item (e.g. consuming a medkit or toggling a flashlight).
    /// Returns true if an action was executed.
    /// </summary>
    public bool UseSelectedItem()
    {
        ItemData item = GetSelectedItem();
        if (item == null)
        {
            return false;
        }

        if (item.ItemId == "medkit")
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health != null && health.CurrentHealth.Value >= health.MaxHealth)
            {
                return false;
            }

            UseConsumableServerRpc(SelectedSlotIndex.Value);
            return true;
        }
        else if (item.ItemId == "flashlight")
        {
            ToggleFlashlight();
            return true;
        }

        return false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UseConsumableServerRpc(int slotIndex)
    {
        if (!IsServer || Slots == null || slotIndex < 0 || slotIndex >= Slots.Count)
        {
            return;
        }

        InventorySlot slot = Slots[slotIndex];
        if (slot.IsEmpty)
        {
            return;
        }

        string id = slot.itemId.ToString();
        if (id == "medkit")
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health != null && health.Heal(50))
            {
                slot.count--;
                if (slot.count <= 0)
                {
                    Slots[slotIndex] = default;
                }
                else
                {
                    Slots[slotIndex] = slot;
                }
                ItemUseFeedbackClientRpc("Applied First Aid Kit! (+50 HP)", true);
            }
            else
            {
                ItemUseFeedbackClientRpc("Health is already full!", false);
            }
        }
    }

    [Rpc(SendTo.Owner)]
    private void ItemUseFeedbackClientRpc(string message, bool success)
    {
        Debug.Log($"[PlayerInventory] {message}");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void BuyItemServerRpc(string itemId)
    {
        if (!IsServer || string.IsNullOrEmpty(itemId))
        {
            return;
        }

        ItemData item = ItemRegistry.GetItem(itemId);
        if (item == null)
        {
            return;
        }

        PlayerWallet wallet = GetComponent<PlayerWallet>();
        if (wallet == null)
        {
            return;
        }

        if (!HasFreeSlot())
        {
            BuyFeedbackClientRpc("Inventory full! Drop an item [Q] first.", false);
            return;
        }

        if (!wallet.TrySpendBankedMoney(item.BuyPrice))
        {
            BuyFeedbackClientRpc($"Not enough crew funds! Need ${item.BuyPrice} in company bank.", false);
            return;
        }

        AddItem(itemId, 1);
        int remainingFunds = GameManager.Instance != null ? GameManager.Instance.GroupCredits.Value : wallet.BankedMoney.Value;
        BuyFeedbackClientRpc($"Purchased {item.DisplayName} for ${item.BuyPrice}! Remaining Crew Funds: ${remainingFunds}", true);
    }

    [Rpc(SendTo.Owner)]
    private void BuyFeedbackClientRpc(string message, bool success)
    {
        ArmoryShopUI.ShowFeedback(message, success);
        if (success)
        {
            AudioClip clink = ProceduralWeaponAudio.GetCoinClinkClip();
            if (clink != null)
            {
                AudioSource.PlayClipAtPoint(clink, transform.position, 0.9f);
            }
        }
    }

    /// <summary>
    /// Calculates total dollar value of all monster scrap carried across hotbar slots.
    /// </summary>
    public int GetCarriedScrapValue()
    {
        if (Slots == null)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].IsEmpty)
            {
                continue;
            }

            ItemData item = ItemRegistry.GetItem(Slots[i].itemId.ToString());
            if (item != null && item.Type == ItemType.Scrap)
            {
                total += item.BuyPrice * Slots[i].count;
            }
        }

        return total;
    }

    /// <summary>
    /// Calculates total number of scrap items carried across hotbar slots.
    /// </summary>
    public int GetCarriedScrapCount()
    {
        if (Slots == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].IsEmpty)
            {
                continue;
            }

            ItemData item = ItemRegistry.GetItem(Slots[i].itemId.ToString());
            if (item != null && item.Type == ItemType.Scrap)
            {
                count += Slots[i].count;
            }
        }

        return count;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SellItemServerRpc(int slotIndex)
    {
        if (!IsServer || Slots == null || slotIndex < 0 || slotIndex >= Slots.Count)
        {
            return;
        }

        InventorySlot slot = Slots[slotIndex];
        if (slot.IsEmpty)
        {
            return;
        }

        ItemData item = ItemRegistry.GetItem(slot.itemId.ToString());
        if (item == null)
        {
            return;
        }

        PlayerWallet wallet = GetComponent<PlayerWallet>();
        if (wallet == null)
        {
            return;
        }

        int payout = 0;
        if (item.Type == ItemType.Scrap)
        {
            payout = item.BuyPrice * slot.count;
        }
        else
        {
            // 50% salvage resale value for weapons, tools, and consumables
            payout = Mathf.Max(5, Mathf.RoundToInt(item.BuyPrice * 0.5f * slot.count));
        }

        Slots[slotIndex] = default;
        if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
        {
            GameManager.Instance.AddCrewCredits(payout, countsTowardsQuota: true);
        }
        else
        {
            wallet.BankedMoney.Value += payout;
            wallet.AddQuotaContribution(payout);
        }

        int totalBank = (GameManager.Instance != null && GameManager.Instance.IsSpawned) ? GameManager.Instance.GroupCredits.Value : wallet.BankedMoney.Value;
        Debug.Log($"[PlayerInventory] Player {OwnerClientId} sold {slot.count}x {item.DisplayName} for +${payout}. Total Crew Bank: ${totalBank}");
        SellFeedbackClientRpc($"Salvaged {slot.count}x {item.DisplayName} for +${payout}! Added to Crew Funds.", true);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SellAllScrapServerRpc()
    {
        if (!IsServer || Slots == null)
        {
            return;
        }

        PlayerWallet wallet = GetComponent<PlayerWallet>();
        if (wallet == null)
        {
            return;
        }

        int totalPayout = 0;
        int totalCount = 0;

        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].IsEmpty)
            {
                continue;
            }

            ItemData item = ItemRegistry.GetItem(Slots[i].itemId.ToString());
            if (item != null && item.Type == ItemType.Scrap)
            {
                totalPayout += item.BuyPrice * Slots[i].count;
                totalCount += Slots[i].count;
                Slots[i] = default;
            }
        }

        if (totalPayout > 0)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            {
                GameManager.Instance.AddCrewCredits(totalPayout, countsTowardsQuota: true);
            }
            else
            {
                wallet.BankedMoney.Value += totalPayout;
                wallet.AddQuotaContribution(totalPayout);
            }

            int totalBank = (GameManager.Instance != null && GameManager.Instance.IsSpawned) ? GameManager.Instance.GroupCredits.Value : wallet.BankedMoney.Value;
            Debug.Log($"[PlayerInventory] Player {OwnerClientId} sold ALL scrap ({totalCount} items) for +${totalPayout}. Total Crew Bank: ${totalBank}");
            SellFeedbackClientRpc($"Salvaged {totalCount} monster scraps for +${totalPayout}! Added to Crew Funds.", true);
        }
        else
        {
            SellFeedbackClientRpc("No monster scrap in inventory to salvage!", false);
        }
    }

    [Rpc(SendTo.Owner)]
    private void SellFeedbackClientRpc(string message, bool success)
    {
        ArmoryShopUI.ShowFeedback(message, success);
        if (success)
        {
            AudioClip clink = ProceduralWeaponAudio.GetCoinClinkClip();
            if (clink != null)
            {
                AudioSource.PlayClipAtPoint(clink, transform.position, 0.95f);
            }
        }
    }
}
