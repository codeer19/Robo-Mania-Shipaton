using System.Collections.Generic;
using UnityEngine;

public class BasicProjectile : MonoBehaviour, IPooledObject
{
    [SerializeField] private float speed = 52f;
    [SerializeField] private int damage = 10;
    [SerializeField] private float lifetime = 2.2f;
    [SerializeField, Min(0.5f)] private float maxTravelDistance = 22f;
    [SerializeField] private bool detonateAtMaxRange = true;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField, Min(0.1f)] private float hitEffectLifetime = 1.5f;

    [Header("Reliable Hit Detection")]
    [Tooltip("Makes fast projectiles forgiving against the visible robot body.")]
    [SerializeField, Range(0.05f, 1f)] private float hitRadius = 0.48f;

    [Header("Rocket Splash")]
    [SerializeField] private float explosionRadius = 1.45f;

    [Range(0f, 1f)]
    [SerializeField] private float minimumSplashDamagePercent = 0.4f;

    [Header("Team Tracer")]
    [SerializeField] private bool useTeamTintedTracer;
    [SerializeField] private Color blueTracerColour = new Color(0.08f, 0.64f, 1f, 1f);
    [SerializeField] private Color redTracerColour = new Color(1f, 0.15f, 0.06f, 1f);

    private static Material sharedTracerMaterial;

    private static readonly RaycastHit[] SweepBuffer = new RaycastHit[24];
    private static readonly Collider[] SplashBuffer = new Collider[32];

    private readonly HashSet<Damageable> splashTargets =
        new HashSet<Damageable>();

    private GameObject ownerObject;
    private FortressTeam ownerTeam;
    private bool hasOwnerTeam;
    private bool hasHit;
    private Vector3 spawnPosition;
    private float expireAtTime;
    private int configuredDamage = -1;

    public float Speed => speed;
    public int ConfiguredDamage => authoredDamage >= 0 ? authoredDamage : damage;
    public void ShowOnlineImpact(Vector3 point, Vector3 normal) => SpawnHitEffect(point, normal);
    public float MaxTravelDistance => maxTravelDistance;
    public float HitRadius => hitRadius;
    public float ExplosionRadius => explosionRadius;
    public LayerMask HitMask => hitMask;

    // Missiles in flight, for the Missile Interceptor: it checks this short list
    // on a timer instead of searching the scene.
    private static readonly List<BasicProjectile> active = new List<BasicProjectile>();
    public static IReadOnlyList<BasicProjectile> Active => active;
    public bool InFlight => !hasHit && hasOwnerTeam && isActiveAndEnabled;
    public FortressTeam Team => ownerTeam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();
    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);

    /// <summary>Shot down in flight by a Missile Interceptor: the burst is shown, nothing is damaged.</summary>
    public void Intercept()
    {
        if (hasHit) return;
        hasHit = true;
        SpawnHitEffect(transform.position, -transform.forward);
        CombatPool.Release(gameObject);
    }

    public void OnRetrievedFromPool()
    {
        hasHit = false;
        ownerObject = null;
        hasOwnerTeam = false;

        if (configuredDamage > 0)
            damage = configuredDamage;

        BeginFlight();
    }

    public void OnReturnedToPool()
    {
        hasHit = true;
    }

    private void Start()
    {
        if (configuredDamage < 0)
            configuredDamage = damage;

        BeginFlight();
    }

    private void BeginFlight()
    {
        spawnPosition = transform.position;

        // The sweep expires the projectile at maximum range. This timer is only
        // a backstop for a projectile that is somehow never ticked, so it must
        // never be shorter than the time the shot legitimately needs to travel.
        float travelSeconds = maxTravelDistance / Mathf.Max(1f, speed);
        expireAtTime = Time.time + Mathf.Max(lifetime, travelSeconds + 0.5f);
    }

    private void Update()
    {
        if (hasHit)
            return;

        if (Time.time >= expireAtTime)
        {
            ExpireAtMaximumRange();
            return;
        }

        float travelDistance = Mathf.Max(0f, speed) * Time.deltaTime;

        int hitCount = Physics.SphereCastNonAlloc(
            transform.position,
            hitRadius,
            transform.forward,
            SweepBuffer,
            travelDistance,
            hitMask,
            QueryTriggerInteraction.Collide
        );

        bool foundHit = false;
        RaycastHit closestHit = default;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = SweepBuffer[index];

            if (ShouldIgnore(hit.collider))
                continue;

            if (!foundHit || hit.distance < closestHit.distance)
            {
                closestHit = hit;
                foundHit = true;
            }
        }

        if (foundHit)
        {
            // A sphere cast that already overlaps a collider at its start
            // reports distance 0 and point (0,0,0). Detonating on that raw
            // value would explode at the world origin instead of point blank.
            bool startedOverlapping =
                closestHit.distance <= 0f ||
                closestHit.point.sqrMagnitude <= 0.000001f;

            Vector3 hitPoint = startedOverlapping
                ? transform.position
                : closestHit.point;

            Vector3 hitNormal = closestHit.normal.sqrMagnitude > 0.000001f
                ? closestHit.normal
                : -transform.forward;

            TryHit(
                closestHit.collider,
                hitPoint,
                hitNormal
            );

            return;
        }

        transform.position += transform.forward * travelDistance;

        if ((transform.position - spawnPosition).sqrMagnitude >=
            maxTravelDistance * maxTravelDistance)
        {
            ExpireAtMaximumRange();
        }
    }

    /// <summary>
    /// The damage this round was authored with, captured before any override.
    ///
    /// Projectiles are pooled, so an override used to stick to the instance and
    /// leak into every later shot fired from it. Restoring the authored value on
    /// each launch also lets an override of 0 mean "no damage" - which is what a
    /// replayed copy of an opponent's shot needs, since that hit is already being
    /// resolved by the player who was actually shot.
    /// </summary>
    private int AuthoredDamage => authoredDamage;

    private int authoredDamage = -1;

    // Captured before any launch can override it. Reading it lazily would risk
    // latching a value that a previous shot had already replaced.
    private void Awake() => authoredDamage = damage;

    public void Initialize(GameObject owner, int damageOverride = -1)
    {
        ownerObject = owner;

        damage = damageOverride >= 0 ? damageOverride : AuthoredDamage;

        FortressTarget ownerTarget =
            owner.GetComponentInParent<FortressTarget>();

        if (ownerTarget != null)
        {
            ownerTeam = ownerTarget.Team;
            hasOwnerTeam = true;
            ApplyTeamTracer();
        }
    }

    public void Initialize(FortressTeam team, int damageOverride = -1)
    {
        ownerTeam = team;
        hasOwnerTeam = true;
        ApplyTeamTracer();

        damage = damageOverride >= 0 ? damageOverride : AuthoredDamage;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (ShouldIgnore(other))
            return;

        Vector3 hitPoint =
            ColliderUtility.ClosestPointSafe(other, transform.position);

        Vector3 hitNormal =
            (transform.position - hitPoint).normalized;

        TryHit(other, hitPoint, hitNormal);
    }

    private bool ShouldIgnore(Collider hitCollider)
    {
        if (hitCollider == null)
            return true;

        if (hitCollider.gameObject == gameObject ||
            hitCollider.transform.IsChildOf(transform))
        {
            return true;
        }

        if (ownerObject != null &&
            (hitCollider.gameObject == ownerObject ||
             hitCollider.transform.IsChildOf(ownerObject.transform)))
        {
            return true;
        }

        Damageable hitDamageable =
            hitCollider.GetComponentInParent<Damageable>();

        // Pickups, arena sensors, placement volumes and other harmless trigger
        // colliders must not swallow a visible rocket or bullet before it can
        // reach a combat target. Damageable trigger hitboxes are still valid.
        if (hitCollider.isTrigger && hitDamageable == null)
        {
            return true;
        }

        // A dead robot keeps its hitbox while its visual is hidden for the
        // respawn countdown. Shots must pass through the corpse instead of
        // detonating on an invisible target that can no longer take damage.
        if (hitDamageable != null && hitDamageable.IsDead)
        {
            return true;
        }

        FortressTarget targetIdentity =
            hitCollider.GetComponentInParent<FortressTarget>();

        return hasOwnerTeam &&
               targetIdentity != null &&
               targetIdentity.Team == ownerTeam;
    }

    private void TryHit(
        Collider hitCollider,
        Vector3 hitPoint,
        Vector3 hitNormal)
    {
        if (hasHit || hitCollider == null)
            return;

        hasHit = true;

        SpawnHitEffect(hitPoint, hitNormal);
        ApplyExplosionDamage(hitPoint, hitCollider);

        CombatPool.Release(gameObject);
    }

    private void ApplyExplosionDamage(
        Vector3 explosionPoint,
        Collider directHit)
    {
        if (damage <= 0) return;
        if (explosionRadius <= 0f)
        {
            if (directHit != null)
                ApplyDamageTo(directHit, damage, explosionPoint, false);
            return;
        }

        int overlapCount = Physics.OverlapSphereNonAlloc(
            explosionPoint,
            explosionRadius,
            SplashBuffer,
            hitMask,
            QueryTriggerInteraction.Collide
        );

        splashTargets.Clear();

        for (int index = 0; index < overlapCount; index++)
        {
            Collider hit = SplashBuffer[index];

            if (ShouldIgnore(hit))
                continue;

            Damageable target =
                hit.GetComponentInParent<Damageable>();

            if (target == null || splashTargets.Contains(target))
                continue;

            float distance = Vector3.Distance(
                explosionPoint,
                ColliderUtility.ClosestPointSafe(hit, explosionPoint)
            );

            float distancePercent =
                Mathf.Clamp01(distance / explosionRadius);

            float damagePercent = Mathf.Lerp(
                1f,
                minimumSplashDamagePercent,
                distancePercent
            );

            int finalDamage = Mathf.Max(
                1,
                Mathf.RoundToInt(damage * damagePercent)
            );

            ApplyDamageTo(hit, finalDamage, explosionPoint, true);
            splashTargets.Add(target);
        }

        Damageable directTarget = directHit != null
            ? directHit.GetComponentInParent<Damageable>()
            : null;

        if (directTarget != null &&
            !splashTargets.Contains(directTarget) &&
            !ShouldIgnore(directHit))
        {
            ApplyDamageTo(directHit, damage, explosionPoint, false);
        }
    }

    private void ExpireAtMaximumRange()
    {
        if (hasHit)
            return;

        hasHit = true;

        if (detonateAtMaxRange)
        {
            SpawnHitEffect(transform.position, -transform.forward);
            ApplyExplosionDamage(transform.position, null);
        }

        CombatPool.Release(gameObject);
    }

    private void ApplyDamageTo(
        Collider hitCollider,
        int amount,
        Vector3 hitPoint,
        bool isSplash)
    {
        if (hitCollider == null)
            return;

        Damageable target =
            hitCollider.GetComponentInParent<Damageable>();

        if (target == null)
            return;

        // Carrying the shooter and impact point through means the victim can
        // attribute the kill and present a directional hit.
        target.TakeDamage(new DamageInfo(
            amount,
            ownerObject,
            hasOwnerTeam,
            ownerTeam,
            hitPoint,
            transform.forward,
            isSplash));
    }

    private void SpawnHitEffect(
        Vector3 position,
        Vector3 normal)
    {
        // Ahead of the VFX early-out: the impact is still audible on a projectile
        // that has no hit effect assigned. Reached once per impact per client
        // from both the offline collision and the online ShowOnlineImpact path.
        ReleaseAudio.PlayAt(ReleaseAudioCue.MissileImpact, position, Random.Range(0.96f, 1.04f));

        if (hitEffectPrefab == null)
            return;

        Quaternion rotation =
            normal.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(normal)
            : Quaternion.identity;

        ProjectileLauncher.SpawnEffect(
            hitEffectPrefab,
            position,
            rotation,
            hitEffectLifetime);
    }

    private void ApplyTeamTracer()
    {
        if (!useTeamTintedTracer || !hasOwnerTeam)
            return;

        Color colour = ownerTeam == FortressTeam.Blue
            ? blueTracerColour
            : redTracerColour;

        foreach (TrailRenderer trail in GetComponentsInChildren<TrailRenderer>(true))
        {
            trail.sharedMaterial = GetTracerMaterial();
            trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(colour, 0.22f),
                    new GradientColorKey(colour * 0.65f, 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.82f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                }
            };
            trail.startWidth = Mathf.Max(trail.startWidth, 0.2f);
            trail.endWidth = Mathf.Max(trail.endWidth, 0.035f);
        }
    }

    private static Material GetTracerMaterial()
    {
        if (sharedTracerMaterial != null)
            return sharedTracerMaterial;

        sharedTracerMaterial =
            VfxParticleMaterial.ResolveInstance(null, Color.white);

        if (sharedTracerMaterial != null)
            sharedTracerMaterial.name = "Runtime Team Projectile Tracer";

        return sharedTracerMaterial;
    }
}
