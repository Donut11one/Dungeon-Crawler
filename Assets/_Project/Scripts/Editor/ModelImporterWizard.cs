#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility window for one-click setup of FBX 3D models into the game.
/// Scans Assets/_Project/Art/Models and offers automated wiring for:
/// 1. Physical Scrap & Item Pickups (creates prefab + ItemData ScriptableObject)
/// 2. Monster & Abnormality Visuals (parents to monster prefab)
/// 3. Modular Environment Props (creates static prefab with colliders)
/// </summary>
public class ModelImporterWizard : EditorWindow
{
    private const string ModelsRoot = "Assets/_Project/Art/Models";
    private const string PrefabsItemsRoot = "Assets/_Project/Prefabs/Items";
    private const string ScriptableObjectsItemsRoot = "Assets/_Project/ScriptableObjects/Items";

    private Vector2 scrollPos;
    private int selectedCategoryTab = 0;
    private readonly string[] categoryTabs = new string[] { "All Models", "Items / Scrap", "Characters / Monsters", "Environment" };

    [MenuItem("Tools/Dungeon Crawler/Model Importer Wizard")]
    public static void OpenWindow()
    {
        ModelImporterWizard window = GetWindow<ModelImporterWizard>("Model Importer");
        window.minSize = new Vector2(500f, 400f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("📦 3D Model Importer & Game Integrator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drop your .fbx files into Assets/_Project/Art/Models/ (or its subfolders). " +
            "This tool detects imported models and wires them into game prefabs and ScriptableObjects with one click.",
            MessageType.Info
        );

        EditorGUILayout.Space(5);
        selectedCategoryTab = GUILayout.Toolbar(selectedCategoryTab, categoryTabs);
        EditorGUILayout.Space(10);

        if (!Directory.Exists(ModelsRoot))
        {
            EditorGUILayout.HelpBox($"Models directory '{ModelsRoot}' not found.", MessageType.Warning);
            if (GUILayout.Button("Create Folder Structure"))
            {
                EnsureDirectory(ModelsRoot + "/Characters");
                EnsureDirectory(ModelsRoot + "/Items");
                EnsureDirectory(ModelsRoot + "/Environment");
                AssetDatabase.Refresh();
            }
            return;
        }

        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new string[] { ModelsRoot });

        if (fbxGuids == null || fbxGuids.Length == 0)
        {
            EditorGUILayout.HelpBox(
                $"No 3D models found in '{ModelsRoot}'.\n\n" +
                "Place your .fbx, .obj, or .blend files into:\n" +
                "• Assets/_Project/Art/Models/Items/\n" +
                "• Assets/_Project/Art/Models/Characters/\n" +
                "• Assets/_Project/Art/Models/Environment/",
                MessageType.Warning
            );

            if (GUILayout.Button("📂 Open Models Folder in Explorer", GUILayout.Height(30)))
            {
                EditorUtility.RevealInFinder(ModelsRoot);
            }
            return;
        }

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        for (int i = 0; i < fbxGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(fbxGuids[i]);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            string fileName = Path.GetFileName(path);
            bool isItem = path.Contains("/Items");
            bool isChar = path.Contains("/Characters");
            bool isEnv = path.Contains("/Environment");

            if (selectedCategoryTab == 1 && !isItem)
            {
                continue;
            }
            if (selectedCategoryTab == 2 && !isChar)
            {
                continue;
            }
            if (selectedCategoryTab == 3 && !isEnv)
            {
                continue;
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (modelAsset == null)
            {
                continue;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"📄 {fileName}", EditorStyles.boldLabel, GUILayout.Width(220));
            EditorGUILayout.LabelField(path.Replace(ModelsRoot + "/", ""), EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("✨ Create Scrap Item Prefab", GUILayout.Height(24)))
            {
                CreateItemPrefabFromModel(modelAsset, path, ItemType.Scrap);
            }

            if (GUILayout.Button("⚔️ Create Weapon Prefab", GUILayout.Height(24)))
            {
                CreateItemPrefabFromModel(modelAsset, path, ItemType.Weapon);
            }

            if (GUILayout.Button("🏛️ Create Prop Prefab", GUILayout.Height(24)))
            {
                CreateEnvironmentPropPrefab(modelAsset, path);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);
        if (GUILayout.Button("🔄 Refresh Model List", GUILayout.Height(28)))
        {
            AssetDatabase.Refresh();
        }
    }

    private static void CreateItemPrefabFromModel(GameObject modelAsset, string assetPath, ItemType itemType)
    {
        EnsureDirectory(PrefabsItemsRoot);
        EnsureDirectory(ScriptableObjectsItemsRoot);

        string cleanName = modelAsset.name.Replace(" ", "_");
        string prefabPath = $"{PrefabsItemsRoot}/{cleanName}_Prefab.prefab";
        string soPath = $"{ScriptableObjectsItemsRoot}/{cleanName}_Data.asset";

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        instance.name = cleanName;

        // Ensure root has a visual container and physics collider
        if (instance.GetComponent<Collider>() == null)
        {
            // Add a convex mesh collider or box collider
            MeshFilter mf = instance.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                MeshCollider mc = instance.AddComponent<MeshCollider>();
                mc.convex = true;
            }
            else
            {
                instance.AddComponent<BoxCollider>();
            }
        }

        // Save as Prefab
        bool success;
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out success);
        DestroyImmediate(instance);

        if (!success || savedPrefab == null)
        {
            Debug.LogError($"[ModelImporterWizard] Failed to save prefab at {prefabPath}");
            return;
        }

        // Create ItemData ScriptableObject
        ItemData itemData = ScriptableObject.CreateInstance<ItemData>();
        string itemId = cleanName.ToLower();

        // Populate fields via SerializedObject to support serialized private fields
        SerializedObject so = new SerializedObject(itemData);
        so.FindProperty("itemId").stringValue = itemId;
        so.FindProperty("displayName").stringValue = ObjectNames.NicifyVariableName(cleanName);
        so.FindProperty("description").stringValue = $"Salvaged {ObjectNames.NicifyVariableName(cleanName)} from subterranean depths.";
        so.FindProperty("itemType").enumValueIndex = (int)itemType;
        so.FindProperty("worldPrefab").objectReferenceValue = savedPrefab;
        so.FindProperty("equippedPrefab").objectReferenceValue = savedPrefab;
        so.FindProperty("buyPrice").intValue = itemType == ItemType.Scrap ? 85 : 50;
        so.FindProperty("slotColor").colorValue = itemType == ItemType.Scrap ? new Color(1f, 0.75f, 0.2f) : new Color(0.8f, 0.3f, 0.3f);
        so.ApplyModifiedProperties();

        AssetDatabase.CreateAsset(itemData, soPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ModelImporterWizard] Successfully created item prefab at '{prefabPath}' and ItemData at '{soPath}'!");
        Selection.activeObject = itemData;
    }

    private static void CreateEnvironmentPropPrefab(GameObject modelAsset, string assetPath)
    {
        string propsRoot = "Assets/_Project/Prefabs/Dungeon/Props";
        EnsureDirectory(propsRoot);

        string cleanName = modelAsset.name.Replace(" ", "_");
        string prefabPath = $"{propsRoot}/{cleanName}_Prop.prefab";

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        instance.name = cleanName;

        // Ensure colliders on children
        MeshFilter[] meshFilters = instance.GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < meshFilters.Length; i++)
        {
            if (meshFilters[i].GetComponent<Collider>() == null)
            {
                MeshCollider mc = meshFilters[i].gameObject.AddComponent<MeshCollider>();
                mc.convex = false;
            }
        }

        bool success;
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out success);
        DestroyImmediate(instance);

        if (success)
        {
            Debug.Log($"[ModelImporterWizard] Created Environment Prop prefab at '{prefabPath}'!");
            AssetDatabase.Refresh();
        }
    }

    private static void EnsureDirectory(string relativePath)
    {
        if (!Directory.Exists(relativePath))
        {
            Directory.CreateDirectory(relativePath);
        }
    }
}
#endif
