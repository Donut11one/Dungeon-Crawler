#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine.AI;

/// <summary>
/// Editor utility that creates the complete pre-graybox demo scene with
/// all prefabs, NetworkManager, and game systems fully wired up.
/// Access via menu: Tools > Dungeon Crawler > Setup Demo Scene.
/// </summary>
public static class DemoSceneSetup
{
    private const string PrefabFolder = "Assets/_Project/Prefabs";
    private const string EntitiesFolder = "Assets/_Project/Prefabs/Entities";
    private const string ItemsFolder = "Assets/_Project/Prefabs/Items";

    [MenuItem("Tools/Dungeon Crawler/Setup Demo Scene")]
    public static void SetupScene()
    {
        // Confirm with user
        if (!EditorUtility.DisplayDialog(
            "Setup Demo Scene",
            "This will create a new demo scene with all prefabs and systems.\n\n" +
            "Existing prefabs in _Project/Prefabs/ will be overwritten.\n\nContinue?",
            "Yes, Set It Up", "Cancel"))
        {
            return;
        }

        // Ensure folders exist
        EnsureFolder("Assets/_Project");
        EnsureFolder(PrefabFolder);
        EnsureFolder(EntitiesFolder);
        EnsureFolder(ItemsFolder);

        // Create new scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Remove default directional light (we'll make our own)
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject go in roots)
        {
            if (go.GetComponent<Light>() != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        // === LIGHTING ===
        GameObject lightObj = new GameObject("DungeonLight");
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = new Color(0.85f, 0.9f, 1f);
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Ambient light (so the dungeon isn't pitch black)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.15f, 0.12f, 0.1f);

        // === NETWORK MANAGER ===
        GameObject networkManagerObj = new GameObject("NetworkManager");
        NetworkManager networkManager = networkManagerObj.AddComponent<NetworkManager>();
        UnityTransport transport = networkManagerObj.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");

        // === GAME MANAGER ===
        GameObject gameManagerObj = new GameObject("GameManager");
        gameManagerObj.AddComponent<NetworkObject>();
        gameManagerObj.AddComponent<GameManager>();
        gameManagerObj.AddComponent<DungeonBuilder>();

        // === UI (non-networked, scene objects) ===
        GameObject uiObj = new GameObject("GameUI");
        uiObj.AddComponent<LobbyUI>();
        uiObj.AddComponent<GameHUD>();

        // === CREATE PREFABS ===
        GameObject playerPrefab = CreatePlayerPrefab();
        GameObject barrelPrefab = CreateBarrelPrefab();
        GameObject enemyPrefab = CreateEnemyPrefab();

        // === REGISTER PREFABS WITH NETWORK MANAGER ===
        // Set the player prefab
        networkManager.NetworkConfig = new NetworkConfig();
        networkManager.NetworkConfig.PlayerPrefab = playerPrefab;

        // Assign DefaultNetworkPrefabs list so NGO serializes all prefabs into the scene
        NetworkPrefabsList defaultList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        if (defaultList != null)
        {
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(defaultList);
        }

        // Add barrel and enemy as network prefabs
        networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = barrelPrefab });
        networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = enemyPrefab });
        EditorUtility.SetDirty(networkManager);

        // === WIRE UP DUNGEON BUILDER ===
        DungeonBuilder dungeonBuilder = gameManagerObj.GetComponent<DungeonBuilder>();
        SerializedObject so = new SerializedObject(dungeonBuilder);
        so.FindProperty("barrelPrefab").objectReferenceValue = barrelPrefab;
        so.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(gameManagerObj);

        // === SAVE SCENE ===
        string scenePath = "Assets/_Project/Scenes";
        EnsureFolder(scenePath);
        EditorSceneManager.SaveScene(scene, scenePath + "/DemoScene.unity");

        // Select the NetworkManager so the user can see the setup
        Selection.activeGameObject = networkManagerObj;

        EditorUtility.DisplayDialog(
            "Demo Scene Ready!",
            "The demo scene has been created at:\n" +
            "Assets/_Project/Scenes/DemoScene.unity\n\n" +
            "Prefabs created:\n" +
            "• Player (Entities/PlayerPrefab)\n" +
            "• Barrel (Items/BarrelPrefab)\n" +
            "• Enemy (Entities/EnemyPrefab)\n\n" +
            "Hit Play to test! Use the lobby UI to Host or Join.",
            "Got It!");

        Debug.Log("[DemoSceneSetup] Demo scene created successfully!");
    }

    private static GameObject CreatePlayerPrefab()
    {
        // Root object
        GameObject player = new GameObject("PlayerPrefab");

        // NetworkObject
        player.AddComponent<NetworkObject>();

        // Physics
        Rigidbody rb = player.AddComponent<Rigidbody>();
        rb.mass = 70f;
        rb.linearDamping = 6f;
        rb.angularDamping = 10f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = 0.4f;
        capsule.center = new Vector3(0f, 1f, 0f);

        // Visual body (capsule)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.parent = player.transform;
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        // Remove the collider from the visual mesh (we use the root collider)
        Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
        // Give it a distinct color
        Renderer bodyRend = body.GetComponent<Renderer>();
        Material playerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        playerMat.color = new Color(0.2f, 0.6f, 0.3f); // Hazmat green
        bodyRend.sharedMaterial = playerMat;

        // Camera holder
        GameObject cameraHolder = new GameObject("CameraHolder");
        cameraHolder.transform.parent = player.transform;
        cameraHolder.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        // Camera
        GameObject cameraObj = new GameObject("PlayerCamera");
        cameraObj.transform.parent = cameraHolder.transform;
        cameraObj.transform.localPosition = new Vector3(0.35f, 0.35f, -2.8f);
        cameraObj.transform.localRotation = Quaternion.identity;
        Camera cam = cameraObj.AddComponent<Camera>();
        cam.nearClipPlane = 0.1f;
        cam.fieldOfView = 75f;
        cameraObj.AddComponent<AudioListener>();

        // Hold point (where grabbed objects float)
        GameObject holdPoint = new GameObject("HoldPoint");
        holdPoint.transform.parent = cameraHolder.transform;
        holdPoint.transform.localPosition = new Vector3(0f, -0.2f, 1.5f);

        // Game scripts
        player.AddComponent<OwnerNetworkTransform>();
        PlayerController controller = player.AddComponent<PlayerController>();
        player.AddComponent<RagdollController>();
        player.AddComponent<PlayerHealth>();
        player.AddComponent<PlayerWallet>();
        player.AddComponent<PlayerInventory>();
        player.AddComponent<PlayerAttack>();
        GrabThrow grabThrow = player.AddComponent<GrabThrow>();

        // Wire up serialized references
        SerializedObject controllerSO = new SerializedObject(controller);
        controllerSO.FindProperty("cameraHolder").objectReferenceValue = cameraHolder.transform;
        controllerSO.ApplyModifiedProperties();

        SerializedObject grabSO = new SerializedObject(grabThrow);
        grabSO.FindProperty("holdPoint").objectReferenceValue = holdPoint.transform;
        grabSO.ApplyModifiedProperties();

        // Save as prefab
        string path = EntitiesFolder + "/PlayerPrefab.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, path);
        Object.DestroyImmediate(player);

        Debug.Log("[DemoSceneSetup] Created Player prefab at " + path);
        return prefab;
    }

    private static GameObject CreateBarrelPrefab()
    {
        // Create a cylinder to represent a barrel
        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "BarrelPrefab";
        barrel.transform.localScale = new Vector3(0.8f, 0.6f, 0.8f);
        barrel.transform.position = Vector3.zero;

        // Color
        Renderer rend = barrel.GetComponent<Renderer>();
        Material barrelMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        barrelMat.color = new Color(0.6f, 0.35f, 0.15f); // Barrel brown
        rend.sharedMaterial = barrelMat;

        // Physics
        Rigidbody rb = barrel.AddComponent<Rigidbody>();
        rb.mass = 15f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Networking
        barrel.AddComponent<NetworkObject>();
        barrel.AddComponent<NetworkTransform>();

        // Grab & damage
        barrel.AddComponent<GrabbableObject>();
        barrel.AddComponent<ImpactDamage>();

        // Save as prefab
        string path = ItemsFolder + "/BarrelPrefab.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(barrel, path);
        Object.DestroyImmediate(barrel);

        Debug.Log("[DemoSceneSetup] Created Barrel prefab at " + path);
        return prefab;
    }

    private static GameObject CreateEnemyPrefab()
    {
        GameObject enemy = new GameObject("EnemyPrefab");

        // NetworkObject
        enemy.AddComponent<NetworkObject>();

        // Physics
        Rigidbody rb = enemy.AddComponent<Rigidbody>();
        rb.mass = 60f;
        rb.linearDamping = 4f;
        rb.angularDamping = 10f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        CapsuleCollider capsule = enemy.AddComponent<CapsuleCollider>();
        capsule.height = 2.2f;
        capsule.radius = 0.5f;
        capsule.center = new Vector3(0f, 1.1f, 0f);

        // Visual body (capsule, bigger and red)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.parent = enemy.transform;
        body.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        body.transform.localScale = new Vector3(1f, 1.1f, 1f);
        Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
        Renderer bodyRend = body.GetComponent<Renderer>();
        Material enemyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        enemyMat.color = new Color(0.7f, 0.15f, 0.15f); // Angry red
        bodyRend.sharedMaterial = enemyMat;

        // "Eyes" (two small spheres for comedic effect)
        CreateEye(body.transform, new Vector3(-0.2f, 0.3f, 0.4f));
        CreateEye(body.transform, new Vector3(0.2f, 0.3f, 0.4f));

        // NavMeshAgent
        NavMeshAgent agent = enemy.AddComponent<NavMeshAgent>();
        agent.speed = 3.5f;
        agent.angularSpeed = 300f;
        agent.acceleration = 10f;
        agent.stoppingDistance = 1.5f;
        agent.radius = 0.5f;
        agent.height = 2.2f;

        // Game scripts
        enemy.AddComponent<NetworkTransform>();
        enemy.AddComponent<RagdollController>();
        enemy.AddComponent<BasicEnemyAI>();
        enemy.AddComponent<EnemyHealth>();

        // Save as prefab
        string path = EntitiesFolder + "/EnemyPrefab.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(enemy, path);
        Object.DestroyImmediate(enemy);

        Debug.Log("[DemoSceneSetup] Created Enemy prefab at " + path);
        return prefab;
    }

    private static void CreateEye(Transform parent, Vector3 localPos)
    {
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        eye.transform.parent = parent;
        eye.transform.localPosition = localPos;
        eye.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
        Object.DestroyImmediate(eye.GetComponent<SphereCollider>());

        Renderer rend = eye.GetComponent<Renderer>();
        Material eyeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        eyeMat.color = Color.yellow;
        eyeMat.SetFloat("_Smoothness", 0.9f);
        rend.sharedMaterial = eyeMat;
    }

    private static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
#endif
