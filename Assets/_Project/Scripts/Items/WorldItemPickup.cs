using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked world item pickup.
/// Spawns when items are dropped or found in the dungeon.
/// Automatically renders as a rotating colored 3D marker based on ItemData.SlotColor.
/// Collectible by walking over or pressing [E].
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(SphereCollider))]
public class WorldItemPickup : NetworkBehaviour
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
        if (col != null)
        {
            col.isTrigger = true;
            col.radius = 1.0f;
        }

        spinPhase = Random.Range(0f, Mathf.PI * 2f);
    }

    public override void OnNetworkSpawn()
    {
        ItemId.OnValueChanged += OnItemIdChanged;
        SetupVisuals();
    }

    public override void OnNetworkDespawn()
    {
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

        ItemData data = ItemRegistry.GetItem(ItemId.Value.ToString());
        Color displayColor = data != null ? data.SlotColor : new Color(0.8f, 0.8f, 0.8f);

        // Create rudimentary rotating cube mesh for the greybox item pickup
        visualMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visualMesh.name = "PickupVisual";
        visualMesh.transform.parent = transform;
        visualMesh.transform.localPosition = Vector3.zero;
        visualMesh.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);

        // Remove the default collider from the visual child
        Collider childCol = visualMesh.GetComponent<Collider>();
        if (childCol != null)
        {
            Destroy(childCol);
        }

        Renderer rend = visualMesh.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = displayColor;
            rend.material = mat;
        }
    }

    private void Update()
    {
        // Gentle bob and spin animation
        if (visualMesh != null)
        {
            float bob = Mathf.Sin(Time.time * 2.5f + spinPhase) * 0.1f;
            visualMesh.transform.localPosition = new Vector3(0f, 0.25f + bob, 0f);
            visualMesh.transform.Rotate(Vector3.up * (90f * Time.deltaTime));
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer)
        {
            return;
        }

        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory != null)
        {
            bool added = inventory.AddItem(ItemId.Value.ToString(), Count.Value);
            if (added)
            {
                NetworkObject netObj = GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn();
                }
            }
        }
    }

    /// <summary>
    /// Spawns a networked pickup item into the world with physics launch velocity.
    /// </summary>
    public static GameObject SpawnPickup(string itemId, int count, Vector3 position, Vector3 velocity)
    {
        GameObject obj = new GameObject($"Pickup_{itemId}");
        obj.transform.position = position;

        NetworkObject netObj = obj.AddComponent<NetworkObject>();
        Rigidbody rb = obj.AddComponent<Rigidbody>();
        rb.mass = 2f;
        rb.linearDamping = 1f;

        SphereCollider sc = obj.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 1.0f;

        WorldItemPickup pickup = obj.AddComponent<WorldItemPickup>();

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
