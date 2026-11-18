#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch and menu utility to ensure all dungeon assets, prefabs, and default layouts
/// are generated and properly registered.
/// </summary>
public static class DungeonSetupBatch
{
    [MenuItem("Tools/Dungeon Crawler/Run Full Dungeon Setup", false, 0)]
    public static void SetupAll()
    {
        Debug.Log("[DungeonSetupBatch] Starting full setup...");

        DoorPrefabCreator.EnsureDoorPrefab(true);
        DoorPrefabCreator.RegisterInNetworkPrefabsList();

        string dir = "Assets/_Project/ScriptableObjects/Dungeon";
        if (!AssetDatabase.IsValidFolder("Assets/_Project/ScriptableObjects"))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
        }
        if (!AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Dungeon");
        }

        string defaultLayoutPath = $"{dir}/DefaultDungeonLayout.asset";
        DungeonLayoutData layout = AssetDatabase.LoadAssetAtPath<DungeonLayoutData>(defaultLayoutPath);
        if (layout == null)
        {
            layout = DungeonLayoutData.CreateDefaultFacilityLayoutAsset(defaultLayoutPath);
            Debug.Log($"[DungeonSetupBatch] Created default layout asset at {defaultLayoutPath}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DungeonSetupBatch] Full setup complete!");
    }
}
#endif
