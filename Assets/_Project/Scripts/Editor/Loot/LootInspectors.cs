using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LootSpawnPoint))]
public class LootSpawnPointEditor : Editor
{
    private static readonly List<string> issues = new List<string>();

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        LootSpawnPoint point = (LootSpawnPoint)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("label"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnEnabled"));

        // ── Loot table ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Loot Table", EditorStyles.boldLabel);
        SerializedProperty preset = serializedObject.FindProperty("presetTable");
        EditorGUILayout.PropertyField(preset, new GUIContent("Shared Preset (optional)", "Assign a LootTable asset to share one configuration between locations. Leave empty for this location's own table."));

        if (preset.objectReferenceValue != null)
        {
            EditorGUILayout.HelpBox("Using a shared preset: this location's own table below is ignored. Edit the preset asset to change every location that uses it.", MessageType.Info);
            SerializedObject presetObject = new SerializedObject(preset.objectReferenceValue);
            LootTableEditorGUI.DrawTable(presetObject.FindProperty("definition"), true);
        }
        else
        {
            LootTableEditorGUI.DrawTable(serializedObject.FindProperty("customTable"));
            if (GUILayout.Button("Save This Table As Preset...", EditorStyles.miniButton))
            {
                SaveAsPreset(point);
            }
        }

        // ── Spawn count ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spawn Count", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("minSpawnCount"), new GUIContent("Min Rolls"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSpawnCount"), new GUIContent("Max Rolls"));

        LootTableDefinition table = point.ActiveTable;
        List<float> chances = new List<float>();
        LootRoller.GetEffectiveChances(table, chances, out float emptyChance);
        float expected = (point.MinSpawnCount + point.MaxSpawnCount) * 0.5f * (1f - emptyChance);
        EditorGUILayout.HelpBox($"Each spawn rolls {point.MinSpawnCount}-{point.MaxSpawnCount} time(s); every roll independently gives nothing or one treasure. Expected treasures per spawn: {expected:0.00}.", MessageType.None);

        // ── Placement ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
        DrawFields("scatterRadius", "minSeparation", "snapToGround", "randomYaw");
        if (serializedObject.FindProperty("snapToGround").boolValue)
        {
            DrawFields("groundMask", "groundProbeHeight", "groundProbeDistance", "heightOffset");
        }

        // ── Respawn ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Respawn", EditorStyles.boldLabel);
        SerializedProperty mode = serializedObject.FindProperty("respawnMode");
        EditorGUILayout.PropertyField(mode);
        if (mode.enumValueIndex == (int)LootRespawnMode.AfterCleared)
        {
            DrawFields("respawnDelayMin", "respawnDelayMax", "maxRespawns");
        }
        else
        {
            EditorGUILayout.HelpBox("Rolled once at the start of every round and never again until the next round.", MessageType.None);
        }

        serializedObject.ApplyModifiedProperties();

        issues.Clear();
        point.GetValidationIssues(issues);
        if (issues.Count > 0)
        {
            EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Warning);
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime (server)", EditorStyles.boldLabel);
            string respawnText = point.SecondsUntilRespawn >= 0f ? $"respawn in {point.SecondsUntilRespawn:0.0}s" : "no respawn pending";
            EditorGUILayout.HelpBox($"Active treasures: {point.ActiveCount}   |   Respawns used: {point.RespawnsDone}   |   {respawnText}", MessageType.None);
        }
    }

    private void DrawFields(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(names[i]));
        }
    }

    private static void SaveAsPreset(LootSpawnPoint point)
    {
        string suggested = point.DisplayName.Replace(' ', '_') + "_Table";
        string path = EditorUtility.SaveFilePanelInProject("Save Loot Table Preset", suggested, "asset", "Choose where to save the preset.", "Assets/_Project/ScriptableObjects/Loot");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        LootTable asset = CreateInstance<LootTable>();
        LootTableDefinition source = point.ActiveTable;
        asset.Definition.emptyChancePercent = source.emptyChancePercent;
        for (int i = 0; i < source.entries.Count; i++)
        {
            LootEntry e = source.entries[i];
            asset.Definition.entries.Add(new LootEntry { treasure = e.treasure, weight = e.weight, maxPerBatch = e.maxPerBatch });
        }

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(asset);
    }
}

