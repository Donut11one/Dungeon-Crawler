#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Custom inspector and in-scene interactive handles for DungeonRoom.
/// Allows level designers to punch or seal doorway openings on any wall (N/S/E/W)
/// directly in the Scene View or Inspector, and drop functional blast doors into openings.
/// </summary>
[CustomEditor(typeof(DungeonRoom))]
public class DungeonRoomEditor : Editor
{
    private string newDoorChannel = "Door_Channel_1";
    private bool newDoorStartsOpen = false;

    public override void OnInspectorGUI()
    {
        DungeonRoom room = (DungeonRoom)target;
        if (room == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField($"Room: {room.RoomLabel}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Dimensions: {room.Dimensions.x}m × {room.Dimensions.y}m × {room.Dimensions.z}m", EditorStyles.miniLabel);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Doorway Openings & Blast Doors", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Toggle openings on any wall to cut doorway gaps. Click 'Add Blast Door' to snap an interactive door into the opening.",
            MessageType.None
        );

        EditorGUILayout.Space(4f);
        DrawWallOpeningControl(room, WallSide.North);
        DrawWallOpeningControl(room, WallSide.South);
        DrawWallOpeningControl(room, WallSide.East);
        DrawWallOpeningControl(room, WallSide.West);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Add Blast Door to Active Openings", EditorStyles.boldLabel);
        newDoorChannel = EditorGUILayout.TextField("Mechanism Channel", newDoorChannel);
        newDoorStartsOpen = EditorGUILayout.Toggle("Start Open", newDoorStartsOpen);

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("🔄 Rebuild All Walls", GUILayout.Height(26f)))
        {
            Undo.RecordObject(room, "Rebuild Room Walls");
            room.RebuildWalls();
            EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
        }

        EditorGUILayout.Space(4f);
        DrawDefaultInspector();
    }

    private void DrawWallOpeningControl(DungeonRoom room, WallSide side)
    {
        bool hasOpening = room.HasDoorway(side);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        GUI.color = hasOpening ? new Color(0.3f, 1f, 0.4f) : Color.gray;
        EditorGUILayout.LabelField(hasOpening ? $"🟢 {side} Wall: OPEN" : $"⚪ {side} Wall: SOLID", EditorStyles.boldLabel, GUILayout.Width(160f));
        GUI.color = Color.white;

        string toggleBtnText = hasOpening ? "Close Opening" : "Punch Opening";
        if (GUILayout.Button(toggleBtnText, GUILayout.Width(120f)))
        {
            Undo.RecordObject(room, $"Toggle {side} Doorway");
            room.ToggleDoorway(side);
            EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
        }

        EditorGUILayout.EndHorizontal();

        if (hasOpening)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            if (GUILayout.Button($"➕ Add Blast Door on {side}", GUILayout.Height(22f)))
            {
                PlaceDoorOnOpening(room, side);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void PlaceDoorOnOpening(DungeonRoom room, WallSide side)
    {
        GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Dungeon/DoorPrefab.prefab");
        if (doorPrefab == null)
        {
            Debug.LogError("[DungeonRoomEditor] Could not find DoorPrefab at Assets/_Project/Prefabs/Dungeon/DoorPrefab.prefab");
            return;
        }

        Vector3 wallLocalPos = room.GetWallCenterLocal(side);
        Vector3 wallWorldPos = room.transform.position + room.transform.rotation * wallLocalPos;

        Quaternion doorRot = side switch
        {
            WallSide.North => room.transform.rotation * Quaternion.identity,
            WallSide.South => room.transform.rotation * Quaternion.Euler(0f, 180f, 0f),
            WallSide.East => room.transform.rotation * Quaternion.Euler(0f, 90f, 0f),
            _ => room.transform.rotation * Quaternion.Euler(0f, -90f, 0f)
        };

        GameObject doorObj = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, room.transform);
        doorObj.transform.position = wallWorldPos;
        doorObj.transform.rotation = doorRot;
        doorObj.name = $"BlastDoor_{newDoorChannel}";

        DungeonDoor door = doorObj.GetComponent<DungeonDoor>();
        if (door != null)
        {
            door.Configure($"Blast Door [{newDoorChannel}]", newDoorChannel, newDoorStartsOpen);
        }

        Undo.RegisterCreatedObjectUndo(doorObj, $"Add Blast Door on {side}");
        Selection.activeGameObject = doorObj;
        EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
    }

    private void OnSceneGUI()
    {
        DungeonRoom room = (DungeonRoom)target;
        if (room == null)
        {
            return;
        }

        DrawSceneWallHandles(room, WallSide.North);
        DrawSceneWallHandles(room, WallSide.South);
        DrawSceneWallHandles(room, WallSide.East);
        DrawSceneWallHandles(room, WallSide.West);
    }

    private void DrawSceneWallHandles(DungeonRoom room, WallSide side)
    {
        Vector3 wallWorldCenter = room.GetWallCenterWorld(side);
        bool hasOpening = room.HasDoorway(side);

        Vector3 buttonPos = wallWorldCenter + Vector3.up * 2.2f;

        Handles.color = hasOpening ? new Color(0.2f, 1f, 0.4f, 0.8f) : new Color(0.8f, 0.8f, 0.8f, 0.6f);

        if (hasOpening)
        {
            Handles.DrawWireCube(wallWorldCenter + Vector3.up * 1.9f, new Vector3(3.2f, 3.8f, 0.4f));
        }

        GUIStyle style = new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter
        };

        Handles.BeginGUI();
        Vector2 screenPos = HandleUtility.WorldToGUIPoint(buttonPos);
        Rect rect = new Rect(screenPos.x - 60f, screenPos.y - 12f, 120f, 24f);

        string label = hasOpening ? $"[🚪 {side}: OPEN]" : $"[🧱 {side}: WALL]";
        GUI.backgroundColor = hasOpening ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.8f, 0.3f);

        if (GUI.Button(rect, label, style))
        {
            Undo.RecordObject(room, $"Toggle {side} Doorway");
            room.ToggleDoorway(side);
            EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
        }

        GUI.backgroundColor = Color.white;
        Handles.EndGUI();
    }
}
#endif
