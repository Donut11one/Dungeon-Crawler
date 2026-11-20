using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked coin collectible.
/// Inherits from WorldPickup: players aim directly at the coin and press [E] to collect it.
/// Adds currency directly into the player's run wallet / bank and despawns.
/// </summary>
public class Coin : WorldPickup
{
    [Header("Coin Value")]
    [SerializeField] private int coinValue = 10;

    public NetworkVariable<int> NetworkedCoinValue = new NetworkVariable<int>(
        10,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Visual Animation")]
    [SerializeField] private float bobHeight = 0.15f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float spinSpeed = 90f;

    private static GameObject coinPrefabCache;

    /// <summary>Backwards compatibility accessor for focused coin.</summary>
    public static Coin FocusedCoin
    {
        get
        {
            return FocusedPickup as Coin;
        }
    }

    private Vector3 startPosition;
    private float bobOffset;

    private void Awake()
    {
        startPosition = transform.position;
        bobOffset = Random.Range(0f, Mathf.PI * 2f);

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
            rend.material = MaterialHelper.CreateLitMaterial(rend, new Color(1f, 0.82f, 0.1f));
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        startPosition = transform.position;
        bobOffset = Random.Range(0f, Mathf.PI * 2f);

        if (IsServer && NetworkedCoinValue.Value == 0)
        {
            NetworkedCoinValue.Value = coinValue > 0 ? coinValue : 10;
        }
    }

    protected override void Update()
    {
        base.Update();

        if (IsCollected.Value)
        {
            return;
        }

        // Visual bob and spin
        float y = startPosition.y + Mathf.Sin(Time.time * bobSpeed + bobOffset) * bobHeight;
        transform.position = new Vector3(startPosition.x, y, startPosition.z);
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    public override string GetPromptText(GameObject localPlayer)
    {
        return $"[E] PICK UP COIN (+${GetValue()})";
    }

    public override string GetFloatingTagText()
    {
        return $"+${GetValue()}";
    }

    public override Color GetAccentColor()
    {
        return new Color(1f, 0.85f, 0.2f);
    }

    public override bool CanPickup(GameObject localPlayer)
    {
        return true;
    }

    protected override void OnPickupServer(ulong collectorClientId, GameObject playerObject)
    {
        PlayerWallet wallet = playerObject.GetComponent<PlayerWallet>();
        if (wallet != null)
        {
            wallet.AddMoney(GetValue());
        }

        CompletePickup();
    }

    public int GetValue()
    {
        return NetworkedCoinValue.Value > 0 ? NetworkedCoinValue.Value : coinValue;
    }

    /// <summary>
    /// Resolves the CoinPrefab from AssetDatabase or NetworkManager's NetworkConfig.
    /// </summary>
    public static GameObject GetCoinPrefab()
    {
        if (coinPrefabCache != null)
        {
            return coinPrefabCache;
        }

#if UNITY_EDITOR
        coinPrefabCache = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Items/CoinPrefab.prefab");
#endif

        if (coinPrefabCache == null && NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
        {
            foreach (var netPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (netPrefab.Prefab != null && netPrefab.Prefab.GetComponent<Coin>() != null)
                {
                    coinPrefabCache = netPrefab.Prefab;
                    break;
                }
            }
        }

        return coinPrefabCache;
    }

    /// <summary>
    /// Spawns dropped coins scattered on the ground around the specified origin position.
    /// Used when players die or when containers burst.
    /// </summary>
    public static void SpawnCoinDrops(Vector3 origin, int totalAmount)
    {
        if (totalAmount <= 0)
        {
            return;
        }

        GameObject prefab = GetCoinPrefab();
        if (prefab == null)
        {
            Debug.LogError("[Coin] Cannot drop coins - CoinPrefab could not be resolved!");
            return;
        }

        // Split totalAmount into 1 to 6 coins depending on size
        int coinCount = Mathf.Clamp(Mathf.CeilToInt(totalAmount / 20f), 1, 6);
        int baseValue = totalAmount / coinCount;
        int remainder = totalAmount % coinCount;

        for (int i = 0; i < coinCount; i++)
        {
            int thisCoinValue = baseValue + (i < remainder ? 1 : 0);
            if (thisCoinValue <= 0)
            {
                continue;
            }

            // Scatter in a ring around the origin
            float angle = (360f / coinCount) * i + Random.Range(-15f, 15f);
            float dist = Random.Range(0.8f, 1.6f);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dist;
            Vector3 spawnPos = origin + offset;

            // Ground raycast so coins don't spawn floating or inside walls
            if (Physics.Raycast(spawnPos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                spawnPos.y = hit.point.y + 0.45f;
            }
            else
            {
                spawnPos.y = origin.y + 0.3f;
            }

            GameObject coinObj = Object.Instantiate(prefab, spawnPos, Quaternion.identity);
            NetworkObject netObj = coinObj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }

            Coin coinComp = coinObj.GetComponent<Coin>();
            if (coinComp != null)
            {
                coinComp.NetworkedCoinValue.Value = thisCoinValue;
            }
        }
    }
}