[CustomEditor(typeof(LootTable))]
public class LootTableAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Reusable loot table. Assign it to any LootSpawnPoint as its Shared Preset.", MessageType.Info);
        LootTableEditorGUI.DrawTable(serializedObject.FindProperty("definition"));
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(LootSpawner))]
public class LootSpawnerEditor : Editor
{
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        LootSpawner spawner = (LootSpawner)target;

        EditorGUILayout.Space();
        if (spawner.TreasurePrefab == null)
        {
            EditorGUILayout.HelpBox("No Treasure Prefab assigned - nothing can spawn.", MessageType.Error);
            if (GUILayout.Button("Assign Default TreasurePickupPrefab"))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreasurePickup.DefaultPrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[LootSpawner] Default prefab not found. Run Tools > Dungeon Crawler > Loot > Create Sample Treasures And Prefabs first.");
                }
                else
                {
                    Undo.RecordObject(spawner, "Assign Treasure Prefab");
                    spawner.TreasurePrefab = prefab;
                    EditorUtility.SetDirty(spawner);
                }
            }
        }

        if (GUILayout.Button("Collect Child Spawn Points Into List"))
        {
            Undo.RecordObject(spawner, "Collect Spawn Points");
            spawner.EditorCollectChildPoints();
            EditorUtility.SetDirty(spawner);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spawn Point Overview", EditorStyles.boldLabel);
        IReadOnlyList<LootSpawnPoint> points = spawner.ResolvePoints();
        if (points.Count == 0)
        {
            EditorGUILayout.HelpBox("No spawn points found. Add child objects with a LootSpawnPoint (GameObject > Dungeon Crawler > Loot Spawn Point).", MessageType.Warning);
        }

        List<float> chances = new List<float>();
        List<string> issues = new List<string>();
        for (int i = 0; i < points.Count; i++)
        {
            LootSpawnPoint point = points[i];
            LootRoller.GetEffectiveChances(point.ActiveTable, chances, out float empty);
            issues.Clear();
            point.GetValidationIssues(issues);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(point, typeof(LootSpawnPoint), true, GUILayout.MinWidth(120f));
                string status = point.SpawnEnabled ? "" : "  [disabled]";
                string warn = issues.Count > 0 ? "  (!) " + issues.Count : "";
                string active = Application.isPlaying ? $"  live: {point.ActiveCount}" : "";
                GUILayout.Label($"{point.MinSpawnCount}-{point.MaxSpawnCount} rolls, {empty * 100f:0}% empty{status}{warn}{active}", EditorStyles.miniLabel);
            }
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"Server session: {(spawner.ServerSessionActive ? "active" : "not running")}   |   Active treasures: {spawner.ActiveTreasureCount}", MessageType.None);

            using (new EditorGUI.DisabledScope(!spawner.ServerSessionActive))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reroll All"))
                {
                    spawner.RerollAll();
                }
                if (GUILayout.Button("Spawn Unrolled"))
                {
                    spawner.SpawnAll();
                }
                if (GUILayout.Button("Clear All"))
                {
                    spawner.ClearAll();
                }
            }
        }
    }
}

[CustomEditor(typeof(TreasureData))]
public class TreasureDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        TreasureData data = (TreasureData)target;

        EditorGUILayout.Space();
        if (data.VisualPrefab != null)
        {
            Texture2D preview = AssetPreview.GetAssetPreview(data.VisualPrefab);
            if (preview != null)
            {
                Rect rect = GUILayoutUtility.GetRect(96f, 96f, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }
            else if (AssetPreview.IsLoadingAssetPreview(data.VisualPrefab.GetEntityId()))
            {
                Repaint();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No visual prefab assigned: a generic magenta placeholder sphere is shown in the world.", MessageType.Info);
        }

        string id = data.TreasureId;
        if (string.IsNullOrEmpty(id))
        {
            EditorGUILayout.HelpBox("This treasure has no usable id.", MessageType.Error);
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:TreasureData");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TreasureData other = AssetDatabase.LoadAssetAtPath<TreasureData>(path);
            if (other != null && other != data && other.TreasureId == id)
            {
                EditorGUILayout.HelpBox($"Duplicate id '{id}' also used by '{other.name}' ({path}). Treasure ids must be unique.", MessageType.Error);
            }
        }
    }
}
