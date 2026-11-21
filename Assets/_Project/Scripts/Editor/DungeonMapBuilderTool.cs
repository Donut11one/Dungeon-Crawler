#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Unified in-scene visual map builder tool for level designers and developers.
/// Renders an in-scene floating toolbar and handles 3D click-to-place placement
/// for thematic rooms, unlockable blast doors, interactive wall levers,
/// monster spawns, and extraction pads directly in the Unity Scene View.
/// Saves and loads complete dungeon layouts directly to/from DungeonLayoutData assets.
/// </summary>
public class DungeonMapBuilderTool : EditorWindow
{
    public enum BuilderToolMode
    {
        Select = 0,
        Room = 1,
        Door = 2,
        Lever = 3,
        Enemy = 4,
        ExtractionPad = 5,
        PlayerSpawn = 6,
        Erase = 7
    }

    public enum ThematicRoomPreset
    {
        StartingHub_16x16 = 0,
        ExtractionChamber_10x12 = 1,
        ContainmentCell_10x10 = 2,
        CoopPuzzleVault_12x10 = 3,
        StorageDepot_12x12 = 4,
        ConnectingCorridor_4x14 = 5,
        CrossroadsJunction_12x12 = 6,
        CustomBlankRoom = 7
    }

    // ── Tool State ───────────────────────────────────────────────────────────
    private BuilderToolMode currentMode = BuilderToolMode.Select;
    private ThematicRoomPreset roomPreset = ThematicRoomPreset.StartingHub_16x16;
    private Vector2 customRoomSize = new Vector2(10f, 10f);
    private float wallHeight = 4f;
    private float doorWidth = 3.2f;

    // Doorway cut flags for Custom Blank Room
    private bool cutDoorNorth = true;
    private bool cutDoorSouth = false;
    private bool cutDoorEast = false;
    private bool cutDoorWest = false;

    // Mechanism channel linking
    private string activeChannel = "MainHall_NorthGate";
    private float leverResetDelay = 0f;
    private bool doorStartsOpen = false;

    // Grid snapping & rotation
    private bool snapToGrid = true;
    private float snapStep = 1.0f;
    private float placementRotationY = 0f;

    // Monster Archetype selection
    private string selectedEnemyId = "basic_abnormality";
    private int enemyArchetypeIndex = 0;
    private string[] availableEnemyIds = new string[]
    {
        "basic_abnormality"
    };

    // Layout asset link
    private DungeonLayoutData targetLayoutAsset;

    // Prefab references
    private GameObject doorPrefab;
    private GameObject leverPrefab;
    private GameObject enemyPrefab;

    [MenuItem("Tools/Dungeon Crawler/Scene Map Builder", false, 1)]
    public static void OpenWindow()
    {
        DungeonMapBuilderTool window = GetWindow<DungeonMapBuilderTool>("Scene Map Builder");
        window.minSize = new Vector2(360f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        LoadPrefabs();
        RefreshEnemyArchetypes();
        ApplyPresetDoorwayDefaults(roomPreset);

        if (targetLayoutAsset == null)
        {
            DungeonBuilder builder = FindAnyObjectByType<DungeonBuilder>();
            if (builder != null && builder.ActiveLayout != null)
            {
                targetLayoutAsset = builder.ActiveLayout;
            }
            else
            {
                targetLayoutAsset = AssetDatabase.LoadAssetAtPath<DungeonLayoutData>(
                    "Assets/_Project/ScriptableObjects/Dungeon/DefaultDungeonLayout.asset"
                );
            }
        }
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void LoadPrefabs()
    {
        doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Dungeon/DoorPrefab.prefab");
        leverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Dungeon/LeverPrefab.prefab");
        enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Entities/EnemyPrefab.prefab");

        if (doorPrefab == null)
        {
            DoorPrefabCreator.EnsureDoorPrefab(true);
            doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Dungeon/DoorPrefab.prefab");
        }
        if (leverPrefab == null)
        {
            LeverPrefabCreator.EnsureLeverPrefab(true);
            leverPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Dungeon/LeverPrefab.prefab");
        }
    }

    private void RefreshEnemyArchetypes()
    {
        EnemyRegistry.Initialize();
        List<EnemyData> enemies = EnemyRegistry.GetAllEnemies();
        if (enemies != null && enemies.Count > 0)
        {
            availableEnemyIds = new string[enemies.Count];
            for (int i = 0; i < enemies.Count; i++)
            {
                availableEnemyIds[i] = enemies[i].enemyId;
            }
        }
    }

    // ── Editor Window GUI ────────────────────────────────────────────────────
    private void OnGUI()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Dungeon Scene Map Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Use the toolbar floating inside the Scene View to click and place items directly onto the map.\n" +
            "Hotkeys: [R] Rotate 90° | [G] Toggle Snap | [Esc] Select tool",
            MessageType.Info
        );

        EditorGUILayout.Space(6f);
        DrawToolSelectorGUI();

        EditorGUILayout.Space(8f);
        DrawActiveToolSettingsGUI();

        EditorGUILayout.Space(8f);
        DrawChannelLinkingGUI();

        EditorGUILayout.Space(8f);
        DrawGridAndRotationGUI();

        EditorGUILayout.Space(8f);
        DrawSceneActionsGUI();
    }

