#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor utility window for one-click setup of 3D models into the game.
/// Provides dedicated per-archetype management for Player and all distinct Enemy types:
/// 1. Player Characters (replaces capsule pill on PlayerPrefab)
/// 2. Distinct Enemies & Monsters (attach different models to different enemy prefabs)
/// 3. Physical Scrap & Item Pickups (creates prefab + ItemData ScriptableObject)
/// 4. Modular Environment Props (creates static prefab with colliders)
/// </summary>
public class ModelImporterWizard : EditorWindow
{
    private const string ModelsRoot = "Assets/_Project/Art/Models";
    private const string PrefabsItemsRoot = "Assets/_Project/Prefabs/Items";
    private const string PrefabsEntitiesRoot = "Assets/_Project/Prefabs/Entities";
    private const string ScriptableObjectsItemsRoot = "Assets/_Project/ScriptableObjects/Items";

    private static readonly string[] SupportedModelExtensions = new string[]
    {
        ".fbx",
        ".obj",
        ".blend",
        ".gltf",
        ".glb",
        ".dae"
    };

    private static readonly Dictionary<string, GameObject> customArchetypePrefabOverrides = new Dictionary<string, GameObject>();

    private Vector2 scrollPos;
    private int selectedCategoryTab = 0;
    private string searchFilter = "";
    private bool searchAllProject = false;
    private GameObject directDropModel = null;
    private bool showEnemyListFold = false;
    private int selectedEnemyArchetypeIndex = 0;

    private readonly string[] categoryTabs = new string[]
    {
        "All Models",
        "Characters / Players",
        "Enemies / Monsters",
        "Animations",
        "Items / Scrap",
        "Environment"
    };

    public struct EnemyArchetypeConfig
    {
        public string archetypeId;
        public string displayName;
        public string[] candidatePrefabFileNames;
        public string icon;
    }

    public static readonly EnemyArchetypeConfig[] StandardEnemyArchetypes = new EnemyArchetypeConfig[]
    {
        new EnemyArchetypeConfig
        {
            archetypeId = "basic_abnormality",
            displayName = "Containment Abnormality",
            candidatePrefabFileNames = new string[] { "EnemyPrefab.prefab", "Enemy_ContainmentAbnormality.prefab" },
            icon = "👹"
        },
        new EnemyArchetypeConfig
        {
            archetypeId = "flesh_brute",
            displayName = "Flesh Brute",
            candidatePrefabFileNames = new string[] { "Enemy_FleshBrute.prefab" },
            icon = "🩸"
        },
        new EnemyArchetypeConfig
        {
            archetypeId = "scrap_scavenger",
            displayName = "Scrap Scavenger",
            candidatePrefabFileNames = new string[] { "Enemy_ScrapScavenger.prefab" },
            icon = "⚙️"
        },
        new EnemyArchetypeConfig
        {
            archetypeId = "shadow_stalker",
            displayName = "Shadow Prowler",
            candidatePrefabFileNames = new string[] { "Enemy_ShadowProwler.prefab", "Enemy_ShadowStalker.prefab" },
            icon = "👤"
        },
        new EnemyArchetypeConfig
        {
            archetypeId = "swarm_crawler",
            displayName = "Hive Swarmer",
            candidatePrefabFileNames = new string[] { "Enemy_HiveSwarmer.prefab", "Enemy_SwarmCrawler.prefab" },
            icon = "🐝"
        }
    };

    [MenuItem("Tools/Dungeon Crawler/Model Importer Wizard")]
    public static void OpenWindow()
    {
        ModelImporterWizard window = GetWindow<ModelImporterWizard>("Model Importer");
        window.minSize = new Vector2(700f, 560f);
        window.Show();
    }

    [MenuItem("Tools/Dungeon Crawler/Ensure All Enemy Prefabs Exist")]
    public static void EnsureAllEnemyPrefabsMenu()
    {
        EnsureAllEnemyPrefabsExist();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("📦 3D Model Importer & Game Integrator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Configure distinct 3D models for Player characters and each unique Enemy archetype. " +
            "Drop your .fbx, .obj, or .blend files into Assets/_Project/Art/Models/ to assign them with one click.",
            MessageType.Info
        );

        EditorGUILayout.Space(6);

        // -------------------------------------------------------------
        // SECTION 1: Target Prefabs & Per-Enemy Management
        // -------------------------------------------------------------
        DrawTargetPrefabsSection();

        EditorGUILayout.Space(8);

        // -------------------------------------------------------------
        // SECTION 2: Direct Model Drag & Drop Slot
        // -------------------------------------------------------------
        DrawDirectDropSection();

        EditorGUILayout.Space(8);

        // -------------------------------------------------------------
        // SECTION 3: Filter & Category Tabs
        // -------------------------------------------------------------
        EditorGUILayout.LabelField("📁 Discovered Models in Project", EditorStyles.boldLabel);

        selectedCategoryTab = GUILayout.Toolbar(selectedCategoryTab, categoryTabs);
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        searchFilter = EditorGUILayout.TextField("🔍 Filter by Name", searchFilter);
        if (!string.IsNullOrEmpty(searchFilter))
        {
            if (GUILayout.Button("✕", GUILayout.Width(22)))
            {
                searchFilter = "";
                GUI.FocusControl(null);
            }
        }
        EditorGUILayout.EndHorizontal();

        searchAllProject = EditorGUILayout.ToggleLeft("Scan entire project (search beyond Assets/_Project/Art/Models)", searchAllProject);

        EditorGUILayout.Space(6);

        // -------------------------------------------------------------
        // SECTION 4: Discovered Models List
        // -------------------------------------------------------------
        List<string> modelPaths = FindAllModelPaths(searchAllProject);

        if (modelPaths.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No 3D models (.fbx, .obj, .blend, .gltf) found in '{ModelsRoot}'.\n\n" +
                "Place your 3D files into:\n" +
                "• Assets/_Project/Art/Models/Characters/ (Player rigs)\n" +
                "• Assets/_Project/Art/Models/Monsters/ (Abnormalities, beasts)\n" +
                "• Assets/_Project/Art/Models/Items/ (Scrap, weapons)\n" +
                "• Assets/_Project/Art/Models/Environment/ (Dungeon architecture)",
                MessageType.Warning
            );

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("📂 Open Models Folder in Explorer", GUILayout.Height(30)))
            {
                EnsureDirectory(ModelsRoot + "/Characters");
                EnsureDirectory(ModelsRoot + "/Monsters");
                EnsureDirectory(ModelsRoot + "/Items");
                EnsureDirectory(ModelsRoot + "/Environment");
                EditorUtility.RevealInFinder(ModelsRoot);
            }

