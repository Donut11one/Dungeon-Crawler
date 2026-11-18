using System.Collections.Generic;
using UnityEngine;

public enum WallSide
{
    North,
    South,
    East,
    West
}

[System.Serializable]
public class DoorwayDefinition
{
    public WallSide side = WallSide.North;
    public float width = 3.2f;
    public float offset = 0f;
    public bool hasDoor = false;
    public string doorChannel = "";
    public bool doorStartsOpen = false;

    public DoorwayDefinition()
    {
    }

    public DoorwayDefinition(WallSide side, float width = 3.2f, float offset = 0f, bool hasDoor = false, string doorChannel = "", bool doorStartsOpen = false)
    {
        this.side = side;
        this.width = width;
        this.offset = offset;
        this.hasDoor = hasDoor;
        this.doorChannel = doorChannel;
        this.doorStartsOpen = doorStartsOpen;
    }
}

[System.Serializable]
public class LeverPlacementData
{
    public string leverName = "Security Lever";
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localEulerAngles = Vector3.zero;
    public string mechanismChannel = "";
    public float resetDelay = 0f;

    public LeverPlacementData()
    {
    }

    public LeverPlacementData(string name, Vector3 pos, Vector3 rot, string channel, float reset = 0f)
    {
        leverName = name;
        localPosition = pos;
        localEulerAngles = rot;
        mechanismChannel = channel;
        resetDelay = reset;
    }
}

[System.Serializable]
public class DoorPlacementData
{
    public string doorName = "Security Blast Door";
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localEulerAngles = Vector3.zero;
    public string mechanismChannel = "";
    public bool startsOpen = false;

    public DoorPlacementData()
    {
    }

    public DoorPlacementData(string name, Vector3 pos, Vector3 rot, string channel, bool open = false)
    {
        doorName = name;
        localPosition = pos;
        localEulerAngles = rot;
        mechanismChannel = channel;
        startsOpen = open;
    }
}

[System.Serializable]
public class EnemyPlacementData
{
    public Vector3 localPosition = Vector3.zero;
    public string enemyId = "";

    public EnemyPlacementData()
    {
    }

    public EnemyPlacementData(Vector3 pos, string id = "")
    {
        localPosition = pos;
        enemyId = id;
    }
}

[System.Serializable]
public class RoomLayoutData
{
    public string roomId = "Room";
    public string roomLabel = "Room";
    public Vector3 position = Vector3.zero;
    public Vector3 dimensions = new Vector3(10f, 4f, 10f);

    public Color floorColor = new Color(0.25f, 0.25f, 0.28f);
    public Color wallColor = new Color(0.35f, 0.32f, 0.30f);
    public Color ceilingColor = new Color(0.20f, 0.20f, 0.22f);

    public List<DoorwayDefinition> doorways = new List<DoorwayDefinition>();
    public List<LeverPlacementData> levers = new List<LeverPlacementData>();
    public List<DoorPlacementData> doors = new List<DoorPlacementData>();
    public List<Vector3> coinSpawns = new List<Vector3>();
    public List<EnemyPlacementData> enemySpawns = new List<EnemyPlacementData>();
    public List<Vector3> playerSpawns = new List<Vector3>();

    public bool hasExtractionPoint = false;
    public Vector3 extractionLocalPosition = Vector3.zero;
}

/// <summary>
/// ScriptableObject asset defining a complete dungeon layout.
/// Used by level designers and developers to create and tune maps, rooms,
/// doorways, unlockable doors, levers, enemies, coins, and extraction zones
/// without writing any code.
/// </summary>
[CreateAssetMenu(fileName = "NewDungeonLayout", menuName = "Dungeon Crawler/Dungeon Layout")]
public class DungeonLayoutData : ScriptableObject
{
    [Header("Layout Info")]
    public string layoutName = "Custom Dungeon Facility";
    public float defaultWallHeight = 4f;
    public float defaultWallThickness = 0.3f;
    public float defaultDoorWidth = 3.2f;

    [Header("Rooms")]
    public List<RoomLayoutData> rooms = new List<RoomLayoutData>();

    public RoomLayoutData AddRoom(string id, string label, Vector3 pos, Vector3 dims)
    {
        RoomLayoutData r = new RoomLayoutData
        {
            roomId = id,
            roomLabel = label,
            position = pos,
            dimensions = dims
        };
        rooms.Add(r);
        return r;
    }

