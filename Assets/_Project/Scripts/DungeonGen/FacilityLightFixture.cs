using UnityEngine;

public enum FacilityLightType
{
    FluorescentCeiling,
    EmergencyWallBulkhead,
    WarningStrobeRed,
    WarmIncandescent
}

public enum LightFlickerMode
{
    Steady,
    OccasionalFlicker,
    RapidFlicker,
    SlowPulse
}

/// <summary>
/// Modular local light prop component for the subterranean facility.
/// Manages physical fixture geometry, emissive glow materials, local Point/Spot lights,
/// and atmospheric behaviors like fluorescent stutter or emergency pulsing.
/// </summary>
public class FacilityLightFixture : MonoBehaviour
{
    [Header("Fixture Setup")]
    [SerializeField] private FacilityLightType fixtureType = FacilityLightType.FluorescentCeiling;
    [SerializeField] private LightFlickerMode flickerMode = LightFlickerMode.Steady;
    [SerializeField] private float baseIntensity = 2.0f;
    [SerializeField] private float lightRange = 13.5f;
    [SerializeField] private Color lightColor = new Color(0.88f, 0.94f, 1.0f);

    [Header("References")]
    [SerializeField] private Light fixtureLight;
    [SerializeField] private Renderer bulbRenderer;

    public FacilityLightType FixtureType
    {
        get
        {
            return fixtureType;
        }
        set
        {
            fixtureType = value;
        }
    }

    public LightFlickerMode FlickerMode
    {
        get
        {
            return flickerMode;
        }
        set
        {
            flickerMode = value;
        }
    }

    public Light LightComponent
    {
        get
        {
            return fixtureLight;
        }
    }

    private Material bulbMaterialInstance;
    private float nextFlickerTime;
    private float flickerEndTime;
    private bool isFlickering;

    public void Initialize(FacilityLightType type, LightFlickerMode flicker, float intensity, float range, Color color)
    {
        fixtureType = type;
        flickerMode = flicker;
        baseIntensity = intensity;
        lightRange = range;
        lightColor = color;

        if (fixtureLight != null)
        {
            bool isCeilingSpot = fixtureType == FacilityLightType.FluorescentCeiling ||
                                 fixtureType == FacilityLightType.WarmIncandescent;

            fixtureLight.type = isCeilingSpot ? LightType.Spot : LightType.Point;
            fixtureLight.color = lightColor;
            fixtureLight.intensity = baseIntensity;
            fixtureLight.range = lightRange;

            if (isCeilingSpot)
            {
                fixtureLight.spotAngle = 140f;
                fixtureLight.innerSpotAngle = 55f;
                // Each Spot uses 1 shadow slice instead of 6 (Point), keeping URP shadow atlas fast and overflow-free
                fixtureLight.shadows = LightShadows.Hard;
            }
            else
            {
                fixtureLight.shadows = LightShadows.None;
            }
        }

        UpdateBulbEmission(1.0f);
    }

    private void Awake()
    {
        if (fixtureLight == null)
        {
            fixtureLight = GetComponentInChildren<Light>();
        }

        if (bulbRenderer != null)
        {
            bulbMaterialInstance = bulbRenderer.material;
        }

        nextFlickerTime = Time.time + Random.Range(3f, 8f);
    }

    private void Update()
    {
        if (fixtureLight == null)
        {
            return;
        }

        switch (flickerMode)
        {
            case LightFlickerMode.Steady:
                fixtureLight.intensity = baseIntensity;
                UpdateBulbEmission(1.0f);
                break;

            case LightFlickerMode.OccasionalFlicker:
                HandleOccasionalFlicker();
                break;

            case LightFlickerMode.RapidFlicker:
                HandleRapidFlicker();
                break;

            case LightFlickerMode.SlowPulse:
                HandleSlowPulse();
                break;
        }
    }

    private void HandleOccasionalFlicker()
    {
        if (!isFlickering)
        {
            if (Time.time >= nextFlickerTime)
            {
                isFlickering = true;
                flickerEndTime = Time.time + Random.Range(0.12f, 0.35f);
            }
            else
            {
                fixtureLight.intensity = baseIntensity;
                UpdateBulbEmission(1.0f);
            }
        }
        else
        {
            if (Time.time < flickerEndTime)
            {
                float dimFactor = Random.value > 0.4f ? Random.Range(0.05f, 0.35f) : 1.0f;
                fixtureLight.intensity = baseIntensity * dimFactor;
                UpdateBulbEmission(dimFactor);
            }
            else
            {
                isFlickering = false;
                nextFlickerTime = Time.time + Random.Range(4f, 10f);
                fixtureLight.intensity = baseIntensity;
                UpdateBulbEmission(1.0f);
            }
        }
    }

    private void HandleRapidFlicker()
    {
        float noise = Mathf.PerlinNoise(Time.time * 25f, 0f);
        float factor = noise > 0.3f ? noise : 0.05f;
        fixtureLight.intensity = baseIntensity * factor;
        UpdateBulbEmission(factor);
    }

    private void HandleSlowPulse()
    {
        float pulse = (Mathf.Sin(Time.time * 2.5f) + 1f) * 0.5f;
        float factor = Mathf.Lerp(0.2f, 1.2f, pulse);
        fixtureLight.intensity = baseIntensity * factor;
        UpdateBulbEmission(factor);
    }

