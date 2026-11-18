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
}
