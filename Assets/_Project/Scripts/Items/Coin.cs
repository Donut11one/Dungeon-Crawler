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

    [Header("Visual Animation")]
    [SerializeField] private float bobHeight = 0.15f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float spinSpeed = 90f;

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
        return $"[E] PICK UP COIN (+${coinValue})";
    }

    public override string GetFloatingTagText()
    {
        return $"+${coinValue}";
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
            wallet.AddMoney(coinValue);
        }

        CompletePickup();
    }

    public int GetValue()
    {
        return coinValue;
    }
}