    private void UpdateBulbEmission(float factor)
    {
        if (bulbMaterialInstance != null)
        {
            Color emissive = lightColor * (factor * 2.2f);
            bulbMaterialInstance.SetColor("_EmissionColor", emissive);
            bulbMaterialInstance.EnableKeyword("_EMISSION");
        }
    }

    /// <summary>
    /// Factory helper to build a full local light fixture with casing, emissive bulb, and Point Light.
    /// </summary>
    public static GameObject CreateFixture(
        Transform parent,
        string fixtureName,
        Vector3 localPos,
        Quaternion localRot,
        FacilityLightType type = FacilityLightType.FluorescentCeiling,
        LightFlickerMode flicker = LightFlickerMode.Steady
    )
    {
        GameObject root = new GameObject(fixtureName);
        if (parent != null)
        {
            root.transform.SetParent(parent, false);
        }
        root.transform.localPosition = localPos;
        root.transform.localRotation = localRot;
        root.isStatic = true;

        Color lightCol;
        float intensity;
        float range;
        Vector3 casingScale;
        Vector3 bulbScale;
        Vector3 lightOffset;

        switch (type)
        {
            case FacilityLightType.EmergencyWallBulkhead:
                lightCol = new Color(1.0f, 0.45f, 0.15f);
                intensity = 2.8f;
                range = 8.0f;
                casingScale = new Vector3(0.35f, 0.35f, 0.12f);
                bulbScale = new Vector3(0.22f, 0.22f, 0.18f);
                lightOffset = new Vector3(0f, 0f, 0.25f);
                break;

            case FacilityLightType.WarningStrobeRed:
                lightCol = new Color(1.0f, 0.20f, 0.15f);
                intensity = 3.0f;
                range = 8.5f;
                casingScale = new Vector3(0.35f, 0.35f, 0.12f);
                bulbScale = new Vector3(0.22f, 0.22f, 0.18f);
                lightOffset = new Vector3(0f, 0f, 0.25f);
                break;

            case FacilityLightType.WarmIncandescent:
                lightCol = new Color(1.0f, 0.84f, 0.62f);
                intensity = 5.5f;
                range = 14.0f;
                casingScale = new Vector3(0.45f, 0.08f, 1.8f);
                bulbScale = new Vector3(0.25f, 0.05f, 1.6f);
                lightOffset = new Vector3(0f, -0.2f, 0f);
                break;

            default: // FluorescentCeiling
                lightCol = new Color(0.88f, 0.94f, 1.0f);
                intensity = 5.5f;
                range = 15.0f;
                casingScale = new Vector3(0.45f, 0.08f, 1.8f);
                bulbScale = new Vector3(0.25f, 0.05f, 1.6f);
                lightOffset = new Vector3(0f, -0.2f, 0f);
                break;
        }

        // 1. Casing (chassis)
        GameObject casing = GameObject.CreatePrimitive(PrimitiveType.Cube);
        casing.name = "Housing";
        casing.transform.SetParent(root.transform, false);
        casing.transform.localPosition = Vector3.zero;
        casing.transform.localRotation = Quaternion.identity;
        casing.transform.localScale = casingScale;
        casing.isStatic = true;
        Object.DestroyImmediate(casing.GetComponent<Collider>());
        Renderer casingRend = casing.GetComponent<Renderer>();
        MaterialHelper.ApplyMaterial(casingRend, new Color(0.18f, 0.19f, 0.20f));
        if (casingRend != null)
        {
            casingRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 2. Bulb (emissive glowing tube)
        GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bulb.name = "Bulb";
        bulb.transform.SetParent(root.transform, false);
        bulb.transform.localPosition = lightOffset * 0.4f;
        bulb.transform.localRotation = Quaternion.identity;
        bulb.transform.localScale = bulbScale;
        bulb.isStatic = true;
        Object.DestroyImmediate(bulb.GetComponent<Collider>());

        Renderer bulbRend = bulb.GetComponent<Renderer>();
        if (bulbRend != null)
        {
            bulbRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Material bulbMat = MaterialHelper.CreateLitMaterial(bulbRend, lightCol);
        bulbMat.EnableKeyword("_EMISSION");
        bulbMat.SetColor("_EmissionColor", lightCol * 2.2f);
        if (Application.isPlaying)
        {
            bulbRend.material = bulbMat;
        }
        else
        {
            bulbRend.sharedMaterial = bulbMat;
        }

        // 3. Local Light (Ceiling Spot pointing downward saves 5x shadow atlas tiles per room)
        bool isCeiling = type == FacilityLightType.FluorescentCeiling || type == FacilityLightType.WarmIncandescent;
        GameObject lightObj = new GameObject("LightSource");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = lightOffset;
        lightObj.transform.localRotation = isCeiling ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;

        Light fixtureLightComp = lightObj.AddComponent<Light>();
        fixtureLightComp.type = isCeiling ? LightType.Spot : LightType.Point;
        fixtureLightComp.color = lightCol;
        fixtureLightComp.intensity = intensity;
        fixtureLightComp.range = range;
        if (isCeiling)
        {
            fixtureLightComp.spotAngle = 140f;
            fixtureLightComp.innerSpotAngle = 55f;
            fixtureLightComp.shadows = LightShadows.Hard;
        }
        else
        {
            fixtureLightComp.shadows = LightShadows.None;
        }

        // 4. Fixture script
        FacilityLightFixture fixture = root.AddComponent<FacilityLightFixture>();
        fixture.bulbRenderer = bulbRend;
        fixture.fixtureLight = fixtureLightComp;
        fixture.Initialize(type, flicker, intensity, range, lightCol);

        return root;
    }
}
