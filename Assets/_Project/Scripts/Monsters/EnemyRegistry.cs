using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static lookup registry for all enemy archetypes.
/// Parses and populates enemies dynamically from Enemies.json.
/// Allows instant tuning of monster health, speeds, attack cooldowns, and names via JSON.
/// </summary>
public static class EnemyRegistry
{
    [System.Serializable]
    private class EnemyJsonDatabase
    {
        public EnemyData[] enemies;
    }

    private static readonly Dictionary<string, EnemyData> Registry = new();
    private static readonly List<EnemyData> EnemyList = new();
    private static bool initialized = false;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        Registry.Clear();
        EnemyList.Clear();

        bool loadedFromJson = LoadFromJson();

        if (!loadedFromJson || Registry.Count == 0)
        {
            RegisterDefaultEnemies();
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

        TextAsset asset = Resources.Load<TextAsset>("Enemies");
        if (asset != null)
        {
            jsonText = asset.text;
        }
        else
        {
            string directPath = System.IO.Path.Combine(Application.dataPath, "_Project/Resources/Enemies.json");
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
            trimmed = "{\"enemies\":" + trimmed + "}";
        }

        EnemyJsonDatabase db = JsonUtility.FromJson<EnemyJsonDatabase>(trimmed);
        if (db == null || db.enemies == null || db.enemies.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < db.enemies.Length; i++)
        {
            EnemyData data = db.enemies[i];
            if (data == null || string.IsNullOrEmpty(data.enemyId))
            {
                continue;
            }

            Register(data);
        }

        Debug.Log($"[EnemyRegistry] Successfully loaded {Registry.Count} enemy archetypes from Enemies.json");
        return true;
    }

    private static void RegisterDefaultEnemies()
    {
        EnemyData defaultEnemy = new EnemyData
        {
            enemyId = "basic_abnormality",
            displayName = "Containment Abnormality",
            description = "A twitching red bio-mechanical entity contained in the lower basement.",
            maxHealth = 60,
            chaseSpeed = 4.2f,
            wanderSpeed = 2.2f,
            aggroRange = 12.0f,
            loseTargetRange = 18.0f,
            directProximityRange = 4.5f,
            attackRange = 2.2f,
            attackDamage = 20,
            attackCooldown = 4.5f,
            attackWindup = 0.35f,
            attackRecovery = 1.2f,
            attackKnockback = 8.0f,
            bodyColorHex = "#B32626",
            eyeColorHex = "#FFEE33"
        };
        Register(defaultEnemy);
    }

    public static void Register(EnemyData enemy)
    {
        if (enemy == null || string.IsNullOrEmpty(enemy.enemyId))
        {
            return;
        }

        Registry[enemy.enemyId] = enemy;
        if (!EnemyList.Contains(enemy))
        {
            EnemyList.Add(enemy);
        }
    }

    public static EnemyData GetEnemy(string enemyId)
    {
        if (!initialized)
        {
            Initialize();
        }

        if (string.IsNullOrEmpty(enemyId))
        {
            return GetDefaultEnemy();
        }

        if (Registry.TryGetValue(enemyId, out EnemyData data))
        {
            return data;
        }

        return GetDefaultEnemy();
    }

    public static EnemyData GetDefaultEnemy()
    {
        if (!initialized)
        {
            Initialize();
        }

        if (EnemyList.Count > 0)
        {
            return EnemyList[0];
        }

        return new EnemyData();
    }

    public static List<EnemyData> GetAllEnemies()
    {
        if (!initialized)
        {
            Initialize();
        }

        return new List<EnemyData>(EnemyList);
    }
}
