using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies the saved loadout to any robot hierarchy. This is shared by the menu preview and the
/// live player, so the cosmetic presentation does not fork into two implementations.
/// </summary>
public static class RobotCosmeticApplier
{
    private const string RuntimeRootName = "__RoboManiaCosmetics";
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    /// <summary>Explicit identity for previews and replicated players; never reads local ownership.</summary>
    public static void ApplySkin(GameObject root, string skinId)
    {
        var skin = RobotCosmeticCatalog.Find(skinId);
        if (skin == null || skin.Category != RobotCosmeticCategory.Skin) return;
        Apply(root, skin, null, null, null);
    }

    public static void ApplyEquipped(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Apply(
            root,
            PlayerProfileService.GetEquipped(RobotCosmeticCategory.Skin),
            PlayerProfileService.GetEquipped(RobotCosmeticCategory.ColorCombo),
            PlayerProfileService.GetEquipped(RobotCosmeticCategory.EyeShape),
            PlayerProfileService.GetEquipped(RobotCosmeticCategory.ScreenEffect));
    }

    public static void Apply(
        GameObject root,
        RobotCosmeticDefinition skin,
        RobotCosmeticDefinition colorCombo,
        RobotCosmeticDefinition eyeShape,
        RobotCosmeticDefinition screenEffect)
    {
        if (root == null)
        {
            return;
        }

        skin = skin ?? RobotCosmeticCatalog.Find(RobotCosmeticCatalog.GetDefaultId(RobotCosmeticCategory.Skin));
        colorCombo = colorCombo ?? RobotCosmeticCatalog.Find(RobotCosmeticCatalog.GetDefaultId(RobotCosmeticCategory.ColorCombo));
        eyeShape = eyeShape ?? RobotCosmeticCatalog.Find(RobotCosmeticCatalog.GetDefaultId(RobotCosmeticCategory.EyeShape));
        screenEffect = screenEffect ?? RobotCosmeticCatalog.Find(RobotCosmeticCatalog.GetDefaultId(RobotCosmeticCategory.ScreenEffect));

        RobotRigBindings sparkRig = root.GetComponentInChildren<RobotRigBindings>(true);
        if (sparkRig != null && sparkRig.HasAuthoredSurfaces && sparkRig.Animator != null)
        {
            using var surfaceTiming = new NetworkStartupDiagnostics.Step("Cosmetic.SparkSkin");
            GameObject spark = sparkRig.Animator.gameObject;
            SparkSkinVisual visual = spark.GetComponent<SparkSkinVisual>();
            if (visual == null) visual = spark.AddComponent<SparkSkinVisual>();
            visual.Apply(skin.Id);
        }

        Color primary = Color.Lerp(skin.Primary, colorCombo.Primary, 0.78f);
        // The FBX has a deliberate warm-orange construction channel. Mixing the
        // skin's dark trim into this colour produced the muddy brown panels seen
        // in the old menu, so keep the warm channel warm and classify trim from
        // the authored material colour instead.
        Color secondary = Color.Lerp(skin.Accent, colorCombo.Secondary, 0.78f);
        Color accent = Color.Lerp(skin.Accent, colorCombo.Accent, 0.82f);

        using (var paletteTiming = new NetworkStartupDiagnostics.Step("Cosmetic.Palette"))
            ApplyPalette(root, primary, secondary, accent, GetMetallic(skin.Id), GetSmoothness(skin.Id));

        Transform previous = root.transform.Find(RuntimeRootName);
        if (previous != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(previous.gameObject);
            }
            else
            {
                Object.DestroyImmediate(previous.gameObject);
            }
        }

        Bounds bounds;
        using (var boundsTiming = new NetworkStartupDiagnostics.Step("Cosmetic.Bounds"))
            bounds = CalculateRobotBounds(root);
        using var attachmentTiming = new NetworkStartupDiagnostics.Step("Cosmetic.Attachments");
        GameObject runtimeRoot = new GameObject(RuntimeRootName);
        runtimeRoot.transform.SetParent(root.transform, false);
        SetLayerRecursively(runtimeRoot, root.layer);
        RobotCosmeticRuntimeResources resources = runtimeRoot.AddComponent<RobotCosmeticRuntimeResources>();

