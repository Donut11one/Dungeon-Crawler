using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime lookup of TreasureData by id. Clients only receive a treasure id over the network, so every
/// peer needs to be able to resolve it back to the asset. LootSpawner registers every treasure referenced by
/// its loot tables automatically; anything else can live in a Resources/Treasures folder to be discovered.
/// </summary>
public static class TreasureRegistry
{
    private const string ResourcesFolder = "Treasures";

    private static readonly Dictionary<string, TreasureData> treasuresById =
        new Dictionary<string, TreasureData>(System.StringComparer.Ordinal);
    private static bool resourcesScanned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        treasuresById.Clear();
        resourcesScanned = false;
    }

    public static int Count
    {
        get { return treasuresById.Count; }
    }

    /// <summary>Registers a treasure. Returns false for null/invalid assets or an id already used by another asset.</summary>
    public static bool Register(TreasureData treasure)
    {
        if (treasure == null)
        {
            return false;
        }

        string id = treasure.TreasureId;
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning($"[TreasureRegistry] Treasure '{treasure.name}' has no usable id and was not registered.", treasure);
            return false;
        }

        if (treasuresById.TryGetValue(id, out TreasureData existing) && existing != null)
        {
            if (existing != treasure)
            {
                Debug.LogWarning($"[TreasureRegistry] Duplicate treasure id '{id}' on '{treasure.name}' and '{existing.name}'. Keeping '{existing.name}'. Give every TreasureData a unique id.", treasure);
                return false;
            }
            return true;
        }

        treasuresById[id] = treasure;
        return true;
    }

    public static bool TryGet(string id, out TreasureData treasure)
    {
        treasure = null;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (treasuresById.TryGetValue(id, out treasure) && treasure != null)
        {
            return true;
        }

        if (!resourcesScanned)
        {
            resourcesScanned = true;
            TreasureData[] found = Resources.LoadAll<TreasureData>(ResourcesFolder);
            for (int i = 0; i < found.Length; i++)
            {
                Register(found[i]);
            }

            if (treasuresById.TryGetValue(id, out treasure) && treasure != null)
            {
                return true;
            }
        }

        treasure = null;
        return false;
    }
}