    private void DrawToolSelectorGUI()
    {
        EditorGUILayout.LabelField("Active Placement Tool", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        DrawToolButton("✋ Select", BuilderToolMode.Select, new Color(0.7f, 0.7f, 0.7f));
        DrawToolButton("🏢 Room", BuilderToolMode.Room, new Color(0.3f, 0.8f, 1f));
        DrawToolButton("🚪 Door", BuilderToolMode.Door, new Color(0.3f, 0.9f, 0.5f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        DrawToolButton("🕹️ Lever", BuilderToolMode.Lever, new Color(1f, 0.8f, 0.2f));
        DrawToolButton("👾 Monster", BuilderToolMode.Enemy, new Color(1f, 0.4f, 0.4f));
        DrawToolButton("🎯 Extraction", BuilderToolMode.ExtractionPad, new Color(0.4f, 1f, 0.6f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        DrawToolButton("🚩 Player Spawn", BuilderToolMode.PlayerSpawn, new Color(0.5f, 0.7f, 1f));
        DrawToolButton("🧹 Erase", BuilderToolMode.Erase, new Color(1f, 0.3f, 0.3f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolButton(string label, BuilderToolMode mode, Color activeColor)
    {
        bool isSelected = currentMode == mode;
        GUI.backgroundColor = isSelected ? activeColor : Color.white;
        if (GUILayout.Button(label, GUILayout.Height(30f)))
        {
            currentMode = mode;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawActiveToolSettingsGUI()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Tool Configuration: {currentMode}", EditorStyles.boldLabel);

        switch (currentMode)
        {
            case BuilderToolMode.Room:
                ThematicRoomPreset prevPreset = roomPreset;
                roomPreset = (ThematicRoomPreset)EditorGUILayout.EnumPopup("Preset", roomPreset);
                if (roomPreset != prevPreset)
                {
                    ApplyPresetDoorwayDefaults(roomPreset);
                }

                if (roomPreset == ThematicRoomPreset.CustomBlankRoom)
                {
                    customRoomSize = EditorGUILayout.Vector2Field("Size (W, D)", customRoomSize);
                }
                else
                {
                    DrawPresetDescription(roomPreset);
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Doorway Openings:", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Check which walls have open doorways cut into them. Rotate room with [R].", MessageType.None);
                EditorGUILayout.BeginHorizontal();
                cutDoorNorth = EditorGUILayout.ToggleLeft("N", cutDoorNorth, GUILayout.Width(45f));
                cutDoorSouth = EditorGUILayout.ToggleLeft("S", cutDoorSouth, GUILayout.Width(45f));
                cutDoorEast = EditorGUILayout.ToggleLeft("E", cutDoorEast, GUILayout.Width(45f));
                cutDoorWest = EditorGUILayout.ToggleLeft("W", cutDoorWest, GUILayout.Width(45f));
                EditorGUILayout.EndHorizontal();
                break;

            case BuilderToolMode.Door:
                activeChannel = EditorGUILayout.TextField("Lever Channel", activeChannel);
                doorStartsOpen = EditorGUILayout.Toggle("Start Open", doorStartsOpen);
                EditorGUILayout.HelpBox("Click any doorway opening to place an unlockable blast door.", MessageType.None);
                break;

            case BuilderToolMode.Lever:
                activeChannel = EditorGUILayout.TextField("Target Channel", activeChannel);
                leverResetDelay = EditorGUILayout.FloatField("Reset Delay (0 = Toggle)", leverResetDelay);
                EditorGUILayout.HelpBox("Click near any wall to place a lever automatically snapped flush against the wall.", MessageType.None);
                break;

            case BuilderToolMode.Enemy:
                if (availableEnemyIds != null && availableEnemyIds.Length > 0)
                {
                    enemyArchetypeIndex = EditorGUILayout.Popup("Archetype", enemyArchetypeIndex, availableEnemyIds);
                    selectedEnemyId = availableEnemyIds[Mathf.Clamp(enemyArchetypeIndex, 0, availableEnemyIds.Length - 1)];
                }
                break;

            case BuilderToolMode.Erase:
                EditorGUILayout.HelpBox("Click any placed object in the Scene View to delete it.", MessageType.Warning);
                break;

            case BuilderToolMode.Select:
            default:
                EditorGUILayout.HelpBox("Select mode active. Click any tool above to start placing objects.", MessageType.None);
                break;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawPresetDescription(ThematicRoomPreset preset)
    {
        string desc = preset switch
        {
            ThematicRoomPreset.StartingHub_16x16 => "🚀 Starting Hub: Safe starting airlock with 4 player spawns, and exit security door.",
            ThematicRoomPreset.ExtractionChamber_10x12 => "🎯 Extraction Chamber: Extraction pad at center, emergency lighting.",
            ThematicRoomPreset.ContainmentCell_10x10 => "🔒 Containment Cell: Locked blast door, exterior lever, and containment monster inside.",
            ThematicRoomPreset.CoopPuzzleVault_12x10 => "🤝 Co-op Puzzle Vault: Sealed vault with 2 timed levers (4s delay).",
            ThematicRoomPreset.StorageDepot_12x12 => "📦 Storage Depot: Warehouse with 4 corner pillars and 1 lurking monster.",
            ThematicRoomPreset.ConnectingCorridor_4x14 => "🛤️ Connecting Corridor: 4x14 hallway with North and South doorways for chase routes.",
            ThematicRoomPreset.CrossroadsJunction_12x12 => "➕ Crossroads Junction: 4-way intersection connecting four branches of the dungeon.",
            _ => ""
        };
        EditorGUILayout.HelpBox(desc, MessageType.None);
    }

    private void DrawChannelLinkingGUI()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Mechanism Channel Linking", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        activeChannel = EditorGUILayout.TextField("Active Channel", activeChannel);
        if (GUILayout.Button("New", GUILayout.Width(48f)))
        {
            activeChannel = $"Gate_{Random.Range(10, 99)}";
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            $"Doors and Levers placed with channel '{activeChannel}' automatically link together. Dotted glowing lines in the Scene View visualize active links.",
            MessageType.None
        );
        EditorGUILayout.EndVertical();
    }

    private void DrawGridAndRotationGUI()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Placement Controls", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        snapToGrid = EditorGUILayout.Toggle("Grid Snap", snapToGrid);
        if (snapToGrid)
        {
            snapStep = EditorGUILayout.FloatField("Step (m)", snapStep, GUILayout.Width(120f));
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Rotation: {placementRotationY:F0}°");
        if (GUILayout.Button("Rotate 90° [R]", GUILayout.Width(110f)))
        {
            placementRotationY = (placementRotationY + 90f) % 360f;
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("Reset", GUILayout.Width(50f)))
        {
            placementRotationY = 0f;
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawSceneActionsGUI()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Map File Operations", EditorStyles.boldLabel);

        targetLayoutAsset = (DungeonLayoutData)EditorGUILayout.ObjectField("Active Layout Asset", targetLayoutAsset, typeof(DungeonLayoutData), false);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
        if (GUILayout.Button("💾 SAVE MAP", GUILayout.Height(32f)))
        {
            SaveSceneToLayoutAsset();
        }

        GUI.backgroundColor = new Color(0.3f, 0.75f, 1f);
        if (GUILayout.Button("📁 LOAD MAP", GUILayout.Height(32f)))
        {
            LoadLayoutAssetIntoScene();
        }

        GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
        if (GUILayout.Button("➕ NEW MAP", GUILayout.Height(32f)))
        {
            CreateNewMapAsset();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);
        if (GUILayout.Button("🧭 Bake NavMesh", GUILayout.Height(26f)))
        {
            BakeSceneNavMesh();
        }

        EditorGUILayout.EndVertical();
    }

    // ── Scene View Interactive GUI ───────────────────────────────────────────
    private void OnSceneGUI(SceneView sceneView)
    {
        DrawSceneViewToolbar(sceneView);
        DrawChannelConnectionLines();

        if (currentMode == BuilderToolMode.Select)
        {
            return;
        }

        // Prevent Unity default selection gizmos from consuming the click
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        Event e = Event.current;
        Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        Vector3 hitPoint;
        Vector3 hitNormal;
        bool hasHit = RaycastSceneOrFloor(worldRay, out hitPoint, out hitNormal);

        if (!hasHit)
        {
            return;
        }

        Vector3 snappedPoint = SnapPosition(hitPoint);

        // Handle Hotkeys
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.R)
            {
                placementRotationY = (placementRotationY + 90f) % 360f;
                e.Use();
                sceneView.Repaint();
                return;
            }
            if (e.keyCode == KeyCode.G)
            {
                snapToGrid = !snapToGrid;
                e.Use();
                sceneView.Repaint();
                return;
            }
            if (e.keyCode == KeyCode.Escape)
            {
                currentMode = BuilderToolMode.Select;
                e.Use();
                sceneView.Repaint();
                return;
            }
        }

        // Draw 3D Ghost Preview
        DrawPlacementGhost(snappedPoint, hitNormal);

        // Handle Left-Click Placement / Erase
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            ExecutePlacementOrErase(snappedPoint, hitNormal);
            e.Use();
            sceneView.Repaint();
        }
    }

    private void DrawSceneViewToolbar(SceneView sceneView)
    {
        Handles.BeginGUI();

        float barWidth = sceneView.position.width - 24f;
        GUILayout.BeginArea(new Rect(12f, 10f, barWidth, 42f));
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12
        };
        titleStyle.normal.textColor = Color.white;
        EditorGUILayout.LabelField("MAP BUILDER", titleStyle, GUILayout.Width(92f));

        EnsureLayoutAssetTarget();
        string activeMapName = targetLayoutAsset != null ? targetLayoutAsset.layoutName : "None";
        GUIStyle mapLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = new Color(0.4f, 1f, 0.7f) }
        };
        EditorGUILayout.LabelField($"[{activeMapName}]", mapLabelStyle, GUILayout.Width(140f));

        DrawSceneToolbarButton("Select", BuilderToolMode.Select);
        DrawSceneToolbarButton("Room", BuilderToolMode.Room);
        DrawSceneToolbarButton("Door", BuilderToolMode.Door);
        DrawSceneToolbarButton("Lever", BuilderToolMode.Lever);
        DrawSceneToolbarButton("Monster", BuilderToolMode.Enemy);
        DrawSceneToolbarButton("Extraction", BuilderToolMode.ExtractionPad);
        DrawSceneToolbarButton("Erase", BuilderToolMode.Erase);

        GUILayout.FlexibleSpace();

        if (GUILayout.Button($"Rot: {placementRotationY:F0}° [R]", EditorStyles.toolbarButton, GUILayout.Width(82f)))
        {
            placementRotationY = (placementRotationY + 90f) % 360f;
        }

        snapToGrid = GUILayout.Toggle(snapToGrid, "Snap [G]", EditorStyles.toolbarButton, GUILayout.Width(62f));

        // 💾 Prominent Green SAVE MAP Button
        GUI.backgroundColor = new Color(0.2f, 0.9f, 0.4f);
        if (GUILayout.Button("💾 SAVE MAP", EditorStyles.toolbarButton, GUILayout.Width(88f)))
        {
            SaveSceneToLayoutAsset();
        }

        GUI.backgroundColor = new Color(0.3f, 0.75f, 1f);
        if (GUILayout.Button("📁 LOAD", EditorStyles.toolbarButton, GUILayout.Width(58f)))
        {
            LoadLayoutAssetIntoScene();
        }

        GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
        if (GUILayout.Button("➕ NEW", EditorStyles.toolbarButton, GUILayout.Width(54f)))
        {
            CreateNewMapAsset();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();

        if (currentMode == BuilderToolMode.Room)
        {
            GUILayout.BeginArea(new Rect(12f, 44f, barWidth, 26f));
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Preset:", EditorStyles.miniBoldLabel, GUILayout.Width(46f));
            ThematicRoomPreset newPreset = (ThematicRoomPreset)EditorGUILayout.EnumPopup(roomPreset, EditorStyles.toolbarPopup, GUILayout.Width(170f));
            if (newPreset != roomPreset)
            {
                roomPreset = newPreset;
                ApplyPresetDoorwayDefaults(roomPreset);
            }

            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Openings:", EditorStyles.miniBoldLabel, GUILayout.Width(62f));
            cutDoorNorth = GUILayout.Toggle(cutDoorNorth, "N", EditorStyles.toolbarButton, GUILayout.Width(26f));
            cutDoorSouth = GUILayout.Toggle(cutDoorSouth, "S", EditorStyles.toolbarButton, GUILayout.Width(26f));
            cutDoorEast = GUILayout.Toggle(cutDoorEast, "E", EditorStyles.toolbarButton, GUILayout.Width(26f));
            cutDoorWest = GUILayout.Toggle(cutDoorWest, "W", EditorStyles.toolbarButton, GUILayout.Width(26f));

            GUILayout.Space(12f);
            EditorGUILayout.LabelField("Press [R] to rotate openings in 90° increments", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
        else if (currentMode == BuilderToolMode.Door)
        {
            GUILayout.BeginArea(new Rect(12f, 44f, barWidth, 26f));
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Channel:", EditorStyles.miniBoldLabel, GUILayout.Width(54f));
            activeChannel = EditorGUILayout.TextField(activeChannel, EditorStyles.toolbarTextField, GUILayout.Width(140f));
            doorStartsOpen = GUILayout.Toggle(doorStartsOpen, "Start Open", EditorStyles.toolbarButton, GUILayout.Width(76f));
            GUILayout.Space(10f);
            EditorGUILayout.LabelField("✨ Magnetic Snap: Move mouse near doorway opening to auto-align", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        Handles.EndGUI();
    }

    private void DrawSceneToolbarButton(string label, BuilderToolMode mode)
    {
        bool active = currentMode == mode;
        GUI.backgroundColor = active ? new Color(0.2f, 0.85f, 0.4f) : Color.white;
        if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(62f)))
        {
            currentMode = mode;
        }
        GUI.backgroundColor = Color.white;
    }

    private bool RaycastSceneOrFloor(Ray ray, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 200f))
        {
            hitPoint = hit.point;
            hitNormal = hit.normal;
            return true;
        }

        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        float enter;
        if (groundPlane.Raycast(ray, out enter))
        {
            hitPoint = ray.GetPoint(enter);
            hitNormal = Vector3.up;
            return true;
        }

        hitPoint = Vector3.zero;
        hitNormal = Vector3.up;
        return false;
    }

    private Vector3 SnapPosition(Vector3 raw)
    {
        if (!snapToGrid || snapStep <= 0.05f)
        {
            return raw;
        }

        return new Vector3(
            Mathf.Round(raw.x / snapStep) * snapStep,
            raw.y,
            Mathf.Round(raw.z / snapStep) * snapStep
        );
    }

    private void DrawPlacementGhost(Vector3 pos, Vector3 normal)
    {
        Quaternion rot = Quaternion.Euler(0f, placementRotationY, 0f);

        switch (currentMode)
        {
            case BuilderToolMode.Room:
                Vector2 roomDimensions = GetCurrentRoomDimensions();
                Handles.color = new Color(0.2f, 0.8f, 1f, 0.8f);
                Vector3 center = pos + new Vector3(0f, wallHeight * 0.5f, 0f);
                Matrix4x4 savedRoomM = Handles.matrix;
                Handles.matrix = Matrix4x4.TRS(center, rot, Vector3.one);
                Handles.DrawWireCube(Vector3.zero, new Vector3(roomDimensions.x, wallHeight, roomDimensions.y));

                // Bright green doorway opening portals on the ghost preview
                Handles.color = new Color(0.2f, 1f, 0.4f, 0.95f);
                if (cutDoorNorth)
                {
                    Handles.DrawWireCube(new Vector3(0f, 0f, roomDimensions.y * 0.5f), new Vector3(doorWidth, wallHeight * 0.9f, 0.4f));
                }
                if (cutDoorSouth)
                {
                    Handles.DrawWireCube(new Vector3(0f, 0f, -roomDimensions.y * 0.5f), new Vector3(doorWidth, wallHeight * 0.9f, 0.4f));
                }
                if (cutDoorEast)
                {
                    Handles.DrawWireCube(new Vector3(roomDimensions.x * 0.5f, 0f, 0f), new Vector3(0.4f, wallHeight * 0.9f, doorWidth));
                }
                if (cutDoorWest)
                {
                    Handles.DrawWireCube(new Vector3(-roomDimensions.x * 0.5f, 0f, 0f), new Vector3(0.4f, wallHeight * 0.9f, doorWidth));
                }
                Handles.matrix = savedRoomM;

                Handles.color = new Color(0.2f, 0.8f, 1f, 0.2f);
                Handles.DrawSolidDisc(pos, Vector3.up, 1.2f);
                break;

            case BuilderToolMode.Door:
                Vector3 doorGhostPos = pos;
                Quaternion doorGhostRot = rot;
                if (TryFindClosestDoorway(pos, 2.5f, out Vector3 snappedDoorPos, out Quaternion snappedDoorRot))
                {
                    doorGhostPos = snappedDoorPos;
                    doorGhostRot = snappedDoorRot;
                    Handles.color = new Color(0.2f, 1f, 0.4f, 0.95f);
                }
                else
                {
                    Handles.color = new Color(0.3f, 1f, 0.5f, 0.85f);
                }

                Matrix4x4 savedDoorM = Handles.matrix;
                Handles.matrix = Matrix4x4.TRS(doorGhostPos + new Vector3(0f, 1.9f, 0f), doorGhostRot, Vector3.one);
                Handles.DrawWireCube(Vector3.zero, new Vector3(3.2f, 3.8f, 0.6f));
                Handles.matrix = savedDoorM;
                break;

            case BuilderToolMode.Lever:
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f);
                Handles.DrawWireCube(pos + new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.65f, 0.4f));
                Handles.DrawLine(pos, pos + rot * Vector3.forward * 0.8f);
                break;

            case BuilderToolMode.Enemy:
                Handles.color = new Color(1f, 0.25f, 0.25f, 0.9f);
                Handles.DrawWireCube(pos + Vector3.up * 1f, new Vector3(0.8f, 2f, 0.8f));
                break;

            case BuilderToolMode.ExtractionPad:
                Handles.color = new Color(0.1f, 1f, 0.4f, 0.5f);
                Handles.DrawSolidDisc(pos + Vector3.up * 0.05f, Vector3.up, 1.5f);
                Handles.color = Color.white;
                Handles.DrawWireDisc(pos + Vector3.up * 0.05f, Vector3.up, 1.5f);
                break;

            case BuilderToolMode.PlayerSpawn:
                Handles.color = new Color(0.3f, 0.7f, 1f, 0.9f);
                Handles.DrawWireCube(pos + Vector3.up * 1f, new Vector3(0.6f, 2f, 0.6f));
                break;

            case BuilderToolMode.Erase:
                Handles.color = new Color(1f, 0.15f, 0.15f, 0.9f);
                Handles.DrawWireDisc(pos + Vector3.up * 0.1f, Vector3.up, 1.2f);
                break;
        }
    }

    private void DrawChannelConnectionLines()
    {
        DungeonLever[] levers = FindObjectsByType<DungeonLever>(FindObjectsInactive.Exclude);
        DungeonDoor[] doors = FindObjectsByType<DungeonDoor>(FindObjectsInactive.Exclude);

        if (levers == null || doors == null)
        {
            return;
        }

        for (int l = 0; l < levers.Length; l++)
        {
            DungeonLever lever = levers[l];
            if (lever == null || string.IsNullOrEmpty(lever.MechanismChannel))
            {
                continue;
            }

            for (int d = 0; d < doors.Length; d++)
            {
                DungeonDoor door = doors[d];
                if (door == null || string.IsNullOrEmpty(door.MechanismChannel))
                {
                    continue;
                }

                if (string.Equals(lever.MechanismChannel, door.MechanismChannel, System.StringComparison.OrdinalIgnoreCase))
                {
                    Handles.color = new Color(0.3f, 1f, 0.4f, 0.85f);
                    Handles.DrawDottedLine(lever.transform.position, door.transform.position, 4f);
                }
            }
        }
    }

    private Vector2 GetCurrentRoomDimensions()
    {
        return roomPreset switch
        {
            ThematicRoomPreset.StartingHub_16x16 => new Vector2(16f, 16f),
            ThematicRoomPreset.ExtractionChamber_10x12 => new Vector2(10f, 12f),
            ThematicRoomPreset.ContainmentCell_10x10 => new Vector2(10f, 10f),
            ThematicRoomPreset.CoopPuzzleVault_12x10 => new Vector2(12f, 10f),
            ThematicRoomPreset.StorageDepot_12x12 => new Vector2(12f, 12f),
            ThematicRoomPreset.ConnectingCorridor_4x14 => new Vector2(4f, 14f),
            ThematicRoomPreset.CrossroadsJunction_12x12 => new Vector2(12f, 12f),
            ThematicRoomPreset.CustomBlankRoom => customRoomSize,
            _ => new Vector2(10f, 10f)
        };
    }

    // ── Click Execution ──────────────────────────────────────────────────────
    private void ExecutePlacementOrErase(Vector3 pos, Vector3 normal)
    {
        GameObject dungeonRoot = GetOrCreateDungeonRoot();

        if (currentMode == BuilderToolMode.Erase)
        {
            ExecuteEraseAt(pos);
            return;
        }

        Quaternion rot = Quaternion.Euler(0f, placementRotationY, 0f);

        DungeonRoom[] allRooms = FindObjectsByType<DungeonRoom>(FindObjectsInactive.Exclude);
        DungeonRoom targetRoom = FindClosestRoom(pos, allRooms);
        GameObject parentObj = targetRoom != null ? targetRoom.gameObject : dungeonRoot;

        switch (currentMode)
        {
            case BuilderToolMode.Room:
                PlacePresetRoom(dungeonRoot, pos);
                break;

            case BuilderToolMode.Door:
                Vector3 finalDoorPos = pos;
                Quaternion finalDoorRot = rot;
                if (TryFindClosestDoorway(pos, 2.5f, out Vector3 snappedDoorPos, out Quaternion snappedDoorRot))
                {
                    finalDoorPos = snappedDoorPos;
                    finalDoorRot = snappedDoorRot;
                }
                PlaceDoor(parentObj, finalDoorPos, finalDoorRot);
                break;

            case BuilderToolMode.Lever:
                PlaceLever(parentObj, pos, normal, rot);
                break;

            case BuilderToolMode.Enemy:
                PlaceEnemy(parentObj, pos);
                break;

            case BuilderToolMode.ExtractionPad:
                PlaceExtractionPad(parentObj, pos);
                break;

            case BuilderToolMode.PlayerSpawn:
                PlacePlayerSpawn(parentObj, pos);
                break;
        }
    }

    private void ApplyPresetDoorwayDefaults(ThematicRoomPreset preset)
    {
        switch (preset)
        {
            case ThematicRoomPreset.StartingHub_16x16:
                cutDoorNorth = true;
                cutDoorSouth = false;
                cutDoorEast = true;
                cutDoorWest = true;
                break;

            case ThematicRoomPreset.ExtractionChamber_10x12:
                cutDoorNorth = false;
                cutDoorSouth = false;
                cutDoorEast = false;
                cutDoorWest = true;
                break;

            case ThematicRoomPreset.ContainmentCell_10x10:
                cutDoorNorth = false;
                cutDoorSouth = true;
                cutDoorEast = false;
                cutDoorWest = false;
                break;

            case ThematicRoomPreset.CoopPuzzleVault_12x10:
                cutDoorNorth = true;
                cutDoorSouth = false;
                cutDoorEast = false;
                cutDoorWest = false;
                break;

            case ThematicRoomPreset.StorageDepot_12x12:
                cutDoorNorth = false;
                cutDoorSouth = true;
                cutDoorEast = false;
                cutDoorWest = false;
                break;

            case ThematicRoomPreset.ConnectingCorridor_4x14:
                cutDoorNorth = true;
                cutDoorSouth = true;
                cutDoorEast = false;
                cutDoorWest = false;
                break;

            case ThematicRoomPreset.CrossroadsJunction_12x12:
                cutDoorNorth = true;
                cutDoorSouth = true;
                cutDoorEast = true;
                cutDoorWest = true;
                break;

            case ThematicRoomPreset.CustomBlankRoom:
            default:
                cutDoorNorth = true;
                cutDoorSouth = false;
                cutDoorEast = false;
                cutDoorWest = false;
                break;
        }
    }

    private bool TryFindClosestDoorway(Vector3 hitPos, float maxDistance, out Vector3 snappedPos, out Quaternion snappedRot)
    {
        snappedPos = hitPos;
        snappedRot = Quaternion.Euler(0f, placementRotationY, 0f);

        DungeonRoom[] rooms = FindObjectsByType<DungeonRoom>(FindObjectsInactive.Exclude);
        float bestDistSq = maxDistance * maxDistance;
        bool found = false;

        for (int r = 0; r < rooms.Length; r++)
        {
            DungeonRoom rm = rooms[r];
            if (rm.Doorways == null || rm.Doorways.Count == 0)
            {
                continue;
            }

            for (int d = 0; d < rm.Doorways.Count; d++)
            {
                WallSide side = rm.Doorways[d].side;
                Vector3 doorwayWorldCenter = rm.GetWallCenterWorld(side);
                float distSq = (doorwayWorldCenter - hitPos).sqrMagnitude;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    snappedPos = doorwayWorldCenter;
                    snappedRot = side switch
                    {
                        WallSide.North => rm.transform.rotation * Quaternion.identity,
                        WallSide.South => rm.transform.rotation * Quaternion.Euler(0f, 180f, 0f),
                        WallSide.East => rm.transform.rotation * Quaternion.Euler(0f, 90f, 0f),
                        _ => rm.transform.rotation * Quaternion.Euler(0f, -90f, 0f)
                    };
                    found = true;
                }
            }
        }

        return found;
    }

    private void PlacePresetRoom(GameObject dungeonRoot, Vector3 center)
    {
        Vector2 dims = GetCurrentRoomDimensions();
        string presetName = roomPreset.ToString().Split('_')[0];
        string roomLabel = $"{presetName}_{Random.Range(100, 999)}";

        GameObject roomObj = new GameObject(roomLabel);
        roomObj.transform.parent = dungeonRoot.transform;
        roomObj.transform.position = center;
        roomObj.transform.rotation = Quaternion.Euler(0f, placementRotationY, 0f);

        DungeonRoom room = roomObj.AddComponent<DungeonRoom>();
        room.Configure(roomLabel, roomLabel, new Vector3(dims.x, wallHeight, dims.y));

        Color floorCol = new Color(0.25f, 0.25f, 0.28f);
        Color wallCol = new Color(0.35f, 0.32f, 0.30f);
        Color ceilingCol = new Color(0.20f, 0.20f, 0.22f);

        // Floor & Ceiling
        CreatePrimitiveCube(roomObj.transform, "Floor", Vector3.zero, new Vector3(dims.x, 0.2f, dims.y), floorCol);
        CreatePrimitiveCube(roomObj.transform, "Ceiling", new Vector3(0f, wallHeight, 0f), new Vector3(dims.x, 0.2f, dims.y), ceilingCol);

        List<DoorwayDefinition> defs = new List<DoorwayDefinition>();

        if (cutDoorNorth)
        {
            defs.Add(new DoorwayDefinition(WallSide.North, doorWidth, 0f, false, ""));
        }
        if (cutDoorSouth)
        {
            defs.Add(new DoorwayDefinition(WallSide.South, doorWidth, 0f, false, ""));
        }
        if (cutDoorEast)
        {
            defs.Add(new DoorwayDefinition(WallSide.East, doorWidth, 0f, false, ""));
        }
        if (cutDoorWest)
        {
            defs.Add(new DoorwayDefinition(WallSide.West, doorWidth, 0f, false, ""));
        }

        Quaternion roomRot = Quaternion.Euler(0f, placementRotationY, 0f);

        switch (roomPreset)
        {
            case ThematicRoomPreset.StartingHub_16x16:
                PlacePlayerSpawn(roomObj, center + roomRot * new Vector3(-3f, 0f, -3f));
                PlacePlayerSpawn(roomObj, center + roomRot * new Vector3(3f, 0f, -3f));
                PlacePlayerSpawn(roomObj, center + roomRot * new Vector3(0f, 0f, 3f));

                PlaceLever(roomObj, center + roomRot * new Vector3(3.0f, 0f, 7.85f), roomRot * Vector3.back, roomRot * Quaternion.Euler(0f, 180f, 0f));

                if (cutDoorNorth)
                {
                    PlaceDoor(roomObj, center + roomRot * new Vector3(0f, 0f, 8f), roomRot * Quaternion.identity);
                }

                break;

            case ThematicRoomPreset.ExtractionChamber_10x12:
                PlaceExtractionPad(roomObj, center);
                break;

            case ThematicRoomPreset.ContainmentCell_10x10:
                string cellChannel = $"Cell_{Random.Range(10, 99)}";
                activeChannel = cellChannel;

                if (cutDoorSouth)
                {
                    PlaceDoor(roomObj, center + roomRot * new Vector3(0f, 0f, -5f), roomRot * Quaternion.Euler(0f, 180f, 0f));
                    PlaceLever(roomObj, center + roomRot * new Vector3(2.5f, 0f, -5.15f), roomRot * Vector3.forward, roomRot * Quaternion.identity);
                }

                PlaceEnemy(roomObj, center);
                break;

            case ThematicRoomPreset.CoopPuzzleVault_12x10:
                string vaultChannel = $"Vault_{Random.Range(10, 99)}";
                activeChannel = vaultChannel;

                if (cutDoorNorth)
                {
                    PlaceDoor(roomObj, center + roomRot * new Vector3(0f, 0f, 5f), roomRot * Quaternion.identity);
                }

                leverResetDelay = 4.0f;
                PlaceLever(roomObj, center + roomRot * new Vector3(-5.85f, 0f, 0f), roomRot * Vector3.right, roomRot * Quaternion.Euler(0f, 90f, 0f));
                PlaceLever(roomObj, center + roomRot * new Vector3(5.85f, 0f, 0f), roomRot * Vector3.left, roomRot * Quaternion.Euler(0f, -90f, 0f));
                leverResetDelay = 0f;
                break;

            case ThematicRoomPreset.StorageDepot_12x12:
                CreatePrimitiveCube(roomObj.transform, "Pillar_1", new Vector3(-3.5f, 2f, 3.5f), new Vector3(0.8f, 4f, 0.8f), wallCol);
                CreatePrimitiveCube(roomObj.transform, "Pillar_2", new Vector3(3.5f, 2f, 3.5f), new Vector3(0.8f, 4f, 0.8f), wallCol);
                CreatePrimitiveCube(roomObj.transform, "Pillar_3", new Vector3(-3.5f, 2f, -3.5f), new Vector3(0.8f, 4f, 0.8f), wallCol);
                CreatePrimitiveCube(roomObj.transform, "Pillar_4", new Vector3(3.5f, 2f, -3.5f), new Vector3(0.8f, 4f, 0.8f), wallCol);

                PlaceEnemy(roomObj, center + roomRot * new Vector3(0f, 0f, 2f));
                break;

            case ThematicRoomPreset.ConnectingCorridor_4x14:
                break;
        }

        room.SetDoorways(defs);
        room.RebuildWalls(wallHeight, doorWidth, wallCol);

        Undo.RegisterCreatedObjectUndo(roomObj, "Place Room");
        Selection.activeGameObject = roomObj;
    }

    private void PlaceDoor(GameObject parent, Vector3 pos, Quaternion rot)
    {
        if (doorPrefab == null)
        {
            LoadPrefabs();
        }

        GameObject doorObj = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, parent.transform);
        doorObj.transform.position = pos;
        doorObj.transform.rotation = rot;
        doorObj.name = $"BlastDoor_{activeChannel}";

        DungeonDoor door = doorObj.GetComponent<DungeonDoor>();
        if (door != null)
        {
            door.Configure($"Blast Door [{activeChannel}]", activeChannel, doorStartsOpen);
        }

        Undo.RegisterCreatedObjectUndo(doorObj, "Place Blast Door");
        Selection.activeGameObject = doorObj;
    }

    private void PlaceLever(GameObject parent, Vector3 pos, Vector3 normal, Quaternion defaultRot)
    {
        if (leverPrefab == null)
        {
            LoadPrefabs();
        }

        Quaternion leverRot = defaultRot;
        Vector3 leverPos = pos;

        // Wall snapping
        if (Mathf.Abs(normal.y) < 0.3f)
        {
            leverRot = Quaternion.LookRotation(normal, Vector3.up);
            leverPos = new Vector3(pos.x, 1.4f, pos.z);
        }
        else
        {
            leverPos.y = 1.4f;
        }

        GameObject leverObj = (GameObject)PrefabUtility.InstantiatePrefab(leverPrefab, parent.transform);
        leverObj.transform.position = leverPos;
        leverObj.transform.rotation = leverRot;
        leverObj.name = $"Lever_{activeChannel}";

        DungeonLever lever = leverObj.GetComponent<DungeonLever>();
        if (lever != null)
        {
            lever.SetMechanismChannel(activeChannel);
        }

        Undo.RegisterCreatedObjectUndo(leverObj, "Place Lever");
        Selection.activeGameObject = leverObj;
    }

    private void PlaceEnemy(GameObject parent, Vector3 pos)
    {
        if (enemyPrefab == null)
        {
            LoadPrefabs();
        }

        EnemyData data = !string.IsNullOrEmpty(selectedEnemyId) ? EnemyRegistry.GetEnemy(selectedEnemyId) : null;
        int packCount = (data != null && data.packSpawnCount > 1) ? data.packSpawnCount : 1;

        for (int p = 0; p < packCount; p++)
        {
            Vector3 spawnPos = new Vector3(pos.x, pos.y + 0.5f, pos.z);
            if (p > 0)
            {
                Vector2 offset = Random.insideUnitCircle * (1.1f + p * 0.4f);
                spawnPos += new Vector3(offset.x, 0f, offset.y);
            }

            GameObject enemyObj = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, parent.transform);
            enemyObj.transform.position = spawnPos;
            enemyObj.name = $"Enemy_{selectedEnemyId}";

            EnemyHealth eh = enemyObj.GetComponent<EnemyHealth>();
            if (eh != null && data != null)
            {
                eh.ApplyEnemyData(data);
            }

            Undo.RegisterCreatedObjectUndo(enemyObj, "Place Enemy");
            if (p == 0)
            {
                Selection.activeGameObject = enemyObj;
            }
        }
    }

    private void PlaceExtractionPad(GameObject parent, Vector3 pos)
    {
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "ExtractionPoint";
        pad.transform.SetParent(parent.transform, false);
        pad.transform.position = new Vector3(pos.x, pos.y + 0.05f, pos.z);
        pad.transform.localRotation = Quaternion.identity;
        pad.transform.localScale = new Vector3(3f, 0.1f, 3f);

        Renderer rend = pad.GetComponent<Renderer>();
        if (rend != null)
        {
            MaterialHelper.ApplyMaterial(rend, new Color(0.1f, 0.85f, 0.4f, 0.6f));
        }

        Collider col = pad.GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        pad.AddComponent<ExtractionPoint>();

        Undo.RegisterCreatedObjectUndo(pad, "Place Extraction Pad");
        Selection.activeGameObject = pad;
    }

    private void PlacePlayerSpawn(GameObject parent, Vector3 pos)
    {
        GameObject marker = new GameObject($"PlayerSpawn_{Random.Range(10, 99)}");
        marker.transform.SetParent(parent.transform, false);
        marker.transform.position = new Vector3(pos.x, pos.y + 1f, pos.z);
        marker.transform.localRotation = Quaternion.identity;

        Undo.RegisterCreatedObjectUndo(marker, "Place Player Spawn");
        Selection.activeGameObject = marker;
    }

    private void ExecuteEraseAt(Vector3 clickPos)
    {
        Collider[] hits = Physics.OverlapSphere(clickPos, 1.5f);
        if (hits == null || hits.Length == 0)
        {
            return;
        }

        GameObject targetToDelete = null;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform tr = hits[i].transform;
            if (tr.GetComponentInParent<DungeonRoom>() != null)
            {
                targetToDelete = tr.GetComponentInParent<DungeonRoom>().gameObject;
                break;
            }
            if (tr.GetComponentInParent<DungeonDoor>() != null)
            {
                targetToDelete = tr.GetComponentInParent<DungeonDoor>().gameObject;
                break;
            }
            if (tr.GetComponentInParent<DungeonLever>() != null)
            {
                targetToDelete = tr.GetComponentInParent<DungeonLever>().gameObject;
                break;
            }
            if (tr.GetComponentInParent<EnemyHealth>() != null)
            {
                targetToDelete = tr.GetComponentInParent<EnemyHealth>().gameObject;
                break;
            }
            if (tr.GetComponentInParent<ExtractionPoint>() != null)
            {
                targetToDelete = tr.GetComponentInParent<ExtractionPoint>().gameObject;
                break;
            }
        }

        if (targetToDelete != null)
        {
            Undo.DestroyObjectImmediate(targetToDelete);
        }
    }