        RobotRigBindings rig = root.GetComponentInChildren<RobotRigBindings>(true);
        if (rig != null && rig.HasAuthoredOptics)
        {
            // The model ships its own face. Generating optics here would stack a
            // second pair of eyes over the authored one.
        }
        else if (rig != null && rig.IsValid)
        {
            var optics = new GameObject("AnimatedOptics");
            optics.transform.SetParent(runtimeRoot.transform, false);
            SetLayerRecursively(optics, root.layer);
            CreateEyes(optics.transform, root.transform, bounds, eyeShape, accent, resources);
            optics.AddComponent<RobotCosmeticBoneAttachment>().Bind(rig.Head, root.GetComponentInChildren<RobotExpressionVisual>(true));
        }
        else CreateEyes(runtimeRoot.transform, root.transform, bounds, eyeShape, accent, resources);
        // Ground auras are disabled for player presentation. Keep the saved
        // cosmetic selection and effect implementation available for a future build.
    }

    private static void ApplyPalette(
        GameObject root,
        Color primary,
        Color secondary,
        Color accent,
        float metallic,
        float smoothness)
    {
        // A model that ships its own finished palette opts out entirely. The role
        // classifier resolves to a single "primary" slot, so a multi-tone body
        // would come back painted one flat colour.
        RobotRigBindings authored = root.GetComponentInChildren<RobotRigBindings>(true);
        if (authored != null && authored.HasAuthoredSurfaces)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Transform cosmeticRoot = root.transform.Find(RuntimeRootName);
        Color darkTrim = Color.Lerp(new Color(0.025f, 0.03f, 0.045f, 1f), primary, 0.12f);

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null || !IsRobotSurfaceRenderer(renderer) ||
                IsInsideRuntimeCosmetics(renderer.transform, cosmeticRoot))
            {
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                string searchableName = (renderer.name + " " + material.name).ToLowerInvariant();
                SurfaceRole role = ClassifySurface(material, searchableName);
                Color panelColor = role == SurfaceRole.Trim
                    ? darkTrim
                    : role == SurfaceRole.Warm
                        ? secondary
                        : role == SurfaceRole.Optic
                            ? accent
                            : primary;

                renderer.GetPropertyBlock(block, materialIndex);
                if (material.HasProperty(BaseColorId))
                {
                    block.SetColor(BaseColorId, panelColor);
                }

                if (material.HasProperty(ColorId))
                {
                    block.SetColor(ColorId, panelColor);
                }

                if (material.HasProperty(EmissionColorId))
                {
                    bool illuminated = role == SurfaceRole.Optic;
                    block.SetColor(EmissionColorId, illuminated ? accent * 1.35f : Color.black);
                }

                if (material.HasProperty(MetallicId))
                {
                    block.SetFloat(MetallicId, metallic);
                }

                if (material.HasProperty(SmoothnessId))
                {
                    block.SetFloat(SmoothnessId, smoothness);
                }

                renderer.SetPropertyBlock(block, materialIndex);
                block.Clear();
            }
        }
    }

    private enum SurfaceRole
    {
        Primary,
        Warm,
        Trim,
        Optic
    }

    private static SurfaceRole ClassifySurface(Material material, string searchableName)
    {
        if (searchableName.Contains("wheel") || searchableName.Contains("tire") ||
            searchableName.Contains("rubber") || searchableName.Contains("dark"))
        {
            return SurfaceRole.Trim;
        }

        if (searchableName.Contains("light") || searchableName.Contains("glow") ||
            searchableName.Contains("eye") || searchableName.Contains("screen") ||
            searchableName.Contains("optic"))
        {
            return SurfaceRole.Optic;
        }

        Color authored = GetAuthoredColor(material);
        Color.RGBToHSV(authored, out float hue, out float saturation, out float value);

        // robot1.fbx uses near-black/charcoal trim, saturated orange hardware,
        // violet armour and softer lavender optics. Those authored roles remain
        // stable even though Blender exported generic Cube/Cylinder names.
        if (value < 0.32f || (saturation < 0.18f && value < 0.52f))
        {
            return SurfaceRole.Trim;
        }

        if ((hue < 0.13f || hue > 0.97f) && saturation > 0.55f &&
            authored.r > authored.b * 1.35f)
        {
            return SurfaceRole.Warm;
        }

        if (hue > 0.67f && hue < 0.86f && saturation < 0.78f)
        {
            return SurfaceRole.Optic;
        }

        return SurfaceRole.Primary;
    }

    private static Color GetAuthoredColor(Material material)
    {
        if (material.HasProperty(BaseColorId))
        {
            return material.GetColor(BaseColorId);
        }

        if (material.HasProperty(ColorId))
        {
            return material.GetColor(ColorId);
        }

        return Color.white;
    }

    private static void CreateEyes(
        Transform parent,
        Transform robotRoot,
        Bounds bounds,
        RobotCosmeticDefinition eyeShape,
        Color glow,
        RobotCosmeticRuntimeResources resources)
    {
        if (eyeShape == null)
        {
            return;
        }

        Vector3 scale = robotRoot.lossyScale;
        float inverseX = Mathf.Approximately(scale.x, 0f) ? 1f : 1f / Mathf.Abs(scale.x);
        float inverseY = Mathf.Approximately(scale.y, 0f) ? 1f : 1f / Mathf.Abs(scale.y);
        float inverseZ = Mathf.Approximately(scale.z, 0f) ? 1f : 1f / Mathf.Abs(scale.z);
        float width = Mathf.Max(0.18f, bounds.size.x * inverseX);
        float height = Mathf.Max(0.18f, bounds.size.y * inverseY);
        float depth = Mathf.Max(0.18f, bounds.size.z * inverseZ);

        // Replace the FBX's two lavender eye spheres instead of drawing a second
        // face over the body. Their authored bounds give us an exact head-screen
        // anchor even though Blender exported generic object names.
        bool foundAuthoredOptics = TryHideAndMeasureAuthoredOptics(
            robotRoot,
            bounds,
            out Bounds authoredOpticBounds);
        Vector3 worldCentre = foundAuthoredOptics
            ? authoredOpticBounds.center +
              robotRoot.forward * Mathf.Max(0.012f, authoredOpticBounds.size.z * 0.18f)
            : bounds.center + Vector3.up * bounds.size.y * 0.43f +
              robotRoot.forward * bounds.size.z * 0.505f;
        Vector3 localCentre = parent.InverseTransformPoint(worldCentre);
        float eyeWidth = width * 0.11f;
        float eyeHeight = height * 0.055f;
        float eyeDepth = Mathf.Max(0.015f, depth * 0.018f);

        Material backing = resources.CreateUnlitMaterial(new Color(0.012f, 0.025f, 0.05f, 0.98f));
        Material eyeMaterial = resources.CreateUnlitMaterial(glow * 1.35f);

        CreatePrimitive("OpticBacking", PrimitiveType.Cube, parent, localCentre,
            new Vector3(width * 0.31f, height * 0.105f, eyeDepth), Quaternion.identity, backing);

        switch (eyeShape.Id)
        {
            case "eyes_visor":
                CreatePrimitive("TacticalVisor", PrimitiveType.Cube, parent, localCentre + Vector3.forward * eyeDepth,
                    new Vector3(width * 0.25f, eyeHeight, eyeDepth * 1.4f), Quaternion.identity, eyeMaterial);
                break;

            case "eyes_tri":
                CreatePrimitive("TriSightLeft", PrimitiveType.Sphere, parent,
                    localCentre + new Vector3(-eyeWidth, 0f, eyeDepth),
                    new Vector3(eyeWidth, eyeHeight * 1.55f, eyeDepth), Quaternion.identity, eyeMaterial);
                CreatePrimitive("TriSightRight", PrimitiveType.Sphere, parent,
                    localCentre + new Vector3(eyeWidth, 0f, eyeDepth),
                    new Vector3(eyeWidth, eyeHeight * 1.55f, eyeDepth), Quaternion.identity, eyeMaterial);
                CreatePrimitive("TriSightCentre", PrimitiveType.Sphere, parent,
                    localCentre + new Vector3(0f, eyeHeight * 1.1f, eyeDepth),
                    new Vector3(eyeWidth * 0.8f, eyeHeight * 1.25f, eyeDepth), Quaternion.identity, eyeMaterial);
                break;

            case "eyes_x":
                CreatePrimitive("XRayA", PrimitiveType.Cube, parent, localCentre + Vector3.forward * eyeDepth,
                    new Vector3(width * 0.26f, eyeHeight * 0.65f, eyeDepth), Quaternion.Euler(0f, 0f, 25f), eyeMaterial);
                CreatePrimitive("XRayB", PrimitiveType.Cube, parent, localCentre + Vector3.forward * eyeDepth,
                    new Vector3(width * 0.26f, eyeHeight * 0.65f, eyeDepth), Quaternion.Euler(0f, 0f, -25f), eyeMaterial);
                break;

            default:
                CreatePrimitive("ScoutOpticLeft", PrimitiveType.Sphere, parent,
                    localCentre + new Vector3(-eyeWidth, 0f, eyeDepth),
                    new Vector3(eyeWidth, eyeHeight * 1.45f, eyeDepth), Quaternion.identity, eyeMaterial);
                CreatePrimitive("ScoutOpticRight", PrimitiveType.Sphere, parent,
                    localCentre + new Vector3(eyeWidth, 0f, eyeDepth),
                    new Vector3(eyeWidth, eyeHeight * 1.45f, eyeDepth), Quaternion.identity, eyeMaterial);
                break;
        }
    }

    private static bool TryHideAndMeasureAuthoredOptics(
        Transform robotRoot,
        Bounds robotBounds,
        out Bounds opticBounds)
    {
        opticBounds = default;
        bool found = false;
        RobotRigBindings rig = robotRoot.GetComponentInChildren<RobotRigBindings>(true);
        if (rig != null && rig.IsValid)
        {
            opticBounds = new Bounds((rig.LeftEye.position + rig.RightEye.position) * 0.5f,
                new Vector3(1f, 0.36f, 0.29f));
            foreach (Renderer surface in rig.GetComponentsInChildren<Renderer>(true))
                if (surface.name == "Optic_L" || surface.name == "Optic_R") surface.enabled = false;
            return true;
        }
        Transform cosmeticRoot = robotRoot.Find(RuntimeRootName);

        foreach (Renderer renderer in robotRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !IsRobotSurfaceRenderer(renderer) ||
                IsInsideRuntimeCosmetics(renderer.transform, cosmeticRoot) ||
                renderer.bounds.center.y < robotBounds.center.y + robotBounds.size.y * 0.24f)
            {
                continue;
            }

            bool isAuthoredOptic = false;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                string searchableName = (renderer.name + " " + material.name).ToLowerInvariant();
                if (ClassifySurface(material, searchableName) == SurfaceRole.Optic)
                {
                    isAuthoredOptic = true;
                    break;
                }
            }

            if (!isAuthoredOptic)
            {
                continue;
            }

            if (!found)
            {
                opticBounds = renderer.bounds;
                found = true;
            }
            else
            {
                opticBounds.Encapsulate(renderer.bounds);
            }

            renderer.enabled = false;
        }

        return found;
    }

    private static GameObject CreatePrimitive(
        string objectName,
        PrimitiveType type,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Quaternion localRotation,
        Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = objectName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = localRotation;
        primitive.transform.localScale = localScale;
        primitive.layer = parent.gameObject.layer;

        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            if (Application.isPlaying)
            {
                Object.Destroy(collider);
            }
            else
            {
                Object.DestroyImmediate(collider);
            }
        }

        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        return primitive;
    }

    private static float GetMetallic(string skinId)
    {
        // The starter frame is moulded arena polymer, not polished metal. Premium
        // skins may still introduce a slightly harder finish without returning to
        // the chrome/product-render look used by the old menu.
        return skinId == "skin_core" ? 0.015f :
            skinId == "skin_ironclad" ? 0.22f :
            skinId == "skin_solar" ? 0.14f : 0.06f;
    }

    private static float GetSmoothness(string skinId)
    {
        return skinId == "skin_core" ? 0.34f :
            skinId == "skin_void" ? 0.5f :
            skinId == "skin_ironclad" ? 0.42f : 0.38f;
    }

    private static Bounds CalculateRobotBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Transform cosmeticRoot = root.transform.Find(RuntimeRootName);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null || !IsRobotSurfaceRenderer(renderer) ||
                IsInsideRuntimeCosmetics(renderer.transform, cosmeticRoot))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds
            ? bounds
            : new Bounds(root.transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
    }

    private static bool IsRobotSurfaceRenderer(Renderer renderer)
    {
        return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    private static bool IsInsideRuntimeCosmetics(Transform candidate, Transform cosmeticRoot)
    {
        if (candidate == null || cosmeticRoot == null)
        {
            return false;
        }

        return candidate == cosmeticRoot || candidate.IsChildOf(cosmeticRoot);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int childIndex = 0; childIndex < root.transform.childCount; childIndex++)
        {
            SetLayerRecursively(root.transform.GetChild(childIndex).gameObject, layer);
        }
    }
}