    public bool RemoveRoom(string id)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].roomId == id)
            {
                rooms.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    public RoomLayoutData GetRoom(string id)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].roomId == id)
            {
                return rooms[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Returns all distinct mechanism channels used by levers and doors in this layout.
    /// </summary>
    public List<string> GetAllChannels()
    {
        HashSet<string> channels = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        for (int r = 0; r < rooms.Count; r++)
        {
            RoomLayoutData room = rooms[r];
            for (int l = 0; l < room.levers.Count; l++)
            {
                if (!string.IsNullOrEmpty(room.levers[l].mechanismChannel))
                {
                    channels.Add(room.levers[l].mechanismChannel);
                }
            }

            for (int d = 0; d < room.doors.Count; d++)
            {
                if (!string.IsNullOrEmpty(room.doors[d].mechanismChannel))
                {
                    channels.Add(room.doors[d].mechanismChannel);
                }
            }

            for (int dw = 0; dw < room.doorways.Count; dw++)
            {
                if (room.doorways[dw].hasDoor && !string.IsNullOrEmpty(room.doorways[dw].doorChannel))
                {
                    channels.Add(room.doorways[dw].doorChannel);
                }
            }
        }

        return new List<string>(channels);
    }

    /// <summary>
    /// Validates layout consistency: checks for orphan levers (no doors) or orphan doors (no levers).
    /// </summary>
    public List<string> ValidateLayout()
    {
        List<string> issues = new List<string>();
        HashSet<string> leverChannels = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        HashSet<string> doorChannels = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        for (int r = 0; r < rooms.Count; r++)
        {
            RoomLayoutData room = rooms[r];
            for (int l = 0; l < room.levers.Count; l++)
            {
                if (!string.IsNullOrEmpty(room.levers[l].mechanismChannel))
                {
                    leverChannels.Add(room.levers[l].mechanismChannel);
                }
            }

            for (int d = 0; d < room.doors.Count; d++)
            {
                if (!string.IsNullOrEmpty(room.doors[d].mechanismChannel))
                {
                    doorChannels.Add(room.doors[d].mechanismChannel);
                }
            }

            for (int dw = 0; dw < room.doorways.Count; dw++)
            {
                if (room.doorways[dw].hasDoor && !string.IsNullOrEmpty(room.doorways[dw].doorChannel))
                {
                    doorChannels.Add(room.doorways[dw].doorChannel);
                }
            }
        }

        foreach (string leverCh in leverChannels)
        {
            if (!doorChannels.Contains(leverCh))
            {
                issues.Add($"Warning: Lever Channel '{leverCh}' has no corresponding door to open.");
            }
        }

        foreach (string doorCh in doorChannels)
        {
            if (!leverChannels.Contains(doorCh))
            {
                issues.Add($"Warning: Door Channel '{doorCh}' has no lever to unlock it.");
            }
        }

        return issues;
    }

#if UNITY_EDITOR
    public static DungeonLayoutData CreateDefaultFacilityLayoutAsset(string assetPath)
    {
        string dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
        if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/_Project/ScriptableObjects"))
        {
            UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
        }
        if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
        {
            UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Dungeon");
        }

        DungeonLayoutData layout = ScriptableObject.CreateInstance<DungeonLayoutData>();
        layout.layoutName = "Containment Facility Sublevel 1";

        // 1. Main Hall
        RoomLayoutData main = layout.AddRoom("MainHall", "Main Hall", Vector3.zero, new Vector3(16f, 4f, 16f));
        main.doorways.Add(new DoorwayDefinition(WallSide.North, 3.2f, 0f, true, "MainHall_NorthGate", false));
        main.doorways.Add(new DoorwayDefinition(WallSide.East, 3.2f, 0f, false, ""));
        main.doorways.Add(new DoorwayDefinition(WallSide.West, 3.2f, 0f, false, ""));
        main.levers.Add(new LeverPlacementData("Security Lever", new Vector3(3.0f, 1.4f, 7.85f), new Vector3(0f, 180f, 0f), "MainHall_NorthGate"));
        main.playerSpawns.Add(new Vector3(-3f, 1f, -3f));
        main.playerSpawns.Add(new Vector3(3f, 1f, -3f));
        main.playerSpawns.Add(new Vector3(0f, 1f, 3f));
        main.coinSpawns.Add(new Vector3(-5f, 0.8f, 5f));
        main.coinSpawns.Add(new Vector3(5f, 0.8f, 5f));
        main.coinSpawns.Add(new Vector3(0f, 0.8f, -5f));
        main.coinSpawns.Add(new Vector3(-5f, 0.8f, -5f));
        main.coinSpawns.Add(new Vector3(4f, 0.8f, -4f));

        // 2. North Corridor
        RoomLayoutData nCorr = layout.AddRoom("NorthCorridor", "North Corridor", new Vector3(0f, 0f, 15f), new Vector3(4f, 4f, 14f));
        nCorr.doorways.Add(new DoorwayDefinition(WallSide.South, 3.2f, 0f, false, ""));
        nCorr.doorways.Add(new DoorwayDefinition(WallSide.North, 3.2f, 0f, false, ""));
        nCorr.coinSpawns.Add(new Vector3(1f, 0.8f, -2f));
        nCorr.coinSpawns.Add(new Vector3(-1f, 0.8f, 2f));
        nCorr.enemySpawns.Add(new EnemyPlacementData(new Vector3(0f, 0.5f, 0f), "basic_abnormality"));

        // 3. North Storage
        RoomLayoutData nStore = layout.AddRoom("NorthStorage", "North Storage", new Vector3(0f, 0f, 26f), new Vector3(10f, 4f, 8f));
        nStore.doorways.Add(new DoorwayDefinition(WallSide.South, 3.2f, 0f, false, ""));
        nStore.coinSpawns.Add(new Vector3(-3f, 0.8f, -1f));
        nStore.coinSpawns.Add(new Vector3(3f, 0.8f, 1f));
        nStore.coinSpawns.Add(new Vector3(0f, 0.8f, 3f));
        nStore.coinSpawns.Add(new Vector3(-4f, 0.8f, 1f));
        nStore.enemySpawns.Add(new EnemyPlacementData(new Vector3(0f, 0.5f, 0f), "basic_abnormality"));

        // 4. East Corridor
        RoomLayoutData eCorr = layout.AddRoom("EastCorridor", "East Corridor", new Vector3(15f, 0f, 0f), new Vector3(14f, 4f, 4f));
        eCorr.doorways.Add(new DoorwayDefinition(WallSide.West, 3.2f, 0f, false, ""));
        eCorr.doorways.Add(new DoorwayDefinition(WallSide.East, 3.2f, 0f, false, ""));
        eCorr.coinSpawns.Add(new Vector3(-2f, 0.8f, 0f));
        eCorr.coinSpawns.Add(new Vector3(2f, 0.8f, 1f));

        // 5. East Chamber
        RoomLayoutData eChamb = layout.AddRoom("EastChamber", "East Extraction Chamber", new Vector3(26f, 0f, 0f), new Vector3(8f, 4f, 10f));
        eChamb.doorways.Add(new DoorwayDefinition(WallSide.West, 3.2f, 0f, false, ""));
        eChamb.coinSpawns.Add(new Vector3(-1f, 0.8f, -3f));
        eChamb.coinSpawns.Add(new Vector3(1f, 0.8f, 3f));
        eChamb.enemySpawns.Add(new EnemyPlacementData(new Vector3(0f, 0.5f, 3f), "basic_abnormality"));
        eChamb.hasExtractionPoint = true;
        eChamb.extractionLocalPosition = Vector3.zero;

        // 6. West Corridor
        RoomLayoutData wCorr = layout.AddRoom("WestCorridor", "West Corridor", new Vector3(-14f, 0f, 0f), new Vector3(12f, 4f, 4f));
        wCorr.doorways.Add(new DoorwayDefinition(WallSide.East, 3.2f, 0f, false, ""));
        wCorr.doorways.Add(new DoorwayDefinition(WallSide.West, 3.2f, 0f, false, ""));

        // 7. West Dead End
        RoomLayoutData wDead = layout.AddRoom("WestDeadEnd", "West Dead End", new Vector3(-23f, 0f, 0f), new Vector3(6f, 4f, 6f));
        wDead.doorways.Add(new DoorwayDefinition(WallSide.East, 3.2f, 0f, false, ""));
        wDead.coinSpawns.Add(new Vector3(0f, 0.8f, 0f));
        wDead.coinSpawns.Add(new Vector3(2f, 0.8f, 2f));
        wDead.coinSpawns.Add(new Vector3(-1f, 0.8f, -2f));
        wDead.enemySpawns.Add(new EnemyPlacementData(new Vector3(0f, 0.5f, 0f), "basic_abnormality"));

        UnityEditor.AssetDatabase.CreateAsset(layout, assetPath);
        UnityEditor.AssetDatabase.SaveAssets();

        return layout;
    }
#endif
}
