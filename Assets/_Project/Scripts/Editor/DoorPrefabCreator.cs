using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Editor utility: ensures DoorPrefab exists and is registered in DefaultNetworkPrefabs.
/// Runs automatically on editor load or via Tools > Dungeon Crawler > Create Door Prefab.
/// </summary>
public static class DoorPrefabCreator
{
    public const string DoorPrefabPath = "Assets/_Project/Prefabs/Dungeon/DoorPrefab.prefab";
    private const string DefaultNetworkPrefabsPath = "Assets/_Project/Resources/DefaultNetworkPrefabs.asset";
    private const string LegacyNetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    [InitializeOnLoadMethod]
    private static void AutoEnsureSetup()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureDoorPrefab();
            RegisterInNetworkPrefabsList();
        };
    }

    [MenuItem("Tools/Dungeon Crawler/Create Door Prefab")]
    public static void CreateDoorPrefab()
    {
        EnsureDoorPrefab(true);
        RegisterInNetworkPrefabsList();
    }

    public static void EnsureDoorPrefab(bool forceRecreate = false)
    {
        if (!forceRecreate && AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath) != null)
        {
            return;
        }

        string dir = "Assets/_Project/Prefabs/Dungeon";
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
        }
        if (!AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Dungeon");
        }

        GameObject doorObj = new GameObject("DoorPrefab");

        doorObj.AddComponent<NetworkObject>();
        DungeonDoor door = doorObj.AddComponent<DungeonDoor>();
        door.EnsureComponentsAndVisuals();

        bool saved;
        PrefabUtility.SaveAsPrefabAsset(doorObj, DoorPrefabPath, out saved);
        Object.DestroyImmediate(doorObj);

        if (saved)
        {
            Debug.Log($"[DoorPrefabCreator] Created DoorPrefab at {DoorPrefabPath}");
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

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
        if (prefab == null)
        {
            return;
        }

        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab
            {
                Prefab = prefab
            });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DoorPrefabCreator] Registered DoorPrefab in {DefaultNetworkPrefabsPath}");
        }
    }
}
#endif