    private static GameObject CreatePrimitiveCube(Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = pos;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = scale;
        cube.isStatic = true;

        Renderer rend = cube.GetComponent<Renderer>();
        if (rend != null)
        {
            MaterialHelper.ApplyMaterial(rend, color);
        }

        return cube;
    }

    private static GameObject GetOrCreateDungeonRoot()
    {
        GameObject root = GameObject.Find("Dungeon");
        if (root == null)
        {
            root = new GameObject("Dungeon");
            Undo.RegisterCreatedObjectUndo(root, "Create Dungeon Root");
        }
        return root;
    }

    // ── Saving, Loading & Export ─────────────────────────────────────────────

    /// <summary>
    /// Scans the entire scene for placed rooms, blast doors, wall levers,
    /// monsters, player spawns, and extraction points.
    /// Serializes everything into the active DungeonLayoutData asset and binds it
    /// to the scene's DungeonBuilder so it loads immediately when clicking Play.
    /// </summary>
    public void SaveSceneToLayoutAsset()
    {
        EnsureLayoutAssetTarget();

        if (targetLayoutAsset == null)
        {
            Debug.LogError("[MapBuilder] No valid target layout asset selected to save into.");
            return;
        }

        targetLayoutAsset.rooms.Clear();

        DungeonRoom[] sceneRooms = FindObjectsByType<DungeonRoom>(FindObjectsInactive.Exclude);
        Dictionary<DungeonRoom, RoomLayoutData> roomDataMap = new Dictionary<DungeonRoom, RoomLayoutData>();

        for (int r = 0; r < sceneRooms.Length; r++)
        {
            DungeonRoom sr = sceneRooms[r];
            RoomLayoutData data = targetLayoutAsset.AddRoom(sr.RoomId, sr.RoomLabel, sr.transform.position, sr.transform.eulerAngles, sr.Dimensions);

            if (sr.Doorways != null)
            {
                for (int i = 0; i < sr.Doorways.Count; i++)
                {
                    data.doorways.Add(sr.Doorways[i]);
                }
            }

            roomDataMap[sr] = data;
        }

        // Fallback root room if user placed objects without creating a DungeonRoom
        RoomLayoutData fallbackRoom = null;
        if (sceneRooms.Length == 0)
        {
            fallbackRoom = targetLayoutAsset.AddRoom("MainArea", "Main Facility Area", Vector3.zero, Vector3.zero, new Vector3(40f, wallHeight, 40f));
        }

        // 1. Scan and save all Blast Doors in the scene
        DungeonDoor[] doors = FindObjectsByType<DungeonDoor>(FindObjectsInactive.Exclude);
        for (int d = 0; d < doors.Length; d++)
        {
            DungeonDoor door = doors[d];
            DungeonRoom hostRoom = door.GetComponentInParent<DungeonRoom>() ?? FindClosestRoom(door.transform.position, sceneRooms);
            RoomLayoutData rData = hostRoom != null ? roomDataMap[hostRoom] : fallbackRoom;

            Vector3 localPos = hostRoom != null ? hostRoom.transform.InverseTransformPoint(door.transform.position) : door.transform.position;
            Vector3 localEuler = hostRoom != null ? (Quaternion.Inverse(hostRoom.transform.rotation) * door.transform.rotation).eulerAngles : door.transform.eulerAngles;

            rData.doors.Add(new DoorPlacementData(
                door.DoorName,
                localPos,
                localEuler,
                door.MechanismChannel,
                door.IsOpen.Value
            ));
        }

        // 2. Scan and save all Levers in the scene
        DungeonLever[] levers = FindObjectsByType<DungeonLever>(FindObjectsInactive.Exclude);
        for (int l = 0; l < levers.Length; l++)
        {
            DungeonLever lever = levers[l];
            DungeonRoom hostRoom = lever.GetComponentInParent<DungeonRoom>() ?? FindClosestRoom(lever.transform.position, sceneRooms);
            RoomLayoutData rData = hostRoom != null ? roomDataMap[hostRoom] : fallbackRoom;

            Vector3 localPos = hostRoom != null ? hostRoom.transform.InverseTransformPoint(lever.transform.position) : lever.transform.position;
            Vector3 localEuler = hostRoom != null ? (Quaternion.Inverse(hostRoom.transform.rotation) * lever.transform.rotation).eulerAngles : lever.transform.eulerAngles;

            rData.levers.Add(new LeverPlacementData(
                lever.name,
                localPos,
                localEuler,
                lever.MechanismChannel,
                0f
            ));
        }

        // 3. Scan and save all Monsters in the scene
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude);
        for (int e = 0; e < enemies.Length; e++)
        {
            EnemyHealth enemy = enemies[e];
            DungeonRoom hostRoom = enemy.GetComponentInParent<DungeonRoom>() ?? FindClosestRoom(enemy.transform.position, sceneRooms);
            RoomLayoutData rData = hostRoom != null ? roomDataMap[hostRoom] : fallbackRoom;

            Vector3 localPos = hostRoom != null ? hostRoom.transform.InverseTransformPoint(enemy.transform.position) : enemy.transform.position;
            string id = enemy.gameObject.name.Replace("Enemy_", "").Replace("(Clone)", "").Trim();

            rData.enemySpawns.Add(new EnemyPlacementData(localPos, id));
        }

