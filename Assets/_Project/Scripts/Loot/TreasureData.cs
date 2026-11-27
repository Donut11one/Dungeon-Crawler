using UnityEngine;

/// <summary>
/// Reusable, data-only definition of a treasure (name, value, rarity, look and audio).
/// Create new ones with Assets > Create > Dungeon Crawler > Loot > Treasure Data, then add them to any
/// loot table. No code changes are needed to introduce a new treasure.
/// </summary>
[CreateAssetMenu(fileName = "NewTreasure", menuName = "Dungeon Crawler/Loot/Treasure Data", order = 100)]
public class TreasureData : ScriptableObject
{
    public const int MaxIdLength = 48;

    [Header("Identity")]
    [Tooltip("Unique id used to identify this treasure over the network. Filled in automatically from the asset name. Lowercase letters, digits, underscore and dash only.")]
    [SerializeField] private string treasureId;
    [SerializeField] private string displayName = "New Treasure";
    [TextArea(2, 4)]
    [SerializeField] private string description;
    [SerializeField] private TreasureRarity rarity = TreasureRarity.Common;

    [Header("Value")]
    [Tooltip("Run-money added to the collector's PlayerWallet. It is banked when the player extracts.")]
    [Min(0)]
    [SerializeField] private int value = 10;

    [Header("Appearance")]
    [Tooltip("Model shown in the world. Leave empty to use a generic placeholder. Colliders on this model are ignored.")]
    [SerializeField] private GameObject visualPrefab;
    [Min(0.05f)]
    [SerializeField] private float visualScale = 1f;
    [Tooltip("Height of the model above its glowing pedestal.")]
    [Min(0f)]
    [SerializeField] private float hoverHeight = 0.12f;
    [Tooltip("Degrees per second the model spins.")]
    [SerializeField] private float spinSpeed = 60f;
    [Min(0f)]
    [SerializeField] private float bobHeight = 0.05f;
    [Min(0f)]
    [SerializeField] private float bobSpeed = 2f;
    [Tooltip("Colour of the pedestal glow and pickup prompt. When off, the rarity colour is used.")]
    [SerializeField] private bool useCustomAccentColor;
    [SerializeField] private Color customAccentColor = Color.white;

    [Header("Audio")]
    [Tooltip("Optional pickup sound. When empty a short procedural chime (pitched by rarity) is played.")]
    [SerializeField] private AudioClip pickupSound;
    [Range(0f, 1f)]
    [SerializeField] private float pickupVolume = 0.8f;

    /// <summary>Stable id used for network sync and lookups. Falls back to the sanitised asset name.</summary>
    public string TreasureId
    {
        get
        {
            return string.IsNullOrWhiteSpace(treasureId) ? SanitizeId(name) : treasureId;
        }
    }

    public string DisplayName { get { return string.IsNullOrWhiteSpace(displayName) ? name : displayName; } }
    public string Description { get { return description; } }
    public TreasureRarity Rarity { get { return rarity; } }
    public int Value { get { return Mathf.Max(0, value); } }
    public GameObject VisualPrefab { get { return visualPrefab; } }
    public float VisualScale { get { return Mathf.Max(0.05f, visualScale); } }
    public float HoverHeight { get { return hoverHeight; } }
    public float SpinSpeed { get { return spinSpeed; } }
    public float BobHeight { get { return bobHeight; } }
    public float BobSpeed { get { return bobSpeed; } }
    public AudioClip PickupSound { get { return pickupSound; } }
    public float PickupVolume { get { return pickupVolume; } }

    public Color AccentColor
    {
        get { return useCustomAccentColor ? customAccentColor : rarity.GetColor(); }
    }

    /// <summary>Lower-cases and strips everything except a-z, 0-9, underscore and dash (spaces become underscores).</summary>
    public static string SanitizeId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw.Trim().ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
            {
                sb.Append(c);
            }
            else if (c == ' ')
            {
                sb.Append('_');
            }

            if (sb.Length >= MaxIdLength)
            {
                break;
            }
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(treasureId))
        {
            treasureId = SanitizeId(name);
        }
        else
        {
            string clean = SanitizeId(treasureId);
            if (clean != treasureId)
            {
                treasureId = clean;
            }
        }

        if (value < 0)
        {
            value = 0;
        }
    }
#endif
}
