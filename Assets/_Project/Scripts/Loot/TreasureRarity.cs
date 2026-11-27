using UnityEngine;

/// <summary>
/// Rarity tier of a treasure. Drives the default glow / UI colour and the pitch of the pickup chime.
/// </summary>
public enum TreasureRarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

public static class TreasureRarityExtensions
{
    public static Color GetColor(this TreasureRarity rarity)
    {
        switch (rarity)
        {
            case TreasureRarity.Uncommon:
                return new Color(0.35f, 0.85f, 0.45f);
            case TreasureRarity.Rare:
                return new Color(0.30f, 0.62f, 1.00f);
            case TreasureRarity.Epic:
                return new Color(0.72f, 0.40f, 1.00f);
            case TreasureRarity.Legendary:
                return new Color(1.00f, 0.72f, 0.18f);
            default:
                return new Color(0.85f, 0.85f, 0.85f);
        }
    }
}
