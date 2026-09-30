using UnityEngine;

public class RocketImpactVFX : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.7f;
    private Light impactLight;
    private float initialLightIntensity;
    private Material impactParticleMaterial;

    private void Awake()
    {
        // Stops the old weak particle effect on this prefab.
        foreach (ParticleSystem oldSystem in
                 GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystemRenderer oldRenderer =
                oldSystem.GetComponent<ParticleSystemRenderer>();
            if (impactParticleMaterial == null && oldRenderer != null)
                impactParticleMaterial = oldRenderer.sharedMaterial;

            oldSystem.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }

        CreateFlash();
        CreateSparks();
        CreateDebris();
        CreateSmoke();
        CreateShockwave();
        CreateImpactLight();
        AddCameraKick(0.16f, 18f);

        Destroy(gameObject, lifetime);
    }

    private void AddCameraKick(float maximumKick, float fullFalloffDistance)
    {
        if (CameraShake.Instance == null)
            return;

        Camera gameplayCamera = Camera.main;
        float distanceFactor = 1f;
        if (gameplayCamera != null)
        {
            float distance = Vector3.Distance(
                gameplayCamera.transform.position,
                transform.position);
            distanceFactor = 1f - Mathf.Clamp01(
                distance / Mathf.Max(1f, fullFalloffDistance));
        }

        CameraShake.Instance.Shake(maximumKick * Mathf.Lerp(0.35f, 1f, distanceFactor));
    }

    private void Update()
    {
        if (impactLight == null)
            return;

        impactLight.intensity = Mathf.MoveTowards(
            impactLight.intensity,
            0f,
            initialLightIntensity * Time.deltaTime / Mathf.Max(0.05f, lifetime * 0.55f));
    }

    private void CreateShockwave()
    {
        GameObject ring = new GameObject("Impact Shockwave", typeof(LineRenderer), typeof(ImpactShockwave));
        ring.transform.SetParent(transform, false);
    }

    private void CreateImpactLight()
    {
        // Skipped on WebGL: a realtime point light on every missile impact is the
        // most frequent light in a fight. The flash, sparks and shockwave remain.
        if (Application.platform == RuntimePlatform.WebGLPlayer) return;
        impactLight = gameObject.AddComponent<Light>();
        impactLight.type = LightType.Point;
        impactLight.color = new Color(1f, 0.34f, 0.035f);
        impactLight.range = 4.2f;
        impactLight.intensity = 3.2f;
        impactLight.shadows = LightShadows.None;
        initialLightIntensity = impactLight.intensity;
    }

    private void CreateFlash()
    {
        ParticleSystem flash = CreateSystem("Impact Flash");

        ParticleSystem.MainModule main = flash.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.09f;
        main.startSpeed = 0f;
        main.startSize = 0.72f;
        main.startColor = new Color(1f, 0.72f, 0.08f, 1f);
        main.maxParticles = 1;

        ParticleSystem.EmissionModule emission = flash.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)1)
        });

        ParticleSystem.ShapeModule shape = flash.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.02f;

        flash.Play();
    }

    private void CreateSparks()
    {
        ParticleSystem sparks = CreateSystem("Fire Sparks");

        ParticleSystem.MainModule main = sparks.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime =
            new ParticleSystem.MinMaxCurve(0.16f, 0.32f);
        main.startSpeed =
            new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
        main.startSize =
            new ParticleSystem.MinMaxCurve(0.045f, 0.11f);
        main.startColor =
            new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.82f, 0.12f, 1f),
                new Color(1f, 0.12f, 0.015f, 1f)
            );
        main.gravityModifier = 1.1f;
        main.maxParticles = 24;

        ParticleSystem.EmissionModule emission = sparks.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)18)
        });

        ParticleSystem.ShapeModule shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 48f;
        shape.radius = 0.04f;

        ParticleSystemRenderer renderer =
            sparks.GetComponent<ParticleSystemRenderer>();

        renderer.lengthScale = 2.2f;
        renderer.velocityScale = 0.25f;

        sparks.Play();
    }

    private void CreateDebris()
    {
        ParticleSystem debris = CreateSystem("Hot Debris");

        ParticleSystem.MainModule main = debris.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime =
            new ParticleSystem.MinMaxCurve(0.25f, 0.42f);
        main.startSpeed =
            new ParticleSystem.MinMaxCurve(1.4f, 3.2f);
        main.startSize =
            new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
        main.startColor = new Color(0.78f, 0.12f, 0.025f, 1f);
        main.gravityModifier = 1.6f;
        main.maxParticles = 12;

        ParticleSystem.EmissionModule emission = debris.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)8)
        });

        ParticleSystem.ShapeModule shape = debris.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 75f;
        shape.radius = 0.04f;

        debris.Play();
    }

    private void CreateSmoke()
    {
        ParticleSystem smoke = CreateSystem("Smoke Puff");

        ParticleSystem.MainModule main = smoke.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime =
            new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
        main.startSpeed =
            new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSize =
            new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
        main.startColor =
            new ParticleSystem.MinMaxGradient(
                new Color(0.16f, 0.09f, 0.035f, 0.58f),
                new Color(0.055f, 0.045f, 0.04f, 0.25f)
            );
        main.maxParticles = 8;

        ParticleSystem.EmissionModule emission = smoke.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)5)
        });

        ParticleSystem.ShapeModule shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.06f;

        smoke.Play();
    }

    private ParticleSystem CreateSystem(string systemName)
    {
        GameObject child = new GameObject(systemName);
        child.transform.SetParent(transform, false);

        ParticleSystem system = child.AddComponent<ParticleSystem>();

        ParticleSystemRenderer renderer =
            system.GetComponent<ParticleSystemRenderer>();

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = 2f;
        if (impactParticleMaterial != null)
            renderer.sharedMaterial = impactParticleMaterial;

        return system;
    }
}
