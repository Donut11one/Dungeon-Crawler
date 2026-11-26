using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Editor utility: ensures ItemPickupPrefab exists and is registered in DefaultNetworkPrefabs.
/// Runs automatically on editor load or via Tools > Dungeon Crawler > Create Item Pickup Prefab.
/// </summary>
public static class ItemPrefabCreator
{
    public const string ItemPickupPrefabPath = "Assets/_Project/Prefabs/Items/ItemPickupPrefab.prefab";
    private const string DefaultNetworkPrefabsPath = "Assets/_Project/Resources/DefaultNetworkPrefabs.asset";
    private const string LegacyNetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    [InitializeOnLoadMethod]
    private static void AutoEnsureSetup()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureItemPickupPrefab();
            RegisterInNetworkPrefabsList();
        };
    }

    [MenuItem("Tools/Dungeon Crawler/Create Item Pickup Prefab")]
    public static void CreateItemPickupPrefab()
    {
        EnsureItemPickupPrefab(true);
        RegisterInNetworkPrefabsList();
    }

    public static void EnsureItemPickupPrefab(bool forceRecreate = false)
    {
        if (!forceRecreate && AssetDatabase.LoadAssetAtPath<GameObject>(ItemPickupPrefabPath) != null)
        {
            return;
        }

        string dir = "Assets/_Project/Prefabs/Items";
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
        }
        if (!AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Items");
        }

        GameObject itemObj = new GameObject("ItemPickupPrefab");

        SphereCollider sc = itemObj.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 0.75f;

        itemObj.AddComponent<NetworkObject>();
        itemObj.AddComponent<WorldItemPickup>();

        bool saved;
        PrefabUtility.SaveAsPrefabAsset(itemObj, ItemPickupPrefabPath, out saved);
        Object.DestroyImmediate(itemObj);

        if (saved)
        {
            Debug.Log($"[ItemPrefabCreator] Created ItemPickupPrefab at {ItemPickupPrefabPath}");
            AssetDatabase.Refresh();
        }
    }

    public static void RegisterInNetworkPrefabsList()
    {
        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(DefaultNetworkPrefabsPath);
        if (list == null)
        {
            list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(LegacyNetworkPrefabsPath);
        }
        if (list == null)
        {
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPickupPrefabPath);
        if (prefab == null)
        {
            return;
        }

        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ItemPrefabCreator] Registered ItemPickupPrefab in {DefaultNetworkPrefabsPath}");
        }
    }
}
#endif
