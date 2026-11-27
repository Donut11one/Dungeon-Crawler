using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the loot system: sample treasure models, TreasureData assets, the networked pickup
/// prefab (registered in DefaultNetworkPrefabs), a shared loot preset and demo spawn points in the open scene.
/// Existing assets are never overwritten, so it is safe to run repeatedly.
/// </summary>
public static class LootSystemSetup
{
    private const string TreasureDir = "Assets/_Project/ScriptableObjects/Treasures";
    private const string LootDir = "Assets/_Project/ScriptableObjects/Loot";
    private const string PrefabDir = "Assets/_Project/Prefabs/Treasures";
    private const string VisualDir = "Assets/_Project/Prefabs/Treasures/Visuals";
    private const string MaterialDir = "Assets/_Project/Art/Materials/Treasures";
    private const string DefaultNetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    private const string PresetPath = LootDir + "/Preset_CommonStash.asset";
    private const string LootRootName = "LootSystem";

    // ── Menu entries ─────────────────────────────────────────────────────────
    [MenuItem("Tools/Dungeon Crawler/Loot/Create Sample Treasures And Prefabs")]
    public static void CreateSampleAssetsMenu()
    {
        CreateSampleAssets();
        Debug.Log("[LootSystemSetup] Sample treasures, prefabs and preset are ready.");
    }

    [MenuItem("Tools/Dungeon Crawler/Loot/Add Loot System To Open Scene")]
    public static void AddLootSystemToOpenScene()
    {
        Dictionary<string, TreasureData> treasures = CreateSampleAssets();

        LootSpawner existing = Object.FindAnyObjectByType<LootSpawner>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            Debug.Log("[LootSystemSetup] This scene already has a LootSpawner; nothing was added.", existing);
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreasurePickup.DefaultPrefabPath);
        GameObject root = new GameObject(LootRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Loot System");
        LootSpawner spawner = root.AddComponent<LootSpawner>();

        SerializedObject spawnerObject = new SerializedObject(spawner);
        spawnerObject.FindProperty("treasurePrefab").objectReferenceValue = prefab;
        spawnerObject.ApplyModifiedPropertiesWithoutUndo();

        LootTable preset = AssetDatabase.LoadAssetAtPath<LootTable>(PresetPath);
        int created = 0;
        foreach (DemoPoint demo in BuildDemoPoints())
        {
            GameObject room = GameObject.Find(demo.room);
            if (room == null)
            {
                Debug.LogWarning($"[LootSystemSetup] Room '{demo.room}' not found; skipped '{demo.label}'.");
                continue;
            }

            GameObject go = new GameObject("LootPoint_" + demo.label.Replace(' ', '_'));
            go.transform.SetParent(root.transform, false);
            go.transform.position = room.transform.TransformPoint(demo.offset);
            LootSpawnPoint point = go.AddComponent<LootSpawnPoint>();
            ConfigurePoint(point, demo, treasures, preset);
            created++;
        }

        if (created == 0)
        {
            GameObject go = new GameObject("LootPoint_Default");
            go.transform.SetParent(root.transform, false);
            go.transform.position = Vector3.up * 0.1f;
            LootSpawnPoint point = go.AddComponent<LootSpawnPoint>();
            FillWithAllTreasures(point);
            created = 1;
        }

        SerializedObject refresh = new SerializedObject(spawner);
        refresh.ApplyModifiedPropertiesWithoutUndo();
        spawner.EditorCollectChildPoints();
        EditorUtility.SetDirty(spawner);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeObject = root;
        Debug.Log($"[LootSystemSetup] Added '{LootRootName}' with {created} spawn point(s). Save the scene to keep it.", root);
    }

