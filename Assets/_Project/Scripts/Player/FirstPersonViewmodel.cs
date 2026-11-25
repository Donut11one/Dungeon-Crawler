using Unity.Netcode;
using UnityEngine;

/// <summary>
/// First-person viewmodel controller.
/// Renders stylized 3D weapon models in the player's camera view when items are equipped.
/// Features smooth movement bobbing, instant weapon recoil kick, muzzle flashes, and reload animations.
/// </summary>
public class FirstPersonViewmodel : NetworkBehaviour
{
    [Header("Viewmodel Positioning")]
    [SerializeField] private Vector3 defaultOffset = new Vector3(0.28f, -0.24f, 0.48f);
    [SerializeField] private float bobFrequency = 10.5f;
    [SerializeField] private float bobHorizontalAmplitude = 0.014f;
    [SerializeField] private float bobVerticalAmplitude = 0.011f;

    private Transform viewmodelRoot;
    private GameObject currentModel;
    private string currentItemId = string.Empty;
    private PlayerInventory inventory;
    private Camera playerCam;
    private Rigidbody rb;

    private Vector3 recoilPosOffset;
    private Quaternion recoilRotOffset = Quaternion.identity;
    private Light muzzleFlashLight;
    private float muzzleFlashTimer;
    private float reloadTimer;
    private float reloadDuration;
    private float bobTimer;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        SetupViewmodelRoot();
    }

    private void SetupViewmodelRoot()
    {
        playerCam = GetComponentInChildren<Camera>();
        if (playerCam == null)
        {
            return;
        }

        // Create or find dedicated viewmodel holder under the camera
        Transform existing = playerCam.transform.Find("ViewmodelRoot");
        if (existing != null)
        {
            viewmodelRoot = existing;
        }
        else
        {
            GameObject rootGo = new GameObject("ViewmodelRoot");
            rootGo.transform.parent = playerCam.transform;
            rootGo.transform.localPosition = defaultOffset;
            rootGo.transform.localRotation = Quaternion.identity;
            viewmodelRoot = rootGo.transform;
        }

        // Setup muzzle flash light
        GameObject flashGo = new GameObject("MuzzleFlashLight");
        flashGo.transform.parent = viewmodelRoot;
        flashGo.transform.localPosition = new Vector3(0f, 0.05f, 0.5f);
        muzzleFlashLight = flashGo.AddComponent<Light>();
        muzzleFlashLight.type = LightType.Point;
        muzzleFlashLight.range = 8.5f;
        muzzleFlashLight.intensity = 3.2f;
        muzzleFlashLight.color = new Color(1f, 0.85f, 0.4f);
        muzzleFlashLight.enabled = false;
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (viewmodelRoot == null)
        {
            SetupViewmodelRoot();
            if (viewmodelRoot == null)
            {
                return;
            }
        }

        CheckEquippedItemChange();
        UpdateMuzzleFlash();
    }

    private void LateUpdate()
    {
        if (!IsOwner || viewmodelRoot == null)
        {
            return;
        }

        // 1. Calculate walk bobbing
        Vector3 targetBob = Vector3.zero;
        float speed = 0f;
        if (rb != null)
        {
            Vector3 horizVel = rb.linearVelocity;
            horizVel.y = 0f;
            speed = horizVel.magnitude;
        }

        if (speed > 0.5f)
        {
            bobTimer += Time.deltaTime * bobFrequency * Mathf.Clamp(speed / 4.5f, 0.5f, 1.6f);
            float bobX = Mathf.Cos(bobTimer * 0.5f) * bobHorizontalAmplitude;
            float bobY = Mathf.Sin(bobTimer) * bobVerticalAmplitude;
            targetBob = new Vector3(bobX, bobY, 0f);
        }
        else
        {
            bobTimer = 0f;
        }

        // 2. Interpolate recoil spring recovery
        recoilPosOffset = Vector3.Lerp(recoilPosOffset, Vector3.zero, Time.deltaTime * 16f);
        recoilRotOffset = Quaternion.Slerp(recoilRotOffset, Quaternion.identity, Time.deltaTime * 14f);

        // 3. Reload tilt interpolation
        Quaternion reloadRot = Quaternion.identity;
        Vector3 reloadPos = Vector3.zero;
        if (reloadTimer > 0f)
        {
            reloadTimer -= Time.deltaTime;
            float reloadPct = 1f - Mathf.Clamp01(reloadTimer / reloadDuration);
            float dip = Mathf.Sin(reloadPct * Mathf.PI);
            reloadRot = Quaternion.Euler(dip * 28f, dip * 15f, dip * -12f);
            reloadPos = new Vector3(0f, -dip * 0.14f, -dip * 0.08f);
        }

        // 4. Apply combined transform
        viewmodelRoot.localPosition = defaultOffset + targetBob + recoilPosOffset + reloadPos;
        viewmodelRoot.localRotation = recoilRotOffset * reloadRot;
    }

    private void CheckEquippedItemChange()
    {
        if (inventory == null)
        {
            return;
        }

        ItemData active = inventory.GetSelectedItem();
        string activeId = active != null ? active.ItemId : string.Empty;

        if (activeId != currentItemId)
        {
            currentItemId = activeId;
            BuildViewModel(active);
        }
    }

    private void BuildViewModel(ItemData item)
    {
        if (currentModel != null)
        {
            Destroy(currentModel);
            currentModel = null;
        }

        if (item == null)
        {
            return; // Fists (no weapon model)
        }

        currentModel = new GameObject($"Viewmodel_{item.ItemId}");
        currentModel.transform.parent = viewmodelRoot;
        currentModel.transform.localPosition = Vector3.zero;
        currentModel.transform.localRotation = Quaternion.identity;

        switch (item.ItemId)
        {
            case "nailgun":
                BuildNailgunModel(currentModel.transform);
                break;
            case "shotgun":
                BuildShotgunModel(currentModel.transform);
                break;
            case "crowbar":
                BuildCrowbarModel(currentModel.transform);
                break;
            case "stun_baton":
                BuildStunBatonModel(currentModel.transform);
                break;
            case "medkit":
                BuildMedkitModel(currentModel.transform);
                break;
            case "flashlight":
                BuildFlashlightModel(currentModel.transform);
                break;
            default:
                BuildGenericToolModel(currentModel.transform, item.SlotColor);
                break;
        }
    }

    // ── Procedural Weapon 3D Viewmodels ─────────────────────────────────────────

    private void BuildNailgunModel(Transform parent)
    {
        Color chassisOrange = new Color(0.95f, 0.48f, 0.12f); // Industrial tool safety orange
        Color gunmetal = new Color(0.18f, 0.20f, 0.24f);
        Color steel = new Color(0.75f, 0.78f, 0.82f);

        // Main tool body / housing
        CreatePart(parent, "Body", new Vector3(0f, 0f, 0.05f), new Vector3(0.09f, 0.12f, 0.24f), chassisOrange);

        // Forward nose / nail guide nozzle
        CreatePart(parent, "Nose", new Vector3(0f, -0.015f, 0.21f), new Vector3(0.045f, 0.045f, 0.12f), steel);

        // Handle / trigger grip
        CreatePart(parent, "Grip", new Vector3(0f, -0.11f, -0.04f), new Vector3(0.042f, 0.14f, 0.055f), gunmetal, Quaternion.Euler(18f, 0f, 0f));

        // Compressed air canister / bottom tank
        CreatePart(parent, "AirTank", new Vector3(0f, -0.17f, -0.02f), new Vector3(0.065f, 0.10f, 0.065f), steel);

        // Top angled nail magazine
        CreatePart(parent, "Magazine", new Vector3(0f, 0.075f, 0.08f), new Vector3(0.035f, 0.065f, 0.20f), gunmetal, Quaternion.Euler(-15f, 0f, 0f));
    }

    private void BuildShotgunModel(Transform parent)
    {
        Color darkReceiver = new Color(0.12f, 0.13f, 0.15f);
        Color steelBarrel = new Color(0.28f, 0.30f, 0.34f);
        Color woodGrip = new Color(0.42f, 0.24f, 0.14f); // Weathered wooden stock

        // Receiver box
        CreatePart(parent, "Receiver", new Vector3(0f, 0f, 0.04f), new Vector3(0.075f, 0.095f, 0.24f), darkReceiver);

        // Heavy steel barrel
        CreatePart(parent, "Barrel", new Vector3(0f, 0.02f, 0.34f), new Vector3(0.048f, 0.048f, 0.42f), steelBarrel);

        // Under-barrel tubular magazine
        CreatePart(parent, "MagTube", new Vector3(0f, -0.025f, 0.30f), new Vector3(0.038f, 0.038f, 0.36f), darkReceiver);

        // Ribbed pump slide grip
        CreatePart(parent, "PumpGrip", new Vector3(0f, -0.025f, 0.24f), new Vector3(0.058f, 0.058f, 0.14f), woodGrip);

        // Rear stock / pistol grip
        CreatePart(parent, "Stock", new Vector3(0f, -0.07f, -0.12f), new Vector3(0.052f, 0.13f, 0.16f), woodGrip, Quaternion.Euler(22f, 0f, 0f));
    }

    private void BuildCrowbarModel(Transform parent)
    {
        Color crowbarRed = new Color(0.85f, 0.25f, 0.20f);
        Color steelBevel = new Color(0.70f, 0.72f, 0.75f);

        // Main shaft
        CreatePart(parent, "Shaft", new Vector3(0f, 0f, 0.15f), new Vector3(0.035f, 0.035f, 0.44f), crowbarRed);

        // Curved hook end
        CreatePart(parent, "Hook", new Vector3(0f, 0.04f, 0.36f), new Vector3(0.035f, 0.09f, 0.045f), crowbarRed, Quaternion.Euler(-35f, 0f, 0f));

        // Chisel claws
        CreatePart(parent, "Claw", new Vector3(0f, 0.07f, 0.39f), new Vector3(0.045f, 0.015f, 0.05f), steelBevel);
    }

    private void BuildStunBatonModel(Transform parent)
    {
        Color batonBlack = new Color(0.15f, 0.16f, 0.18f);
        Color stunCyan = new Color(0.15f, 0.85f, 1.0f);

        // Rubber grip
        CreatePart(parent, "Grip", new Vector3(0f, -0.05f, 0.02f), new Vector3(0.038f, 0.038f, 0.16f), batonBlack);

        // Reinforced shaft
        CreatePart(parent, "Shaft", new Vector3(0f, 0f, 0.22f), new Vector3(0.034f, 0.034f, 0.28f), batonBlack);

        // Arc discharge head (glowing cyan)
        CreatePart(parent, "StunHead", new Vector3(0f, 0.005f, 0.38f), new Vector3(0.055f, 0.055f, 0.12f), stunCyan);
    }

    private void BuildMedkitModel(Transform parent)
    {
        Color caseWhite = new Color(0.92f, 0.94f, 0.96f);
        Color crossGreen = new Color(0.20f, 0.85f, 0.35f);

        // Case box
        CreatePart(parent, "Case", new Vector3(0f, -0.04f, 0.18f), new Vector3(0.22f, 0.15f, 0.10f), caseWhite, Quaternion.Euler(15f, -10f, 0f));

        // Green cross symbol
        CreatePart(parent, "CrossH", new Vector3(0f, -0.04f, 0.232f), new Vector3(0.09f, 0.025f, 0.01f), crossGreen, Quaternion.Euler(15f, -10f, 0f));
        CreatePart(parent, "CrossV", new Vector3(0f, -0.04f, 0.232f), new Vector3(0.025f, 0.09f, 0.01f), crossGreen, Quaternion.Euler(15f, -10f, 0f));
    }

    private void BuildFlashlightModel(Transform parent)
    {
        Color torchBlack = new Color(0.20f, 0.22f, 0.25f);
        Color bezelYellow = new Color(0.95f, 0.85f, 0.20f);

        // Handle body
        CreatePart(parent, "Handle", new Vector3(0f, -0.03f, 0.12f), new Vector3(0.042f, 0.042f, 0.22f), torchBlack);

        // Wide reflector head
        CreatePart(parent, "Bezel", new Vector3(0f, -0.025f, 0.24f), new Vector3(0.075f, 0.075f, 0.07f), bezelYellow);
    }

    private void BuildGenericToolModel(Transform parent, Color accent)
    {
        CreatePart(parent, "GenericBody", new Vector3(0f, 0f, 0.15f), new Vector3(0.08f, 0.08f, 0.28f), accent);
    }

    private GameObject CreatePart(Transform parent, string partName, Vector3 pos, Vector3 scale, Color color, Quaternion? rot = null)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.parent = parent;
        part.transform.localPosition = pos;
        part.transform.localScale = scale;
        part.transform.localRotation = rot ?? Quaternion.identity;

        Collider c = part.GetComponent<Collider>();
        if (c != null)
        {
            Destroy(c);
        }

        Renderer r = part.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = MaterialHelper.CreateLitMaterial(r, color);
        }

        return part;
    }

    // ── Public Animation & Feedback Hooks ──────────────────────────────────────

    public void TriggerRecoil(float kickDistance, float kickAngle)
    {
        recoilPosOffset = new Vector3(0f, kickDistance * 0.4f, -kickDistance);
        recoilRotOffset = Quaternion.Euler(-kickAngle, 0f, 0f);
    }

    public void TriggerMuzzleFlash(Color flashColor)
    {
        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.color = flashColor;
            muzzleFlashLight.enabled = true;
            muzzleFlashTimer = 0.045f;
        }
    }

    private void UpdateMuzzleFlash()
    {
        if (muzzleFlashTimer > 0f)
        {
            muzzleFlashTimer -= Time.deltaTime;
            if (muzzleFlashTimer <= 0f && muzzleFlashLight != null)
            {
                muzzleFlashLight.enabled = false;
            }
        }
    }

    public void TriggerReloadAnimation(float duration)
    {
        reloadDuration = duration;
        reloadTimer = duration;
    }
}
