using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// Lightweight, zero-GC unmanaged struct representing one hotbar inventory slot.
/// Fully compatible with NGO INetworkSerializable and NetworkList.
/// </summary>
[System.Serializable]
public struct InventorySlot : INetworkSerializable, System.IEquatable<InventorySlot>
{
    public FixedString32Bytes itemId;
    public int count;

    public bool IsEmpty
    {
        get
        {
            return count <= 0 || itemId.IsEmpty;
        }
    }

    public InventorySlot(string id, int amount = 1)
    {
        itemId = new FixedString32Bytes(id ?? string.Empty);
        count = amount;
    }

    public void Clear()
    {
        itemId = default;
        count = 0;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref count);
    }

    public bool Equals(InventorySlot other)
    {
        return itemId.Equals(other.itemId) && count == other.count;
    }

    public override bool Equals(object obj)
    {
        if (obj is InventorySlot other)
        {
            return Equals(other);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(itemId, count);
    }

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "[Empty]";
        }
        return $"{itemId} x{count}";
    }
}
