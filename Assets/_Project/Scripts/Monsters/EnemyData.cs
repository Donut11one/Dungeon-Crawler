using UnityEngine;

/// <summary>
/// Data definition for an enemy archetype parsed from Enemies.json.
/// Contains stats, combat numbers, and visual color theming.
/// </summary>
[System.Serializable]
public class EnemyData
{
    public string enemyId = "basic_abnormality";
    public string displayName = "Containment Abnormality";
    public string description = "A twitching red bio-mechanical entity.";
    public int maxHealth = 60;
    public float chaseSpeed = 4.2f;
    public float wanderSpeed = 2.2f;
    public float aggroRange = 12f;
    public float loseTargetRange = 18f;
    public float directProximityRange = 4.5f;
    public float attackRange = 2.2f;
    public int attackDamage = 20;
    public float attackCooldown = 4.5f;
    public float attackWindup = 0.35f;
    public float attackRecovery = 1.2f;
    public float attackKnockback = 8f;
    public string bodyColorHex = "#B32626";
    public string eyeColorHex = "#FFEE33";

    public Color GetBodyColor()
    {
        if (!string.IsNullOrEmpty(bodyColorHex) && ColorUtility.TryParseHtmlString(bodyColorHex, out Color color))
        {
            return color;
        }
        return new Color(0.7f, 0.15f, 0.15f);
    }

    public Color GetEyeColor()
    {
        if (!string.IsNullOrEmpty(eyeColorHex) && ColorUtility.TryParseHtmlString(eyeColorHex, out Color color))
        {
            return color;
        }
        return Color.yellow;
    }
}
