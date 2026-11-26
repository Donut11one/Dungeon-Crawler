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

        float tile = tileSize > 0.1f ? tileSize : 4f;
        float horizSpan = Mathf.Max(scale.x, scale.z);
        float vertSpan = scale.y > 0.5f ? scale.y : Mathf.Min(scale.x, scale.z);

        if (Application.isPlaying)
        {
            Material inst = new Material(baseMat);
            if (inst.mainTexture != null)
            {
                inst.mainTextureScale = new Vector2(horizSpan / tile, vertSpan / tile);
            }
            rend.material = inst;
        }
        else
        {
            rend.sharedMaterial = baseMat;
        }
    }
}
