#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class AnimationDiagnostic
{
    static AnimationDiagnostic()
    {
        EditorApplication.delayCall += RunDiagnostics;
    }

    [MenuItem("Tools/Dungeon Crawler/Run Animation Diagnostics")]
    public static void RunDiagnostics()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== ANIMATION PIPELINE DIAGNOSTICS ===");

        string rigPath = "Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx";
        string walkPath = "Assets/_Project/Art/Models/Characters/MainCharacter_Walk_Animation.fbx";
        string idlePath = "Assets/_Project/Art/Models/Characters/MainCharacter_Idle.fbx";
        string playerPrefabPath = "Assets/_Project/Prefabs/Entities/PlayerPrefab.prefab";
        string enemyPrefabPath = "Assets/_Project/Prefabs/Entities/EnemyPrefab.prefab";

        // 1. Rig Sub-Assets
        Object[] rigAssets = AssetDatabase.LoadAllAssetsAtPath(rigPath);
        sb.AppendLine($"\n--- RIG SUB-ASSETS ({rigPath}) ---");
        Avatar rigAvatar = null;
        if (rigAssets != null)
        {
            foreach (var a in rigAssets)
            {
                if (a != null)
                {
                    sb.AppendLine($"  - {a.GetType().Name}: '{a.name}'");
                    if (a is Mesh m)
                    {
                        sb.AppendLine($"    Mesh Bounds: min.y={m.bounds.min.y:F3}, max.y={m.bounds.max.y:F3}, center.y={m.bounds.center.y:F3}, size.y={m.bounds.size.y:F3}");
                    }
                    if (a is Avatar av)
                    {
                        rigAvatar = av;
                        sb.AppendLine($"    Avatar isValid: {av.isValid}, isHuman: {av.isHuman}");
                    }
                }
            }
        }

        // 2. Idle Sub-Assets
        Object[] idleAssets = AssetDatabase.LoadAllAssetsAtPath(idlePath);
        sb.AppendLine($"\n--- IDLE SUB-ASSETS ({idlePath}) ---");
        AnimationClip idleClip = null;
        if (idleAssets != null)
        {
            foreach (var a in idleAssets)
            {
                if (a != null)
                {
                    sb.AppendLine($"  - {a.GetType().Name}: '{a.name}'");
                    if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        idleClip = clip;
                    }
                }
            }
        }

        if (idleClip != null)
        {
            sb.AppendLine($"\n--- IDLE CLIP CURVE BINDINGS ({idleClip.name}, Length: {idleClip.length}s) ---");
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(idleClip);
            sb.AppendLine($"Total curve bindings: {bindings.Length}");
            HashSet<string> uniquePaths = new HashSet<string>();
            foreach (var b in bindings)
            {
                uniquePaths.Add(b.path);
            }
            sb.AppendLine($"Unique Animated Transform Paths ({uniquePaths.Count}):");
            int count = 0;
            foreach (var p in uniquePaths)
            {
                if (count++ < 25)
                {
                    sb.AppendLine($"    '{p}'");
                }
            }
            if (uniquePaths.Count > 25)
            {
                sb.AppendLine($"    ... and {uniquePaths.Count - 25} more");
            }
        }
        else
        {
            sb.AppendLine("  [INFO] No standalone AnimationClip found directly in Idle FBX yet.");
        }

        // 3. Walk Sub-Assets
        Object[] walkAssets = AssetDatabase.LoadAllAssetsAtPath(walkPath);
        sb.AppendLine($"\n--- WALK SUB-ASSETS ({walkPath}) ---");
        AnimationClip walkClip = null;
        if (walkAssets != null)
        {
            foreach (var a in walkAssets)
            {
                if (a != null)
                {
                    sb.AppendLine($"  - {a.GetType().Name}: '{a.name}'");
                    if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        walkClip = clip;
                    }
                }
            }
        }

        // 4. Curve Bindings on Walk Clip
        if (walkClip != null)
        {
            sb.AppendLine($"\n--- WALK CLIP CURVE BINDINGS ({walkClip.name}, Length: {walkClip.length}s) ---");
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(walkClip);
            sb.AppendLine($"Total curve bindings: {bindings.Length}");
            HashSet<string> uniquePaths = new HashSet<string>();
            foreach (var b in bindings)
            {
                uniquePaths.Add(b.path);
            }
            sb.AppendLine($"Unique Animated Transform Paths ({uniquePaths.Count}):");
            int count = 0;
            foreach (var p in uniquePaths)
            {
                if (count++ < 25)
                {
                    sb.AppendLine($"    '{p}'");
                }
            }
            if (uniquePaths.Count > 25)
            {
                sb.AppendLine($"    ... and {uniquePaths.Count - 25} more");
            }
        }
        else
        {
            sb.AppendLine("  [ERROR] No AnimationClip found in Walk FBX!");
        }

        // 4. PlayerPrefab Hierarchy and Animator
        GameObject playerGo = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
        sb.AppendLine($"\n--- PLAYER PREFAB ({playerPrefabPath}) ---");
        if (playerGo != null)
        {
            Animator[] anims = playerGo.GetComponentsInChildren<Animator>(true);
            sb.AppendLine($"Found {anims.Length} Animator(s):");
            foreach (var a in anims)
            {
                string relPath = AnimationUtility.CalculateTransformPath(a.transform, playerGo.transform);
                sb.AppendLine($"  - Animator on GameObject: '{a.gameObject.name}' (Path: '{relPath}')");
                sb.AppendLine($"    Controller: {(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null")}");
                sb.AppendLine($"    Avatar: {(a.avatar != null ? a.avatar.name : "null")}");
                sb.AppendLine($"    cullingMode: {a.cullingMode}, applyRootMotion: {a.applyRootMotion}");

                // List children under this Animator GameObject to see if they match walkClip curve paths!
                sb.AppendLine($"    Children paths under this Animator:");
                Transform[] childTransforms = a.GetComponentsInChildren<Transform>(true);
                int cCount = 0;
                foreach (var ct in childTransforms)
                {
                    if (ct != a.transform && cCount++ < 15)
                    {
                        string childRel = AnimationUtility.CalculateTransformPath(ct, a.transform);
                        sb.AppendLine($"      '{childRel}'");
                    }
                }
            }
        }

        // 5. EnemyPrefab Hierarchy and Animator
        GameObject enemyGo = AssetDatabase.LoadAssetAtPath<GameObject>(enemyPrefabPath);
        sb.AppendLine($"\n--- ENEMY PREFAB ({enemyPrefabPath}) ---");
        if (enemyGo != null)
        {
            Animator[] anims = enemyGo.GetComponentsInChildren<Animator>(true);
            sb.AppendLine($"Found {anims.Length} Animator(s):");
            foreach (var a in anims)
            {
                string relPath = AnimationUtility.CalculateTransformPath(a.transform, enemyGo.transform);
                sb.AppendLine($"  - Animator on GameObject: '{a.gameObject.name}' (Path: '{relPath}')");
                sb.AppendLine($"    Controller: {(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null")}");
                sb.AppendLine($"    Avatar: {(a.avatar != null ? a.avatar.name : "null")}");
            }
        }

        // 6. FleshBrute Prefab Details
        string brutePath = "Assets/_Project/Prefabs/Entities/Enemy_FleshBrute.prefab";
        GameObject bruteGo = AssetDatabase.LoadAssetAtPath<GameObject>(brutePath);
        sb.AppendLine($"\n--- FLESH BRUTE PREFAB ({brutePath}) ---");
        if (bruteGo != null)
        {
            UnityEngine.AI.NavMeshAgent agent = bruteGo.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                sb.AppendLine($"  NavMeshAgent: baseOffset={agent.baseOffset}, height={agent.height}, radius={agent.radius}");
            }
            CapsuleCollider col = bruteGo.GetComponent<CapsuleCollider>();
            if (col != null)
            {
                sb.AppendLine($"  CapsuleCollider: center={col.center}, height={col.height}, radius={col.radius}");
            }
            else
            {
                sb.AppendLine("  CapsuleCollider: None");
            }
            Rigidbody rb = bruteGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                sb.AppendLine($"  Rigidbody: isKinematic={rb.isKinematic}, useGravity={rb.useGravity}");
            }
            Transform body = bruteGo.transform.Find("Body");
            if (body != null)
            {
                sb.AppendLine($"  Body localPos: {body.localPosition}");
                for (int c = 0; c < body.childCount; c++)
                {
                    Transform child = body.GetChild(c);
                    sb.AppendLine($"    Body child '{child.name}': localPos={child.localPosition}, localScale={child.localScale}");
                }
            }
        }

        string outPath = Path.Combine(Application.dataPath, "../Library/AnimationDiagnosticOutput.txt");
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log(sb.ToString());
    }
}
#endif
