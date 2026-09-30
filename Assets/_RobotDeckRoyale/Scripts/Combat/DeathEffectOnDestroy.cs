using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Damageable))]
public class DeathEffectOnDestroy : MonoBehaviour
{
    [SerializeField] private GameObject deathEffectPrefab;
    [Tooltip("Optional stronger orange/red impact used for damaging self-destructs.")]
    [SerializeField] private GameObject explosiveImpactPrefab;

    [Header("Optional Death Explosion")]
    [SerializeField, Min(0)] private int explosionDamage;
    [SerializeField, Min(0f)] private float explosionRadius = 2.35f;
    [SerializeField] private LayerMask explosionMask = ~0;

    [Header("Feel")]
    [Tooltip("Camera impulse when this dies, before distance falloff.")]
    [SerializeField, Range(0f, 1f)] private float deathCameraKick = 0.34f;

    [Tooltip("Seconds the frame holds on this kill. Set to 0 for chaff that " +
             "dies often enough that freezing on it would be noise.")]
    [SerializeField, Range(0f, 0.3f)] private float deathFreezeDuration = 0.1f;

    [Tooltip("Material for the code-built death burst. Serialized so a build " +
             "does not rely on Shader.Find resolving the URP particle shader.")]
    [SerializeField] private Material burstParticleMaterial;

    private Damageable damageable;
    private FortressTarget identity;

    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        identity = GetComponent<FortressTarget>();
    }

    private void OnEnable()
    {
        if (damageable != null)
        {
            damageable.Died += HandleDeath;
        }
    }

    private void OnDisable()
    {
        if (damageable != null)
        {
            damageable.Died -= HandleDeath;
        }
    }

    private void HandleDeath(Damageable destroyedObject)
    {
        Vector3 groundPosition = transform.position + Vector3.up * 0.12f;
        Vector3 position = transform.position + Vector3.up * 0.72f;

        ApplyExplosionDamage(groundPosition, destroyedObject);
        PlayDeathVisuals(position);
    }

    /// <summary>
    /// The break effect alone, with no damage. Online structure views call this
    /// when the authority reports the structure destroyed.
    /// </summary>
    public void PlayDeathEffectOnly() => PlayDeathVisuals(transform.position + Vector3.up * 0.72f);

    private void PlayDeathVisuals(Vector3 position)
    {
        if (deathEffectPrefab != null)
            Instantiate(deathEffectPrefab, position, Quaternion.identity);

        // Damaging crawler deaths need a more forceful, readable impact than
        // the generic defeat puff. PF_HitImpact owns the warm fire/smoke palette,
        // shockwave and short camera impulse, while this component remains the
        // single authority for team-filtered radius damage.
        if (explosionDamage > 0 && explosiveImpactPrefab != null)
            Instantiate(explosiveImpactPrefab, position, Quaternion.identity);

        // A short, readable burst is created in code so every bot (including
        // the small crawler swarm) has a clear defeat moment even when a
        // prefab's particle systems are stripped on a target platform.
        GameObject burst = new GameObject("Robot Death Burst");
        burst.transform.position = position;
        ParticleSystem particles = burst.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.62f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.1f, 7.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        Color teamColour = identity != null && identity.Team == FortressTeam.Red
            ? new Color(1f, 0.12f, 0.045f)
            : new Color(0.05f, 0.58f, 1f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            Color.Lerp(teamColour, Color.white, 0.35f),
            new Color(1f, 0.31f, 0.035f));
        main.maxParticles = 48;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)38) });
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.24f;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        // This burst is built in code, so its renderer starts with the non-URP
        // default material and the whole death explosion drew as a magenta blob.
        renderer.sharedMaterial = VfxParticleMaterial.Resolve(burstParticleMaterial);
        // No realtime point light: every death (a swarm is four at once) added a
        // per-pixel light to forward rendering on mobile WebGL. The additive
        // burst and shockwave carry the flash on their own.

        DeathShockwaveVFX shockwave = burst.AddComponent<DeathShockwaveVFX>();
        shockwave.Configure(teamColour, Mathf.Max(1.5f, explosionRadius), 0.48f);

        particles.Play();

        // A kill had no camera response at all, which is most of why destroying
        // something did not land. Scaled by distance so a kill across the arena
        // registers without punching as hard as one in your face.
        if (CameraShake.Instance != null)
        {
            float distance = Vector3.Distance(
                CameraShake.Instance.transform.position, position);
            float closeness = 1f - Mathf.Clamp01(distance / 70f);
            CameraShake.Instance.Shake(deathCameraKick * Mathf.Lerp(0.4f, 1f, closeness));
        }

        // A kill is the one beat that should always stop the frame, so it skips
        // the rate limit that keeps sustained fire from stuttering.
        if (deathFreezeDuration > 0f)
        {
            CombatFeedbackOverlay.GetOrCreate().RequestImpactPause(
                deathFreezeDuration, 0.08f, true);
        }

        Destroy(burst, 0.78f);
    }

    private void ApplyExplosionDamage(Vector3 position, Damageable destroyedObject)
    {
        if (explosionDamage <= 0 || explosionRadius <= 0f)
            return;

        Collider[] hits = Physics.OverlapSphere(
            position,
            explosionRadius,
            explosionMask,
            QueryTriggerInteraction.Collide);
        HashSet<Damageable> damaged = new HashSet<Damageable>();

        foreach (Collider hit in hits)
        {
            if (hit == null)
                continue;

            Damageable target = hit.GetComponentInParent<Damageable>();
            if (target == null || target == destroyedObject || target.IsDead ||
                !damaged.Add(target))
            {
                continue;
            }

            if (identity != null)
                target.TakeDamage(explosionDamage, identity.Team, gameObject);
            else
                target.TakeDamage(explosionDamage);
        }
    }
}

// Runtime-only expanding ring. Keeping it code-generated makes the crawler
// blast readable even if optional particle prefab assets are stripped.
internal sealed class DeathShockwaveVFX : MonoBehaviour
{
    private const int SegmentCount = 48;

    private LineRenderer ring;
    private Material material;
    private Color colour;
    private float maximumRadius;
    private float duration;
    private float elapsed;

    public void Configure(Color tint, float radius, float lifetime)
    {
        colour = tint;
        maximumRadius = radius;
        duration = Mathf.Max(0.05f, lifetime);

        material = VfxParticleMaterial.ResolveInstance(null, colour);

        if (material == null)
            return;

        material.name = "Runtime Death Shockwave";

        ring = gameObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = material;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = SegmentCount;
        ring.alignment = LineAlignment.View;
        ring.numCornerVertices = 3;
        ring.numCapVertices = 2;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.sortingOrder = 20;

        UpdateRing(0f);
    }

    private void Update()
    {
        if (ring == null)
            return;

        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        UpdateRing(progress);

        if (progress >= 1f)
            ring.enabled = false;
    }

    private void UpdateRing(float progress)
    {
        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        float radius = Mathf.Lerp(0.18f, maximumRadius, eased);

        for (int index = 0; index < SegmentCount; index++)
        {
            float angle = index * Mathf.PI * 2f / SegmentCount;
            ring.SetPosition(index, new Vector3(
                Mathf.Cos(angle) * radius,
                -0.58f,
                Mathf.Sin(angle) * radius));
        }

        float alpha = 1f - progress;
        Color faded = new Color(colour.r, colour.g, colour.b, alpha * 0.9f);
        ring.startColor = faded;
        ring.endColor = faded;
        ring.startWidth = Mathf.Lerp(0.32f, 0.035f, progress);
        ring.endWidth = ring.startWidth;
    }

    private void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }
}
