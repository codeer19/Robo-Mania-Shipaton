using UnityEngine;

/// <summary>
/// Supplies materials for renderers that are built from script.
///
/// A ParticleSystemRenderer or LineRenderer created at runtime starts with no
/// material, and the built-in default it falls back to is not a URP shader, so
/// it draws as solid magenta. Several effects here build their renderers in
/// code, so the fix lives in one place rather than being repeated -- and
/// forgotten -- in each of them.
///
/// The materials are loaded from Resources rather than resolved with
/// <see cref="Shader.Find"/>. Shader.Find only sees shaders the player actually
/// shipped, and nothing referenced these URP shaders from an asset, so every
/// code-built effect resolved to null on device and drew magenta -- the pink
/// residue left behind by a destroyed robot. A material asset under Resources
/// is a hard reference, so the shader and its variants are always built in.
/// </summary>
public static class VfxParticleMaterial
{
    private const string ParticlePath = "VFX/M_VfxParticleAdditive";
    private const string UnlitPath = "VFX/M_VfxUnlitTransparent";
    private const string LitPath = "VFX/M_VfxLit";

    private static Material particleFallback;
    private static Material unlitFallback;
    private static Material litFallback;

    /// <summary>
    /// Returns <paramref name="authored"/> when one was assigned, otherwise the
    /// shared additive particle material.
    /// </summary>
    public static Material Resolve(Material authored)
    {
        if (authored != null)
            return authored;

        return particleFallback != null
            ? particleFallback
            : particleFallback = Load(ParticlePath, "Universal Render Pipeline/Particles/Unlit");
    }

    /// <summary>
    /// Unlit transparent material for code-built line renderers, rings and
    /// ground guides. Pass the authored material when the effect serializes one.
    /// </summary>
    public static Material ResolveUnlit(Material authored)
    {
        if (authored != null)
            return authored;

        return unlitFallback != null
            ? unlitFallback
            : unlitFallback = Load(UnlitPath, "Universal Render Pipeline/Unlit");
    }

    /// <summary>
    /// Lit material for code-built props and previews.
    /// </summary>
    public static Material ResolveLit(Material authored)
    {
        if (authored != null)
            return authored;

        return litFallback != null
            ? litFallback
            : litFallback = Load(LitPath, "Universal Render Pipeline/Lit");
    }

    /// <summary>
    /// Returns an instance of the shared material so a caller that tints or
    /// animates it does not write through to every other effect using it.
    /// </summary>
    public static Material ResolveInstance(Material authored, Color tint)
    {
        Material source = Resolve(authored);

        if (source == null)
            return null;

        Material instance = new Material(source)
        {
            name = source.name + " (runtime)",
            hideFlags = HideFlags.DontSave
        };

        instance.color = tint;
        return instance;
    }

    /// <summary>
    /// Returns an instance of the shared unlit material, tinted.
    /// </summary>
    public static Material ResolveUnlitInstance(Material authored, Color tint)
    {
        Material source = ResolveUnlit(authored);

        if (source == null)
            return null;

        Material instance = new Material(source)
        {
            name = source.name + " (runtime)",
            hideFlags = HideFlags.DontSave
        };

        instance.color = tint;
        return instance;
    }

    private static Material Load(string resourcePath, string shaderFallback)
    {
        Material material = Resources.Load<Material>(resourcePath);

        if (material != null)
            return material;

        // Editor-only safety net. In a player this branch means the Resources
        // asset was deleted or renamed, and the effect is about to draw magenta.
        Shader shader = Shader.Find(shaderFallback) ?? Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogError(
                "VfxParticleMaterial could not load Resources/" + resourcePath +
                " and no fallback shader was available. Code-built VFX will draw magenta.");

            return null;
        }

        Debug.LogWarning(
            "VfxParticleMaterial fell back to Shader.Find for " + resourcePath +
            ". This will not resolve in a player build.");

        return new Material(shader) { name = "VfxFallback (runtime)" };
    }
}