        // 5. Scan and save Extraction Point
        ExtractionPoint[] pads = FindObjectsByType<ExtractionPoint>(FindObjectsInactive.Exclude);
        if (pads.Length > 0)
        {
            ExtractionPoint ep = pads[0];
            DungeonRoom hostRoom = ep.GetComponentInParent<DungeonRoom>() ?? FindClosestRoom(ep.transform.position, sceneRooms);
            RoomLayoutData rData = hostRoom != null ? roomDataMap[hostRoom] : fallbackRoom;

            rData.hasExtractionPoint = true;
            rData.extractionLocalPosition = hostRoom != null ? hostRoom.transform.InverseTransformPoint(ep.transform.position) : ep.transform.position;
        }

        // 6. Scan and save Player Spawns
        GameObject[] sceneObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
        for (int i = 0; i < sceneObjects.Length; i++)
        {
            if (sceneObjects[i].name.StartsWith("PlayerSpawn"))
            {
                DungeonRoom hostRoom = sceneObjects[i].GetComponentInParent<DungeonRoom>() ?? FindClosestRoom(sceneObjects[i].transform.position, sceneRooms);
                RoomLayoutData rData = hostRoom != null ? roomDataMap[hostRoom] : fallbackRoom;
                Vector3 localPos = hostRoom != null ? hostRoom.transform.InverseTransformPoint(sceneObjects[i].transform.position) : sceneObjects[i].transform.position;
                rData.playerSpawns.Add(localPos);
            }
        }

