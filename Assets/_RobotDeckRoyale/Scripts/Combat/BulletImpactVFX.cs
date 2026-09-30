using UnityEngine;

public sealed class BulletImpactVFX : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.42f;

    [Tooltip("Material for the spawned particles. Serialized so a build does not " +
             "depend on Shader.Find, which only resolves shaders the player has " +
             "been told to always include.")]
    [SerializeField] private Material particleMaterial;

    private Light impactLight;
    private float initialIntensity;

    private void Awake()
    {
        CreateFlash();
        CreateSparks();

        // Per-impact realtime lights are skipped on WebGL (routine combat effect,
        // costly in forward rendering on mobile browsers); the flash and sparks remain.
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            impactLight = gameObject.AddComponent<Light>();
            impactLight.type = LightType.Point;
            impactLight.color = new Color(1f, 0.42f, 0.08f);
            impactLight.range = 1.7f;
            impactLight.intensity = 1.15f;
            impactLight.shadows = LightShadows.None;
            initialIntensity = impactLight.intensity;
        }

        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(0.035f);

        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        if (impactLight != null)
        {
            impactLight.intensity = Mathf.MoveTowards(
                impactLight.intensity,
                0f,
                initialIntensity * Time.deltaTime / Mathf.Max(0.05f, lifetime));
        }
    }

    private void CreateFlash()
    {
        ParticleSystem flash = CreateSystem("Bullet Flash");
        ParticleSystem.MainModule main = flash.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.08f;
        main.startSpeed = 0f;
        main.startSize = 0.32f;
        main.startColor = new Color(1f, 0.72f, 0.18f, 1f);
        main.maxParticles = 1;

        ParticleSystem.EmissionModule emission = flash.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        flash.Play();
    }

    private void CreateSparks()
    {
        ParticleSystem sparks = CreateSystem("Bullet Sparks");
        ParticleSystem.MainModule main = sparks.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.065f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.72f, 0.12f, 1f),
            new Color(0.08f, 0.08f, 0.09f, 1f));
        main.gravityModifier = 1.2f;
        main.maxParticles = 12;

        ParticleSystem.EmissionModule emission = sparks.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)9) });

        ParticleSystem.ShapeModule shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 62f;
        shape.radius = 0.025f;
        sparks.Play();
    }

    private ParticleSystem CreateSystem(string objectName)
    {
        GameObject child = new GameObject(objectName);
        child.transform.SetParent(transform, false);
        ParticleSystem system = child.AddComponent<ParticleSystem>();
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = 2f;

        // Without this the renderer keeps Unity's non-URP default and draws as
        // solid magenta -- the purple splash on every bullet hit.
        renderer.sharedMaterial = VfxParticleMaterial.Resolve(particleMaterial);

        return system;
    }
}
