using System.Collections;
using UnityEngine;

public class MuzzleFlashVFX : MonoBehaviour
{
    [Tooltip("Authored muzzle-flash prefab. When assigned it is pooled and played instead of the code-built stand-in below.")]
    [SerializeField] private GameObject flashPrefab;
    [SerializeField, Min(0.05f)] private float flashPrefabLifetime = 0.5f;

    [Header("Fallback (used only when no prefab is assigned)")]
    [SerializeField] private Color flashColor = new Color(1f, 0.5f, 0.08f, 1f);
    [SerializeField] private float flashDuration = 0.055f;
    [SerializeField] private float flashSize = 0.34f;

    private static Material sharedFlashMaterial;

    private RobotBlaster blaster;
    private ParticleSystem flashParticles;
    private Light flashLight;

    private static Material GetFlashMaterial()
    {
        if (sharedFlashMaterial != null)
            return sharedFlashMaterial;

        sharedFlashMaterial =
            VfxParticleMaterial.ResolveInstance(null, Color.white);

        if (sharedFlashMaterial != null)
            sharedFlashMaterial.name = "Runtime Muzzle Flash";

        return sharedFlashMaterial;
    }

    private void Awake()
    {
        blaster = GetComponentInParent<RobotBlaster>();

        // Only build the stand-in when no authored asset has been supplied.
        if (flashPrefab == null)
            CreateFlash();
    }

    private void OnEnable()
    {
        if (blaster != null)
            blaster.Fired += PlayFlash;
    }

    private void OnDisable()
    {
        if (blaster != null)
            blaster.Fired -= PlayFlash;
    }

    private void CreateFlash()
    {
        GameObject flashObject = new GameObject("Muzzle Flash");
        flashObject.transform.SetParent(transform, false);

        flashParticles = flashObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = flashParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.06f;
        main.startSpeed = 0f;
        main.startSize = flashSize;
        main.startColor = flashColor;
        main.maxParticles = 1;

        ParticleSystem.EmissionModule emission = flashParticles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)1)
        });

        ParticleSystem.ShapeModule shape = flashParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.01f;

        ParticleSystemRenderer renderer =
            flashParticles.GetComponent<ParticleSystemRenderer>();

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = 3f;

        // A particle system added at runtime has no material, so the flash was
        // never actually drawn. Without this the shot has no visible telegraph.
        renderer.sharedMaterial = GetFlashMaterial();

        // No realtime light on WebGL: every muzzle (robots, turrets, each Spidy)
        // kept an enabled per-pixel point light at zero intensity, which forward
        // rendering still pays for on mobile browsers. The particle flash carries
        // the telegraph. Other platforms keep the light.
        if (Application.platform == RuntimePlatform.WebGLPlayer) return;
        flashLight = flashObject.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = flashColor;
        flashLight.range = 3f;
        flashLight.intensity = 0f;
    }

    private void PlayFlash(RobotBlaster firingBlaster)
    {
        PlayFlash();
    }

    public void PlayFlash()
    {
        if (flashPrefab != null)
        {
            ProjectileLauncher.SpawnEffect(
                flashPrefab,
                transform.position,
                transform.rotation,
                flashPrefabLifetime);
            return;
        }

        if (flashParticles != null)
        {
            flashParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );

            flashParticles.Play();
        }

        if (flashLight != null)
        {
            StopAllCoroutines();
            StartCoroutine(FlashLight());
        }
    }

    private IEnumerator FlashLight()
    {
        flashLight.intensity = 4f;

        yield return new WaitForSeconds(flashDuration);

        flashLight.intensity = 0f;
    }
}
