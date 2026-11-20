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

    private SphereCollider col;
    private GameObject visualMesh;
    private float spinPhase;

    private void Awake()
    {
        col = GetComponent<SphereCollider>();
        if (col == null)
        {
            col = gameObject.AddComponent<SphereCollider>();
        }
        col.isTrigger = true;
        col.radius = 0.75f;

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
        }
        else
        {
            // Rudimentary stylized cube mesh for the item pickup
            visualMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualMesh.name = "PickupVisual";
            visualMesh.transform.parent = transform;
            visualMesh.transform.localPosition = Vector3.zero;
            visualMesh.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

            Collider childCol = visualMesh.GetComponent<Collider>();
            if (childCol != null)
            {
                Destroy(childCol);
            }

            Renderer rend = visualMesh.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = MaterialHelper.CreateLitMaterial(rend, displayColor);
            }
        }
    }

    protected override void Update()
    {
        base.Update();

        if (IsCollected.Value)
        {
            return;
        }

        // Gentle bob and spin animation
        if (visualMesh != null)
        {
            float bob = Mathf.Sin(Time.time * 2.5f + spinPhase) * 0.08f;
            visualMesh.transform.localPosition = new Vector3(0f, 0.2f + bob, 0f);
            visualMesh.transform.Rotate(Vector3.up * (90f * Time.deltaTime));
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
        return count > 1 ? $"[E] PICK UP {itemName} (x{count})" : $"[E] PICK UP {itemName}";
    }

    public override string GetFloatingTagText()
    {
        string idStr = ItemId.Value.ToString();
        ItemData data = ItemRegistry.GetItem(idStr);
        string itemName = data != null ? data.DisplayName : idStr;
        int count = Count.Value;
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
        rb.mass = 1.5f;
        rb.linearDamping = 1f;

        SphereCollider sc = obj.GetComponent<SphereCollider>();
        if (sc == null)
        {
            sc = obj.AddComponent<SphereCollider>();
        }
        sc.isTrigger = true;
        sc.radius = 0.75f;

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
