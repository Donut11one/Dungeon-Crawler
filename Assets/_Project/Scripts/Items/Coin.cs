using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked coin collectible. Players walk into the trigger to collect it.
/// Awards money server-authoritatively and despawns on collection.
/// Design pillars: Clippable (players can knock each other into coins),
/// Emergent (money feeds extraction pressure), Ragdoll-agnostic.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class Coin : NetworkBehaviour
{
    [Header("Value")]
    [SerializeField] private int coinValue = 10;

    [Header("Visual")]
    [SerializeField] private float bobHeight = 0.15f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float spinSpeed = 90f;

    public NetworkVariable<bool> IsCollected = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Vector3 startPosition;
    private float bobOffset;

    private void Awake()
    {
        SphereCollider col = GetComponent<SphereCollider>();
        if (col == null)
        {
            col = gameObject.AddComponent<SphereCollider>();
        }
        col.isTrigger = true;
        col.radius = 0.75f;

        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(1f, 0.82f, 0.1f);
            rend.material = mat;
        }
    }

    public override void OnNetworkSpawn()
    {
        startPosition = transform.position;
        // Stagger the bob phase so coins don't all move in sync
        bobOffset = Random.Range(0f, Mathf.PI * 2f);

        IsCollected.OnValueChanged += OnCollectedChanged;
    }

    public override void OnNetworkDespawn()
    {
        IsCollected.OnValueChanged -= OnCollectedChanged;
    }

    private void Update()
    {
        if (IsCollected.Value)
        {
            return;
        }

        // Visual bob and spin (local only — purely cosmetic)
        float y = startPosition.y + Mathf.Sin(Time.time * bobSpeed + bobOffset) * bobHeight;
        transform.position = new Vector3(startPosition.x, y, startPosition.z);
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer)
        {
            return;
        }

        if (IsCollected.Value)
        {
            return;
        }

        // Only players can collect coins
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
        {
            return;
        }

        if (playerHealth.IsDead.Value)
        {
            return;
        }

        // Find the PlayerWallet on the same object
        PlayerWallet wallet = other.GetComponentInParent<PlayerWallet>();
        if (wallet == null)
        {
            return;
        }

        IsCollected.Value = true;
        wallet.AddMoney(coinValue);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnCollectedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // Hide immediately on all clients
            gameObject.SetActive(false);
        }
    }

    public int GetValue()
    {
        return coinValue;
    }
}
