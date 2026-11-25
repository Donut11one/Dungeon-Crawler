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

    private PlayerAttack playerAttack;

    private void Awake()
    {
        playerAttack = GetComponent<PlayerAttack>();

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
        }

        UpdateThirdPersonWeaponVisual(SelectedSlotIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        SelectedSlotIndex.OnValueChanged -= OnSelectedSlotChanged;
    }

    private void OnSelectedSlotChanged(int previous, int current)
    {
        UpdateThirdPersonWeaponVisual(current);
    }

    private void UpdateThirdPersonWeaponVisual(int slotIndex)
    {
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

        ItemData active = GetSelectedItem();
        if (active == null)
        {
            return;
        }

        currentThirdPersonVisual = new GameObject($"TP_{active.ItemId}");
        currentThirdPersonVisual.transform.parent = thirdPersonHolder;
        currentThirdPersonVisual.transform.localPosition = Vector3.zero;
        currentThirdPersonVisual.transform.localRotation = Quaternion.identity;

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
        }
    }

    private Transform FindHandBone(Transform current)
    {
        if (current.name == "Hand.R" || current.name == "Wrist.R")
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

    private void HandleHotbarSelectionInput()
    {
        if (Keyboard.current == null)
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
    }

    private Light flashlightLight;

    /// <summary>
    /// Toggles the tactical flashlight beam mounted to the player's camera.
    /// </summary>
    public void ToggleFlashlight()
    {
        if (flashlightLight == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                Transform existing = cam.transform.Find("FlashlightBeam");
                if (existing != null)
                {
                    flashlightLight = existing.GetComponent<Light>();
                }
                else
                {
                    GameObject lightGo = new GameObject("FlashlightBeam");
                    lightGo.transform.parent = cam.transform;
                    lightGo.transform.localPosition = new Vector3(0.2f, -0.15f, 0.3f);
                    lightGo.transform.localRotation = Quaternion.identity;
                    flashlightLight = lightGo.AddComponent<Light>();
                    flashlightLight.type = LightType.Spot;
                    flashlightLight.range = 28f;
                    flashlightLight.spotAngle = 48f;
                    flashlightLight.innerSpotAngle = 32f;
                    flashlightLight.intensity = 2.8f;
                    flashlightLight.color = new Color(1f, 0.96f, 0.85f);
                    flashlightLight.shadows = LightShadows.Hard;
                }
            }
        }

        if (flashlightLight != null)
        {
            flashlightLight.enabled = !flashlightLight.enabled;
        }
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
            BuyFeedbackClientRpc($"Not enough money! Need ${item.BuyPrice} in bank.", false);
            return;
        }

        AddItem(itemId, 1);
        BuyFeedbackClientRpc($"Purchased {item.DisplayName} for ${item.BuyPrice}!", true);
    }

    [Rpc(SendTo.Owner)]
    private void BuyFeedbackClientRpc(string message, bool success)
    {
        ArmoryShopUI.ShowFeedback(message, success);
    }
}
