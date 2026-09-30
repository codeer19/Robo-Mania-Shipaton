using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public class RocketTrailVFX : MonoBehaviour
{
    private static Material sharedTrailMaterial;
    private static Material sharedEngineMaterial;

    [SerializeField] private float trailTime = 0.09f;
    [SerializeField] private float startWidth = 0.2f;
    [SerializeField] private float endWidth = 0.025f;
    [SerializeField] private float desiredRocketLength = 1.5f;
    [SerializeField] private Color rocketEmission = new Color(1f, 0.22f, 0.025f, 1f);

    private void Awake()
    {
        TrailRenderer trail = GetComponent<TrailRenderer>();

        trail.time = trailTime;
        trail.minVertexDistance = 0.025f;
        trail.widthMultiplier = 1f;
        trail.startWidth = startWidth;
        trail.endWidth = endWidth;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.numCapVertices = 3;
        trail.numCornerVertices = 2;
        trail.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sortingOrder = 10;

        Gradient gradient = new Gradient();

        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(
                    new Color(1f, 0.95f, 0.55f), 0f),
                new GradientColorKey(
                    new Color(1f, 0.32f, 0.05f), 0.35f),
                new GradientColorKey(
                    new Color(0.9f, 0.08f, 0.02f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.65f, 0.35f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        trail.colorGradient = gradient;
        trail.material = GetTrailMaterial();

        EnhanceRocketModel();
        CreateEngineGlow();
    }

    private static Material GetTrailMaterial()
    {
        if (sharedTrailMaterial != null)
            return sharedTrailMaterial;

        sharedTrailMaterial =
            VfxParticleMaterial.ResolveInstance(null, Color.white);

        return sharedTrailMaterial;
    }

    private void EnhanceRocketModel()
    {
        Transform rocketVisual = transform.Find("RocketVisual");
        if (rocketVisual == null)
            return;

        Renderer[] renderers = rocketVisual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds combined = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            combined.Encapsulate(renderers[index].bounds);

        float currentLength = Mathf.Max(combined.size.x, combined.size.y, combined.size.z);
        if (currentLength > 0.001f)
            rocketVisual.localScale *= desiredRocketLength / currentLength;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        int emissionId = Shader.PropertyToID("_EmissionColor");

        foreach (Renderer rocketRenderer in renderers)
        {
            rocketRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            rocketRenderer.GetPropertyBlock(block);
            block.SetColor(emissionId, rocketEmission * 1.7f);
            rocketRenderer.SetPropertyBlock(block);
        }
    }

    private void CreateEngineGlow()
    {
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "Rocket Engine Glow";
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = Vector3.back * (desiredRocketLength * 0.48f);
        glow.transform.localScale = new Vector3(0.18f, 0.18f, 0.12f);

        Collider glowCollider = glow.GetComponent<Collider>();
        if (glowCollider != null)
            Destroy(glowCollider);

        MeshRenderer renderer = glow.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetEngineMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // The emissive glow mesh reads on its own; a per-rocket realtime point
        // light is skipped on WebGL, where routine combat lights are costly.
        if (Application.platform == RuntimePlatform.WebGLPlayer) return;
        Light light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.34f, 0.04f);
        light.intensity = 1.4f;
        light.range = 2.2f;
        light.shadows = LightShadows.None;
    }

    private static Material GetEngineMaterial()
    {
        if (sharedEngineMaterial != null)
            return sharedEngineMaterial;

        Color color = new Color(1f, 0.18f, 0.015f, 1f);
        sharedEngineMaterial =
            VfxParticleMaterial.ResolveUnlitInstance(null, color);

        if (sharedEngineMaterial == null)
            return null;

        if (sharedEngineMaterial.HasProperty("_BaseColor"))
            sharedEngineMaterial.SetColor("_BaseColor", color);
        if (sharedEngineMaterial.HasProperty("_EmissionColor"))
            sharedEngineMaterial.SetColor("_EmissionColor", color * 2f);
        return sharedEngineMaterial;
    }
}
