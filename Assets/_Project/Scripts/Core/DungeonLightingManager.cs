using UnityEngine;

/// <summary>
/// Manages the subterranean horror lighting environment.
/// Disables global/ambient directional lighting and enforces pitch-black darkness,
/// ensuring all illumination originates locally from player flashlights and placed facility light props.
/// </summary>
public class DungeonLightingManager : MonoBehaviour
{
    public static DungeonLightingManager Instance { get; private set; }

    [Header("Dark Atmosphere Configuration")]
    [SerializeField] private bool disableGlobalDirectionalLights = true;
    [SerializeField] private Color ambientColor = new Color(0.01f, 0.01f, 0.015f);
    [SerializeField] private bool enableAtmosphericFog = true;
    [SerializeField] private float fogDensity = 0.012f;
    [SerializeField] private Color fogColor = Color.black;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ApplyDarkAtmosphere();
    }

    private void Start()
    {
        ApplyDarkAtmosphere();
    }

    /// <summary>
    /// Enforces pure pitch-black environment settings and disables any global directional sunlight.
    /// All illumination in the facility must come from local lights (flashlights and light props).
    /// </summary>
    public static void ApplyDarkAtmosphere()
    {
        bool disableDir = Instance != null ? Instance.disableGlobalDirectionalLights : true;
        Color ambientCol = Instance != null ? Instance.ambientColor : new Color(0.01f, 0.01f, 0.015f);
        bool useFog = Instance != null ? Instance.enableAtmosphericFog : true;
        float density = Instance != null ? Instance.fogDensity : 0.012f;
        Color fogCol = Instance != null ? Instance.fogColor : Color.black;

        // 1. Disable all global directional sun/ambient lights in the scene
        if (disableDir)
        {
            Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            if (allLights != null)
            {
                for (int i = 0; i < allLights.Length; i++)
                {
                    Light l = allLights[i];
                    if (l != null && l.type == LightType.Directional)
                    {
                        l.enabled = false;
                        l.intensity = 0f;
                    }
                }
            }
        }

        // 2. Pitch-black ambient illumination (no global sky or flat fill)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambientCol;
        RenderSettings.ambientIntensity = 0f;
        RenderSettings.reflectionIntensity = 0.05f;

        // 3. Atmospheric subterranean black fog to swallow distant unlit corridors
        RenderSettings.fog = useFog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = density;
        RenderSettings.fogColor = fogCol;
    }
}
