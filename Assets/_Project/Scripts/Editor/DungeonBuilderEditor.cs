#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for DungeonBuilder.
/// Displays an active map resolution badge explaining exactly which map
/// will be loaded at runtime, along with quick buttons to launch the Dungeon Designer,
/// preview the layout in the Scene View, clear preview objects, and bake the NavMesh.
/// </summary>
[CustomEditor(typeof(DungeonBuilder))]
public class DungeonBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DungeonBuilder builder = (DungeonBuilder)target;

        EditorGUILayout.Space(4f);

        // Map Resolution Status Box
        DungeonLayoutData resolved = builder.GetResolvedLayout();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (resolved != null)
        {
            GUIStyle boldGreen = new GUIStyle(EditorStyles.boldLabel);
            boldGreen.normal.textColor = new Color(0.2f, 0.85f, 0.35f);
            EditorGUILayout.LabelField($"ACTIVE MAP: DESIGNED ASSET [{resolved.layoutName}]", boldGreen);
            EditorGUILayout.LabelField(
                $"• Mode: {builder.MapSource} | Rooms: {resolved.rooms.Count} | Doors: {CountDoors(resolved)} | Levers: {CountLevers(resolved)}",
                EditorStyles.miniLabel
            );
        }
        else
        {
            GUIStyle boldYellow = new GUIStyle(EditorStyles.boldLabel);
            boldYellow.normal.textColor = new Color(1f, 0.75f, 0.2f);
            EditorGUILayout.LabelField("ACTIVE MAP: BUILT-IN PROCEDURAL CODE MAP", boldYellow);
            EditorGUILayout.LabelField(
                "• Mode: Built-in Facility Layout (7 rooms, North blast door, MainHall lever)",
                EditorStyles.miniLabel
            );
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Level Designer Quick Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.3f, 0.75f, 1f);
        if (GUILayout.Button("Open Scene Map Builder", GUILayout.Height(30f)))
        {
            DungeonMapBuilderTool.OpenWindow();
        }

        GUI.backgroundColor = new Color(0.3f, 0.85f, 0.4f);
        if (GUILayout.Button("Preview in Scene", GUILayout.Height(30f)))
        {
            builder.BuildDungeonInEditor();
        }

        GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
        if (GUILayout.Button("Clear Preview", GUILayout.Height(30f)))
        {
            builder.ClearDungeonInEditor();
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);
        GUI.backgroundColor = new Color(0.95f, 0.75f, 0.2f);
        if (GUILayout.Button("🎨 Apply Surface Materials & Tiling to Scene", GUILayout.Height(28f)))
        {
            builder.ApplyMaterialsToAllSceneSurfaces();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent("🎨 Applied Materials & Tiling!"));
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(2f);
        GUI.backgroundColor = new Color(0.25f, 0.35f, 0.5f);
        if (GUILayout.Button("🌙 Apply Dark Atmosphere (Local Lighting Only)", GUILayout.Height(28f)))
        {
            DungeonLightingManager.ApplyDarkAtmosphere();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent("🌙 Applied Dark Local Lighting!"));
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(6f);

        DrawDefaultInspector();
    }

    private static int CountDoors(DungeonLayoutData layout)
    {
        if (layout == null)
        {
            return 0;
        }

        int count = 0;
        for (int r = 0; r < layout.rooms.Count; r++)
        {
            RoomLayoutData room = layout.rooms[r];
            count += room.doors.Count;
            for (int d = 0; d < room.doorways.Count; d++)
            {
                if (room.doorways[d].hasDoor)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static int CountLevers(DungeonLayoutData layout)
    {
        if (layout == null)
        {
            return 0;
        }

        int count = 0;
        for (int r = 0; r < layout.rooms.Count; r++)
        {
            count += layout.rooms[r].levers.Count;
        }
        return count;
    }
}
#endif