            if (GUILayout.Button("🔄 Refresh Assets", GUILayout.Height(30)))
            {
                AssetDatabase.Refresh();
            }
            EditorGUILayout.EndHorizontal();
            return;
        }

        EnemyArchetypeConfig activeCfg = StandardEnemyArchetypes[Mathf.Clamp(selectedEnemyArchetypeIndex, 0, StandardEnemyArchetypes.Length - 1)];
        GameObject activeEnemyPrefab = GetPrefabForArchetype(activeCfg);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        int displayedCount = 0;
        for (int i = 0; i < modelPaths.Count; i++)
        {
            string path = modelPaths[i];
            string fileName = Path.GetFileName(path);
            string lowerPath = path.ToLowerInvariant();
            string lowerFileName = fileName.ToLowerInvariant();

            // Search filter
            if (!string.IsNullOrEmpty(searchFilter) && !lowerFileName.Contains(searchFilter.ToLowerInvariant()))
            {
                continue;
            }

            bool isChar = lowerPath.Contains("character") || lowerPath.Contains("player") || lowerPath.Contains("hero") || lowerPath.Contains("humanoid");
            bool isMonster = lowerPath.Contains("monster") || lowerPath.Contains("enemy") || lowerPath.Contains("boss") || lowerPath.Contains("brute") || lowerPath.Contains("crawler") || lowerPath.Contains("stalker") || lowerPath.Contains("creature");
            bool isAnimation = lowerFileName.Contains("anim") || lowerFileName.Contains("walk") || lowerFileName.Contains("run") || lowerFileName.Contains("attack") || lowerFileName.Contains("idle") || lowerFileName.Contains("jump");
            bool isItem = lowerPath.Contains("item") || lowerPath.Contains("scrap") || lowerPath.Contains("weapon") || lowerPath.Contains("gear") || lowerPath.Contains("pickup");
            bool isEnv = lowerPath.Contains("environment") || lowerPath.Contains("prop") || lowerPath.Contains("dungeon") || lowerPath.Contains("room") || lowerPath.Contains("wall") || lowerPath.Contains("door") || lowerPath.Contains("floor");

            // Category filter
            if (selectedCategoryTab == 1 && !isChar)
            {
                continue;
            }
            if (selectedCategoryTab == 2 && !isMonster)
            {
                continue;
            }
            if (selectedCategoryTab == 3 && !isAnimation)
            {
                continue;
            }
            if (selectedCategoryTab == 4 && !isItem)
            {
                continue;
            }
            if (selectedCategoryTab == 5 && !isEnv)
            {
                continue;
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (modelAsset == null)
            {
                continue;
            }

            displayedCount++;
            string ext = Path.GetExtension(path).ToUpperInvariant().Replace(".", "");
            string sizeStr = GetFileSizeString(path);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Title & Select Button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📄 {fileName}  [{ext}]  ({sizeStr})", EditorStyles.boldLabel, GUILayout.Width(360));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("🔍 Select in Project", GUILayout.Width(130), GUILayout.Height(22)))
            {
                Selection.activeObject = modelAsset;
                EditorGUIUtility.PingObject(modelAsset);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(path, EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // Model Actions
            EditorGUILayout.LabelField("Player & Enemy Assignment:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("👤 Attach to Player (Body)", GUILayout.Height(24)))
            {
                AttachCharacterToPlayer(modelAsset);
            }

            // Primary enemy button attaches directly to currently selected enemy
            string activeEnemyLabel = activeEnemyPrefab != null ? $"👹 Attach to {activeCfg.displayName}" : $"👹 Attach to {activeCfg.displayName} [Create]";
            if (GUILayout.Button(activeEnemyLabel, GUILayout.Height(24)))
            {
                if (activeEnemyPrefab != null)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(activeEnemyPrefab);
                    AttachModelToEnemyPrefab(modelAsset, prefabPath);
                }
                else
                {
                    CreateDedicatedMonsterPrefab(modelAsset, activeCfg.archetypeId, activeCfg.displayName.Replace(" ", ""));
                }
            }

            if (EditorGUILayout.DropdownButton(new GUIContent("▼ More Enemies..."), FocusType.Passive, GUILayout.Width(140), GUILayout.Height(24)))
            {
                ShowAttachToEnemyMenu(modelAsset);
            }

            if (EditorGUILayout.DropdownButton(new GUIContent("👾 New Prefab ▼"), FocusType.Passive, GUILayout.Width(110), GUILayout.Height(24)))
            {
                GenericMenu menu = new GenericMenu();
                for (int a = 0; a < StandardEnemyArchetypes.Length; a++)
                {
                    EnemyArchetypeConfig cfg = StandardEnemyArchetypes[a];
                    menu.AddItem(new GUIContent($"{cfg.icon} {cfg.displayName} ({cfg.archetypeId})"), false, () => CreateDedicatedMonsterPrefab(modelAsset, cfg.archetypeId, cfg.displayName.Replace(" ", "")));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"➕ Custom ({modelAsset.name})"), false, () => CreateDedicatedMonsterPrefab(modelAsset, "basic_abnormality", modelAsset.name.Replace(" ", "_")));
                menu.ShowAsContext();
            }

            EditorGUILayout.EndHorizontal();

            if (isAnimation || lowerFileName.Contains("walk") || lowerFileName.Contains("anim"))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Animation Setup Options:", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("🚶 Bind to Player (Walk Anim)", GUILayout.Height(26)))
                {
                    AnimationSetupHelper.SetupPlayerWalkAnimation(path);
                }

                if (GUILayout.Button("👹 Bind to All Enemies (Walk & Chase Anim)", GUILayout.Height(26)))
                {
                    AnimationSetupHelper.SetupMonsterWalkAnimation(path);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Items & Environment Options:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("✨ Create Scrap Item", GUILayout.Height(24)))
            {
                CreateItemPrefabFromModel(modelAsset, path, ItemType.Scrap);
            }

            if (GUILayout.Button("⚔️ Create Weapon", GUILayout.Height(24)))
            {
                CreateItemPrefabFromModel(modelAsset, path, ItemType.Weapon);
            }

            if (GUILayout.Button("🏛️ Create Prop", GUILayout.Height(24)))
            {
                CreateEnvironmentPropPrefab(modelAsset, path);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        if (displayedCount == 0)
        {
            EditorGUILayout.HelpBox($"No models match the filter '{searchFilter}' in category '{categoryTabs[selectedCategoryTab]}'.", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🔄 Refresh Model List", GUILayout.Height(28)))
        {
            AssetDatabase.Refresh();
        }
        if (GUILayout.Button("📂 Reveal Models in Explorer", GUILayout.Height(28)))
        {
            EnsureDirectory(ModelsRoot);
            EditorUtility.RevealInFinder(ModelsRoot);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTargetPrefabsSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("🎯 Target Prefabs & Entity Management", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        // Player Prefab Row
        string playerPath = $"{PrefabsEntitiesRoot}/PlayerPrefab.prefab";
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("👤 Player Prefab", EditorStyles.boldLabel, GUILayout.Width(140));

        EditorGUI.BeginChangeCheck();
        GameObject newPlayerPrefab = (GameObject)EditorGUILayout.ObjectField(playerPrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck() && newPlayerPrefab != null)
        {
            playerPrefab = newPlayerPrefab;
            Selection.activeObject = newPlayerPrefab;
            EditorGUIUtility.PingObject(newPlayerPrefab);
        }

        if (GUILayout.Button("🔍 Select", GUILayout.Width(70)))
        {
            if (playerPrefab != null)
            {
                Selection.activeObject = playerPrefab;
                EditorGUIUtility.PingObject(playerPrefab);
            }
        }

        if (GUILayout.Button("📂 Open", GUILayout.Width(60)))
        {
            if (playerPrefab != null)
            {
                AssetDatabase.OpenAsset(playerPrefab);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("👹 Active Target Enemy Archetype:", EditorStyles.boldLabel);

        // Build list of display names for the dropdown
        string[] archetypeOptions = new string[StandardEnemyArchetypes.Length];
        for (int i = 0; i < StandardEnemyArchetypes.Length; i++)
        {
            EnemyArchetypeConfig cfg = StandardEnemyArchetypes[i];
            GameObject p = GetPrefabForArchetype(cfg);
            string pName = p != null ? p.name : "Missing Prefab";
            archetypeOptions[i] = $"{cfg.icon} {cfg.displayName} ({pName})";
        }

        EditorGUI.BeginChangeCheck();
        selectedEnemyArchetypeIndex = EditorGUILayout.Popup("Selected Enemy Type", selectedEnemyArchetypeIndex, archetypeOptions);
        if (EditorGUI.EndChangeCheck())
        {
            GUI.FocusControl(null);
        }

        if (selectedEnemyArchetypeIndex < 0 || selectedEnemyArchetypeIndex >= StandardEnemyArchetypes.Length)
        {
            selectedEnemyArchetypeIndex = 0;
        }

        EnemyArchetypeConfig activeCfg = StandardEnemyArchetypes[selectedEnemyArchetypeIndex];
        GameObject activeEnemyPrefab = GetPrefabForArchetype(activeCfg);
        string activeModelInfo = GetPrefabModelSummary(activeEnemyPrefab);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"{activeCfg.icon} {activeCfg.displayName} Prefab", EditorStyles.boldLabel, GUILayout.Width(190));

        EditorGUI.BeginChangeCheck();
        GameObject userAssigned = (GameObject)EditorGUILayout.ObjectField(activeEnemyPrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            if (userAssigned != null)
            {
                customArchetypePrefabOverrides[activeCfg.archetypeId] = userAssigned;
                activeEnemyPrefab = userAssigned;
                RegisterInNetworkPrefabs(userAssigned);
                Selection.activeObject = userAssigned;
                EditorGUIUtility.PingObject(userAssigned);
            }
        }

        if (activeEnemyPrefab != null)
        {
            if (GUILayout.Button("🔍 Select", GUILayout.Width(70)))
            {
                Selection.activeObject = activeEnemyPrefab;
                EditorGUIUtility.PingObject(activeEnemyPrefab);
            }

            if (GUILayout.Button("📂 Open", GUILayout.Width(60)))
            {
                AssetDatabase.OpenAsset(activeEnemyPrefab);
            }
        }
        else
        {
            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
            if (GUILayout.Button("➕ Create Prefab", GUILayout.Width(130)))
            {
                CreateDedicatedMonsterPrefab(null, activeCfg.archetypeId, activeCfg.displayName.Replace(" ", ""));
            }
            GUI.backgroundColor = Color.white;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Model Attached: {activeModelInfo}", EditorStyles.miniLabel);

        GameObject candidateModel = directDropModel != null ? directDropModel : Selection.activeGameObject;
        if (candidateModel != null && AssetDatabase.Contains(candidateModel) && activeEnemyPrefab != null)
        {
            if (GUILayout.Button($"Attach '{candidateModel.name}' to {activeCfg.displayName}", EditorStyles.miniButton, GUILayout.Width(220)))
            {
                string path = AssetDatabase.GetAssetPath(activeEnemyPrefab);
                AttachModelToEnemyPrefab(candidateModel, path);
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // Foldout showing all 5 archetypes side-by-side
        showEnemyListFold = EditorGUILayout.Foldout(showEnemyListFold, "📋 View All 5 Enemy Archetype Prefabs Side-by-Side", true);

        if (showEnemyListFold)
        {
            EditorGUILayout.Space(2);

            for (int i = 0; i < StandardEnemyArchetypes.Length; i++)
            {
                EnemyArchetypeConfig cfg = StandardEnemyArchetypes[i];
                GameObject enemyPrefab = GetPrefabForArchetype(cfg);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{cfg.icon} {cfg.displayName}", GUILayout.Width(180));

                EditorGUI.BeginChangeCheck();
                GameObject picked = (GameObject)EditorGUILayout.ObjectField(enemyPrefab, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (picked != null)
                    {
                        customArchetypePrefabOverrides[cfg.archetypeId] = picked;
                        RegisterInNetworkPrefabs(picked);
                        Selection.activeObject = picked;
                        EditorGUIUtility.PingObject(picked);
                    }
                }

                if (enemyPrefab != null)
                {
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                    {
                        selectedEnemyArchetypeIndex = i;
                        Selection.activeObject = enemyPrefab;
                        EditorGUIUtility.PingObject(enemyPrefab);
                    }
                }
                else
                {
                    if (GUILayout.Button("➕ Create", GUILayout.Width(65)))
                    {
                        CreateDedicatedMonsterPrefab(null, cfg.archetypeId, cfg.displayName.Replace(" ", ""));
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2);
            if (GUILayout.Button("✨ Ensure All 5 Enemy Archetype Prefabs Exist & Register in Netcode", GUILayout.Height(24)))
            {
                EnsureAllEnemyPrefabsExist();
            }

            if (GUILayout.Button("🔧 Repair All Player & Monster Animation Setups (Direct Animators on Visuals)", GUILayout.Height(24)))
            {
                AnimationSetupHelper.RepairPrefabAnimationSetups();
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawDirectDropSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("📥 Direct Model Drag & Drop (Any Model File)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Drag any 3D model asset from your Project window directly into this field for immediate assignment:", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        directDropModel = (GameObject)EditorGUILayout.ObjectField("Target Model Asset", directDropModel, typeof(GameObject), false);

        if (directDropModel != null)
        {
            if (GUILayout.Button("🔍 Ping Model", GUILayout.Width(100)))
            {
                Selection.activeObject = directDropModel;
                EditorGUIUtility.PingObject(directDropModel);
            }
        }
        EditorGUILayout.EndHorizontal();

        if (directDropModel != null)
        {
            EnemyArchetypeConfig activeCfg = StandardEnemyArchetypes[Mathf.Clamp(selectedEnemyArchetypeIndex, 0, StandardEnemyArchetypes.Length - 1)];
            GameObject activeEnemyPrefab = GetPrefabForArchetype(activeCfg);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("👤 Attach to Player", GUILayout.Height(24)))
            {
                AttachCharacterToPlayer(directDropModel);
            }

            string targetEnemyBtn = activeEnemyPrefab != null ? $"👹 Attach to {activeCfg.displayName}" : $"👹 Attach to {activeCfg.displayName} [Create]";
            if (GUILayout.Button(targetEnemyBtn, GUILayout.Height(24)))
            {
                if (activeEnemyPrefab != null)
                {
                    string path = AssetDatabase.GetAssetPath(activeEnemyPrefab);
                    AttachModelToEnemyPrefab(directDropModel, path);
                }
                else
                {
                    CreateDedicatedMonsterPrefab(directDropModel, activeCfg.archetypeId, activeCfg.displayName.Replace(" ", ""));
                }
            }

            if (EditorGUILayout.DropdownButton(new GUIContent("▼ More Enemies..."), FocusType.Passive, GUILayout.Width(130), GUILayout.Height(24)))
            {
                ShowAttachToEnemyMenu(directDropModel);
            }

            string assetPath = AssetDatabase.GetAssetPath(directDropModel);
            if (!string.IsNullOrEmpty(assetPath) && (assetPath.ToLowerInvariant().Contains("anim") || assetPath.ToLowerInvariant().Contains("walk")))
            {
                if (GUILayout.Button("🚶 Bind Walk to Player", GUILayout.Height(24)))
                {
                    AnimationSetupHelper.SetupPlayerWalkAnimation(assetPath);
                }

                if (GUILayout.Button("👹 Bind Walk to All Enemies", GUILayout.Height(24)))
                {
                    AnimationSetupHelper.SetupMonsterWalkAnimation(assetPath);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void ShowAttachToEnemyMenu(GameObject modelAsset)
    {
        GenericMenu menu = new GenericMenu();

        for (int i = 0; i < StandardEnemyArchetypes.Length; i++)
        {
            EnemyArchetypeConfig cfg = StandardEnemyArchetypes[i];
            GameObject p = GetPrefabForArchetype(cfg);
            bool exists = p != null;
            string pName = p != null ? p.name : cfg.candidatePrefabFileNames[0];
            string label = $"{cfg.icon} {cfg.displayName} ({pName})" + (exists ? "" : " [Will Create]");

            menu.AddItem(new GUIContent(label), false, () =>
            {
                if (!exists)
                {
                    CreateDedicatedMonsterPrefab(modelAsset, cfg.archetypeId, cfg.displayName.Replace(" ", ""));
                }
                else
                {
                    string path = AssetDatabase.GetAssetPath(p);
                    AttachModelToEnemyPrefab(modelAsset, path);
                }
            });
        }

        // List any extra enemy prefabs in folder
        if (Directory.Exists(PrefabsEntitiesRoot))
        {
            string[] extra = Directory.GetFiles(PrefabsEntitiesRoot, "Enemy*.prefab", SearchOption.TopDirectoryOnly);
            bool addedExtraHeader = false;

            for (int e = 0; e < extra.Length; e++)
            {
                string fileName = Path.GetFileName(extra[e]);
                bool isStandard = false;
                for (int s = 0; s < StandardEnemyArchetypes.Length; s++)
                {
                    for (int c = 0; c < StandardEnemyArchetypes[s].candidatePrefabFileNames.Length; c++)
                    {
                        if (StandardEnemyArchetypes[s].candidatePrefabFileNames[c] == fileName)
                        {
                            isStandard = true;
                            break;
                        }
                    }
                    if (isStandard)
                    {
                        break;
                    }
                }

                if (!isStandard)
                {
                    if (!addedExtraHeader)
                    {
                        menu.AddSeparator("");
                        addedExtraHeader = true;
                    }

                    string extraPath = extra[e].Replace('\\', '/');
                    menu.AddItem(new GUIContent($"👾 Custom/{fileName}"), false, () => AttachModelToEnemyPrefab(modelAsset, extraPath));
                }
            }
        }

        menu.ShowAsContext();
    }

    public static GameObject GetPrefabForArchetype(EnemyArchetypeConfig cfg)
    {
        if (customArchetypePrefabOverrides.TryGetValue(cfg.archetypeId, out GameObject customObj) && customObj != null)
        {
            return customObj;
        }

        for (int i = 0; i < cfg.candidatePrefabFileNames.Length; i++)
        {
            string path = $"{PrefabsEntitiesRoot}/{cfg.candidatePrefabFileNames[i]}";
            if (File.Exists(path))
            {
                GameObject loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }

        return null;
    }

    private static string GetPrefabModelSummary(GameObject prefab)
    {
        if (prefab == null)
        {
            return "Prefab not created yet";
        }

        Transform body = prefab.transform.Find("Body");
        if (body == null)
        {
            return "No 'Body' root found";
        }

        Transform visual = body.Find("MonsterVisual");
        if (visual != null)
        {
            Renderer r = visual.GetComponentInChildren<Renderer>();
            return visual.name + (r != null ? $" ({r.gameObject.name})" : "");
        }

        Transform charVisual = body.Find("CharacterVisual");
        if (charVisual != null)
        {
            Renderer r = charVisual.GetComponentInChildren<Renderer>();
            return charVisual.name + (r != null ? $" ({r.gameObject.name})" : "");
        }

        MeshFilter mf = body.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            return $"Capsule / Mesh ({mf.sharedMesh.name})";
        }

        return "Default / None";
    }

    public static void EnsureAllEnemyPrefabsExist()
    {
        EnsureDirectory(PrefabsEntitiesRoot);
        EnemyRegistry.Initialize();

        for (int i = 0; i < StandardEnemyArchetypes.Length; i++)
        {
            EnemyArchetypeConfig cfg = StandardEnemyArchetypes[i];
            GameObject existing = GetPrefabForArchetype(cfg);

            if (existing == null)
            {
                string targetFileName = cfg.candidatePrefabFileNames[0];
                CreateDedicatedMonsterPrefab(null, cfg.archetypeId, targetFileName.Replace(".prefab", "").Replace("Enemy_", ""));
            }
            else
            {
                RegisterInNetworkPrefabs(existing);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ModelImporterWizard] All 5 standard enemy archetype prefabs verified and registered in Netcode NetworkPrefabs!");
    }

    private List<string> FindAllModelPaths(bool searchAll)
    {
        List<string> results = new List<string>();
        string root = searchAll ? "Assets" : ModelsRoot;

        if (!Directory.Exists(root))
        {
            EnsureDirectory(ModelsRoot);
            return results;
        }

        string[] allFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
        for (int i = 0; i < allFiles.Length; i++)
        {
            string file = allFiles[i];
            string ext = Path.GetExtension(file).ToLowerInvariant();

            bool isSupported = false;
            for (int e = 0; e < SupportedModelExtensions.Length; e++)
            {
                if (ext == SupportedModelExtensions[e])
                {
                    isSupported = true;
                    break;
                }
            }

            if (!isSupported)
            {
                continue;
            }

            string unityPath = file.Replace('\\', '/');
            int assetsIdx = unityPath.IndexOf("Assets/");
            if (assetsIdx >= 0)
            {
                unityPath = unityPath.Substring(assetsIdx);
            }

            if (!results.Contains(unityPath))
            {
                results.Add(unityPath);
            }
        }

        // Fallback: If ModelsRoot has no files, try searching all of Assets/_Project/Art
        if (!searchAll && results.Count == 0 && Directory.Exists("Assets/_Project/Art"))
        {
            return FindAllModelPaths(true);
        }

        return results;
    }

    private static string GetFileSizeString(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                long bytes = new FileInfo(path).Length;
                if (bytes > 1024 * 1024)
                {
                    return $"{(bytes / (1024f * 1024f)):F1} MB";
                }
                if (bytes > 1024)
                {
                    return $"{(bytes / 1024f):F1} KB";
                }
                return $"{bytes} B";
            }
        }
        catch
        {
        }
        return "";
    }

    public static bool IsAnimationOnlyAsset(GameObject asset)
    {
        if (asset == null)
        {
            return false;
        }

        Renderer[] rends = asset.GetComponentsInChildren<Renderer>(true);
        MeshFilter[] filters = asset.GetComponentsInChildren<MeshFilter>(true);
        return rends.Length == 0 && filters.Length == 0;
    }

    [MenuItem("Tools/Dungeon Crawler/Attach Main Character Model to Player")]
    public static void AttachMainCharacterToPlayer()
    {
        string fbxPath = "Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx";
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (modelAsset == null)
        {
            Debug.LogError($"[ModelImporterWizard] Could not find model at '{fbxPath}'");
            return;
        }

        AttachCharacterToPlayer(modelAsset);
    }

    public static void AttachCharacterToPlayer(GameObject modelAsset)
    {
        if (IsAnimationOnlyAsset(modelAsset))
        {
            EditorUtility.DisplayDialog(
                "Animation File Selected",
                $"'{modelAsset.name}' is an animation clip file with no 3D meshes or renderers!\n\n" +
                "To use this file as a walking animation, click 'Bind Walk to Player' instead.\n\n" +
                "To attach a character 3D model, please select 'MainCharacter_rig.fbx'.",
                "OK"
            );
            return;
        }

        string playerPrefabPath = $"{PrefabsEntitiesRoot}/PlayerPrefab.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath) == null)
        {
            Debug.LogError($"[ModelImporterWizard] PlayerPrefab not found at '{playerPrefabPath}'");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(playerPrefabPath))
        {
            GameObject root = scope.prefabContentsRoot;
            Transform bodyTr = root.transform.Find("Body");
            if (bodyTr == null)
            {
                bodyTr = new GameObject("Body").transform;
                bodyTr.SetParent(root.transform, false);
            }

            bodyTr.localPosition = Vector3.zero;
            bodyTr.localRotation = Quaternion.identity;
            bodyTr.localScale = Vector3.one;

            // Remove placeholder capsule renderer/mesh if present
            MeshFilter mf = bodyTr.GetComponent<MeshFilter>();
            if (mf != null)
            {
                DestroyImmediate(mf);
            }
            MeshRenderer mr = bodyTr.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                DestroyImmediate(mr);
            }

            // Clean previous visual children
            for (int i = bodyTr.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(bodyTr.GetChild(i).gameObject);
            }

            // Child the character model rig under Body
            GameObject visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            visualInstance.name = "CharacterVisual";
            visualInstance.transform.SetParent(bodyTr, false);
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;

            // Ensure materials are assigned or converted to URP Lit
            Renderer[] rends = visualInstance.GetComponentsInChildren<Renderer>(true);
            Material litMat = EnsureMaterial("CharacterMaterial", new Color(0.85f, 0.85f, 0.85f));

            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i].sharedMaterial == null || rends[i].sharedMaterial.shader == null || rends[i].sharedMaterial.shader.name.Contains("Standard") || rends[i].sharedMaterial.shader.name.Contains("InternalErrorShader"))
                {
                    if (litMat != null)
                    {
                        rends[i].sharedMaterial = litMat;
                    }
                }
            }

            // Animator MUST be directly on CharacterVisual so that relative bone paths match Generic Mecanim animation clips
            Animator rogueAnim = bodyTr.GetComponent<Animator>();
            if (rogueAnim != null)
            {
                DestroyImmediate(rogueAnim);
            }

            Animator anim = visualInstance.GetComponent<Animator>();
            if (anim == null)
            {
                anim = visualInstance.AddComponent<Animator>();
            }

            if (anim != null)
            {
                string modelPath = AssetDatabase.GetAssetPath(modelAsset);
                Avatar rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(modelPath);
                if (rigAvatar == null)
                {
                    rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>("Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx");
                }
                if (rigAvatar != null)
                {
                    anim.avatar = rigAvatar;
                }

                AnimatorController animController = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Players/PlayerAnimatorController.controller");
                if (animController != null)
                {
                    anim.runtimeAnimatorController = animController;
                }
            }

            Debug.Log($"[ModelImporterWizard] Successfully attached '{modelAsset.name}' to PlayerPrefab under Body!");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Automatically select and ping target prefab in Project view
        GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        if (savedPrefab != null)
        {
            Selection.activeObject = savedPrefab;
            EditorGUIUtility.PingObject(savedPrefab);
        }
    }

    public static void AttachModelToEnemyPrefab(GameObject modelAsset, string enemyPrefabPath = null)
    {
        if (IsAnimationOnlyAsset(modelAsset))
        {
            EditorUtility.DisplayDialog(
                "Animation File Selected",
                $"'{modelAsset.name}' is an animation clip file with no 3D meshes or renderers!\n\n" +
                "To use this file as a walking animation, click 'Bind Walk to All Enemies' instead.\n\n" +
                "To attach an enemy 3D model, please select a model asset with meshes.",
                "OK"
            );
            return;
        }

        if (string.IsNullOrEmpty(enemyPrefabPath))
        {
            enemyPrefabPath = $"{PrefabsEntitiesRoot}/EnemyPrefab.prefab";
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(enemyPrefabPath) == null)
        {
            Debug.LogError($"[ModelImporterWizard] Enemy prefab not found at '{enemyPrefabPath}'");
            return;
        }

        using (var scope = new PrefabUtility.EditPrefabContentsScope(enemyPrefabPath))
        {
            GameObject root = scope.prefabContentsRoot;
            Transform bodyTr = root.transform.Find("Body");
            if (bodyTr == null)
            {
                bodyTr = new GameObject("Body").transform;
                bodyTr.SetParent(root.transform, false);
            }

            bodyTr.localPosition = Vector3.zero;
            bodyTr.localRotation = Quaternion.identity;
            bodyTr.localScale = Vector3.one;

            // Remove placeholder capsule renderer/mesh if present
            MeshFilter mf = bodyTr.GetComponent<MeshFilter>();
            if (mf != null)
            {
                DestroyImmediate(mf);
            }
            MeshRenderer mr = bodyTr.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                DestroyImmediate(mr);
            }

            // Clean previous visual children
            for (int i = bodyTr.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(bodyTr.GetChild(i).gameObject);
            }

            // Child the monster model rig under Body
            GameObject visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            visualInstance.name = "MonsterVisual";
            visualInstance.transform.SetParent(bodyTr, false);
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;

            // Ensure materials are assigned or converted to URP Lit
            Renderer[] rends = visualInstance.GetComponentsInChildren<Renderer>(true);
            Material litMat = EnsureMaterial("MonsterMaterial", new Color(0.75f, 0.2f, 0.2f));

            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i].sharedMaterial == null || rends[i].sharedMaterial.shader == null || rends[i].sharedMaterial.shader.name.Contains("Standard") || rends[i].sharedMaterial.shader.name.Contains("InternalErrorShader"))
                {
                    if (litMat != null)
                    {
                        rends[i].sharedMaterial = litMat;
                    }
                }
            }

            // Animator MUST be directly on CharacterVisual so that relative bone paths match Generic Mecanim animation clips
            Animator rogueAnim = bodyTr.GetComponent<Animator>();
            if (rogueAnim != null)
            {
                DestroyImmediate(rogueAnim);
            }

            Animator anim = visualInstance.GetComponent<Animator>();
            if (anim == null)
            {
                anim = visualInstance.AddComponent<Animator>();
            }

            if (anim != null)
            {
                string modelPath = AssetDatabase.GetAssetPath(modelAsset);
                Avatar rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(modelPath);
                if (rigAvatar == null)
                {
                    rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>("Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx");
                }
                if (rigAvatar != null)
                {
                    anim.avatar = rigAvatar;
                }

                AnimatorController animController = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Monsters/MonsterAnimatorController.controller");
                if (animController != null)
                {
                    anim.runtimeAnimatorController = animController;
                }
            }

            Debug.Log($"[ModelImporterWizard] Successfully attached '{modelAsset.name}' to '{Path.GetFileName(enemyPrefabPath)}' under Body!");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Automatically select and ping target prefab in Project view
        GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(enemyPrefabPath);
        if (savedPrefab != null)
        {
            RegisterInNetworkPrefabs(savedPrefab);
            Selection.activeObject = savedPrefab;
            EditorGUIUtility.PingObject(savedPrefab);
        }
    }

    public static GameObject CreateDedicatedMonsterPrefab(GameObject modelAsset, string enemyArchetypeId, string enemyDisplayName)
    {
        string basePrefabPath = $"{PrefabsEntitiesRoot}/EnemyPrefab.prefab";
        string targetPrefabPath = $"{PrefabsEntitiesRoot}/Enemy_{enemyDisplayName.Replace(" ", "")}.prefab";

        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
        if (basePrefab == null)
        {
            Debug.LogError($"[ModelImporterWizard] Base EnemyPrefab not found at '{basePrefabPath}'");
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = $"Enemy_{enemyDisplayName.Replace(" ", "")}";

        Transform bodyTr = instance.transform.Find("Body");
        if (bodyTr == null)
        {
            bodyTr = new GameObject("Body").transform;
            bodyTr.SetParent(instance.transform, false);
        }

        bodyTr.localPosition = Vector3.zero;
        bodyTr.localRotation = Quaternion.identity;
        bodyTr.localScale = Vector3.one;

        // If a new model is supplied, replace existing visual mesh
        if (modelAsset != null)
        {
            MeshFilter mf = bodyTr.GetComponent<MeshFilter>();
            if (mf != null)
            {
                DestroyImmediate(mf);
            }
            MeshRenderer mr = bodyTr.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                DestroyImmediate(mr);
            }

            for (int i = bodyTr.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(bodyTr.GetChild(i).gameObject);
            }

            GameObject visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            visualInstance.name = "MonsterVisual";
            visualInstance.transform.SetParent(bodyTr, false);
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;

            Renderer[] rends = visualInstance.GetComponentsInChildren<Renderer>(true);
            Material litMat = EnsureMaterial("MonsterMaterial", new Color(0.75f, 0.2f, 0.2f));

            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i].sharedMaterial == null || rends[i].sharedMaterial.shader == null || rends[i].sharedMaterial.shader.name.Contains("Standard") || rends[i].sharedMaterial.shader.name.Contains("InternalErrorShader"))
                {
                    if (litMat != null)
                    {
                        rends[i].sharedMaterial = litMat;
                    }
                }
            }
        }

        // Apply archetype stats
        EnemyRegistry.Initialize();
        EnemyData data = EnemyRegistry.GetEnemy(enemyArchetypeId);
        if (data != null)
        {
            EnemyHealth eh = instance.GetComponent<EnemyHealth>();
            if (eh != null)
            {
                eh.ApplyEnemyData(data);
            }
        }

        bool success;
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, targetPrefabPath, out success);
        DestroyImmediate(instance);

        if (success && saved != null)
        {
            RegisterInNetworkPrefabs(saved);
            Debug.Log($"[ModelImporterWizard] Created dedicated Monster Prefab at '{targetPrefabPath}' for archetype '{enemyArchetypeId}'!");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject savedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefabPath);
            if (savedAsset != null)
            {
                Selection.activeObject = savedAsset;
                EditorGUIUtility.PingObject(savedAsset);
            }
            return savedAsset;
        }

        return null;
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

        if (instance.GetComponent<Collider>() == null)
        {
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

        bool success;
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out success);
        DestroyImmediate(instance);

        if (!success || savedPrefab == null)
        {
            Debug.LogError($"[ModelImporterWizard] Failed to save prefab at {prefabPath}");
            return;
        }

        ItemData itemData = ScriptableObject.CreateInstance<ItemData>();
        string itemId = cleanName.ToLower();

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
        Selection.activeObject = savedPrefab;
        EditorGUIUtility.PingObject(savedPrefab);
    }

    private static void CreateEnvironmentPropPrefab(GameObject modelAsset, string assetPath)
    {
        string propsRoot = "Assets/_Project/Prefabs/Dungeon/Props";
        EnsureDirectory(propsRoot);

        string cleanName = modelAsset.name.Replace(" ", "_");
        string prefabPath = $"{propsRoot}/{cleanName}_Prop.prefab";

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        instance.name = cleanName;

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

            GameObject savedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (savedAsset != null)
            {
                Selection.activeObject = savedAsset;
                EditorGUIUtility.PingObject(savedAsset);
            }
        }
    }

    public static void RegisterInNetworkPrefabs(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/_Project/Resources/DefaultNetworkPrefabs.asset");
        if (list == null)
        {
            list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        }
        if (list != null)
        {
            if (!list.Contains(prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
                AssetDatabase.SaveAssets();
            }
        }
    }

    private static Material EnsureMaterial(string matName, Color fallbackColor)
    {
        string matPath = $"Assets/_Project/Art/Materials/{matName}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader != null)
            {
                mat = new Material(litShader);
                mat.name = matName;
                mat.color = fallbackColor;
                EnsureDirectory("Assets/_Project/Art/Materials");
                AssetDatabase.CreateAsset(mat, matPath);
            }
        }
        return mat;
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
