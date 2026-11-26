using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Modular networked world item pickup for weapons, tools, consumables, and scrap.
/// Inherits from WorldPickup: players aim directly at the item and press [E] to collect it.
/// Adds the item into the player's inventory hotbar and despawns.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(SphereCollider))]
public class WorldItemPickup : WorldPickup
{
    public NetworkVariable<FixedString32Bytes> ItemId = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> Count = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private SphereCollider triggerCol;
    private SphereCollider solidCol;
    private Rigidbody rb;
    private GameObject visualMesh;
    private float spinPhase;
    private float settleTimer = 2.0f;
    private bool hasSettled = false;

    private void Awake()
    {
        SphereCollider[] cols = GetComponents<SphereCollider>();
        if (cols.Length > 0)
        {
            solidCol = cols[0];
            solidCol.isTrigger = false;
            solidCol.radius = 0.22f;
        }
        else
        {
            solidCol = gameObject.AddComponent<SphereCollider>();
            solidCol.isTrigger = false;
            solidCol.radius = 0.22f;
        }

        if (cols.Length > 1)
        {
            triggerCol = cols[1];
        }
        else
        {
            triggerCol = gameObject.AddComponent<SphereCollider>();
        }
        triggerCol.isTrigger = true;
        triggerCol.radius = 0.85f;

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.mass = 1.2f;
            rb.linearDamping = 1.0f;
            rb.angularDamping = 1.5f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        spinPhase = Random.Range(0f, Mathf.PI * 2f);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ItemRegistry.Initialize();
        ItemId.OnValueChanged += OnItemIdChanged;
        SetupVisuals();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        ItemId.OnValueChanged -= OnItemIdChanged;
    }

    private void OnItemIdChanged(FixedString32Bytes previous, FixedString32Bytes current)
    {
        SetupVisuals();
    }

    private void SetupVisuals()
    {
        if (visualMesh != null)
        {
            Destroy(visualMesh);
        }

        string idStr = ItemId.Value.ToString();
        ItemData data = ItemRegistry.GetItem(idStr);
        Color displayColor = data != null ? data.SlotColor : new Color(0.3f, 0.8f, 1f);

        // Check if item has a custom world prefab, else construct a crisp visual marker
        if (data != null && data.WorldPrefab != null)
        {
            visualMesh = Instantiate(data.WorldPrefab, transform);
            visualMesh.transform.localPosition = Vector3.zero;
            visualMesh.transform.localRotation = Quaternion.identity;
            return;
        }

        visualMesh = new GameObject("PickupVisual");
        visualMesh.transform.parent = transform;
        visualMesh.transform.localPosition = Vector3.zero;
        visualMesh.transform.localRotation = Quaternion.identity;

        ConstructProceduralVisual(visualMesh, idStr, displayColor);
    }

