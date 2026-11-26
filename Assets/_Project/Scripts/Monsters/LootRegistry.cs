using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Result of rolling on a monster loot table.
/// </summary>
public struct LootDropResult
{
    public string ItemId;
    public int Count;

    public LootDropResult(string itemId, int count)
    {
        ItemId = itemId;
        Count = count;
    }
}

/// <summary>
/// Static registry that parses monster loot tables from LootTables.json.
/// Cross-references item definitions against ItemRegistry to ensure valid IDs and salvage values.
/// Handles dice rolls and drop counts dynamically upon monster death.
/// </summary>
public static class LootRegistry
{
    [System.Serializable]
    public class DropRule
    {
        public string itemId;
        public float chance = 0.5f;
        public int minCount = 1;
        public int maxCount = 1;
    }

    [System.Serializable]
    public class MonsterLootTable
    {
        public string enemyId;
        public string displayName;
        public int minDrops = 1;
        public int maxDrops = 2;
        public DropRule[] drops;
    }

    [System.Serializable]
    public class LootTableDatabase
    {
        public MonsterLootTable[] lootTables;
    }

    private static readonly Dictionary<string, MonsterLootTable> Tables = new();
    private static bool initialized = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticData()
    {
        initialized = false;
        Tables.Clear();
    }

    public static void Initialize()
    {
        if (initialized && Tables.Count > 0)
        {
            return;
        }

        Tables.Clear();
        bool loaded = LoadFromJson();

        if (!loaded || Tables.Count == 0)
        {
            RegisterDefaultTables();
        }

        initialized = true;
    }

    public static void Reload()
    {
        initialized = false;
        Initialize();
    }

    private static bool LoadFromJson()
    {
        string jsonText = null;

        TextAsset asset = Resources.Load<TextAsset>("LootTables");
        if (asset != null)
        {
            jsonText = asset.text;
        }
        else
        {
            string directPath = System.IO.Path.Combine(Application.dataPath, "_Project/Resources/LootTables.json");
            if (System.IO.File.Exists(directPath))
            {
                jsonText = System.IO.File.ReadAllText(directPath);
            }
        }

        if (string.IsNullOrEmpty(jsonText))
        {
            return false;
        }

        string trimmed = jsonText.Trim();
        if (trimmed.StartsWith("["))
        {
            trimmed = "{\"lootTables\":" + trimmed + "}";
        }

        LootTableDatabase db = JsonUtility.FromJson<LootTableDatabase>(trimmed);
        if (db == null || db.lootTables == null || db.lootTables.Length == 0)
        {
            return false;
        }

        int registeredCount = 0;
        for (int i = 0; i < db.lootTables.Length; i++)
        {
            MonsterLootTable table = db.lootTables[i];
            if (table == null || string.IsNullOrEmpty(table.enemyId))
            {
                continue;
            }

            Tables[table.enemyId] = table;
            registeredCount++;
        }

        Debug.Log($"[LootRegistry] Successfully loaded {registeredCount} monster loot tables from LootTables.json");
        return registeredCount > 0;
    }

    private static void RegisterDefaultTables()
    {
        Tables["default"] = new MonsterLootTable
        {
            enemyId = "default",
            displayName = "Default Monster Table",
            minDrops = 1,
            maxDrops = 2,
            drops = new DropRule[]
            {
                new DropRule { itemId = "scrap_metal", chance = 0.80f, minCount = 1, maxCount = 1 },
                new DropRule { itemId = "salvaged_sensor", chance = 0.50f, minCount = 1, maxCount = 1 },
                new DropRule { itemId = "abnormality_core", chance = 0.35f, minCount = 1, maxCount = 1 }
            }
        };

        Tables["basic_abnormality"] = new MonsterLootTable
        {
            enemyId = "basic_abnormality",
            displayName = "Containment Abnormality",
            minDrops = 1,
            maxDrops = 2,
            drops = new DropRule[]
            {
                new DropRule { itemId = "abnormality_core", chance = 0.65f, minCount = 1, maxCount = 1 },
                new DropRule { itemId = "salvaged_sensor", chance = 0.70f, minCount = 1, maxCount = 1 },
                new DropRule { itemId = "scrap_metal", chance = 0.80f, minCount = 1, maxCount = 2 }
            }
        };

        Tables["flesh_brute"] = new MonsterLootTable
        {
            enemyId = "flesh_brute",
            displayName = "Flesh Brute",
            minDrops = 2,
            maxDrops = 3,
            drops = new DropRule[]
            {
                new DropRule { itemId = "abnormality_core", chance = 1.0f, minCount = 1, maxCount = 1 },
                new DropRule { itemId = "gold_scrap", chance = 0.75f, minCount = 1, maxCount = 1 },
                new DropRule { itemId = "salvaged_sensor", chance = 0.60f, minCount = 1, maxCount = 1 },
                new DropRule { itemId = "scrap_metal", chance = 0.85f, minCount = 1, maxCount = 2 }
            }
        };

        Debug.Log("[LootRegistry] Registered default fallback loot tables.");
    }

