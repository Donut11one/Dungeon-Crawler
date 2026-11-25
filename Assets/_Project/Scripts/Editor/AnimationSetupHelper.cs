#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor utility that generates pre-configured Animator Controllers for
/// the player and monsters with all standard state machine parameters,
/// and automatically wires up walk and movement animation clips.
/// </summary>
public static class AnimationSetupHelper
{
    private const string PlayerControllerPath = "Assets/_Project/Animations/Players/PlayerAnimatorController.controller";
    private const string MonsterControllerPath = "Assets/_Project/Animations/Monsters/MonsterAnimatorController.controller";
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Entities/PlayerPrefab.prefab";

    [InitializeOnLoadMethod]
    private static void AutoEnsureControllers()
    {
        EditorApplication.delayCall += () =>
        {
            SetupAllAnimations();
        };
    }

    [MenuItem("Tools/Dungeon Crawler/Setup Animations")]
    public static void SetupAnimationsMenu()
    {
        SetupAllAnimations();
    }

    public static void SetupAllAnimations()
    {
        CreatePlayerAnimatorController();
        CreateMonsterAnimatorController();
        RepairPrefabAnimationSetups();
        SetupPlayerIdleAnimation();
        SetupPlayerWalkAnimation();
        SetupMonsterIdleAnimation();
        SetupMonsterWalkAnimation();
        SetupMonsterSpecificAnimations();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AnimationSetupHelper] All player and monster animations (Idle, Walk, Run, Attack) have been successfully set up and wired!");
    }

    public static void EnsureAnimatorControllers()
    {
        SetupAllAnimations();
    }

    public static void SetupPlayerIdleAnimation(string specificFbxPath = null)
    {
        // 1. Locate idle animation FBX
        string idleFbxPath = specificFbxPath;
        if (string.IsNullOrEmpty(idleFbxPath) || !File.Exists(idleFbxPath))
        {
            string[] candidatePaths = new string[]
            {
                "Assets/_Project/Art/Models/Characters/MainCharacter_Idle.fbx",
                "Assets/_Project/Animations/Players/MainCharacter_Idle.fbx"
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    idleFbxPath = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(idleFbxPath))
            {
                string[] found = AssetDatabase.FindAssets("MainCharacter_Idle t:GameObject");
                if (found != null && found.Length > 0)
                {
                    idleFbxPath = AssetDatabase.GUIDToAssetPath(found[0]);
                }
            }
        }

        if (string.IsNullOrEmpty(idleFbxPath) || !File.Exists(idleFbxPath))
        {
            return;
        }

        // 2. Ensure MainCharacter_rig.fbx has Generic Avatar created
        string rigPath = "Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx";
        if (File.Exists(rigPath))
        {
            ModelImporter rigImporter = AssetImporter.GetAtPath(rigPath) as ModelImporter;
            if (rigImporter != null)
            {
                bool reimportRig = false;
                if (rigImporter.animationType != ModelImporterAnimationType.Generic)
                {
                    rigImporter.animationType = ModelImporterAnimationType.Generic;
                    reimportRig = true;
                }
                if (rigImporter.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    rigImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    reimportRig = true;
                }
                if (reimportRig)
                {
                    rigImporter.SaveAndReimport();
                }
            }
        }

        // 3. Configure ModelImporter for idle animation
        ModelImporter importer = AssetImporter.GetAtPath(idleFbxPath) as ModelImporter;
        if (importer != null)
        {
            bool needsReimport = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                needsReimport = true;
            }

            Avatar rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(rigPath);
            if (rigAvatar != null && importer.sourceAvatar != rigAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = rigAvatar;
                needsReimport = true;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.clipAnimations;
            }

            if (clips != null && clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (!clips[i].loopTime || clips[i].name != "Idle")
                    {
                        clips[i].name = "Idle";
                        clips[i].loopTime = true;
                        clips[i].loopPose = true;
                        needsReimport = true;
                    }
                }
                importer.clipAnimations = clips;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        // 4. Find AnimationClip inside the imported FBX
        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(idleFbxPath);
        AnimationClip idleClip = null;
        if (subAssets != null)
        {
            foreach (Object obj in subAssets)
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    idleClip = clip;
                    break;
                }
            }
        }

        // 5. Ensure Player Animator Controller
        AnimatorController controller = CreatePlayerAnimatorController();
        if (controller == null)
        {
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        }

        if (controller == null)
        {
            Debug.LogError($"[AnimationSetupHelper] Could not load or create controller at '{PlayerControllerPath}'");
            return;
        }

        var rootStateMachine = controller.layers[0].stateMachine;

        // Find or create states
        AnimatorState idleState = null;
        AnimatorState walkState = null;
        AnimatorState runState = null;

        for (int i = 0; i < rootStateMachine.states.Length; i++)
        {
            var childState = rootStateMachine.states[i];
            if (childState.state.name == "Idle")
            {
                idleState = childState.state;
            }
            else if (childState.state.name == "Walk")
            {
                walkState = childState.state;
            }
            else if (childState.state.name == "Run")
            {
                runState = childState.state;
            }
        }

        if (idleState == null)
        {
            idleState = rootStateMachine.AddState("Idle");
        }
        if (walkState == null)
        {
            walkState = rootStateMachine.AddState("Walk");
        }
        if (runState == null)
        {
            runState = rootStateMachine.AddState("Run");
        }

        rootStateMachine.defaultState = idleState;

        // Assign idle clip
        if (idleClip != null)
        {
            AnimationClip effectiveClip = GetOrCreateUniversalIdleClip(idleClip);
            if (effectiveClip == null)
            {
                effectiveClip = idleClip;
            }

            idleState.motion = effectiveClip;
        }

        // Configure transitions between Idle, Walk, Run
        if (walkState != null)
        {
            ClearTransitionsBetween(idleState, walkState);
            ClearTransitionsBetween(walkState, idleState);

            var toWalk = idleState.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.15f;
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

            var toIdle = walkState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.15f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        string clipMsg = idleClip != null ? idleClip.name : "detected";
        Debug.Log($"[AnimationSetupHelper] Successfully configured player idle animation ({clipMsg}) and wired PlayerAnimatorController Idle state!");
    }

    public static void SetupPlayerWalkAnimation(string specificFbxPath = null)
    {
        // 1. Locate walk animation FBX
        string walkFbxPath = specificFbxPath;
        if (string.IsNullOrEmpty(walkFbxPath) || !File.Exists(walkFbxPath))
        {
            string[] candidatePaths = new string[]
            {
                "Assets/_Project/Art/Models/Characters/MainCharacter_Walk_Animation.fbx",
                "Assets/_Project/Animations/Players/MainCharacter_Walk_Animation.fbx"
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    walkFbxPath = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(walkFbxPath))
            {
                string[] found = AssetDatabase.FindAssets("MainCharacter_Walk t:GameObject");
                if (found != null && found.Length > 0)
                {
                    walkFbxPath = AssetDatabase.GUIDToAssetPath(found[0]);
                }
            }
        }

        if (string.IsNullOrEmpty(walkFbxPath) || !File.Exists(walkFbxPath))
        {
            return;
        }

        // 2. Ensure MainCharacter_rig.fbx has Generic Avatar created
        string rigPath = "Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx";
        if (File.Exists(rigPath))
        {
            ModelImporter rigImporter = AssetImporter.GetAtPath(rigPath) as ModelImporter;
            if (rigImporter != null)
            {
                bool reimportRig = false;
                if (rigImporter.animationType != ModelImporterAnimationType.Generic)
                {
                    rigImporter.animationType = ModelImporterAnimationType.Generic;
                    reimportRig = true;
                }
                if (rigImporter.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    rigImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    reimportRig = true;
                }
                if (reimportRig)
                {
                    rigImporter.SaveAndReimport();
                }
            }
        }

        // 3. Configure ModelImporter for walk animation
        ModelImporter importer = AssetImporter.GetAtPath(walkFbxPath) as ModelImporter;
        if (importer != null)
        {
            bool needsReimport = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                needsReimport = true;
            }

            Avatar rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(rigPath);
            if (rigAvatar != null && importer.sourceAvatar != rigAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = rigAvatar;
                needsReimport = true;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.clipAnimations;
            }

            if (clips != null && clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (!clips[i].loopTime || clips[i].name != "Walk")
                    {
                        clips[i].name = "Walk";
                        clips[i].loopTime = true;
                        clips[i].loopPose = true;
                        needsReimport = true;
                    }
                }
                importer.clipAnimations = clips;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
            }
        }

        // 4. Find AnimationClip inside the imported FBX
        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(walkFbxPath);
        AnimationClip walkClip = null;
        if (subAssets != null)
        {
            foreach (Object obj in subAssets)
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    walkClip = clip;
                    break;
                }
            }
        }

        // 4. Ensure Player Animator Controller
        AnimatorController controller = CreatePlayerAnimatorController();
        if (controller == null)
        {
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        }

        if (controller == null)
        {
            Debug.LogError($"[AnimationSetupHelper] Could not load or create controller at '{PlayerControllerPath}'");
            return;
        }

        var rootStateMachine = controller.layers[0].stateMachine;

        // Find or create states
        AnimatorState idleState = null;
        AnimatorState walkState = null;
        AnimatorState runState = null;
        AnimatorState attackState = null;

        for (int i = 0; i < rootStateMachine.states.Length; i++)
        {
            var childState = rootStateMachine.states[i];
            if (childState.state.name == "Idle")
            {
                idleState = childState.state;
            }
            else if (childState.state.name == "Walk")
            {
                walkState = childState.state;
            }
            else if (childState.state.name == "Run")
            {
                runState = childState.state;
            }
            else if (childState.state.name == "Attack")
            {
                attackState = childState.state;
            }
        }

        if (idleState == null)
        {
            idleState = rootStateMachine.AddState("Idle");
        }
        if (walkState == null)
        {
            walkState = rootStateMachine.AddState("Walk");
        }
        if (runState == null)
        {
            runState = rootStateMachine.AddState("Run");
        }
        if (attackState == null)
        {
            attackState = rootStateMachine.AddState("Attack");
        }

        rootStateMachine.defaultState = idleState;

        // Assign walk clip
        if (walkClip != null)
        {
            AnimationClip effectiveClip = GetOrCreateUniversalWalkClip(walkClip);
            if (effectiveClip == null)
            {
                effectiveClip = walkClip;
            }

            walkState.motion = effectiveClip;
            runState.motion = effectiveClip;
            runState.speed = 1.4f; // Faster playback speed for sprint
        }

        // Configure transitions between Idle, Walk, Run
        ClearTransitionsBetween(idleState, walkState);
        ClearTransitionsBetween(walkState, idleState);
        ClearTransitionsBetween(walkState, runState);
        ClearTransitionsBetween(runState, walkState);

        // Idle -> Walk (Speed > 0.1)
        var toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.15f;
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        // Walk -> Idle (Speed < 0.1)
        var toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.15f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        // Walk -> Run (IsSprinting == true)
        var toRun = walkState.AddTransition(runState);
        toRun.hasExitTime = false;
        toRun.duration = 0.15f;
        toRun.AddCondition(AnimatorConditionMode.If, 0f, "IsSprinting");

        // Run -> Walk (IsSprinting == false)
        var fromRun = runState.AddTransition(walkState);
        fromRun.hasExitTime = false;
        fromRun.duration = 0.15f;
        fromRun.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsSprinting");

        // Attack transition from AnyState
        if (attackState != null)
        {
            if (attackState.motion == null)
            {
                AnimationClip attackClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Players/MainCharacter_Attack.anim");
                if (attackClip != null)
                {
                    attackState.motion = attackClip;
                }
            }

            bool hasAttackAny = false;
            for (int i = 0; i < rootStateMachine.anyStateTransitions.Length; i++)
            {
                if (rootStateMachine.anyStateTransitions[i].destinationState == attackState)
                {
                    hasAttackAny = true;
                    break;
                }
            }
            if (!hasAttackAny)
            {
                var toAttack = rootStateMachine.AddAnyStateTransition(attackState);
                toAttack.hasExitTime = false;
                toAttack.duration = 0.05f;
                toAttack.canTransitionToSelf = false;
                toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            }

            bool hasAttackReturn = false;
            for (int i = 0; i < attackState.transitions.Length; i++)
            {
                if (attackState.transitions[i].destinationState == idleState)
                {
                    hasAttackReturn = true;
                    break;
                }
            }
            if (!hasAttackReturn && idleState != null)
            {
                var fromAttack = attackState.AddTransition(idleState);
                fromAttack.hasExitTime = true;
                fromAttack.exitTime = 0.85f;
                fromAttack.duration = 0.15f;
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // 5. Connect to PlayerPrefab
        if (File.Exists(PlayerPrefabPath))
        {
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (playerRoot != null)
            {
                PlayerAnimationSync sync = playerRoot.GetComponent<PlayerAnimationSync>();
                if (sync != null)
                {
                    SerializedObject syncSO = new SerializedObject(sync);
                    SerializedProperty defaultCtrlProp = syncSO.FindProperty("defaultController");
                    if (defaultCtrlProp != null)
                    {
                        defaultCtrlProp.objectReferenceValue = controller;
                        syncSO.ApplyModifiedProperties();
                    }
                }

                Transform bodyTr = playerRoot.transform.Find("Body");
                if (bodyTr != null)
                {
                    Animator rogueBodyAnim = bodyTr.GetComponent<Animator>();
                    if (rogueBodyAnim != null)
                    {
                        Object.DestroyImmediate(rogueBodyAnim);
                    }

                    Transform visual = bodyTr.Find("CharacterVisual");
                    if (visual == null)
                    {
                        visual = bodyTr.Find("MonsterVisual");
                    }

                    GameObject animTarget = visual != null ? visual.gameObject : bodyTr.gameObject;
                    Animator anim = animTarget.GetComponent<Animator>();
                    if (anim == null)
                    {
                        anim = animTarget.AddComponent<Animator>();
                    }

                    if (anim != null)
                    {
                        anim.runtimeAnimatorController = controller;
                        Avatar rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>("Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx");
                        if (rigAvatar != null)
                        {
                            anim.avatar = rigAvatar;
                        }
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }

            GameObject playerPrefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefabObj != null)
            {
                Selection.activeObject = playerPrefabObj;
                EditorGUIUtility.PingObject(playerPrefabObj);
            }
        }

        string clipMsg = walkClip != null ? walkClip.name : "detected";
        Debug.Log($"[AnimationSetupHelper] Successfully configured player walk animation ({clipMsg}) and wired PlayerAnimatorController!");
    }

    public static void SetupMonsterWalkAnimation(string specificFbxPath = null)
    {
        // 1. Locate walk animation FBX
        string walkFbxPath = specificFbxPath;
        if (string.IsNullOrEmpty(walkFbxPath) || !File.Exists(walkFbxPath))
        {
            string[] candidatePaths = new string[]
            {
                "Assets/_Project/Art/Models/Characters/MainCharacter_Walk_Animation.fbx",
                "Assets/_Project/Animations/Monsters/Monster_Walk_Animation.fbx",
                "Assets/_Project/Animations/Players/MainCharacter_Walk_Animation.fbx"
            };

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                if (File.Exists(candidatePaths[i]))
                {
                    walkFbxPath = candidatePaths[i];
                    break;
                }
            }

            if (string.IsNullOrEmpty(walkFbxPath))
            {
                string[] found = AssetDatabase.FindAssets("MainCharacter_Walk t:GameObject");
                if (found != null && found.Length > 0)
                {
                    walkFbxPath = AssetDatabase.GUIDToAssetPath(found[0]);
                }
            }
        }

        if (string.IsNullOrEmpty(walkFbxPath) || !File.Exists(walkFbxPath))
        {
            return;
        }

        // 2. Find AnimationClip inside the imported FBX
        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(walkFbxPath);
        AnimationClip walkClip = null;
        if (subAssets != null)
        {
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    walkClip = clip;
                    break;
                }
            }
        }

        // 3. Ensure Monster Animator Controller
        AnimatorController controller = CreateMonsterAnimatorController();
        if (controller == null)
        {
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MonsterControllerPath);
        }

        if (controller == null)
        {
            Debug.LogError($"[AnimationSetupHelper] Could not load or create monster controller at '{MonsterControllerPath}'");
            return;
        }

        var rootStateMachine = controller.layers[0].stateMachine;

        // Find or create states
        AnimatorState idleState = null;
        AnimatorState walkState = null;
        AnimatorState chaseState = null;
        AnimatorState attackState = null;
        AnimatorState stunState = null;

        for (int i = 0; i < rootStateMachine.states.Length; i++)
        {
            var childState = rootStateMachine.states[i];
            if (childState.state.name == "Idle")
            {
                idleState = childState.state;
            }
            else if (childState.state.name == "Walk")
            {
                walkState = childState.state;
            }
            else if (childState.state.name == "Chase")
            {
                chaseState = childState.state;
            }
            else if (childState.state.name == "Attack")
            {
                attackState = childState.state;
            }
            else if (childState.state.name == "Stunned")
            {
                stunState = childState.state;
            }
        }

        if (idleState == null)
        {
            idleState = rootStateMachine.AddState("Idle");
        }
        if (walkState == null)
        {
            walkState = rootStateMachine.AddState("Walk");
        }
        if (chaseState == null)
        {
            chaseState = rootStateMachine.AddState("Chase");
        }
        if (attackState == null)
        {
            attackState = rootStateMachine.AddState("Attack");
        }
        if (stunState == null)
        {
            stunState = rootStateMachine.AddState("Stunned");
        }

        rootStateMachine.defaultState = idleState;

        // Assign walk motion
        if (walkClip != null)
        {
            AnimationClip effectiveClip = GetOrCreateUniversalWalkClip(walkClip);
            if (effectiveClip == null)
            {
                effectiveClip = walkClip;
            }

            walkState.motion = effectiveClip;
            walkState.speed = 1.0f;

            chaseState.motion = effectiveClip;
            chaseState.speed = 1.35f;
        }

        // Configure transitions
        ClearTransitionsBetween(idleState, walkState);
        ClearTransitionsBetween(walkState, idleState);
        ClearTransitionsBetween(walkState, chaseState);
        ClearTransitionsBetween(chaseState, walkState);
        ClearTransitionsBetween(idleState, chaseState);
        ClearTransitionsBetween(chaseState, idleState);

        // Idle -> Walk (Speed > 0.1)
        var toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.15f;
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        // Walk -> Idle (Speed < 0.1)
        var toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.15f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        // Walk -> Chase (IsChasing == true)
        var toChase = walkState.AddTransition(chaseState);
        toChase.hasExitTime = false;
        toChase.duration = 0.15f;
        toChase.AddCondition(AnimatorConditionMode.If, 0f, "IsChasing");

        // Chase -> Walk (IsChasing == false)
        var fromChase = chaseState.AddTransition(walkState);
        fromChase.hasExitTime = false;
        fromChase.duration = 0.15f;
        fromChase.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsChasing");

        // Chase -> Idle (Speed < 0.1)
        var chaseToIdle = chaseState.AddTransition(idleState);
        chaseToIdle.hasExitTime = false;
        chaseToIdle.duration = 0.15f;
        chaseToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        // Idle -> Chase (IsChasing == true)
        var idleToChase = idleState.AddTransition(chaseState);
        idleToChase.hasExitTime = false;
        idleToChase.duration = 0.15f;
        idleToChase.AddCondition(AnimatorConditionMode.If, 0f, "IsChasing");

        // Attack transition from AnyState
        if (attackState != null)
        {
            if (attackState.motion == null)
            {
                AnimationClip attackClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Players/MainCharacter_Attack.anim");
                if (attackClip != null)
                {
                    attackState.motion = attackClip;
                }
            }

            bool hasAttackAny = false;
            for (int i = 0; i < rootStateMachine.anyStateTransitions.Length; i++)
            {
                if (rootStateMachine.anyStateTransitions[i].destinationState == attackState)
                {
                    hasAttackAny = true;
                    break;
                }
            }
            if (!hasAttackAny)
            {
                var toAttack = rootStateMachine.AddAnyStateTransition(attackState);
                toAttack.hasExitTime = false;
                toAttack.duration = 0.05f;
                toAttack.canTransitionToSelf = false;
                toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            }

            bool hasAttackReturn = false;
            for (int i = 0; i < attackState.transitions.Length; i++)
            {
                if (attackState.transitions[i].destinationState == idleState)
                {
                    hasAttackReturn = true;
                    break;
                }
            }
            if (!hasAttackReturn && idleState != null)
            {
                var fromAttack = attackState.AddTransition(idleState);
                fromAttack.hasExitTime = true;
                fromAttack.exitTime = 0.85f;
                fromAttack.duration = 0.15f;
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // 4. Connect to all Enemy Prefabs
        List<string> targetPrefabs = new List<string>();
        if (Directory.Exists("Assets/_Project/Prefabs/Entities"))
        {
            string[] foundPrefabs = Directory.GetFiles("Assets/_Project/Prefabs/Entities", "Enemy*.prefab", SearchOption.TopDirectoryOnly);
            for (int f = 0; f < foundPrefabs.Length; f++)
            {
                targetPrefabs.Add(foundPrefabs[f].Replace('\\', '/'));
            }
        }

        if (targetPrefabs.Count == 0)
        {
            targetPrefabs.Add("Assets/_Project/Prefabs/Entities/EnemyPrefab.prefab");
        }

        for (int i = 0; i < targetPrefabs.Count; i++)
        {
            string prefabPath = targetPrefabs[i];
            if (!File.Exists(prefabPath))
            {
                continue;
            }

            GameObject enemyRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (enemyRoot != null)
            {
                MonsterAnimationSync sync = enemyRoot.GetComponent<MonsterAnimationSync>();
                if (sync != null)
                {
                    SerializedObject syncSO = new SerializedObject(sync);
                    SerializedProperty defaultCtrlProp = syncSO.FindProperty("defaultController");
                    if (defaultCtrlProp != null)
                    {
                        defaultCtrlProp.objectReferenceValue = controller;
                        syncSO.ApplyModifiedProperties();
                    }
                }

                Transform bodyTr = enemyRoot.transform.Find("Body");
                if (bodyTr != null)
                {
                    Animator rogueBodyAnim = bodyTr.GetComponent<Animator>();
                    if (rogueBodyAnim != null)
                    {
                        Object.DestroyImmediate(rogueBodyAnim);
                    }

                    Transform visual = bodyTr.Find("MonsterVisual");
                    if (visual == null)
                    {
                        visual = bodyTr.Find("CharacterVisual");
                    }

                    GameObject animTarget = visual != null ? visual.gameObject : bodyTr.gameObject;
                    Animator anim = animTarget.GetComponent<Animator>();
                    if (anim == null)
                    {
                        anim = animTarget.AddComponent<Animator>();
                    }

                    if (anim != null)
                    {
                        anim.runtimeAnimatorController = controller;
                        Avatar rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>("Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx");
                        if (rigAvatar != null)
                        {
                            anim.avatar = rigAvatar;
                        }
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(enemyRoot, prefabPath);
                PrefabUtility.UnloadPrefabContents(enemyRoot);
            }
        }

        GameObject enemyPrefabObj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Entities/EnemyPrefab.prefab");
        if (enemyPrefabObj != null)
        {
            Selection.activeObject = enemyPrefabObj;
            EditorGUIUtility.PingObject(enemyPrefabObj);
        }

        string clipMsg = walkClip != null ? walkClip.name : "detected";
        Debug.Log($"[AnimationSetupHelper] Successfully configured enemy walk animation ({clipMsg}) and wired MonsterAnimatorController!");
    }

    private static void ClearTransitionsBetween(AnimatorState fromState, AnimatorState toState)
    {
        if (fromState == null || toState == null)
        {
            return;
        }

        var transitions = fromState.transitions;
        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            if (transitions[i].destinationState == toState)
            {
                fromState.RemoveTransition(transitions[i]);
            }
        }
    }

    public static AnimatorController CreatePlayerAnimatorController()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath) != null)
        {
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        }

        EnsureDirectory("Assets/_Project/Animations/Players");
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(PlayerControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsSprinting", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

        var rootStateMachine = controller.layers[0].stateMachine;
        var idleState = rootStateMachine.AddState("Idle");
        var walkState = rootStateMachine.AddState("Walk");
        var runState = rootStateMachine.AddState("Run");
        var attackState = rootStateMachine.AddState("Attack");

        rootStateMachine.defaultState = idleState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AnimationSetupHelper] Created Player Animator Controller at '{PlayerControllerPath}'");
        return controller;
    }

    public static AnimatorController CreateMonsterAnimatorController()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(MonsterControllerPath) != null)
        {
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(MonsterControllerPath);
        }

        EnsureDirectory("Assets/_Project/Animations/Monsters");
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(MonsterControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsChasing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsStunned", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

        var rootStateMachine = controller.layers[0].stateMachine;
        var idleState = rootStateMachine.AddState("Idle");
        var chaseState = rootStateMachine.AddState("Chase");
        var attackState = rootStateMachine.AddState("Attack");
        var stunState = rootStateMachine.AddState("Stunned");

        rootStateMachine.defaultState = idleState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AnimationSetupHelper] Created Monster Animator Controller at '{MonsterControllerPath}'");
        return controller;
    }

    public static void RepairPrefabAnimationSetups()
    {
        string rigPath = "Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx";
        GameObject rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
        Avatar rigAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(rigPath);
        AnimatorController playerController = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
        AnimatorController monsterController = AssetDatabase.LoadAssetAtPath<AnimatorController>(MonsterControllerPath);

        // 1. Repair PlayerPrefab
        if (File.Exists(PlayerPrefabPath) && rigAsset != null)
        {
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (playerRoot != null)
            {
                Transform bodyTr = playerRoot.transform.Find("Body");
                if (bodyTr != null)
                {
                    // Clean rogue Animator on Body
                    Animator rogueBodyAnim = bodyTr.GetComponent<Animator>();
                    if (rogueBodyAnim != null)
                    {
                        Object.DestroyImmediate(rogueBodyAnim);
                    }

                    // Check existing CharacterVisual
                    Transform charVisual = bodyTr.Find("CharacterVisual");
                    bool needsNewVisual = false;
                    if (charVisual == null)
                    {
                        needsNewVisual = true;
                    }
                    else
                    {
                        Renderer[] rends = charVisual.GetComponentsInChildren<Renderer>(true);
                        MeshFilter[] filters = charVisual.GetComponentsInChildren<MeshFilter>(true);
                        if (rends.Length == 0 && filters.Length == 0)
                        {
                            needsNewVisual = true;
                            Object.DestroyImmediate(charVisual.gameObject);
                        }
                    }

                    if (needsNewVisual)
                    {
                        GameObject visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
                        visualInstance.name = "CharacterVisual";
                        visualInstance.transform.SetParent(bodyTr, false);
                        visualInstance.transform.localPosition = Vector3.zero;
                        visualInstance.transform.localRotation = Quaternion.identity;
                        visualInstance.transform.localScale = Vector3.one;
                        charVisual = visualInstance.transform;
                    }

                    if (charVisual != null)
                    {
                        Animator anim = charVisual.GetComponent<Animator>();
                        if (anim == null)
                        {
                            anim = charVisual.gameObject.AddComponent<Animator>();
                        }
                        if (anim != null)
                        {
                            if (playerController != null)
                            {
                                anim.runtimeAnimatorController = playerController;
                            }
                            if (rigAvatar != null)
                            {
                                anim.avatar = rigAvatar;
                            }
                        }
                    }
                }

                PlayerAnimationSync sync = playerRoot.GetComponent<PlayerAnimationSync>();
                if (sync != null && playerController != null)
                {
                    SerializedObject syncSO = new SerializedObject(sync);
                    SerializedProperty defaultCtrlProp = syncSO.FindProperty("defaultController");
                    if (defaultCtrlProp != null)
                    {
                        defaultCtrlProp.objectReferenceValue = playerController;
                        syncSO.ApplyModifiedProperties();
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
                PrefabUtility.UnloadPrefabContents(playerRoot);
                Debug.Log("[AnimationSetupHelper] Repaired PlayerPrefab: CharacterVisual rig model assigned with direct Animator & PlayerAnimatorController!");
            }
        }

        // 2. Repair Enemy Prefabs
        List<string> enemyPrefabs = new List<string>();
        if (Directory.Exists("Assets/_Project/Prefabs/Entities"))
        {
            string[] found = Directory.GetFiles("Assets/_Project/Prefabs/Entities", "Enemy*.prefab", SearchOption.TopDirectoryOnly);
            for (int f = 0; f < found.Length; f++)
            {
                enemyPrefabs.Add(found[f].Replace('\\', '/'));
            }
        }

        for (int i = 0; i < enemyPrefabs.Count; i++)
        {
            string enemyPath = enemyPrefabs[i];
            GameObject enemyRoot = PrefabUtility.LoadPrefabContents(enemyPath);
            if (enemyRoot == null)
            {
                continue;
            }

            Transform bodyTr = enemyRoot.transform.Find("Body");
            if (bodyTr != null)
            {
                Animator rogueBodyAnim = bodyTr.GetComponent<Animator>();
                if (rogueBodyAnim != null)
                {
                    Object.DestroyImmediate(rogueBodyAnim);
                }

                Transform monsterVisual = bodyTr.Find("MonsterVisual");
                if (monsterVisual == null)
                {
                    monsterVisual = bodyTr.Find("CharacterVisual");
                }

                bool needsNewVisual = false;
                if (monsterVisual == null)
                {
                    needsNewVisual = true;
                }
                else
                {
                    Renderer[] rends = monsterVisual.GetComponentsInChildren<Renderer>(true);
                    MeshFilter[] filters = monsterVisual.GetComponentsInChildren<MeshFilter>(true);
                    if (rends.Length == 0 && filters.Length == 0)
                    {
                        needsNewVisual = true;
                        Object.DestroyImmediate(monsterVisual.gameObject);
                    }
                }

                if (needsNewVisual && rigAsset != null)
                {
                    GameObject visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset);
                    visualInstance.name = "MonsterVisual";
                    visualInstance.transform.SetParent(bodyTr, false);
                    visualInstance.transform.localPosition = Vector3.zero;
                    visualInstance.transform.localRotation = Quaternion.identity;
                    visualInstance.transform.localScale = Vector3.one;
                    monsterVisual = visualInstance.transform;
                }

                if (monsterVisual != null)
                {
                    Animator anim = monsterVisual.GetComponent<Animator>();
                    if (anim == null)
                    {
                        anim = monsterVisual.gameObject.AddComponent<Animator>();
                    }
                    if (anim != null)
                    {
                        if (monsterController != null)
                        {
                            anim.runtimeAnimatorController = monsterController;
                        }
                        if (rigAvatar != null)
                        {
                            anim.avatar = rigAvatar;
                        }
                    }
                }
            }

            MonsterAnimationSync sync = enemyRoot.GetComponent<MonsterAnimationSync>();
            if (sync != null && monsterController != null)
            {
                SerializedObject syncSO = new SerializedObject(sync);
                SerializedProperty defaultCtrlProp = syncSO.FindProperty("defaultController");
                if (defaultCtrlProp != null)
                {
                    defaultCtrlProp.objectReferenceValue = monsterController;
                    syncSO.ApplyModifiedProperties();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(enemyRoot, enemyPath);
            PrefabUtility.UnloadPrefabContents(enemyRoot);
            Debug.Log($"[AnimationSetupHelper] Repaired enemy prefab '{Path.GetFileName(enemyPath)}': direct Animator on visual with MonsterAnimatorController!");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static AnimationClip GetOrCreateUniversalWalkClip(AnimationClip sourceFbxClip)
    {
        if (sourceFbxClip == null)
        {
            return null;
        }

        string animPath = "Assets/_Project/Animations/Players/MainCharacter_Walk.anim";
        AnimationClip animClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
        if (animClip == null)
        {
            EnsureDirectory("Assets/_Project/Animations/Players");
            animClip = new AnimationClip();
            animClip.name = "MainCharacter_Walk";
            AssetDatabase.CreateAsset(animClip, animPath);
        }

        animClip.ClearCurves();

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceFbxClip);
        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding b = bindings[i];
            AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceFbxClip, b);

            // 1. Original path (e.g. "Body/Leg_1.L")
            AnimationUtility.SetEditorCurve(animClip, b, curve);

            // 2. Dual-bind: prepended with "Rig/" (e.g. "Rig/Body/Leg_1.L")
            if (!b.path.StartsWith("Rig/"))
            {
                EditorCurveBinding rigBinding = b;
                rigBinding.path = "Rig/" + b.path;
                AnimationUtility.SetEditorCurve(animClip, rigBinding, curve);
            }
        }

        var settings = AnimationUtility.GetAnimationClipSettings(sourceFbxClip);
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(animClip, settings);

        EditorUtility.SetDirty(animClip);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AnimationSetupHelper] Created universal dual-bound walk clip at '{animPath}' with {bindings.Length * 2} bindings (Body & Rig/Body)!");
        return animClip;
    }

    public static void SetupMonsterIdleAnimation(string specificFbxPath = null)
    {
        // 1. Locate idle animation FBX
        string idleFbxPath = specificFbxPath;
        if (string.IsNullOrEmpty(idleFbxPath) || !File.Exists(idleFbxPath))
        {
            string[] candidatePaths = new string[]
            {
                "Assets/_Project/Art/Models/Characters/MainCharacter_Idle.fbx",
                "Assets/_Project/Animations/Monsters/Monster_Idle_Animation.fbx",
                "Assets/_Project/Animations/Players/MainCharacter_Idle.fbx"
            };

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                if (File.Exists(candidatePaths[i]))
                {
                    idleFbxPath = candidatePaths[i];
                    break;
                }
            }

            if (string.IsNullOrEmpty(idleFbxPath))
            {
                string[] found = AssetDatabase.FindAssets("MainCharacter_Idle t:GameObject");
                if (found != null && found.Length > 0)
                {
                    idleFbxPath = AssetDatabase.GUIDToAssetPath(found[0]);
                }
            }
        }

        if (string.IsNullOrEmpty(idleFbxPath) || !File.Exists(idleFbxPath))
        {
            return;
        }

        // 2. Find AnimationClip inside the imported FBX
        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(idleFbxPath);
        AnimationClip idleClip = null;
        if (subAssets != null)
        {
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    idleClip = clip;
                    break;
                }
            }
        }

        // 3. Ensure Monster Animator Controller
        AnimatorController controller = CreateMonsterAnimatorController();
        if (controller == null)
        {
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MonsterControllerPath);
        }

        if (controller == null)
        {
            return;
        }

        var rootStateMachine = controller.layers[0].stateMachine;

        // Find Idle state
        AnimatorState idleState = null;
        for (int i = 0; i < rootStateMachine.states.Length; i++)
        {
            var childState = rootStateMachine.states[i];
            if (childState.state.name == "Idle")
            {
                idleState = childState.state;
                break;
            }
        }

        if (idleState != null && idleClip != null)
        {
            AnimationClip effectiveClip = GetOrCreateUniversalIdleClip(idleClip);
            if (effectiveClip == null)
            {
                effectiveClip = idleClip;
            }

            idleState.motion = effectiveClip;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[AnimationSetupHelper] Successfully configured monster idle animation on MonsterAnimatorController!");
        }
    }

    public static AnimationClip GetOrCreateUniversalIdleClip(AnimationClip sourceFbxClip)
    {
        if (sourceFbxClip == null)
        {
            return null;
        }

        string animPath = "Assets/_Project/Animations/Players/MainCharacter_Idle.anim";
        AnimationClip animClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
        if (animClip == null)
        {
            EnsureDirectory("Assets/_Project/Animations/Players");
            animClip = new AnimationClip();
            animClip.name = "MainCharacter_Idle";
            AssetDatabase.CreateAsset(animClip, animPath);
        }

        animClip.ClearCurves();

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceFbxClip);
        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding b = bindings[i];
            AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceFbxClip, b);

            // 1. Original path
            AnimationUtility.SetEditorCurve(animClip, b, curve);

            // 2. Dual-bind: if starts with Rig/, also bind without Rig/
            if (b.path.StartsWith("Rig/"))
            {
                EditorCurveBinding nonRigBinding = b;
                nonRigBinding.path = b.path.Substring(4);
                AnimationUtility.SetEditorCurve(animClip, nonRigBinding, curve);
            }
            // 3. If does not start with Rig/, also bind with Rig/
            else
            {
                EditorCurveBinding rigBinding = b;
                rigBinding.path = "Rig/" + b.path;
                AnimationUtility.SetEditorCurve(animClip, rigBinding, curve);
            }
        }

        var settings = AnimationUtility.GetAnimationClipSettings(sourceFbxClip);
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(animClip, settings);

        EditorUtility.SetDirty(animClip);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AnimationSetupHelper] Created universal dual-bound idle clip at '{animPath}' with {bindings.Length * 2} bindings!");
        return animClip;
    }

    public static void SetupMonsterSpecificAnimations()
    {
        string[] searchFolders = new string[]
        {
            "Assets/_Project/Art/Models/Monsters",
            "Assets/_Project/Art/Models/Characters",
            "Assets/_Project/Animations/Monsters"
        };

        AnimatorController baseMonsterCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(MonsterControllerPath);
        if (baseMonsterCtrl == null)
        {
            return;
        }

        // Collect all available enemy prefabs
        List<string> enemyPrefabs = new List<string>();
        if (Directory.Exists("Assets/_Project/Prefabs/Entities"))
        {
            string[] found = Directory.GetFiles("Assets/_Project/Prefabs/Entities", "Enemy_*.prefab", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < found.Length; i++)
            {
                enemyPrefabs.Add(found[i].Replace('\\', '/'));
            }
        }

        // Collect all FBX and anim files in search folders
        List<string> candidateFiles = new List<string>();
        for (int f = 0; f < searchFolders.Length; f++)
        {
            if (Directory.Exists(searchFolders[f]))
            {
                string[] files = Directory.GetFiles(searchFolders[f], "*.*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext == ".fbx" || ext == ".anim")
                    {
                        candidateFiles.Add(files[i].Replace('\\', '/'));
                    }
                }
            }
        }

        for (int p = 0; p < enemyPrefabs.Count; p++)
        {
            string prefabPath = enemyPrefabs[p];
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            string monsterKey = prefabName.Replace("Enemy_", "").ToLowerInvariant().Replace("_", "");

            AnimationClip customIdle = FindClipForMonster(candidateFiles, monsterKey, "idle");
            AnimationClip customWalk = FindClipForMonster(candidateFiles, monsterKey, "walk");
            AnimationClip customChase = FindClipForMonster(candidateFiles, monsterKey, "chase");
            if (customChase == null)
            {
                customChase = FindClipForMonster(candidateFiles, monsterKey, "run");
            }
            AnimationClip customAttack = FindClipForMonster(candidateFiles, monsterKey, "attack");

            bool hasAnyCustom = customIdle != null || customWalk != null || customChase != null || customAttack != null;
            if (!hasAnyCustom)
            {
                continue;
            }

            EnsureDirectory("Assets/_Project/Animations/Monsters");
            string overridePath = $"Assets/_Project/Animations/Monsters/{prefabName}Controller.overrideController";
            AnimatorOverrideController overrideCtrl = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
            if (overrideCtrl == null)
            {
                overrideCtrl = new AnimatorOverrideController(baseMonsterCtrl);
                AssetDatabase.CreateAsset(overrideCtrl, overridePath);
            }
            else
            {
                overrideCtrl.runtimeAnimatorController = baseMonsterCtrl;
            }

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideCtrl.GetOverrides(overrides);

            for (int i = 0; i < overrides.Count; i++)
            {
                string clipName = overrides[i].Key != null ? overrides[i].Key.name.ToLowerInvariant() : "";
                if (clipName.Contains("idle") && customIdle != null)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, customIdle);
                }
                else if (clipName.Contains("walk") && customWalk != null)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, customWalk);
                }
                else if ((clipName.Contains("chase") || clipName.Contains("run")) && customChase != null)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, customChase);
                }
                else if (clipName.Contains("attack") && customAttack != null)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, customAttack);
                }
            }
            overrideCtrl.ApplyOverrides(overrides);
            EditorUtility.SetDirty(overrideCtrl);

            GameObject enemyRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (enemyRoot != null)
            {
                Transform monsterVisual = enemyRoot.transform.Find("Body/MonsterVisual");
                if (monsterVisual == null)
                {
                    monsterVisual = enemyRoot.transform.Find("Body/CharacterVisual");
                }
                if (monsterVisual != null)
                {
                    Animator anim = monsterVisual.GetComponent<Animator>();
                    if (anim != null)
                    {
                        anim.runtimeAnimatorController = overrideCtrl;
                    }
                }

                MonsterAnimationSync sync = enemyRoot.GetComponent<MonsterAnimationSync>();
                if (sync != null)
                {
                    SerializedObject syncSO = new SerializedObject(sync);
                    SerializedProperty defaultCtrlProp = syncSO.FindProperty("defaultController");
                    if (defaultCtrlProp != null)
                    {
                        defaultCtrlProp.objectReferenceValue = overrideCtrl;
                        syncSO.ApplyModifiedProperties();
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(enemyRoot, prefabPath);
                PrefabUtility.UnloadPrefabContents(enemyRoot);
                Debug.Log($"[AnimationSetupHelper] Wired custom animations for '{prefabName}' using AnimatorOverrideController at '{overridePath}'!");
            }
        }
    }

    private static AnimationClip FindClipForMonster(List<string> files, string monsterKey, string actionKey)
    {
        for (int i = 0; i < files.Count; i++)
        {
            string fileName = Path.GetFileNameWithoutExtension(files[i]).ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
            if (fileName.Contains(monsterKey) && fileName.Contains(actionKey))
            {
                string fPath = files[i];
                if (fPath.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase))
                {
                    return AssetDatabase.LoadAssetAtPath<AnimationClip>(fPath);
                }
                else if (fPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fPath);
                    if (subAssets != null)
                    {
                        for (int s = 0; s < subAssets.Length; s++)
                        {
                            if (subAssets[s] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                            {
                                return clip;
                            }
                        }
                    }
                }
            }
        }
        return null;
    }

    private static void EnsureDirectory(string relativePath)
    {
        if (!System.IO.Directory.Exists(relativePath))
        {
            System.IO.Directory.CreateDirectory(relativePath);
        }
    }
}
#endif