    private void ConstructProceduralVisual(GameObject parent, string id, Color color)
    {
        switch (id)
        {
            case "abnormality_core":
                // Glowing bio-organic core sphere + orbital nodes + point light
                GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                core.transform.parent = parent.transform;
                core.transform.localPosition = Vector3.zero;
                core.transform.localScale = new Vector3(0.32f, 0.32f, 0.32f);
                StripColliders(core);
                ApplyMaterial(core, color, true);

                for (int i = 0; i < 3; i++)
                {
                    float angle = i * (Mathf.PI * 2f / 3f);
                    GameObject node = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    node.transform.parent = parent.transform;
                    node.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.24f, Mathf.Sin(angle) * 0.12f, Mathf.Sin(angle) * 0.24f);
                    node.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
                    StripColliders(node);
                    ApplyMaterial(node, new Color(1f, 0.3f, 0.8f), true);
                }

                AddLight(parent, color, 2.5f, 1.4f);
                break;

            case "gold_scrap":
                // Golden circuitry board with microchips
                GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.transform.parent = parent.transform;
                board.transform.localPosition = Vector3.zero;
                board.transform.localScale = new Vector3(0.36f, 0.06f, 0.26f);
                StripColliders(board);
                ApplyMaterial(board, new Color(0.95f, 0.78f, 0.15f), true);

                GameObject chip1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chip1.transform.parent = parent.transform;
                chip1.transform.localPosition = new Vector3(0.08f, 0.04f, 0.04f);
                chip1.transform.localScale = new Vector3(0.12f, 0.04f, 0.10f);
                StripColliders(chip1);
                ApplyMaterial(chip1, new Color(0.12f, 0.12f, 0.12f));

                GameObject chip2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chip2.transform.parent = parent.transform;
                chip2.transform.localPosition = new Vector3(-0.08f, 0.04f, -0.04f);
                chip2.transform.localScale = new Vector3(0.09f, 0.04f, 0.09f);
                chip2.transform.localScale = new Vector3(0.09f, 0.04f, 0.09f);
                StripColliders(chip2);
                ApplyMaterial(chip2, new Color(0.2f, 0.8f, 0.4f), true);

                AddLight(parent, new Color(1f, 0.85f, 0.2f), 2.2f, 1.2f);
                break;

            case "salvaged_sensor":
                // Optic scanner cylinder with glowing lens
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                body.transform.parent = parent.transform;
                body.transform.localPosition = Vector3.zero;
                body.transform.localScale = new Vector3(0.18f, 0.12f, 0.18f);
                StripColliders(body);
                ApplyMaterial(body, new Color(0.2f, 0.22f, 0.26f));

                GameObject lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lens.transform.parent = parent.transform;
                lens.transform.localPosition = new Vector3(0f, 0.11f, 0f);
                lens.transform.localScale = new Vector3(0.14f, 0.06f, 0.14f);
                StripColliders(lens);
                ApplyMaterial(lens, new Color(0.2f, 1f, 0.5f), true);

                AddLight(parent, new Color(0.2f, 1f, 0.5f), 2.0f, 1.2f);
                break;

            case "scrap_metal":
                // Heavy metallic hull plate
                GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.transform.parent = parent.transform;
                plate.transform.localPosition = Vector3.zero;
                plate.transform.localScale = new Vector3(0.38f, 0.05f, 0.28f);
                plate.transform.localRotation = Quaternion.Euler(12f, 25f, 8f);
                StripColliders(plate);
                ApplyMaterial(plate, new Color(0.55f, 0.60f, 0.68f));
                break;

            case "medkit":
                // White emergency case with red cross
                GameObject medBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                medBox.transform.parent = parent.transform;
                medBox.transform.localPosition = Vector3.zero;
                medBox.transform.localScale = new Vector3(0.32f, 0.22f, 0.18f);
                StripColliders(medBox);
                ApplyMaterial(medBox, new Color(0.92f, 0.92f, 0.92f));

                GameObject crossV = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crossV.transform.parent = parent.transform;
                crossV.transform.localPosition = new Vector3(0f, 0f, 0.095f);
                crossV.transform.localScale = new Vector3(0.04f, 0.12f, 0.02f);
                StripColliders(crossV);
                ApplyMaterial(crossV, new Color(0.95f, 0.15f, 0.15f), true);

                GameObject crossH = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crossH.transform.parent = parent.transform;
                crossH.transform.localPosition = new Vector3(0f, 0f, 0.095f);
                crossH.transform.localScale = new Vector3(0.12f, 0.04f, 0.02f);
                StripColliders(crossH);
                ApplyMaterial(crossH, new Color(0.95f, 0.15f, 0.15f), true);
                break;

            case "flashlight":
                // Torch cylinder with glowing reflector
                GameObject torch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                torch.transform.parent = parent.transform;
                torch.transform.localPosition = Vector3.zero;
                torch.transform.localScale = new Vector3(0.08f, 0.20f, 0.08f);
                torch.transform.localRotation = Quaternion.Euler(75f, 0f, 0f);
                StripColliders(torch);
                ApplyMaterial(torch, new Color(0.85f, 0.70f, 0.15f));
                break;

            case "nailgun":
            case "shotgun":
                // Heavy firearm frame
                GameObject gunBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gunBody.transform.parent = parent.transform;
                gunBody.transform.localPosition = Vector3.zero;
                gunBody.transform.localScale = new Vector3(0.10f, 0.16f, 0.45f);
                StripColliders(gunBody);
                ApplyMaterial(gunBody, color);

                GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                barrel.transform.parent = parent.transform;
                barrel.transform.localPosition = new Vector3(0f, 0.04f, 0.24f);
                barrel.transform.localScale = new Vector3(0.06f, 0.15f, 0.06f);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                StripColliders(barrel);
                ApplyMaterial(barrel, new Color(0.2f, 0.2f, 0.22f));
                break;

            default:
                // Default stylized pickup cube
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.parent = parent.transform;
                cube.transform.localPosition = Vector3.zero;
                cube.transform.localScale = new Vector3(0.32f, 0.32f, 0.32f);
                StripColliders(cube);
                ApplyMaterial(cube, color);
                break;
        }
    }

    private void StripColliders(GameObject go)
    {
        Collider[] childCols = go.GetComponentsInChildren<Collider>();
        for (int i = 0; i < childCols.Length; i++)
        {
            Destroy(childCols[i]);
        }
    }

    private void ApplyMaterial(GameObject go, Color color, bool emissive = false)
    {
        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = MaterialHelper.CreateLitMaterial(rend, color);
            if (emissive && mat != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 0.7f);
            }
            rend.material = mat;
        }
    }

    private void AddLight(GameObject parent, Color color, float range, float intensity)
    {
        GameObject lightObj = new GameObject("PickupGlow");
        lightObj.transform.parent = parent.transform;
        lightObj.transform.localPosition = Vector3.zero;
        Light pLight = lightObj.AddComponent<Light>();
        pLight.type = LightType.Point;
        pLight.color = color;
        pLight.range = range;
        pLight.intensity = intensity;
    }

    protected override void Update()
    {
        base.Update();

        if (IsCollected.Value)
        {
            return;
        }

        // On server: After launch velocity settles or time elapses, lock rigidbody
        if (IsServer && !hasSettled && rb != null)
        {
            settleTimer -= Time.deltaTime;
            if (settleTimer <= 0f || rb.linearVelocity.sqrMagnitude < 0.02f)
            {
                hasSettled = true;
                rb.isKinematic = true;
            }
        }

        // Gentle bob and spin animation for the visual mesh
        if (visualMesh != null)
        {
            float bob = Mathf.Sin(Time.time * 2.5f + spinPhase) * 0.05f;
            visualMesh.transform.localPosition = new Vector3(0f, 0.15f + bob, 0f);
            visualMesh.transform.Rotate(Vector3.up * (65f * Time.deltaTime));
        }
    }

    public override string GetPromptText(GameObject localPlayer)
    {
        string idStr = ItemId.Value.ToString();
        ItemData data = ItemRegistry.GetItem(idStr);
        string itemName = data != null ? data.DisplayName.ToUpper() : idStr.ToUpper();

        if (!CanPickup(localPlayer))
        {
            return $"[INVENTORY FULL] - {itemName}";
        }

        int count = Count.Value;
        if (data != null && data.Type == ItemType.Scrap)
        {
            int totalVal = data.BuyPrice * count;
            return count > 1
                ? $"[E] SALVAGE {itemName} x{count}  (+${totalVal})"
                : $"[E] SALVAGE {itemName}  (+${totalVal})";
        }

        return count > 1 ? $"[E] PICK UP {itemName} (x{count})" : $"[E] PICK UP {itemName}";
    }

    public override string GetFloatingTagText()
    {
        string idStr = ItemId.Value.ToString();
        ItemData data = ItemRegistry.GetItem(idStr);
        string itemName = data != null ? data.DisplayName : idStr;
        int count = Count.Value;

        if (data != null && data.Type == ItemType.Scrap)
        {
            int totalVal = data.BuyPrice * count;
            return count > 1 ? $"{itemName} x{count} [${totalVal}]" : $"{itemName} [${totalVal}]";
        }

        return count > 1 ? $"{itemName} x{count}" : itemName;
    }

    public override Color GetAccentColor()
    {
        string idStr = ItemId.Value.ToString();
        ItemData data = ItemRegistry.GetItem(idStr);
        return data != null ? data.SlotColor : new Color(0.2f, 0.85f, 1f);
    }

    public override bool CanPickup(GameObject localPlayer)
    {
        if (localPlayer == null)
        {
            return false;
        }

        PlayerInventory inventory = localPlayer.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            return false;
        }

        return inventory.CanAddItem(ItemId.Value.ToString(), Count.Value);
    }

    protected override void OnPickupServer(ulong collectorClientId, GameObject playerObject)
    {
        PlayerInventory inventory = playerObject.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            return;
        }

        bool added = inventory.AddItem(ItemId.Value.ToString(), Count.Value);
        if (added)
        {
            CompletePickup();
        }
    }

    private static GameObject itemPickupPrefabCache;

    public static GameObject GetItemPickupPrefab()
    {
        if (itemPickupPrefabCache != null)
        {
            return itemPickupPrefabCache;
        }

#if UNITY_EDITOR
        itemPickupPrefabCache = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Items/ItemPickupPrefab.prefab");
#endif

        if (itemPickupPrefabCache == null && NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
        {
            foreach (var netPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (netPrefab.Prefab != null && netPrefab.Prefab.GetComponent<WorldItemPickup>() != null)
                {
                    itemPickupPrefabCache = netPrefab.Prefab;
                    break;
                }
            }
        }

        return itemPickupPrefabCache;
    }

    /// <summary>
    /// Spawns a networked pickup item into the world with physics launch velocity.
    /// Used when dropping items, breaking crates, or monster loot drops.
    /// </summary>
    public static GameObject SpawnPickup(string itemId, int count, Vector3 position, Vector3 velocity)
    {
        GameObject prefab = GetItemPickupPrefab();
        GameObject obj = prefab != null
            ? Object.Instantiate(prefab, position, Quaternion.identity)
            : new GameObject($"Pickup_{itemId}");

        if (prefab == null)
        {
            obj.transform.position = position;
        }

        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            netObj = obj.AddComponent<NetworkObject>();
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = obj.AddComponent<Rigidbody>();
        }
        rb.mass = 1.2f;
        rb.linearDamping = 1f;
        rb.angularDamping = 1.5f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        SphereCollider[] cols = obj.GetComponents<SphereCollider>();
        SphereCollider solidCol = null;
        SphereCollider triggerCol = null;

        if (cols.Length > 0)
        {
            solidCol = cols[0];
            solidCol.isTrigger = false;
            solidCol.radius = 0.22f;
        }
        else
        {
            solidCol = obj.AddComponent<SphereCollider>();
            solidCol.isTrigger = false;
            solidCol.radius = 0.22f;
        }

        if (cols.Length > 1)
        {
            triggerCol = cols[1];
        }
        else
        {
            triggerCol = obj.AddComponent<SphereCollider>();
        }
        triggerCol.isTrigger = true;
        triggerCol.radius = 0.85f;

        WorldItemPickup pickup = obj.GetComponent<WorldItemPickup>();
        if (pickup == null)
        {
            pickup = obj.AddComponent<WorldItemPickup>();
        }

        netObj.Spawn();

        pickup.ItemId.Value = new FixedString32Bytes(itemId);
        pickup.Count.Value = count;

        if (velocity.sqrMagnitude > 0.01f)
        {
            rb.linearVelocity = velocity;
        }

        return obj;
    }
}
