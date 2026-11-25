using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static lookup registry for all ItemData definitions.
/// Parses items dynamically from Items.json and Scrap.json.
/// Also discovers any custom ScriptableObject ItemData assets placed in Resources.
/// </summary>
public static class ItemRegistry
{
    [System.Serializable]
    public class ItemJsonData
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
    public class ItemJsonDatabase
    {
        public ItemJsonData[] items;
    }

    [System.Serializable]
    public class ScrapJsonData
    {
        public string scrapId;
        public string displayName;
        public string description;
        public int value = 50;
        public int maxStack = 10;
        public string slotColorHex = "#E633D9";
    }

    [System.Serializable]
    public class ScrapJsonDatabase
    {
        public ScrapJsonData[] scraps;
    }

    private static readonly Dictionary<string, ItemData> Registry = new();
    private static bool initialized = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticData()
    {
        initialized = false;
        Registry.Clear();
    }

    public static void Initialize()
    {
        if (initialized && Registry.Count > 0 && ValidateAllItems())
        {
            return;
        }

        Registry.Clear();

        // 1. Load weapons, tools, consumables from Resources/Items.json
        bool loadedItems = false;
        try
        {
            loadedItems = LoadItemsFromJson();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ItemRegistry] Error loading Items.json: {ex.Message}");
            loadedItems = false;
        }

