using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared inspector drawing for a LootTableDefinition: empty-chance slider, entry rows with live normalised
/// percentages, a probability bar, validation messages and helper buttons. Percentages come from the same
/// LootRoller code the game uses, so the preview always matches runtime behaviour.
/// </summary>
public static class LootTableEditorGUI
{
    private static readonly List<float> chances = new List<float>();
    private static readonly List<string> issues = new List<string>();
    private static readonly Dictionary<Object, string> simulations = new Dictionary<Object, string>();

    /// <summary>Builds a plain definition object from serialized data (for previews and validation).</summary>
    public static LootTableDefinition Read(SerializedProperty table)
    {
        LootTableDefinition def = new LootTableDefinition();
        def.emptyChancePercent = table.FindPropertyRelative("emptyChancePercent").floatValue;

        SerializedProperty entries = table.FindPropertyRelative("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty element = entries.GetArrayElementAtIndex(i);
            def.entries.Add(new LootEntry
            {
                treasure = element.FindPropertyRelative("treasure").objectReferenceValue as TreasureData,
                weight = element.FindPropertyRelative("weight").floatValue,
                maxPerBatch = element.FindPropertyRelative("maxPerBatch").intValue
            });
        }
        return def;
    }

    public static void DrawTable(SerializedProperty table, bool readOnly = false)
    {
        SerializedProperty emptyProp = table.FindPropertyRelative("emptyChancePercent");
        SerializedProperty entriesProp = table.FindPropertyRelative("entries");

        using (new EditorGUI.DisabledScope(readOnly))
        {
            EditorGUILayout.Slider(
                emptyProp,
                0f,
                100f,
                new GUIContent("Chance Nothing Spawns (%)", "Per roll. Otherwise a treasure is chosen using the relative weights below."));
        }

        LootTableDefinition def = Read(table);
        LootRoller.GetEffectiveChances(def, chances, out float emptyChance);
        float totalWeight = LootRoller.GetTotalUsableWeight(def);

        DrawStackedBar(def, emptyChance);
        EditorGUILayout.LabelField(
            $"Per roll:  Nothing {emptyChance * 100f:0.#}%   |   Treasure {(1f - emptyChance) * 100f:0.#}%   (weights total {totalWeight:0.##} - normalised automatically)",
            EditorStyles.miniLabel);

        EditorGUILayout.Space(2f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Treasure", EditorStyles.miniBoldLabel, GUILayout.MinWidth(100f), GUILayout.ExpandWidth(true));
            GUILayout.Label(new GUIContent("Weight", "Relative probability. Totals do not need to equal 100."), EditorStyles.miniBoldLabel, GUILayout.Width(56f));
            GUILayout.Label(new GUIContent("Max", "Max copies of this treasure per spawn batch at this location. 0 = unlimited."), EditorStyles.miniBoldLabel, GUILayout.Width(34f));
            GUILayout.Label("Chance", EditorStyles.miniBoldLabel, GUILayout.Width(52f));
            GUILayout.Space(24f);
        }

        int removeIndex = -1;
        using (new EditorGUI.DisabledScope(readOnly))
        {
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                SerializedProperty element = entriesProp.GetArrayElementAtIndex(i);
                float chance = i < chances.Count ? chances[i] : 0f;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("treasure"), GUIContent.none, GUILayout.MinWidth(100f));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("weight"), GUIContent.none, GUILayout.Width(56f));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("maxPerBatch"), GUIContent.none, GUILayout.Width(34f));
                    GUILayout.Label($"{chance * 100f:0.0}%", EditorStyles.miniBoldLabel, GUILayout.Width(52f));
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        removeIndex = i;
                    }
                }

                Rect bar = GUILayoutUtility.GetRect(1f, 3f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.2f));
                TreasureData treasure = def.entries[i].treasure;
                Color fill = treasure != null ? treasure.AccentColor : Color.gray;
                EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(chance), bar.height), fill);
            }
        }

        if (removeIndex >= 0)
        {
            entriesProp.DeleteArrayElementAtIndex(removeIndex);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(readOnly))
            {
                if (GUILayout.Button("+ Add Entry", EditorStyles.miniButton))
                {
                    AddEntry(entriesProp, null);
                }
                if (GUILayout.Button(new GUIContent("+ All Treasures", "Adds every TreasureData asset in the project that is not listed yet."), EditorStyles.miniButton))
                {
                    AddAllTreasures(entriesProp, def);
                }
                if (GUILayout.Button(new GUIContent("Normalise to 100", "Rescales weights so they add up to 100 (ratios are kept)."), EditorStyles.miniButton))
                {
                    LootRoller.NormalizeWeights(def, 100f);
                    WriteWeights(entriesProp, def);
                }
                if (GUILayout.Button(new GUIContent("Equal Weights", "Sets every weight to 1."), EditorStyles.miniButton))
                {
                    for (int i = 0; i < entriesProp.arraySize; i++)
                    {
                        entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("weight").floatValue = 1f;
                    }
                }
            }
            if (GUILayout.Button(new GUIContent("Simulate x10,000", "Rolls this table 10,000 times and reports the observed distribution."), EditorStyles.miniButton))
            {
                simulations[table.serializedObject.targetObject] = Simulate(def);
            }
        }

        if (simulations.TryGetValue(table.serializedObject.targetObject, out string report))
        {
            EditorGUILayout.HelpBox(report, MessageType.None);
        }

        issues.Clear();
        LootRoller.Validate(def, issues);
        if (issues.Count > 0)
        {
            EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Warning);
        }
    }

    private static void DrawStackedBar(LootTableDefinition def, float emptyChance)
    {
        Rect rect = GUILayoutUtility.GetRect(1f, 14f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));

        float x = rect.x;
        float emptyWidth = rect.width * emptyChance;
        EditorGUI.DrawRect(new Rect(x, rect.y, emptyWidth, rect.height), new Color(0.35f, 0.35f, 0.38f));
        x += emptyWidth;

        for (int i = 0; i < chances.Count; i++)
        {
            float width = rect.width * chances[i];
            if (width <= 0f)
            {
                continue;
            }

            TreasureData treasure = def.entries[i].treasure;
            Color color = treasure != null ? treasure.AccentColor : Color.gray;
            EditorGUI.DrawRect(new Rect(x, rect.y, Mathf.Max(0f, width - 1f), rect.height), color);
            x += width;
        }
    }

    private static void AddEntry(SerializedProperty entriesProp, TreasureData treasure)
    {
        entriesProp.arraySize++;
        SerializedProperty element = entriesProp.GetArrayElementAtIndex(entriesProp.arraySize - 1);
        element.FindPropertyRelative("treasure").objectReferenceValue = treasure;
        element.FindPropertyRelative("weight").floatValue = 1f;
        element.FindPropertyRelative("maxPerBatch").intValue = 0;
    }

    private static void AddAllTreasures(SerializedProperty entriesProp, LootTableDefinition def)
    {
        HashSet<TreasureData> existing = new HashSet<TreasureData>();
        for (int i = 0; i < def.entries.Count; i++)
        {
            if (def.entries[i].treasure != null)
            {
                existing.Add(def.entries[i].treasure);
            }
        }

        string[] guids = AssetDatabase.FindAssets("t:TreasureData");
        for (int i = 0; i < guids.Length; i++)
        {
            TreasureData treasure = AssetDatabase.LoadAssetAtPath<TreasureData>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (treasure != null && !existing.Contains(treasure))
            {
                AddEntry(entriesProp, treasure);
            }
        }
    }

    private static void WriteWeights(SerializedProperty entriesProp, LootTableDefinition def)
    {
        for (int i = 0; i < entriesProp.arraySize && i < def.entries.Count; i++)
        {
            entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("weight").floatValue = def.entries[i].weight;
        }
    }

    private static string Simulate(LootTableDefinition def)
    {
        const int rolls = 10000;
        System.Random rng = new System.Random();
        int[] counts = new int[def.entries.Count];
        int nothing = 0;

        for (int i = 0; i < rolls; i++)
        {
            if (LootRoller.TryRoll(def, rng, null, out LootEntry picked))
            {
                counts[def.entries.IndexOf(picked)]++;
            }
            else
            {
                nothing++;
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Simulated {rolls:N0} rolls:");
        sb.AppendLine($"  Nothing: {nothing * 100f / rolls:0.0}%");
        for (int i = 0; i < counts.Length; i++)
        {
            if (def.entries[i].treasure != null && counts[i] > 0)
            {
                sb.AppendLine($"  {def.entries[i].treasure.DisplayName}: {counts[i] * 100f / rolls:0.0}%");
            }
        }
        return sb.ToString().TrimEnd();
    }
}