    [MenuItem("Tools/Dungeon Crawler/Loot/Validate Loot Setup")]
    public static void ValidateSetup()
    {
        int problems = 0;
        LootSpawner[] spawners = Object.FindObjectsByType<LootSpawner>(FindObjectsInactive.Include);
        if (spawners.Length == 0)
        {
            Debug.LogWarning("[Loot Validation] No LootSpawner in the open scene.");
            problems++;
        }

        List<string> issues = new List<string>();
        for (int s = 0; s < spawners.Length; s++)
        {
            LootSpawner spawner = spawners[s];
            if (spawner.TreasurePrefab == null)
            {
                Debug.LogError("[Loot Validation] LootSpawner has no Treasure Prefab.", spawner);
                problems++;
            }
            else if (spawner.TreasurePrefab.GetComponent<NetworkObject>() == null || spawner.TreasurePrefab.GetComponent<TreasurePickup>() == null)
            {
                Debug.LogError("[Loot Validation] Treasure Prefab needs NetworkObject + TreasurePickup.", spawner);
                problems++;
            }

            IReadOnlyList<LootSpawnPoint> points = spawner.ResolvePoints();
            if (points.Count == 0)
            {
                Debug.LogWarning("[Loot Validation] LootSpawner has no spawn points.", spawner);
                problems++;
            }

            for (int i = 0; i < points.Count; i++)
            {
                issues.Clear();
                points[i].GetValidationIssues(issues);
                for (int j = 0; j < issues.Count; j++)
                {
                    Debug.LogWarning($"[Loot Validation] {points[i].DisplayName}: {issues[j]}", points[i]);
                    problems++;
                }
            }
        }

        Dictionary<string, TreasureData> byId = new Dictionary<string, TreasureData>();
        string[] guids = AssetDatabase.FindAssets("t:TreasureData");
        for (int i = 0; i < guids.Length; i++)
        {
            TreasureData data = AssetDatabase.LoadAssetAtPath<TreasureData>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (data == null)
            {
                continue;
            }
            if (byId.TryGetValue(data.TreasureId, out TreasureData other))
            {
                Debug.LogError($"[Loot Validation] Treasure id '{data.TreasureId}' is used by both '{data.name}' and '{other.name}'.", data);
                problems++;
            }
            else
            {
                byId[data.TreasureId] = data;
            }
        }

        Debug.Log(problems == 0
            ? $"[Loot Validation] OK: {spawners.Length} spawner(s), {byId.Count} treasure asset(s), no problems found."
            : $"[Loot Validation] Finished with {problems} problem(s).");
    }

    [MenuItem("GameObject/Dungeon Crawler/Loot Spawn Point", false, 10)]
    public static void CreateSpawnPointMenu(MenuCommand command)
    {
        GameObject parent = command.context as GameObject;
        if (parent == null)
        {
            LootSpawner spawner = Object.FindAnyObjectByType<LootSpawner>();
            parent = spawner != null ? spawner.gameObject : null;
        }

        GameObject go = new GameObject("LootSpawnPoint");
        GameObjectUtility.SetParentAndAlign(go, parent);
        Undo.RegisterCreatedObjectUndo(go, "Create Loot Spawn Point");

        if (SceneView.lastActiveSceneView != null)
        {
            go.transform.position = SceneView.lastActiveSceneView.pivot;
        }

        LootSpawnPoint point = go.AddComponent<LootSpawnPoint>();
        FillWithAllTreasures(point);
        Selection.activeObject = go;
    }

