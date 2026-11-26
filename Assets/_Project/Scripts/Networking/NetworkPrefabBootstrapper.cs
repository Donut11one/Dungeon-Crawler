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
    /// Safe to call multiple times; ignores duplicates.
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

        int registeredCount = 0;
        if (list.PrefabList != null)
        {
            for (int i = 0; i < list.PrefabList.Count; i++)
            {
                NetworkPrefab entry = list.PrefabList[i];
                if (entry != null && entry.Prefab != null)
                {
                    if (!NetworkManager.Singleton.NetworkConfig.Prefabs.Contains(entry.Prefab))
                    {
                        NetworkManager.Singleton.AddNetworkPrefab(entry.Prefab);
                        registeredCount++;
                    }
                }
            }
        }

        IsRegistered = true;
        if (registeredCount > 0)
        {
            Debug.Log($"[NetworkPrefabBootstrapper] Registered {registeredCount} prefabs from DefaultNetworkPrefabs into NetworkManager.");
        }
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
