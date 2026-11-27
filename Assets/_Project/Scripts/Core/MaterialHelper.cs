using UnityEngine;

/// <summary>
/// Helper to safely create and apply lit materials across both Editor and Standalone player builds.
/// In Standalone builds, Shader.Find("Universal Render Pipeline/Lit") returns null if stripped.
/// This helper safely extracts the active pipeline shader from the primitive renderer or falls back safely.
/// In Editor mode, uses sharedMaterial to avoid material leaks.
/// </summary>
public static class MaterialHelper
{
    public static Material CreateLitMaterial(Renderer rend, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? (rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.shader : null)
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Standard");

        Material mat;
        if (shader != null)
        {
            mat = new Material(shader);
        }
        else if (rend != null && rend.sharedMaterial != null)
        {
            mat = new Material(rend.sharedMaterial);
        }
        else
        {
            mat = new Material(Shader.Find("Sprites/Default"));
        }

        if (mat != null)
        {
            mat.color = color;
        }

        return mat;
    }

    /// <summary>
    /// Safely applies a lit colored material to a renderer.
    /// Uses sharedMaterial during edit mode to prevent scene material leakage.
    /// Uses material during play mode for instance-safe rendering.
    /// </summary>
    public static void ApplyMaterial(Renderer rend, Color color)
    {
        if (rend == null)
        {
            return;
        }

        Material mat = CreateLitMaterial(rend, color);
        if (Application.isPlaying)
        {
            rend.material = mat;
        }
        else
        {
            rend.sharedMaterial = mat;
        }
    }

    /// <summary>
    /// Safely applies a custom material (or texture) with automatic UV tiling based on surface dimensions.
    /// Prevents textures from stretching on long walls or wide floors.
    /// Supports Universal Render Pipeline Lit/Simple Lit shaders (_BaseMap) and Standard shaders (_MainTex).
    /// </summary>
    public static void ApplyCustomMaterial(Renderer rend, Material baseMat, Color fallbackColor, Vector3 scale, float tileSize = 4f)
    {
        if (rend == null)
        {
            return;
        }

        if (baseMat == null)
        {
            ApplyMaterial(rend, fallbackColor);
            return;
        }

        float tile = tileSize > 0.05f ? tileSize : 4f;

        // Determine surface orientation and UV repeat counts
        string objName = rend.gameObject.name;
        bool isFloorOrCeiling = objName.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                objName.IndexOf("Ceiling", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                scale.y < 0.5f;

        float repeatX;
        float repeatY;

        if (isFloorOrCeiling)
        {
            // For horizontal slabs (floors and ceilings), X and Z represent the surface plane
            repeatX = Mathf.Max(0.1f, scale.x / tile);
            repeatY = Mathf.Max(0.1f, scale.z / tile);
        }
        else
        {
            // For vertical walls and pillars, horizontal span is max(x, z) and vertical span is y
            float horizSpan = Mathf.Max(scale.x, scale.z);
            float vertSpan = scale.y;
            repeatX = Mathf.Max(0.1f, horizSpan / tile);
            repeatY = Mathf.Max(0.1f, vertSpan / tile);
        }

        // Multiply by base material texture tiling if configured on the material asset
        Vector2 baseScale = Vector2.one;
        if (baseMat.HasProperty("_BaseMap"))
        {
            baseScale = baseMat.GetTextureScale("_BaseMap");
        }
        else if (baseMat.HasProperty("_MainTex"))
        {
            baseScale = baseMat.GetTextureScale("_MainTex");
        }

        if (baseScale.x > 0.001f && baseScale.y > 0.001f)
        {
            repeatX *= baseScale.x;
            repeatY *= baseScale.y;
        }

        Vector2 uvScale = new Vector2(repeatX, repeatY);
        Vector4 stVector = new Vector4(repeatX, repeatY, 0f, 0f);

        if (Application.isPlaying)
        {
            Material inst = new Material(baseMat);

            // Universal Render Pipeline Lit / Simple Lit shaders use _BaseMap
            if (inst.HasProperty("_BaseMap"))
            {
                inst.SetTextureScale("_BaseMap", uvScale);
            }

            // Standard / Built-in / Legacy shaders use _MainTex
            if (inst.HasProperty("_MainTex"))
            {
                inst.SetTextureScale("_MainTex", uvScale);
            }

            // Normal map
            if (inst.HasProperty("_BumpMap"))
            {
                inst.SetTextureScale("_BumpMap", uvScale);
            }

            // Detail albedo map
            if (inst.HasProperty("_DetailAlbedoMap"))
            {
                inst.SetTextureScale("_DetailAlbedoMap", uvScale);
            }

            rend.material = inst;
        }
        else
        {
            rend.sharedMaterial = baseMat;
        }

        // Apply MaterialPropertyBlock for instant preview in the Unity Scene View
        // and per-renderer instancing support without leaking materials in the Editor
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetVector("_BaseMap_ST", stVector);
        mpb.SetVector("_MainTex_ST", stVector);
        mpb.SetVector("_BumpMap_ST", stVector);
        mpb.SetVector("_DetailAlbedoMap_ST", stVector);
        rend.SetPropertyBlock(mpb);
    }
}
