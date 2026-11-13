using UnityEngine;

public enum ItemType
{
    Weapon,
    Tool,
    Consumable,
    Scrap
}

/// <summary>
/// Modular definition for all inventory items, weapons, consumables, and scrap.
/// Designed for extensibility: new item types and behaviors can override OnUse or hook into combat.
/// </summary>
[CreateAssetMenu(fileName = "NewItemData", menuName = "DungeonCrawler/ItemData")]
public class ItemData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string itemId = "item_id";
    [SerializeField] private string displayName = "Item Name";
    [TextArea(2, 4)]
    [SerializeField] private string description = "Item Description";
    [SerializeField] private ItemType itemType = ItemType.Weapon;
    [SerializeField] private int maxStack = 1;

    [Header("UI Appearance")]
    [SerializeField] private Color slotColor = new Color(0.6f, 0.6f, 0.6f);
    [SerializeField] private Sprite icon;

    [Header("Combat (Weapons / Tools / Fists)")]
    [SerializeField] private int damage = 25;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private float attackWindup = 0.25f;
    [SerializeField] private float attackRecovery = 0.55f;
    [SerializeField] private float attackRange = 2.4f;
    [SerializeField] private float knockbackForce = 7f;

    [Header("Shop Economy")]
    [SerializeField] private int buyPrice = 25;

    [Header("World Prefabs")]
    [SerializeField] private GameObject worldPrefab;
    [SerializeField] private GameObject equippedPrefab;

    public string ItemId
    {
        get
        {
            return itemId;
        }
    }

    public string DisplayName
    {
        get
        {
            return displayName;
        }
    }

    public string Description
    {
        get
        {
            return description;
        }
    }

    public ItemType Type
    {
        get
        {
            return itemType;
        }
    }

    public int MaxStack
    {
        get
        {
            return maxStack;
        }
    }

    public Color SlotColor
    {
        get
        {
            return slotColor;
        }
    }

    public Sprite Icon
    {
        get
        {
            return icon;
        }
    }

    public int Damage
    {
        get
        {
            return damage;
        }
    }

    public float AttackCooldown
    {
        get
        {
            return attackCooldown;
        }
    }

    public float AttackWindup
    {
        get
        {
            return attackWindup > 0f ? attackWindup : 0.25f;
        }
    }

    public float AttackRecovery
    {
        get
        {
            return attackRecovery > 0f ? attackRecovery : 0.55f;
        }
    }

    public float AttackRange
    {
        get
        {
            return attackRange;
        }
    }

    public float KnockbackForce
    {
        get
        {
            return knockbackForce;
        }
    }

    public GameObject WorldPrefab
    {
        get
        {
            return worldPrefab;
        }
    }

    public GameObject EquippedPrefab
    {
        get
        {
            return equippedPrefab;
        }
    }

    public int BuyPrice
    {
        get
        {
            return buyPrice;
        }
    }

    /// <summary>
    /// Factory helper for creating runtime ItemData definitions in code without needing asset files.
    /// </summary>
    public static ItemData CreateRuntime(
        string id,
        string name,
        string desc,
        ItemType type,
        int dmg,
        float cooldown,
        float range,
        float knockback,
        Color color,
        int stack = 1,
        int price = 25,
        float windup = 0.25f,
        float recovery = 0.55f)
    {
        ItemData item = CreateInstance<ItemData>();
        item.itemId = id;
        item.displayName = name;
        item.description = desc;
        item.itemType = type;
        item.damage = dmg;
        item.attackCooldown = cooldown;
        item.attackRange = range;
        item.knockbackForce = knockback;
        item.slotColor = color;
        item.maxStack = stack;
        item.buyPrice = price;
        item.attackWindup = windup;
        item.attackRecovery = recovery;
        return item;
    }

    /// <summary>
    /// Modular hook for custom item usage (e.g. eating food, injecting medkit, turning on flashlight).
    /// Returns true if the item was consumed/used.
    /// </summary>
    public virtual bool OnUse(GameObject user)
    {
        return false;
    }
}
