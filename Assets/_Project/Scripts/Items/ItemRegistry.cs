using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static lookup registry for all ItemData definitions.
/// Parses and populates items dynamically from Items.json.
/// Also discovers any custom ScriptableObject ItemData assets placed in Resources.
/// </summary>
public static class ItemRegistry
{
    [System.Serializable]
    private class ItemJsonData
    {
        public string itemId;
        public string displayName;
        public string description;
        public string itemType;
        public int maxStack = 1;
        public int damage = 20;
        public float attackCooldown = 1.2f;
        public float attackWindup = 0.25f;
        public float attackRecovery = 0.55f;
        public float attackRange = 2.4f;
        public float knockbackForce = 6f;
        public int buyPrice = 25;
        public string slotColorHex = "#B0B0B0";
    }

    [System.Serializable]
    private class ItemJsonDatabase
    {
        public ItemJsonData[] items;
    }

    private static readonly Dictionary<string, ItemData> Registry = new();
    private static bool initialized = false;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        Registry.Clear();

        // 1. Load and parse from Resources/Items.json
        bool loadedFromJson = LoadFromJson();

        // 2. If JSON loading produced no items, load default fallback items
        if (!loadedFromJson || Registry.Count == 0)
        {
            RegisterDefaultItems();
        }

        // 3. Also discover any ScriptableObject ItemData assets loaded in Resources if present
        ItemData[] assets = Resources.LoadAll<ItemData>("");
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] != null && !string.IsNullOrEmpty(assets[i].ItemId))
            {
                Registry[assets[i].ItemId] = assets[i];
            }
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

        TextAsset asset = Resources.Load<TextAsset>("Items");
        if (asset != null)
        {
            jsonText = asset.text;
        }
        else
        {
            string directPath = System.IO.Path.Combine(Application.dataPath, "_Project/Resources/Items.json");
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
            trimmed = "{\"items\":" + trimmed + "}";
        }

        ItemJsonDatabase db = JsonUtility.FromJson<ItemJsonDatabase>(trimmed);
        if (db == null || db.items == null || db.items.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < db.items.Length; i++)
        {
            ItemJsonData data = db.items[i];
            if (data == null || string.IsNullOrEmpty(data.itemId))
            {
                continue;
            }

            ItemType type = ItemType.Weapon;
            if (!string.IsNullOrEmpty(data.itemType))
            {
                System.Enum.TryParse(data.itemType, true, out type);
            }

            Color slotColor = Color.gray;
            if (!string.IsNullOrEmpty(data.slotColorHex))
            {
                ColorUtility.TryParseHtmlString(data.slotColorHex, out slotColor);
            }

            ItemData item = ItemData.CreateRuntime(
                data.itemId,
                data.displayName ?? data.itemId,
                data.description ?? string.Empty,
                type,
                data.damage,
                data.attackCooldown,
                data.attackRange,
                data.knockbackForce,
                slotColor,
                data.maxStack > 0 ? data.maxStack : 1,
                data.buyPrice > 0 ? data.buyPrice : 25,
                data.attackWindup > 0f ? data.attackWindup : 0.25f,
                data.attackRecovery > 0f ? data.attackRecovery : 0.55f
            );

            Register(item);
        }

        Debug.Log($"[ItemRegistry] Successfully loaded {Registry.Count} items from Items.json");
        return true;
    }

    private static void RegisterDefaultItems()
    {
        Register(ItemData.CreateRuntime(
            "scrap_pipe",
            "Iron Pipe",
            "Heavy salvaged plumbing pipe. Sturdy enough to crack monster skulls.",
            ItemType.Weapon,
            35,
            0.55f,
            2.6f,
            9f,
            new Color(0.7f, 0.72f, 0.78f)
        ));

        Register(ItemData.CreateRuntime(
            "crowbar",
            "Crowbar",
            "Classic utility prybar. Fast swings with moderate knockback.",
            ItemType.Weapon,
            28,
            0.45f,
            2.4f,
            7.5f,
            new Color(0.85f, 0.35f, 0.3f)
        ));

        Register(ItemData.CreateRuntime(
            "medkit",
            "First Aid",
            "Emergency medical kit. Restores contractor health.",
            ItemType.Consumable,
            0,
            1.0f,
            1.5f,
            0f,
            new Color(0.25f, 0.85f, 0.35f),
            5
        ));
    }

    public static void Register(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.ItemId))
        {
            return;
        }

        Registry[item.ItemId] = item;
    }

    public static ItemData GetItem(string id)
    {
        if (!initialized)
        {
            Initialize();
        }

        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        Registry.TryGetValue(id, out ItemData item);
        return item;
    }

    public static bool HasItem(string id)
    {
        if (!initialized)
        {
            Initialize();
        }

        return !string.IsNullOrEmpty(id) && Registry.ContainsKey(id);
    }

    public static List<ItemData> GetAllItems()
    {
        if (!initialized)
        {
            Initialize();
        }

        return new List<ItemData>(Registry.Values);
    }
}