/// <summary>
/// Owns materials generated for one applied loadout so repeated preview/store selections do not
/// leave runtime materials allocated after the cosmetic hierarchy is replaced.
/// </summary>
public sealed class RobotCosmeticRuntimeResources : MonoBehaviour
{
    private readonly List<Material> materials = new List<Material>();

    public Material CreateUnlitMaterial(Color color)
    {
        Material material = VfxParticleMaterial.ResolveUnlitInstance(null, color);
        if (material == null)
        {
            Debug.LogWarning("Robo Mania cosmetics could not find an unlit shader.", this);
            return null;
        }

        material.enableInstancing = true;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        materials.Add(material);
        return material;
    }

    public Material CreateEffectMaterial(Color color)
    {
        Material material = VfxParticleMaterial.ResolveInstance(null, color);
        if (material == null)
        {
            Debug.LogWarning("Robo Mania cosmetics could not find an effect shader.", this);
            return null;
        }

        material.enableInstancing = true;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        materials.Add(material);
        return material;
    }

    private void OnDestroy()
    {
        for (int index = 0; index < materials.Count; index++)
        {
            Material material = materials[index];
            if (material == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

        materials.Clear();
    }
}

public sealed class RobotCosmeticEffect : MonoBehaviour
{
    private Transform ringPivot;
    private LineRenderer ring;
    private Color primary;
    private float baseRadius;
    private string effectId;

    public void Configure(
        Transform robotRoot,
        Bounds worldBounds,
        string selectedEffectId,
        Color effectPrimary,
        Color accent,
        RobotCosmeticRuntimeResources resources)
    {
        if (robotRoot == null || resources == null)
        {
            enabled = false;
            return;
        }

        effectId = selectedEffectId;
        primary = effectPrimary;
        Vector3 rootScale = robotRoot.lossyScale;
        float xScale = Mathf.Max(0.01f, Mathf.Abs(rootScale.x));
        float zScale = Mathf.Max(0.01f, Mathf.Abs(rootScale.z));
        baseRadius = Mathf.Max(worldBounds.size.x / xScale, worldBounds.size.z / zScale) * 0.62f;

        GameObject pivotObject = new GameObject("AuraRingPivot");
        pivotObject.transform.SetParent(transform, false);
        pivotObject.transform.localPosition = transform.InverseTransformPoint(
            new Vector3(worldBounds.center.x, worldBounds.min.y + 0.03f, worldBounds.center.z));
        ringPivot = pivotObject.transform;

        ring = pivotObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 64;
        ring.widthMultiplier = Mathf.Max(0.018f, baseRadius * 0.038f);
        ring.numCornerVertices = 3;
        ring.numCapVertices = 3;
        ring.sharedMaterial = resources.CreateEffectMaterial(primary);
        ring.startColor = primary;
        ring.endColor = accent;

        for (int index = 0; index < ring.positionCount; index++)
        {
            float angle = index / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(index, new Vector3(Mathf.Cos(angle) * baseRadius, 0f, Mathf.Sin(angle) * baseRadius));
        }

        CreateParticles(accent, resources);
    }

    private void Update()
    {
        if (ringPivot == null || ring == null)
        {
            return;
        }

        float time = Time.unscaledTime;
        float rotationSpeed = effectId == "fx_energy_rain" ? 55f : effectId == "fx_prismatic" ? 72f : 32f;
        ringPivot.localRotation = Quaternion.Euler(0f, time * rotationSpeed, 0f);
        float pulse = 1f + Mathf.Sin(time * (effectId == "fx_scanline" ? 5.5f : 3.4f)) * 0.075f;
        ringPivot.localScale = new Vector3(pulse, 1f, pulse);
        Color animated = primary;
        if (effectId == "fx_prismatic")
        {
            animated = Color.HSVToRGB(Mathf.Repeat(time * 0.12f, 1f), 0.7f, 1f);
        }

        animated.a = 0.82f;
        ring.startColor = animated;
        ring.endColor = new Color(animated.r, animated.g, animated.b, 0.15f);
    }

    private void CreateParticles(Color accent, RobotCosmeticRuntimeResources resources)
    {
        GameObject particlesObject = new GameObject("AuraParticles");
        particlesObject.layer = gameObject.layer;
        particlesObject.transform.SetParent(transform, false);
        ParticleSystem particles = particlesObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = effectId == "fx_energy_rain" ? 1.4f : 0.9f;
        main.startSpeed = effectId == "fx_energy_rain" ? 0.7f : 0.22f;
        main.startSize = Mathf.Max(0.035f, baseRadius * 0.055f);
        main.startColor = new ParticleSystem.MinMaxGradient(primary, accent);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 48;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = effectId == "fx_hologrid" ? 14f : effectId == "fx_prismatic" ? 22f : 10f;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = baseRadius * 0.85f;
        shape.radiusThickness = 0.2f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = resources.CreateEffectMaterial(accent);
        particles.Play();
    }
}