        // 7. Scan and save room props / pillars / custom geometry
        for (int r = 0; r < sceneRooms.Length; r++)
        {
            DungeonRoom sr = sceneRooms[r];
            RoomLayoutData rData = roomDataMap[sr];

            for (int i = 0; i < sr.transform.childCount; i++)
            {
                Transform child = sr.transform.GetChild(i);
                string cName = child.name;

                if (cName == "Floor" || cName == "Ceiling" || cName.StartsWith("Wall") ||
                    cName.StartsWith("Socket_") || cName.StartsWith("BlastDoor") ||
                    cName.StartsWith("Lever") ||
                    cName.StartsWith("Enemy") || cName.StartsWith("PlayerSpawn") ||
                    cName == "ExtractionPoint")
                {
                    continue;
                }

                if (child.GetComponent<DungeonDoor>() != null || child.GetComponent<DungeonLever>() != null ||
                    child.GetComponent<EnemyHealth>() != null ||
                    child.GetComponent<ExtractionPoint>() != null)
                {
                    continue;
                }

                Color col = rData.wallColor;
                Renderer rend = child.GetComponent<Renderer>();
                if (rend != null && rend.sharedMaterial != null)
                {
                    col = rend.sharedMaterial.color;
                }

                rData.props.Add(new PropPlacementData(
                    child.name,
                    child.localPosition,
                    child.localEulerAngles,
                    child.localScale,
                    col
                ));
            }
        }