    /// <summary>
    /// Rolls on the monster's JSON loot table and returns the dropped item IDs and quantities.
    /// Validates all item IDs against ItemRegistry.
    /// </summary>
    public static List<LootDropResult> RollLoot(string enemyId)
    {
        Initialize();

        MonsterLootTable table = null;
        if (!string.IsNullOrEmpty(enemyId))
        {
            Tables.TryGetValue(enemyId, out table);
        }

        if (table == null)
        {
            Tables.TryGetValue("default", out table);
        }

        if (table == null || table.drops == null || table.drops.Length == 0)
        {
            return new List<LootDropResult> { new LootDropResult("scrap_metal", 1) };
        }

        List<LootDropResult> candidates = new List<LootDropResult>();
        for (int i = 0; i < table.drops.Length; i++)
        {
            DropRule rule = table.drops[i];
            if (rule == null || string.IsNullOrEmpty(rule.itemId))
            {
                continue;
            }

            // Cross-reference against ItemRegistry to verify validity
            ItemData itemData = ItemRegistry.GetItem(rule.itemId);
            if (itemData == null)
            {
                Debug.LogWarning($"[LootRegistry] Loot table for '{table.enemyId}' references unknown itemId '{rule.itemId}'!");
                continue;
            }

            float roll = Random.value;
            if (roll <= rule.chance)
            {
                int minC = Mathf.Max(1, rule.minCount);
                int maxC = Mathf.Max(minC, rule.maxCount);
                int count = Random.Range(minC, maxC + 1);
                candidates.Add(new LootDropResult(rule.itemId, count));
            }
        }

        // Shuffle candidates to avoid order bias
        for (int i = 0; i < candidates.Count; i++)
        {
            int swapIdx = Random.Range(i, candidates.Count);
            LootDropResult temp = candidates[i];
            candidates[i] = candidates[swapIdx];
            candidates[swapIdx] = temp;
        }

        int minDrops = Mathf.Max(0, table.minDrops);
        int maxDrops = Mathf.Max(minDrops, table.maxDrops);

        // Guarantee at least minDrops if roll yielded fewer but drops exist
        if (candidates.Count < minDrops && table.drops.Length > 0)
        {
            for (int i = 0; i < table.drops.Length && candidates.Count < minDrops; i++)
            {
                DropRule fallback = table.drops[i];
                if (fallback != null && !string.IsNullOrEmpty(fallback.itemId) && ItemRegistry.GetItem(fallback.itemId) != null)
                {
                    candidates.Add(new LootDropResult(fallback.itemId, 1));
                }
            }
        }

        int targetCount = Random.Range(minDrops, maxDrops + 1);
        if (candidates.Count > targetCount)
        {
            candidates.RemoveRange(targetCount, candidates.Count - targetCount);
        }

        return candidates;
    }

    /// <summary>
    /// Gets the monster loot table for inspection or debugging.
    /// </summary>
    public static MonsterLootTable GetTable(string enemyId)
    {
        Initialize();
        if (!string.IsNullOrEmpty(enemyId) && Tables.TryGetValue(enemyId, out MonsterLootTable table))
        {
            return table;
        }
        return null;
    }
}
