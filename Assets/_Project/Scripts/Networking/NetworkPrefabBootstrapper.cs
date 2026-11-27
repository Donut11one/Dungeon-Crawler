using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Runtime bootstrapper that guarantees all network prefabs (enemies, items, doors, levers, player)
/// are loaded from Resources and registered with NetworkManager in both Editor and Standalone Builds.
/// Runs automatically before and after scene load, and is invoked defensively by LobbyUI and DungeonBuilder.
/// </summary>
public static class NetworkPrefabBootstrapper
{
    public static bool IsRegistered { get; private set; } = false;
    private static NetworkPrefabsList cachedList = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void OnBeforeSceneLoad()
    {
        IsRegistered = false;
        cachedList = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnAfterSceneLoad()
    {
        EnsureAllPrefabsRegistered();
    }

    /// <summary>
    /// Loads the DefaultNetworkPrefabs list asset from Resources or AssetDatabase fallback.
    /// </summary>
    public static NetworkPrefabsList LoadNetworkPrefabsList()
    {
        if (cachedList != null)
        {
            return cachedList;
        }

        // 1. Try Resources (works in Standalone Builds and Editor)
        cachedList = Resources.Load<NetworkPrefabsList>("DefaultNetworkPrefabs");
        if (cachedList != null)
        {
            return cachedList;
        }

        cachedList = Resources.Load<NetworkPrefabsList>("Network/DefaultNetworkPrefabs");
        if (cachedList != null)
        {
            return cachedList;
        }

#if UNITY_EDITOR
        // 2. Editor AssetDatabase fallback
        cachedList = UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
            "Assets/_Project/Resources/DefaultNetworkPrefabs.asset"
        );
        if (cachedList != null)
        {
            return cachedList;
        }

        cachedList = UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
            "Assets/DefaultNetworkPrefabs.asset"
        );
        if (cachedList != null)
        {
            return cachedList;
        }
#endif

