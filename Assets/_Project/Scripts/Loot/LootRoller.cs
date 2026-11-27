using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure probability logic for loot tables (no scene or network dependencies, so it is easy to test).
/// Each roll first decides 'nothing' using the empty chance, then picks one treasure with probability
/// proportional to its weight. Weights are relative, so they never have to total 100.
/// </summary>
public static class LootRoller
{
    /// <summary>An entry can spawn only if it has a treasure and a finite, positive weight.</summary>
    public static bool IsUsable(LootEntry entry)
    {
        return entry != null
            && entry.treasure != null
            && entry.weight > 0f
            && !float.IsNaN(entry.weight)
            && !float.IsInfinity(entry.weight);
    }

    /// <summary>Per-roll chance (0-1) of nothing spawning, ignoring entries.</summary>
    public static float GetEmptyChance01(LootTableDefinition table)
    {
        if (table == null)
        {
            return 1f;
        }

        float percent = float.IsNaN(table.emptyChancePercent) ? 0f : table.emptyChancePercent;
        return Mathf.Clamp01(percent / 100f);
    }

    public static float GetTotalUsableWeight(LootTableDefinition table)
    {
        if (table == null || table.entries == null)
        {
            return 0f;
        }

        float total = 0f;
        for (int i = 0; i < table.entries.Count; i++)
        {
            if (IsUsable(table.entries[i]))
            {
                total += table.entries[i].weight;
            }
        }
        return total;
    }

    /// <summary>
    /// Effective per-roll probability (0-1) of every entry (index-aligned with table.entries) and of nothing.
    /// The values always sum to 1, no matter what the raw weights add up to.
    /// </summary>
    public static void GetEffectiveChances(LootTableDefinition table, List<float> perEntryChances, out float emptyChance)
    {
        perEntryChances.Clear();
        int count = (table != null && table.entries != null) ? table.entries.Count : 0;
        float total = GetTotalUsableWeight(table);

        if (total <= 0f)
        {
            for (int i = 0; i < count; i++)
            {
                perEntryChances.Add(0f);
            }
            emptyChance = 1f;
            return;
        }

        float empty = GetEmptyChance01(table);
        float treasureShare = 1f - empty;
        for (int i = 0; i < count; i++)
        {
            LootEntry entry = table.entries[i];
            perEntryChances.Add(IsUsable(entry) ? treasureShare * (entry.weight / total) : 0f);
        }
        emptyChance = empty;
    }

    /// <summary>
    /// Performs one roll. Returns true and the chosen entry, or false when the roll produced nothing.
    /// takenCounts (optional, one slot per entry) tracks how many of each entry were already picked in the
    /// current batch so maxPerBatch can be honoured; it is updated when an entry is picked.
    /// </summary>
    public static bool TryRoll(LootTableDefinition table, System.Random rng, int[] takenCounts, out LootEntry picked)
    {
        picked = null;
        if (table == null || table.entries == null || rng == null)
        {
            return false;
        }

        float empty = GetEmptyChance01(table);
        if (empty >= 1f)
        {
            return false;
        }
        if (empty > 0f && rng.NextDouble() < empty)
        {
            return false;
        }

        List<LootEntry> entries = table.entries;
        double total = 0.0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsPickable(entries, i, takenCounts))
            {
                total += entries[i].weight;
            }
        }
        if (total <= 0.0)
        {
            return false;
        }

        double roll = rng.NextDouble() * total;
        double cumulative = 0.0;
        int lastPickable = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (!IsPickable(entries, i, takenCounts))
            {
                continue;
            }

            cumulative += entries[i].weight;
            lastPickable = i;
            if (roll < cumulative)
            {
                picked = Take(entries, i, takenCounts);
                return true;
            }
        }

        // Floating-point safety net: the roll landed exactly on the upper bound.
        if (lastPickable >= 0)
        {
            picked = Take(entries, lastPickable, takenCounts);
            return true;
        }
        return false;
    }

    private static bool IsPickable(List<LootEntry> entries, int index, int[] takenCounts)
    {
        LootEntry entry = entries[index];
        if (!IsUsable(entry))
        {
            return false;
        }
        if (entry.maxPerBatch > 0 && takenCounts != null && index < takenCounts.Length && takenCounts[index] >= entry.maxPerBatch)
        {
            return false;
        }
        return true;
    }

    private static LootEntry Take(List<LootEntry> entries, int index, int[] takenCounts)
    {
        if (takenCounts != null && index < takenCounts.Length)
        {
            takenCounts[index]++;
        }
        return entries[index];
    }

    /// <summary>Scales all usable weights proportionally so they add up to targetSum (default 100).</summary>
    public static void NormalizeWeights(LootTableDefinition table, float targetSum = 100f)
    {
        float total = GetTotalUsableWeight(table);
        if (total <= 0f || targetSum <= 0f)
        {
            return;
        }

        float scale = targetSum / total;
        for (int i = 0; i < table.entries.Count; i++)
        {
            if (IsUsable(table.entries[i]))
            {
                table.entries[i].weight *= scale;
            }
        }
    }

    /// <summary>Adds human-readable problems with a table to issues (does not clear it).</summary>
    public static void Validate(LootTableDefinition table, List<string> issues)
    {
        if (table == null)
        {
            issues.Add("Loot table is missing.");
            return;
        }

        if (float.IsNaN(table.emptyChancePercent) || table.emptyChancePercent < 0f || table.emptyChancePercent > 100f)
        {
            issues.Add("Empty chance should be between 0 and 100 (it is clamped automatically).");
        }

        if (table.entries == null || table.entries.Count == 0)
        {
            issues.Add("The table has no treasure entries, so nothing will ever spawn.");
            return;
        }

        HashSet<TreasureData> seen = new HashSet<TreasureData>();
        int usable = 0;
        for (int i = 0; i < table.entries.Count; i++)
        {
            LootEntry entry = table.entries[i];
            if (entry == null)
            {
                issues.Add($"Entry {i} is empty.");
                continue;
            }
            if (entry.treasure == null)
            {
                issues.Add($"Entry {i} has no treasure assigned (ignored).");
                continue;
            }
            if (float.IsNaN(entry.weight) || float.IsInfinity(entry.weight) || entry.weight < 0f)
            {
                issues.Add($"Entry {i} ({entry.treasure.name}) has an invalid weight (ignored).");
                continue;
            }

            if (entry.weight == 0f)
            {
                issues.Add($"Entry {i} ({entry.treasure.name}) has weight 0 and will never spawn.");
            }
            else
            {
                usable++;
            }

            if (!seen.Add(entry.treasure))
            {
                issues.Add($"Entry {i} ({entry.treasure.name}) is listed more than once; the weights simply add together.");
            }
        }

        if (usable == 0)
        {
            issues.Add("No entry has a positive weight, so nothing will ever spawn.");
        }
        else if (table.emptyChancePercent >= 100f)
        {
            issues.Add("Empty chance is 100%, so treasures will never spawn.");
        }
    }
}
