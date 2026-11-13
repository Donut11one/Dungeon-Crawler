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

    public override void OnNetworkSpawn()
    {
        ItemRegistry.Initialize();

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