        return cachedList;
    }

    /// <summary>
    /// Ensures all network prefabs are registered with NetworkManager.Singleton before listening begins.
    /// Safe to call multiple times; ignores duplicates and avoids duplicate GlobalObjectIdHash registrations.
    /// </summary>
    public static void EnsureAllPrefabsRegistered()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            // Netcode forbids modifying prefab lists while listening
            return;
        }

        NetworkPrefabsList list = LoadNetworkPrefabsList();
        if (list == null)
        {
            Debug.LogWarning("[NetworkPrefabBootstrapper] DefaultNetworkPrefabs asset could not be loaded from Resources!");
            return;
        }

        // Clean any duplicates or null entries from list at runtime
        DeduplicateList(list);

        if (NetworkManager.Singleton.NetworkConfig.Prefabs == null)
        {
            NetworkManager.Singleton.NetworkConfig.Prefabs = new NetworkPrefabs();
        }

        if (NetworkManager.Singleton.NetworkConfig.Prefabs.NetworkPrefabsLists == null)
        {
            NetworkManager.Singleton.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList>();
        }

        if (!NetworkManager.Singleton.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list))
        {
            NetworkManager.Singleton.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        }

        // Ensure PlayerPrefab is assigned on NetworkConfig if not already set
        if (NetworkManager.Singleton.NetworkConfig.PlayerPrefab == null)
        {
            GameObject playerPrefab = GetPlayerPrefab();
            if (playerPrefab != null)
            {
                NetworkManager.Singleton.NetworkConfig.PlayerPrefab = playerPrefab;
            }
        }

        IsRegistered = true;
    }

    /// <summary>
    /// Strips any duplicate prefabs or entries with identical GlobalObjectIdHash from a NetworkPrefabsList.
    /// </summary>
    public static void DeduplicateList(NetworkPrefabsList list)
    {
        if (list == null || list.PrefabList == null)
        {
            return;
        }

        HashSet<uint> seenHashes = new HashSet<uint>();
        HashSet<GameObject> seenPrefabs = new HashSet<GameObject>();
        List<NetworkPrefab> toRemove = new List<NetworkPrefab>();

        for (int i = 0; i < list.PrefabList.Count; i++)
        {
            NetworkPrefab entry = list.PrefabList[i];
            if (entry == null || entry.Prefab == null)
            {
                toRemove.Add(entry);
                continue;
            }

            if (seenPrefabs.Contains(entry.Prefab))
            {
                toRemove.Add(entry);
                continue;
            }

            uint hash = entry.SourcePrefabGlobalObjectIdHash;
            if (hash != 0)
            {
                if (seenHashes.Contains(hash))
                {
                    toRemove.Add(entry);
                    continue;
                }
                seenHashes.Add(hash);
            }

            seenPrefabs.Add(entry.Prefab);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            list.Remove(toRemove[i]);
        }
    }

    /// <summary>
    /// Checks whether a prefab is already registered in NetworkPrefabs or DefaultNetworkPrefabs.
    /// </summary>
    public static bool IsPrefabRegistered(GameObject prefab)
    {
        if (prefab == null)
        {
            return false;
        }

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return false;
        }

        NetworkPrefabs prefabs = NetworkManager.Singleton.NetworkConfig.Prefabs;
        if (prefabs == null)
        {
            return false;
        }

        // 1. Check inline prefab list
        if (prefabs.Contains(prefab))
        {
            return true;
        }

        // 2. Check NetworkPrefabsLists
        if (prefabs.NetworkPrefabsLists != null)
        {
            for (int i = 0; i < prefabs.NetworkPrefabsLists.Count; i++)
            {
                NetworkPrefabsList pList = prefabs.NetworkPrefabsLists[i];
                if (pList != null && pList.Contains(prefab))
                {
                    return true;
                }
            }
        }

        // 3. Check loaded DefaultNetworkPrefabs list asset
        NetworkPrefabsList defaultList = LoadNetworkPrefabsList();
        if (defaultList != null && defaultList.Contains(prefab))
        {
            return true;
        }

        // 4. Check Hash collisions if registered
        NetworkObject netObj = prefab.GetComponent<NetworkObject>();
        if (netObj != null && netObj.PrefabIdHash != 0)
        {
            uint hash = netObj.PrefabIdHash;
            if (prefabs.NetworkPrefabOverrideLinks != null && prefabs.NetworkPrefabOverrideLinks.ContainsKey(hash))
            {
                return true;
            }

            if (defaultList != null && defaultList.PrefabList != null)
            {
                for (int i = 0; i < defaultList.PrefabList.Count; i++)
                {
                    NetworkPrefab entry = defaultList.PrefabList[i];
                    if (entry != null && entry.SourcePrefabGlobalObjectIdHash == hash)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Safely registers a prefab only if it is not already registered in NetworkPrefabs or DefaultNetworkPrefabs.
    /// Prevents duplicate GlobalObjectIdHash runtime errors.
    /// </summary>
    public static void RegisterPrefabIfMissing(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            return;
        }

        if (!IsPrefabRegistered(prefab))
        {
            NetworkManager.Singleton.AddNetworkPrefab(prefab);
        }
    }

    /// <summary>
    /// Retrieves the player prefab from DefaultNetworkPrefabs.
    /// </summary>
    public static GameObject GetPlayerPrefab()
    {
        NetworkPrefabsList list = LoadNetworkPrefabsList();
        if (list == null || list.PrefabList == null)
        {
            return null;
        }

        for (int i = 0; i < list.PrefabList.Count; i++)
        {
            NetworkPrefab entry = list.PrefabList[i];
            if (entry != null && entry.Prefab != null)
            {
                if (entry.Prefab.name == "PlayerPrefab" || entry.Prefab.GetComponent<PlayerController>() != null)
                {
                    return entry.Prefab;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Retrieves all enemy prefabs found in the DefaultNetworkPrefabs asset.
    /// </summary>
    public static List<GameObject> GetLoadedEnemyPrefabs()
    {
        List<GameObject> enemies = new List<GameObject>();
        NetworkPrefabsList list = LoadNetworkPrefabsList();
        if (list == null || list.PrefabList == null)
        {
            return enemies;
        }

        for (int i = 0; i < list.PrefabList.Count; i++)
        {
            NetworkPrefab entry = list.PrefabList[i];
            if (entry != null && entry.Prefab != null)
            {
                if (entry.Prefab.GetComponent<EnemyHealth>() != null)
                {
                    if (!enemies.Contains(entry.Prefab))
                    {
                        enemies.Add(entry.Prefab);
                    }
                }
            }
        }

        return enemies;
    }

    /// <summary>
    /// Retrieves the generic base enemy prefab (EnemyPrefab) from DefaultNetworkPrefabs.
    /// </summary>
    public static GameObject GetBaseEnemyPrefab()
    {
        NetworkPrefabsList list = LoadNetworkPrefabsList();
        if (list == null || list.PrefabList == null)
        {
            return null;
        }

        GameObject fallback = null;
        for (int i = 0; i < list.PrefabList.Count; i++)
        {
            NetworkPrefab entry = list.PrefabList[i];
            if (entry != null && entry.Prefab != null && entry.Prefab.GetComponent<EnemyHealth>() != null)
            {
                if (entry.Prefab.name == "EnemyPrefab")
                {
                    return entry.Prefab;
                }
                if (fallback == null)
                {
                    fallback = entry.Prefab;
                }
            }
        }

        return fallback;
    }
}
