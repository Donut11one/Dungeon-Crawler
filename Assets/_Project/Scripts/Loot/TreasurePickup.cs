using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A collectable treasure in the world. Inherits the project's WorldPickup, so aiming, the [E] prompt,
/// line-of-sight checks and server-authoritative collection behave exactly like every other pickup.
/// One generic networked prefab serves every treasure: the TreasureData id is synced and each peer builds
/// the matching look locally, so adding a treasure never requires a new network prefab.
/// Collecting adds the treasure's value to the collector's PlayerWallet (run money).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(SphereCollider))]
public class TreasurePickup : WorldPickup
{
    public const string DefaultPrefabPath = "Assets/_Project/Prefabs/Treasures/TreasurePickupPrefab.prefab";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Prefab Wiring (created automatically when left empty)")]
    [Tooltip("Parent of the spawned model; this is what bobs, spins and scales.")]
    [SerializeField] private Transform visualAnchor;
    [Tooltip("Glowing base that is tinted with the treasure's accent colour.")]
    [SerializeField] private Renderer pedestalRenderer;

    [Header("Behaviour")]
    [Min(0.01f)]
    [SerializeField] private float collectPopDuration = 0.2f;
    [SerializeField] private float focusScaleBoost = 0.2f;

    public NetworkVariable<FixedString64Bytes> TreasureId = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>Server only: raised when this pickup leaves the world for any reason (collected, cleared, shutdown).</summary>
    public event System.Action<TreasurePickup> ServerDespawned;

    /// <summary>Resolved treasure definition (null if the id is unknown on this peer).</summary>
    public TreasureData Data { get; private set; }

    private GameObject visualInstance;
    private Material fallbackMaterial;
    private string builtForId;
    private bool warnedUnknown;
    private bool serverClaimed;
    private float popStartTime = -1f;
    private float phase;
    private float spinAngle;
    private float focusBlend;
    private Vector3 pedestalBaseScale = Vector3.one;
    private MaterialPropertyBlock block;
    private Color accent = new Color(0.2f, 0.85f, 1f);

    private void Awake()
    {
        SphereCollider col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.7f;
        col.center = new Vector3(0f, 0.35f, 0f);

        phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        spinAngle = UnityEngine.Random.Range(0f, 360f);

        if (pedestalRenderer != null)
        {
            pedestalBaseScale = pedestalRenderer.transform.localScale;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        TreasureId.OnValueChanged += OnTreasureIdChanged;
        RefreshVisual();
    }

    public override void OnNetworkDespawn()
    {
        TreasureId.OnValueChanged -= OnTreasureIdChanged;
        bool wasServer = IsServer;
        base.OnNetworkDespawn();

        if (wasServer && ServerDespawned != null)
        {
            ServerDespawned.Invoke(this);
        }
        ServerDespawned = null;
    }

    public override void OnDestroy()
    {
        ReleaseFallbackMaterial();
        base.OnDestroy();
    }

    private void OnTreasureIdChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        RefreshVisual();
    }

    // ── Visuals ──────────────────────────────────────────────────────────────
    private TreasureData ResolveData()
    {
        string id = TreasureId.Value.ToString();
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        TreasureRegistry.TryGet(id, out TreasureData data);
        return data;
    }

    private void EnsureAnchor()
    {
        if (visualAnchor != null)
        {
            return;
        }

        GameObject anchor = new GameObject("VisualAnchor");
        anchor.transform.SetParent(transform, false);
        visualAnchor = anchor.transform;
    }

    private void RefreshVisual()
    {
        string id = TreasureId.Value.ToString();
        if (string.IsNullOrEmpty(id))
        {
            return;
        }
        if (visualInstance != null && id == builtForId)
        {
            return;
        }

        ClearVisual();
        Data = ResolveData();
        EnsureAnchor();

        if (Data != null && Data.VisualPrefab != null)
        {
            visualInstance = Instantiate(Data.VisualPrefab, visualAnchor);
        }
        else
        {
            if (Data == null && !warnedUnknown)
            {
                warnedUnknown = true;
                Debug.LogWarning($"[TreasurePickup] Unknown treasure id '{id}'. Reference its TreasureData from a LootSpawner loot table, or put it in a Resources/Treasures folder.", this);
            }
            visualInstance = BuildFallbackVisual();
        }

        Transform t = visualInstance.transform;
        float scale = Data != null ? Data.VisualScale : 1f;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = t.localScale * scale;

        Collider[] colliders = visualInstance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        accent = Data != null ? Data.AccentColor : new Color(1f, 0.2f, 0.8f);
        builtForId = id;
    }