    // ── Asset creation ───────────────────────────────────────────────────────
    public static Dictionary<string, TreasureData> CreateSampleAssets()
    {
        EnsureFolder(TreasureDir);
        EnsureFolder(LootDir);
        EnsureFolder(VisualDir);
        EnsureFolder(MaterialDir);

        Dictionary<string, Material> m = new Dictionary<string, Material>
        {
            { "Bronze", Mat("Bronze", new Color(0.70f, 0.45f, 0.20f), 0.85f, 0.55f, Color.black) },
            { "Brass", Mat("Brass", new Color(0.95f, 0.72f, 0.28f), 0.90f, 0.75f, new Color(0.18f, 0.12f, 0.02f)) },
            { "Silver", Mat("Silver", new Color(0.85f, 0.87f, 0.92f), 1.00f, 0.85f, new Color(0.04f, 0.04f, 0.05f)) },
            { "RedWine", Mat("RedWine", new Color(0.55f, 0.02f, 0.05f), 0.00f, 0.90f, new Color(0.35f, 0.00f, 0.03f)) },
            { "Gold", Mat("Gold", new Color(1.00f, 0.78f, 0.22f), 1.00f, 0.85f, new Color(0.22f, 0.14f, 0.00f)) },
            { "Ruby", Mat("Ruby", new Color(0.90f, 0.03f, 0.10f), 0.20f, 0.95f, new Color(0.55f, 0.01f, 0.05f)) },
            { "Jade", Mat("Jade", new Color(0.18f, 0.62f, 0.38f), 0.10f, 0.80f, new Color(0.02f, 0.14f, 0.07f)) },
            { "JadeEyes", Mat("JadeEyes", new Color(0.30f, 1.00f, 0.50f), 0.00f, 0.90f, new Color(0.45f, 1.50f, 0.75f)) },
            { "DarkStone", Mat("DarkStone", new Color(0.14f, 0.13f, 0.15f), 0.00f, 0.30f, Color.black) },
            { "JewelBlue", Mat("JewelBlue", new Color(0.10f, 0.30f, 1.00f), 0.10f, 0.95f, new Color(0.10f, 0.30f, 1.00f)) },
            { "JewelGreen", Mat("JewelGreen", new Color(0.10f, 0.90f, 0.30f), 0.10f, 0.95f, new Color(0.10f, 0.90f, 0.30f)) },
            { "Velvet", Mat("Velvet", new Color(0.50f, 0.02f, 0.06f), 0.00f, 0.30f, Color.black) },
            { "Pedestal", Mat("Pedestal", new Color(0.10f, 0.10f, 0.10f), 0.00f, 0.20f, new Color(0.8f, 0.8f, 0.8f)) }
        };

        GameObject coin = SaveVisual(BuildCoinStack(m), "Treasure_CoinStack");
        GameObject goblet = SaveVisual(BuildGoblet(m), "Treasure_SilverGoblet");
        GameObject ruby = SaveVisual(BuildRuby(m), "Treasure_RubyGem");
        GameObject idol = SaveVisual(BuildIdol(m), "Treasure_JadeIdol");
        GameObject crown = SaveVisual(BuildCrown(m), "Treasure_GoldenCrown");

        Dictionary<string, TreasureData> t = new Dictionary<string, TreasureData>();
        t["coin_stack"] = UpsertTreasure("Treasure_CoinStack", "coin_stack", "Tarnished Coin Stack", "A stack of old bronze coins. Not much, but it adds up.", TreasureRarity.Common, 10, coin, 1.1f, 0.10f, 45f, 0.03f);
        t["silver_goblet"] = UpsertTreasure("Treasure_SilverGoblet", "silver_goblet", "Silver Goblet", "A polished goblet still holding something dark red.", TreasureRarity.Uncommon, 30, goblet, 1f, 0.10f, 55f, 0.04f);
        t["ruby_gem"] = UpsertTreasure("Treasure_RubyGem", "ruby_gem", "Ruby Gem", "A blood-red gem set in a golden mount.", TreasureRarity.Rare, 60, ruby, 1f, 0.12f, 80f, 0.06f);
        t["jade_idol"] = UpsertTreasure("Treasure_JadeIdol", "jade_idol", "Jade Idol", "A tiny idol with glowing green eyes. Someone worshipped this.", TreasureRarity.Epic, 125, idol, 1f, 0.10f, 50f, 0.04f);
        t["golden_crown"] = UpsertTreasure("Treasure_GoldenCrown", "golden_crown", "Golden Crown", "A royal crown studded with jewels. The dungeon's finest prize.", TreasureRarity.Legendary, 300, crown, 1.15f, 0.15f, 70f, 0.07f);

        EnsurePickupPrefab(m["Pedestal"]);
        EnsurePreset(t);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return t;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    private static Material Mat(string name, Color color, float metallic, float smoothness, Color emission)
    {
        string path = $"{MaterialDir}/Treasure_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0.001f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    private static GameObject SaveVisual(GameObject root, string name)
    {
        string path = $"{VisualDir}/{name}.prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            root.name = name;
            asset = PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        Object.DestroyImmediate(root);
        return asset;
    }

    // ── Treasure models (all primitives, origin at the base) ─────────────────
    private static GameObject BuildCoinStack(Dictionary<string, Material> m)
    {
        GameObject root = new GameObject("CoinStack");
        Vector3 coin = new Vector3(0.28f, 0.0125f, 0.28f);
        float[] jx = { 0.006f, -0.005f, 0.004f, -0.006f, 0.003f };
        float[] jz = { -0.004f, 0.006f, 0.003f, -0.005f, -0.002f };

        for (int i = 0; i < 5; i++)
        {
            Prim(root.transform, PrimitiveType.Cylinder, "Coin_" + i, new Vector3(jx[i], 0.0125f + i * 0.025f, jz[i]), coin, i == 4 ? m["Brass"] : m["Bronze"], Quaternion.Euler(0f, i * 37f, 0f));
        }
        for (int i = 0; i < 3; i++)
        {
            Prim(root.transform, PrimitiveType.Cylinder, "SideCoin_" + i, new Vector3(-0.26f + (i % 2) * 0.004f, 0.0125f + i * 0.025f, -0.07f), coin, i == 2 ? m["Brass"] : m["Bronze"], Quaternion.Euler(0f, i * 53f, 0f));
        }
        Prim(root.transform, PrimitiveType.Cylinder, "Loose_0", new Vector3(0.25f, 0.0125f, 0.08f), coin, m["Bronze"], Quaternion.Euler(0f, 20f, 0f));
        Prim(root.transform, PrimitiveType.Cylinder, "Loose_1", new Vector3(0.17f, 0.045f, 0.10f), coin, m["Brass"], Quaternion.Euler(14f, 40f, 8f));
        return root;
    }

    private static GameObject BuildGoblet(Dictionary<string, Material> m)
    {
        GameObject root = new GameObject("Goblet");
        Prim(root.transform, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.010f, 0f), new Vector3(0.20f, 0.010f, 0.20f), m["Silver"]);
        Prim(root.transform, PrimitiveType.Cylinder, "Foot", new Vector3(0f, 0.030f, 0f), new Vector3(0.13f, 0.010f, 0.13f), m["Silver"]);
        Prim(root.transform, PrimitiveType.Cylinder, "Stem", new Vector3(0f, 0.110f, 0f), new Vector3(0.045f, 0.070f, 0.045f), m["Silver"]);
        Prim(root.transform, PrimitiveType.Sphere, "Knob", new Vector3(0f, 0.130f, 0f), new Vector3(0.09f, 0.06f, 0.09f), m["Silver"]);
        Prim(root.transform, PrimitiveType.Sphere, "Bowl", new Vector3(0f, 0.270f, 0f), new Vector3(0.20f, 0.16f, 0.20f), m["Silver"]);
        Prim(root.transform, PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.340f, 0f), new Vector3(0.19f, 0.012f, 0.19f), m["Silver"]);
        Prim(root.transform, PrimitiveType.Cylinder, "Wine", new Vector3(0f, 0.353f, 0f), new Vector3(0.165f, 0.003f, 0.165f), m["RedWine"]);
        return root;
    }

