using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Editor utility: ensures LeverPrefab exists and is registered in DefaultNetworkPrefabs.
/// Runs automatically on editor load or via Tools > Dungeon Crawler > Create Lever Prefab.
/// </summary>
public static class LeverPrefabCreator
{
    public const string LeverPrefabPath = "Assets/_Project/Prefabs/Dungeon/LeverPrefab.prefab";
    private const string DefaultNetworkPrefabsPath = "Assets/_Project/Resources/DefaultNetworkPrefabs.asset";
    private const string LegacyNetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    [InitializeOnLoadMethod]
    private static void AutoEnsureSetup()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureLeverPrefab();
            RegisterInNetworkPrefabsList();
        };
    }

    [MenuItem("Tools/Dungeon Crawler/Create Lever Prefab")]
    public static void CreateLeverPrefab()
    {
        EnsureLeverPrefab(true);
        RegisterInNetworkPrefabsList();
    }

    public static void EnsureLeverPrefab(bool forceRecreate = false)
    {
        if (!forceRecreate && AssetDatabase.LoadAssetAtPath<GameObject>(LeverPrefabPath) != null)
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

        GameObject leverObj = new GameObject("LeverPrefab");

        BoxCollider bc = leverObj.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(0.5f, 0.65f, 0.45f);
        bc.center = new Vector3(0f, 0f, 0.15f);

        leverObj.AddComponent<NetworkObject>();
        DungeonLever lever = leverObj.AddComponent<DungeonLever>();
        lever.EnsureComponentsAndVisuals();

        bool saved;
        PrefabUtility.SaveAsPrefabAsset(leverObj, LeverPrefabPath, out saved);
        Object.DestroyImmediate(leverObj);

        if (saved)
        {
            Debug.Log($"[LeverPrefabCreator] Created LeverPrefab at {LeverPrefabPath}");
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

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeverPrefabPath);
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
            Debug.Log($"[LeverPrefabCreator] Registered LeverPrefab in {DefaultNetworkPrefabsPath}");
        }
    }
}
#endif