    private GameObject BuildFallbackVisual()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "FallbackVisual";
        go.transform.SetParent(visualAnchor, false);
        go.transform.localScale = Vector3.one * 0.25f;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            ReleaseFallbackMaterial();
            fallbackMaterial = MaterialHelper.CreateLitMaterial(rend, new Color(1f, 0.2f, 0.8f));
            rend.sharedMaterial = fallbackMaterial;
        }
        return go;
    }

    private void ClearVisual()
    {
        if (visualInstance != null)
        {
            Destroy(visualInstance);
            visualInstance = null;
        }
        ReleaseFallbackMaterial();
        builtForId = null;
    }

    private void ReleaseFallbackMaterial()
    {
        if (fallbackMaterial != null)
        {
            Destroy(fallbackMaterial);
            fallbackMaterial = null;
        }
    }

    protected override void Update()
    {
        base.Update();

        if (IsCollected.Value && popStartTime < 0f)
        {
            return;
        }

        if (builtForId == null)
        {
            RefreshVisual();
        }

        AnimateVisual();
    }

    private void AnimateVisual()
    {
        if (visualAnchor == null)
        {
            return;
        }

        float hover = Data != null ? Data.HoverHeight : 0.12f;
        float bobHeight = Data != null ? Data.BobHeight : 0.05f;
        float bobSpeed = Data != null ? Data.BobSpeed : 2f;
        float spin = Data != null ? Data.SpinSpeed : 60f;

        bool focused = FocusedPickup == this && !IsCollected.Value && popStartTime < 0f;
        focusBlend = Mathf.MoveTowards(focusBlend, focused ? 1f : 0f, Time.deltaTime * 8f);

        float t = Time.time + phase;
        spinAngle += spin * Time.deltaTime * (1f + focusBlend * 1.5f);
        visualAnchor.localPosition = new Vector3(0f, hover + Mathf.Sin(t * bobSpeed) * bobHeight + focusBlend * 0.05f, 0f);
        visualAnchor.localRotation = Quaternion.Euler(0f, spinAngle, 0f);

        float popFactor = 1f;
        if (popStartTime >= 0f)
        {
            float p = Mathf.Clamp01((Time.time - popStartTime) / collectPopDuration);
            popFactor = p < 0.3f ? Mathf.Lerp(1f, 1.35f, p / 0.3f) : Mathf.Lerp(1.35f, 0f, (p - 0.3f) / 0.7f);
        }

        visualAnchor.localScale = Vector3.one * ((1f + focusScaleBoost * focusBlend) * popFactor);
        UpdatePedestal(t, popFactor);
    }

    private void UpdatePedestal(float t, float popFactor)
    {
        if (pedestalRenderer == null)
        {
            return;
        }

        if (block == null)
        {
            block = new MaterialPropertyBlock();
        }

        float pulse = 0.55f + 0.25f * Mathf.Sin(t * 3f) + 1.1f * focusBlend;
        Color baseColor = accent * 0.35f;
        baseColor.a = 1f;

        pedestalRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, baseColor);
        block.SetColor(EmissionColorId, accent * (pulse * 1.5f));
        pedestalRenderer.SetPropertyBlock(block);

        pedestalRenderer.transform.localScale = pedestalBaseScale * ((1f + 0.25f * focusBlend) * popFactor);
    }

    // ── WorldPickup contract (prompt, accent colour, permissions) ────────────
    public override string GetPromptText(GameObject localPlayer)
    {
        TreasureData data = Data != null ? Data : ResolveData();
        string treasureName = data != null ? data.DisplayName.ToUpperInvariant() : "UNKNOWN TREASURE";

        if (!CanPickup(localPlayer))
        {
            return $"[UNAVAILABLE] - {treasureName}";
        }

        if (data == null)
        {
            return $"[E] COLLECT {treasureName}";
        }
        return $"[E] COLLECT {treasureName}  -  ${data.Value}  ({data.Rarity.ToString().ToUpperInvariant()})";
    }

    public override string GetFloatingTagText()
    {
        TreasureData data = Data != null ? Data : ResolveData();
        if (data == null)
        {
            return "Treasure";
        }
        return $"{data.DisplayName}  (${data.Value})";
    }

    public override Color GetAccentColor()
    {
        return Data != null ? Data.AccentColor : accent;
    }

    public override bool CanPickup(GameObject localPlayer)
    {
        if (localPlayer == null || popStartTime >= 0f || IsCollected.Value)
        {
            return false;
        }
        return localPlayer.GetComponent<PlayerWallet>() != null;
    }

    protected override void OnGUI()
    {
        // Hide the prompt while the collect animation plays.
        if (popStartTime >= 0f || serverClaimed)
        {
            return;
        }
        base.OnGUI();
    }

    // ── Collection (server authoritative) ────────────────────────────────────
    protected override void OnPickupServer(ulong collectorClientId, GameObject playerObject)
    {
        if (serverClaimed)
        {
            return;
        }

        PlayerWallet wallet = playerObject != null ? playerObject.GetComponent<PlayerWallet>() : null;
        if (wallet == null)
        {
            Debug.LogError("[TreasurePickup] Collector has no PlayerWallet; treasure was not consumed.", this);
            return;
        }

        TreasureData data = ResolveData();
        if (data == null)
        {
            Debug.LogWarning($"[TreasurePickup] Unknown treasure id '{TreasureId.Value}' on the server; collected for $0.", this);
        }

        int value = data != null ? data.Value : 0;
        serverClaimed = true;

        wallet.AddMoney(value);
        CollectedClientRpc(collectorClientId, value);

        // Let the pop animation play before the object disappears. serverClaimed blocks double collection meanwhile.
        Invoke(nameof(FinishCollection), collectPopDuration);
    }

    private void FinishCollection()
    {
        if (IsSpawned && IsServer)
        {
            CompletePickup();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void CollectedClientRpc(ulong collectorClientId, int value)
    {
        popStartTime = Time.time;

        TreasureData data = Data != null ? Data : ResolveData();
        TreasureFeedback.PlayCollectSound(data, transform.position);

        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.LocalClientId == collectorClientId)
        {
            TreasureFeedback.ShowCollected(data, value);
        }
    }

    // ── Spawning helper (server only) ────────────────────────────────────────
    /// <summary>
    /// Spawns a treasure on the server. Used by LootSpawner, but also handy for monster drops, chests, etc.
    /// </summary>
    public static TreasurePickup SpawnServer(GameObject prefab, TreasureData treasure, Vector3 position, Quaternion rotation)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
        {
            Debug.LogWarning("[TreasurePickup] SpawnServer can only be called on the server.");
            return null;
        }
        if (prefab == null)
        {
            Debug.LogError("[TreasurePickup] No treasure pickup prefab was provided.");
            return null;
        }
        if (treasure == null)
        {
            Debug.LogError("[TreasurePickup] Cannot spawn a null TreasureData.");
            return null;
        }
        if (string.IsNullOrEmpty(treasure.TreasureId))
        {
            Debug.LogError($"[TreasurePickup] Treasure '{treasure.name}' has no id.", treasure);
            return null;
        }

        if (prefab.GetComponent<TreasurePickup>() == null || prefab.GetComponent<NetworkObject>() == null)
        {
            Debug.LogError($"[TreasurePickup] Prefab '{prefab.name}' needs both a NetworkObject and a TreasurePickup component.", prefab);
            return null;
        }

        TreasureRegistry.Register(treasure);

        GameObject instance = Instantiate(prefab, position, rotation);
        TreasurePickup pickup = instance.GetComponent<TreasurePickup>();
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();

        networkObject.Spawn(true);
        pickup.TreasureId.Value = new FixedString64Bytes(treasure.TreasureId);
        return pickup;
    }
}