    private static GameObject BuildRuby(Dictionary<string, Material> m)
    {
        GameObject root = new GameObject("Ruby");
        Quaternion vertexUp = Quaternion.FromToRotation(new Vector3(1f, 1f, 1f).normalized, Vector3.up);

        Prim(root.transform, PrimitiveType.Cylinder, "Setting", new Vector3(0f, 0.015f, 0f), new Vector3(0.30f, 0.015f, 0.30f), m["Gold"]);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f * Mathf.Deg2Rad + Mathf.PI * 0.25f;
            Prim(root.transform, PrimitiveType.Cube, "Prong_" + i, new Vector3(Mathf.Cos(angle) * 0.1f, 0.07f, Mathf.Sin(angle) * 0.1f), new Vector3(0.035f, 0.08f, 0.035f), m["Gold"]);
        }
        Prim(root.transform, PrimitiveType.Cube, "Gem", new Vector3(0f, 0.215f, 0f), Vector3.one * 0.2f, m["Ruby"], vertexUp);
        Prim(root.transform, PrimitiveType.Cube, "GemSmallA", new Vector3(0.21f, 0.12f, 0.05f), Vector3.one * 0.09f, m["Ruby"], Quaternion.Euler(0f, 30f, 0f) * vertexUp);
        Prim(root.transform, PrimitiveType.Cube, "GemSmallB", new Vector3(-0.19f, 0.115f, -0.09f), Vector3.one * 0.085f, m["Ruby"], Quaternion.Euler(0f, -20f, 0f) * vertexUp);
        return root;
    }

    private static GameObject BuildIdol(Dictionary<string, Material> m)
    {
        GameObject root = new GameObject("Idol");
        Prim(root.transform, PrimitiveType.Cube, "Base", new Vector3(0f, 0.03f, 0f), new Vector3(0.22f, 0.06f, 0.22f), m["DarkStone"]);
        Prim(root.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.22f, 0f), Vector3.one * 0.17f, m["Jade"]);
        Prim(root.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.45f, 0f), Vector3.one * 0.14f, m["Jade"]);
        Prim(root.transform, PrimitiveType.Sphere, "EyeL", new Vector3(-0.035f, 0.46f, 0.062f), Vector3.one * 0.028f, m["JadeEyes"]);
        Prim(root.transform, PrimitiveType.Sphere, "EyeR", new Vector3(0.035f, 0.46f, 0.062f), Vector3.one * 0.028f, m["JadeEyes"]);
        Prim(root.transform, PrimitiveType.Cylinder, "Crest", new Vector3(0f, 0.53f, 0f), new Vector3(0.10f, 0.012f, 0.10f), m["Gold"]);
        Prim(root.transform, PrimitiveType.Cube, "ArmL", new Vector3(-0.12f, 0.24f, 0.02f), new Vector3(0.05f, 0.16f, 0.05f), m["Jade"], Quaternion.Euler(0f, 0f, 15f));
        Prim(root.transform, PrimitiveType.Cube, "ArmR", new Vector3(0.12f, 0.24f, 0.02f), new Vector3(0.05f, 0.16f, 0.05f), m["Jade"], Quaternion.Euler(0f, 0f, -15f));
        return root;
    }

    private static GameObject BuildCrown(Dictionary<string, Material> m)
    {
        GameObject root = new GameObject("Crown");
        Prim(root.transform, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0.045f, 0f), new Vector3(0.32f, 0.045f, 0.32f), m["Gold"]);
        Prim(root.transform, PrimitiveType.Cylinder, "Velvet", new Vector3(0f, 0.047f, 0f), new Vector3(0.25f, 0.0465f, 0.25f), m["Velvet"]);

        Material[] jewels = { m["Ruby"], m["JewelBlue"], m["JewelGreen"] };
        for (int i = 0; i < 5; i++)
        {
            float spikeAngle = i * 72f * Mathf.Deg2Rad;
            Vector3 spikePos = new Vector3(Mathf.Cos(spikeAngle) * 0.125f, 0.16f, Mathf.Sin(spikeAngle) * 0.125f);
            Prim(root.transform, PrimitiveType.Cube, "Spike_" + i, spikePos, new Vector3(0.05f, 0.14f, 0.05f), m["Gold"], Quaternion.Euler(0f, -i * 72f, 0f));
            Prim(root.transform, PrimitiveType.Sphere, "Tip_" + i, new Vector3(spikePos.x, 0.24f, spikePos.z), Vector3.one * 0.055f, jewels[i % jewels.Length]);

            float jewelAngle = (i * 72f + 36f) * Mathf.Deg2Rad;
            Prim(root.transform, PrimitiveType.Sphere, "Jewel_" + i, new Vector3(Mathf.Cos(jewelAngle) * 0.163f, 0.05f, Mathf.Sin(jewelAngle) * 0.163f), Vector3.one * 0.06f, jewels[(i + 1) % jewels.Length]);
        }
        return root;
    }

    // ── Data assets ──────────────────────────────────────────────────────────
    private static TreasureData UpsertTreasure(string fileName, string id, string display, string description, TreasureRarity rarity, int value, GameObject visual, float scale, float hover, float spin, float bob)
    {
        string path = $"{TreasureDir}/{fileName}.asset";
        TreasureData asset = AssetDatabase.LoadAssetAtPath<TreasureData>(path);
        if (asset != null)
        {
            return asset;
        }

        asset = ScriptableObject.CreateInstance<TreasureData>();
        AssetDatabase.CreateAsset(asset, path);

        SerializedObject so = new SerializedObject(asset);
        so.FindProperty("treasureId").stringValue = id;
        so.FindProperty("displayName").stringValue = display;
        so.FindProperty("description").stringValue = description;
        so.FindProperty("rarity").enumValueIndex = (int)rarity;
        so.FindProperty("value").intValue = value;
        so.FindProperty("visualPrefab").objectReferenceValue = visual;
        so.FindProperty("visualScale").floatValue = scale;
        so.FindProperty("hoverHeight").floatValue = hover;
        so.FindProperty("spinSpeed").floatValue = spin;
        so.FindProperty("bobHeight").floatValue = bob;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static void EnsurePreset(Dictionary<string, TreasureData> treasures)
    {
        if (AssetDatabase.LoadAssetAtPath<LootTable>(PresetPath) != null)
        {
            return;
        }

        LootTable preset = ScriptableObject.CreateInstance<LootTable>();
        preset.Definition.emptyChancePercent = 30f;
        preset.Definition.entries.Add(new LootEntry { treasure = treasures["coin_stack"], weight = 60f });
        preset.Definition.entries.Add(new LootEntry { treasure = treasures["silver_goblet"], weight = 30f });
        preset.Definition.entries.Add(new LootEntry { treasure = treasures["ruby_gem"], weight = 10f });
        AssetDatabase.CreateAsset(preset, PresetPath);
    }

    private static void EnsurePickupPrefab(Material pedestalMaterial)
    {
        EnsureFolder(PrefabDir);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreasurePickup.DefaultPrefabPath);
        if (prefab == null)
        {
            GameObject root = new GameObject("TreasurePickupPrefab");

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.7f;
            trigger.center = new Vector3(0f, 0.35f, 0f);

            root.AddComponent<NetworkObject>();
            TreasurePickup pickup = root.AddComponent<TreasurePickup>();

            GameObject pedestal = Prim(root.transform, PrimitiveType.Cylinder, "Pedestal", new Vector3(0f, 0.012f, 0f), new Vector3(0.7f, 0.012f, 0.7f), pedestalMaterial);
            GameObject anchor = new GameObject("VisualAnchor");
            anchor.transform.SetParent(root.transform, false);

            SerializedObject so = new SerializedObject(pickup);
            so.FindProperty("visualAnchor").objectReferenceValue = anchor.transform;
            so.FindProperty("pedestalRenderer").objectReferenceValue = pedestal.GetComponent<MeshRenderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            prefab = PrefabUtility.SaveAsPrefabAsset(root, TreasurePickup.DefaultPrefabPath, out bool saved);
            Object.DestroyImmediate(root);
            if (!saved || prefab == null)
            {
                Debug.LogError("[LootSystemSetup] Could not save the TreasurePickup prefab.");
                return;
            }
        }

        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(DefaultNetworkPrefabsPath);
        if (list != null && !list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            Debug.Log($"[LootSystemSetup] Registered TreasurePickupPrefab in {DefaultNetworkPrefabsPath}");
        }
    }

    // ── Scene content ────────────────────────────────────────────────────────
    private class DemoPoint
    {
        public string room;
        public Vector3 offset;
        public string label;
        public float empty;
        public int min;
        public int max;
        public float scatter;
        public string[] ids;
        public float[] weights;
        public int[] caps;
        public bool respawn;
        public float delayMin;
        public float delayMax;
        public int maxRespawns = -1;
        public bool usePreset;
    }

    private static List<DemoPoint> BuildDemoPoints()
    {
        return new List<DemoPoint>
        {
            new DemoPoint { room = "Room_10x10_704", offset = new Vector3(2f, 0.1f, 2f), label = "Entry Hall", empty = 35f, min = 1, max = 2, scatter = 2.5f,
                ids = new[] { "coin_stack", "silver_goblet", "ruby_gem" }, weights = new[] { 70f, 25f, 5f } },
            new DemoPoint { room = "Room_1", offset = new Vector3(-2f, 0.1f, -2f), label = "Side Chamber", min = 1, max = 3, scatter = 2.5f, usePreset = true },
            new DemoPoint { room = "CoopPuzzleVault_893", offset = new Vector3(0f, 0.1f, -1f), label = "Puzzle Vault", empty = 0f, min = 2, max = 3, scatter = 2.2f,
                ids = new[] { "ruby_gem", "jade_idol", "golden_crown" }, weights = new[] { 30f, 45f, 25f }, caps = new[] { 0, 0, 1 } },
            new DemoPoint { room = "CrossroadsJunction_104", offset = new Vector3(3f, 0.1f, 3f), label = "Guarded Junction", empty = 30f, min = 1, max = 2, scatter = 1.8f,
                ids = new[] { "silver_goblet", "ruby_gem", "jade_idol" }, weights = new[] { 40f, 40f, 20f } },
            new DemoPoint { room = "StorageDepot_755", offset = new Vector3(0f, 0.1f, 0f), label = "Storage Depot", empty = 15f, min = 2, max = 4, scatter = 2.6f,
                ids = new[] { "coin_stack", "silver_goblet", "ruby_gem", "jade_idol", "golden_crown" }, weights = new[] { 45f, 30f, 15f, 8f, 2f }, caps = new[] { 0, 0, 0, 0, 1 } },
            new DemoPoint { room = "CustomBlankRoom_818", offset = new Vector3(7f, 0.1f, 6f), label = "Monster Nest Hoard", empty = 20f, min = 2, max = 3, scatter = 4.5f,
                ids = new[] { "ruby_gem", "jade_idol", "golden_crown" }, weights = new[] { 35f, 40f, 25f }, caps = new[] { 0, 0, 1 } },
            new DemoPoint { room = "ConnectingCorridor_443", offset = new Vector3(0f, 0.1f, 0f), label = "Corridor Stash", empty = 60f, min = 1, max = 1, scatter = 0.6f,
                ids = new[] { "coin_stack", "silver_goblet" }, weights = new[] { 80f, 20f } },
            new DemoPoint { room = "ExtractionChamber_928", offset = new Vector3(3.5f, 0.1f, 0f), label = "Extraction Cache", empty = 0f, min = 1, max = 2, scatter = 0.8f,
                ids = new[] { "coin_stack", "silver_goblet" }, weights = new[] { 100f, 30f }, respawn = true, delayMin = 20f, delayMax = 30f, maxRespawns = 3 }
        };
    }

    private static void ConfigurePoint(LootSpawnPoint point, DemoPoint demo, Dictionary<string, TreasureData> treasures, LootTable preset)
    {
        SerializedObject so = new SerializedObject(point);
        so.FindProperty("label").stringValue = demo.label;
        so.FindProperty("minSpawnCount").intValue = demo.min;
        so.FindProperty("maxSpawnCount").intValue = demo.max;
        so.FindProperty("scatterRadius").floatValue = demo.scatter;
        so.FindProperty("respawnMode").enumValueIndex = demo.respawn ? (int)LootRespawnMode.AfterCleared : (int)LootRespawnMode.OncePerRound;
        so.FindProperty("respawnDelayMin").floatValue = demo.delayMin > 0f ? demo.delayMin : 30f;
        so.FindProperty("respawnDelayMax").floatValue = demo.delayMax > 0f ? demo.delayMax : 60f;
        so.FindProperty("maxRespawns").intValue = demo.maxRespawns;

        if (demo.usePreset)
        {
            so.FindProperty("presetTable").objectReferenceValue = preset;
        }
        else
        {
            SerializedProperty table = so.FindProperty("customTable");
            table.FindPropertyRelative("emptyChancePercent").floatValue = demo.empty;
            SerializedProperty entries = table.FindPropertyRelative("entries");
            entries.arraySize = demo.ids.Length;
            for (int i = 0; i < demo.ids.Length; i++)
            {
                SerializedProperty element = entries.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("treasure").objectReferenceValue = treasures[demo.ids[i]];
                element.FindPropertyRelative("weight").floatValue = demo.weights[i];
                element.FindPropertyRelative("maxPerBatch").intValue = demo.caps != null ? demo.caps[i] : 0;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void FillWithAllTreasures(LootSpawnPoint point)
    {
        SerializedObject so = new SerializedObject(point);
        SerializedProperty entries = so.FindProperty("customTable").FindPropertyRelative("entries");
        string[] guids = AssetDatabase.FindAssets("t:TreasureData");
        entries.arraySize = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            TreasureData data = AssetDatabase.LoadAssetAtPath<TreasureData>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (data == null)
            {
                continue;
            }

            entries.arraySize++;
            SerializedProperty element = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            element.FindPropertyRelative("treasure").objectReferenceValue = data;
            element.FindPropertyRelative("weight").floatValue = 1f;
            element.FindPropertyRelative("maxPerBatch").intValue = 0;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