        // 2. Load scrap from Resources/Scrap.json (separate json, no combat stats!)
        bool loadedScraps = false;
        try
        {
            loadedScraps = LoadScrapFromJson();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ItemRegistry] Error loading Scrap.json: {ex.Message}");
            loadedScraps = false;
        }

        // 3. If loading produced no items, load default fallback items
        if (!loadedItems || !loadedScraps || Registry.Count == 0)
        {
            RegisterDefaultItems();
        }

        // 4. Also discover any ScriptableObject ItemData assets loaded in Resources if present
        try
        {
            ItemData[] assets = Resources.LoadAll<ItemData>("");
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && !string.IsNullOrEmpty(assets[i].ItemId))
                {
                    Registry[assets[i].ItemId] = assets[i];
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ItemRegistry] Error loading Resources ItemData assets: {ex.Message}");
        }

        initialized = true;
    }

    private static bool ValidateAllItems()
    {
        if (Registry.Count == 0)
        {
            return false;
        }

        foreach (var kvp in Registry)
        {
            if (kvp.Value == null)
            {
                return false;
            }
        }

        return true;
    }

    public static void Reload()
    {
        initialized = false;
        Initialize();
    }

    private static bool LoadItemsFromJson()
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

        int registeredCount = 0;
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
            registeredCount++;
        }

        Debug.Log($"[ItemRegistry] Successfully loaded {registeredCount} items from Items.json");
        return registeredCount > 0;
    }

    private static bool LoadScrapFromJson()
    {
        string jsonText = null;

        TextAsset asset = Resources.Load<TextAsset>("Scrap");
        if (asset != null)
        {
            jsonText = asset.text;
        }
        else
        {
            string directPath = System.IO.Path.Combine(Application.dataPath, "_Project/Resources/Scrap.json");
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
            trimmed = "{\"scraps\":" + trimmed + "}";
        }

        ScrapJsonDatabase db = JsonUtility.FromJson<ScrapJsonDatabase>(trimmed);
        if (db == null || db.scraps == null || db.scraps.Length == 0)
        {
            return false;
        }

        int registeredCount = 0;
        for (int i = 0; i < db.scraps.Length; i++)
        {
            ScrapJsonData data = db.scraps[i];
            if (data == null || string.IsNullOrEmpty(data.scrapId))
            {
                continue;
            }

            Color slotColor = Color.gray;
            if (!string.IsNullOrEmpty(data.slotColorHex))
            {
                ColorUtility.TryParseHtmlString(data.slotColorHex, out slotColor);
            }

            // Scrap items have NO attack stats or windup
            ItemData scrapItem = ItemData.CreateRuntime(
                data.scrapId,
                data.displayName ?? data.scrapId,
                data.description ?? string.Empty,
                ItemType.Scrap,
                0,      // damage
                0.0f,   // cooldown
                0.0f,   // range
                0.0f,   // knockback
                slotColor,
                data.maxStack > 0 ? data.maxStack : 10,
                data.value > 0 ? data.value : 50,
                0.0f,   // windup
                0.0f    // recovery
            );

            Register(scrapItem);
            registeredCount++;
        }

        Debug.Log($"[ItemRegistry] Successfully loaded {registeredCount} scraps from Scrap.json");
        return registeredCount > 0;
    }

    private static void RegisterDefaultItems()
    {
        // 1. Melee Weapons (2 items: Crowbar and Stun Baton)
        if (!Registry.ContainsKey("crowbar"))
        {
            Register(ItemData.CreateRuntime(
                "crowbar",
                "Crowbar",
                "Classic utility prybar. Fast swings with moderate knockback.",
                ItemType.Weapon,
                28,
                1.10f,
                2.4f,
                7.5f,
                new Color(0.85f, 0.35f, 0.30f),
                1,
                45,
                0.22f,
                0.50f
            ));
        }

        if (!Registry.ContainsKey("stun_baton"))
        {
            Register(ItemData.CreateRuntime(
                "stun_baton",
                "Stun Baton",
                "Security guard riot baton. Delivers immense concussive knockback.",
                ItemType.Weapon,
                22,
                1.30f,
                2.5f,
                14.0f,
                new Color(0.20f, 0.75f, 0.90f),
                1,
                75,
                0.25f,
                0.60f
            ));
        }

        // 2. Ranged Weapons (2 items: Pneumatic Nailgun and Breaching Shotgun)
        if (!Registry.ContainsKey("nailgun"))
        {
            Register(ItemData.CreateRuntime(
                "nailgun",
                "Pneumatic Nailgun",
                "Industrial heavy-duty rivet driver. Rapid-fires high-velocity steel spikes at medium range.",
                ItemType.RangedWeapon,
                38,
                0.45f,
                30.0f,
                8.0f,
                new Color(0.95f, 0.65f, 0.15f),
                1,
                90,
                0.05f,
                0.20f
            ));
        }

        if (!Registry.ContainsKey("shotgun"))
        {
            Register(ItemData.CreateRuntime(
                "shotgun",
                "Breaching Shotgun",
                "Devastating short-range security shotgun. Fires heavy buckshot spreads with massive ragdoll knockback.",
                ItemType.RangedWeapon,
                15,
                1.40f,
                16.0f,
                18.0f,
                new Color(0.90f, 0.20f, 0.20f),
                1,
                120,
                0.10f,
                0.70f
            ));
        }

        // 3. Usable Items & Tools
        if (!Registry.ContainsKey("medkit"))
        {
            Register(ItemData.CreateRuntime(
                "medkit",
                "First Aid Kit",
                "Emergency trauma kit. Left-click to patch wounds and restore 50 HP.",
                ItemType.Consumable,
                0,
                1.50f,
                1.5f,
                0.0f,
                new Color(0.25f, 0.85f, 0.35f),
                5,
                25,
                0.20f,
                0.50f
            ));
        }

        if (!Registry.ContainsKey("flashlight"))
        {
            Register(ItemData.CreateRuntime(
                "flashlight",
                "Tactical Flashlight",
                "Heavy-duty tactical torch. Left-click to illuminate subterranean darkness.",
                ItemType.Tool,
                5,
                0.50f,
                2.0f,
                2.0f,
                new Color(0.95f, 0.90f, 0.25f),
                1,
                15,
                0.10f,
                0.20f
            ));
        }

        // 4. Default Scrap (from Scrap.json - no combat stats!)
        if (!Registry.ContainsKey("abnormality_core"))
        {
            Register(ItemData.CreateRuntime(
                "abnormality_core",
                "Monster Core",
                "Glowing bio-mechanical core salvaged from an abnormality. Highly valuable to research.",
                ItemType.Scrap,
                0,
                0.0f,
                0.0f,
                0.0f,
                new Color(0.90f, 0.20f, 0.85f),
                10,
                100,
                0.0f,
                0.0f
            ));
        }

        if (!Registry.ContainsKey("gold_scrap"))
        {
            Register(ItemData.CreateRuntime(
                "gold_scrap",
                "Relic Circuitry",
                "Intact golden circuit board salvaged from pre-collapse facility machinery.",
                ItemType.Scrap,
                0,
                0.0f,
                0.0f,
                0.0f,
                new Color(1f, 0.84f, 0f),
                10,
                150,
                0.0f,
                0.0f
            ));
        }

        if (!Registry.ContainsKey("salvaged_sensor"))
        {
            Register(ItemData.CreateRuntime(
                "salvaged_sensor",
                "Anomalous Sensor",
                "Pulsing optic array harvested from containment security drones.",
                ItemType.Scrap,
                0,
                0.0f,
                0.0f,
                0.0f,
                new Color(0.20f, 0.90f, 0.60f),
                10,
                75,
                0.0f,
                0.0f
            ));
        }

        if (!Registry.ContainsKey("scrap_metal"))
        {
            Register(ItemData.CreateRuntime(
                "scrap_metal",
                "Salvaged Plating",
                "Reinforced hull scrap retrieved from damaged blast doors.",
                ItemType.Scrap,
                0,
                0.0f,
                0.0f,
                0.0f,
                new Color(0.65f, 0.70f, 0.75f),
                10,
                40,
                0.0f,
                0.0f
            ));
        }
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
        if (!initialized || Registry.Count == 0 || !ValidateAllItems())
        {
            Initialize();
        }

        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (Registry.TryGetValue(id, out ItemData item) && item != null)
        {
            return item;
        }

        // Retry reload once if item was not found
        Reload();
        Registry.TryGetValue(id, out item);
        return item;
    }

    public static bool HasItem(string id)
    {
        if (!initialized || Registry.Count == 0 || !ValidateAllItems())
        {
            Initialize();
        }

        return !string.IsNullOrEmpty(id) && Registry.ContainsKey(id);
    }

    public static List<ItemData> GetAllItems()
    {
        if (!initialized || Registry.Count == 0 || !ValidateAllItems())
        {
            Initialize();
        }

        List<ItemData> list = new List<ItemData>();
        foreach (ItemData item in Registry.Values)
        {
            if (item != null)
            {
                list.Add(item);
            }
        }

        if (list.Count == 0)
        {
            RegisterDefaultItems();
            foreach (ItemData item in Registry.Values)
            {
                if (item != null)
                {
                    list.Add(item);
                }
            }
        }

        return list;
    }
}