        // Bind asset directly into scene's DungeonBuilder so it plays immediately!
        DungeonBuilder builder = FindAnyObjectByType<DungeonBuilder>();
        if (builder != null)
        {
            builder.ActiveLayout = targetLayoutAsset;
            builder.MapSource = DungeonMapSource.DesignedAsset;
            EditorUtility.SetDirty(builder);
        }

        EditorUtility.SetDirty(targetLayoutAsset);
        AssetDatabase.SaveAssets();

        // Mark active scene dirty and persist scene so changes persist into Play Mode
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        // Notifications
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent($"💾 Saved Map: '{targetLayoutAsset.layoutName}'"));
        }
        EditorApplication.Beep();

        Debug.Log($"[MapBuilder] Saved map '{targetLayoutAsset.layoutName}' ({targetLayoutAsset.rooms.Count} rooms, {doors.Length} doors, {levers.Length} levers, {enemies.Length} monsters)!");
    }

    private void LoadLayoutAssetIntoScene()
    {
        EnsureLayoutAssetTarget();

        if (targetLayoutAsset == null)
        {
            Debug.LogWarning("[MapBuilder] No layout asset selected to load.");
            return;
        }

        DungeonBuilder builder = FindAnyObjectByType<DungeonBuilder>();
        if (builder == null)
        {
            GameObject gm = GameObject.Find("GameManager");
            if (gm != null)
            {
                builder = gm.GetComponent<DungeonBuilder>() ?? gm.AddComponent<DungeonBuilder>();
            }
            else
            {
                GameObject newGM = new GameObject("DungeonBuilder_Root");
                builder = newGM.AddComponent<DungeonBuilder>();
            }
        }

        builder.ActiveLayout = targetLayoutAsset;
        builder.MapSource = DungeonMapSource.DesignedAsset;
        builder.BuildDungeonInEditor();

        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent($"📁 Loaded '{targetLayoutAsset.layoutName}'"));
        }
        Debug.Log($"[MapBuilder] Successfully loaded layout '{targetLayoutAsset.layoutName}' into the Scene!");
    }

    private void CreateNewMapAsset()
    {
        string dir = "Assets/_Project/ScriptableObjects/Dungeon";
        if (!AssetDatabase.IsValidFolder("Assets/_Project/ScriptableObjects"))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
        }
        if (!AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Dungeon");
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Create New Dungeon Map",
            "CustomDungeonMap",
            "asset",
            "Choose location for new map asset",
            dir
        );

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        DungeonLayoutData newLayout = ScriptableObject.CreateInstance<DungeonLayoutData>();
        newLayout.layoutName = System.IO.Path.GetFileNameWithoutExtension(path);

        AssetDatabase.CreateAsset(newLayout, path);
        AssetDatabase.SaveAssets();

        targetLayoutAsset = newLayout;

        // Clear scene and bind new map
        DungeonBuilder builder = FindAnyObjectByType<DungeonBuilder>();
        if (builder != null)
        {
            builder.ClearDungeonInEditor();
            builder.ActiveLayout = targetLayoutAsset;
            builder.MapSource = DungeonMapSource.DesignedAsset;
            EditorUtility.SetDirty(builder);
        }

        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent($"➕ Created '{newLayout.layoutName}'"));
        }
    }

    private void EnsureLayoutAssetTarget()
    {
        if (targetLayoutAsset != null)
        {
            return;
        }

        string dir = "Assets/_Project/ScriptableObjects/Dungeon";
        if (!AssetDatabase.IsValidFolder("Assets/_Project/ScriptableObjects"))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
        }
        if (!AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Dungeon");
        }

        string defaultPath = $"{dir}/DefaultDungeonLayout.asset";
        targetLayoutAsset = AssetDatabase.LoadAssetAtPath<DungeonLayoutData>(defaultPath);

        if (targetLayoutAsset == null)
        {
            targetLayoutAsset = DungeonLayoutData.CreateDefaultFacilityLayoutAsset(defaultPath);
        }
    }

    private static DungeonRoom FindClosestRoom(Vector3 position, DungeonRoom[] rooms)
    {
        if (rooms == null || rooms.Length == 0)
        {
            return null;
        }

        DungeonRoom bestRoom = null;
        float bestDistSq = float.MaxValue;

        // 1. Check if position is strictly inside any room's exact bounds
        for (int i = 0; i < rooms.Length; i++)
        {
            DungeonRoom r = rooms[i];
            if (r == null)
            {
                continue;
            }

            Vector3 localPt = r.transform.InverseTransformPoint(position);
            Bounds exactBounds = new Bounds(new Vector3(0f, r.Dimensions.y * 0.5f, 0f), r.Dimensions);
            if (exactBounds.Contains(localPt))
            {
                float distSq = (r.transform.position - position).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestRoom = r;
                }
            }
        }

        if (bestRoom != null)
        {
            return bestRoom;
        }

        // 2. If sitting on a doorway or slightly outside, choose the room whose center is closest
        for (int i = 0; i < rooms.Length; i++)
        {
            DungeonRoom r = rooms[i];
            if (r == null)
            {
                continue;
            }

            float distSq = (r.transform.position - position).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestRoom = r;
            }
        }

        return bestRoom;
    }

    private void BakeSceneNavMesh()
    {
        GameObject root = GameObject.Find("Dungeon");
        if (root == null)
        {
            Debug.LogWarning("[MapBuilder] No 'Dungeon' root found to bake NavMesh.");
            return;
        }

        Unity.AI.Navigation.NavMeshSurface surface = root.GetComponent<Unity.AI.Navigation.NavMeshSurface>();
        if (surface == null)
        {
            surface = root.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
        }

        surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
        surface.BuildNavMesh();

        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent("🧭 NavMesh Baked Successfully!"));
        }
        Debug.Log("[MapBuilder] NavMesh baked successfully!");
    }
}
#endif
