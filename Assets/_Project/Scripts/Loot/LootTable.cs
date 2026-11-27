using System.Collections.Generic;
using UnityEngine;

/// <summary>One treasure that can appear in a loot table, with its relative probability weight.</summary>
[System.Serializable]
public class LootEntry
{
    [Tooltip("Treasure that can spawn from this table.")]
    public TreasureData treasure;

    [Min(0f)]
    [Tooltip("Relative probability. Weights do NOT need to add up to 100 - they are normalised automatically. 0 disables the entry.")]
    public float weight = 1f;

    [Min(0)]
    [Tooltip("Maximum copies of this treasure in one spawn batch at a single location. 0 = unlimited.")]
    public int maxPerBatch = 0;
}

/// <summary>
/// Everything that defines what one spawn location can produce: the chance of nothing, and a weighted list
/// of treasures. Used inline by LootSpawnPoint or wrapped in a reusable LootTable asset.
/// </summary>
[System.Serializable]
public class LootTableDefinition
{
    [Range(0f, 100f)]
    [Tooltip("Per-roll chance (0-100) that nothing spawns. Otherwise a treasure is chosen using the weights below.")]
    public float emptyChancePercent = 25f;

    public List<LootEntry> entries = new List<LootEntry>();
}

/// <summary>
/// Optional reusable preset. Assign one to several spawn locations to share a configuration, or leave the
/// location's own table in charge for fully independent setups.
/// </summary>
[CreateAssetMenu(fileName = "NewLootTable", menuName = "Dungeon Crawler/Loot/Loot Table Preset", order = 101)]
public class LootTable : ScriptableObject
{
    [SerializeField] private LootTableDefinition definition = new LootTableDefinition();

    public LootTableDefinition Definition
    {
        get
        {
            if (definition == null)
            {
                definition = new LootTableDefinition();
            }
            return definition;
        }
    }
}
