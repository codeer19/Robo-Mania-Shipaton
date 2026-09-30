using UnityEngine;

/// <summary>
/// MISSILE INTERCEPTOR - anti-projectile defence. It watches the air around it
/// and shoots down one incoming ENEMY missile at a time, then needs a recharge,
/// so a sustained volley still gets through. It never attacks robots, the base
/// or Spidys, and never touches friendly missiles. Offline it reads the short
/// in-flight list on <see cref="BasicProjectile.Active"/>; online the match
/// authority removes the replicated missile itself.
/// </summary>
public sealed class MissileInterceptor : BuildStructure
{
    [Header("Interception")]
    [SerializeField, Min(2f)] private float range = 7.5f;
    [SerializeField, Min(0.3f)] private float cooldown = 2.2f;

    [Header("Presentation")]
    [SerializeField] private Transform head;
    [SerializeField] private float muzzleHeight = 1.7f;

    public float Range => range;
    public float Cooldown => cooldown;
    public float MuzzleHeight => muzzleHeight;

    private float readyAt, lastShotAt = -99f;
    private int shownShots = -1;
    private Quaternion targetHeading;
    private bool hasHeading;

    protected override float AreaRadius => range;
    protected override AreaIndicator.Kind AreaKind => AreaIndicator.Kind.Interceptor;

    protected override void SimulateOffline()
    {
        if (Time.time < readyAt) return;
        var missiles = BasicProjectile.Active;
        BasicProjectile best = null;
        float nearest = range * range;
        for (int i = 0; i < missiles.Count; i++)
        {
            var missile = missiles[i];
            if (missile == null || !missile.InFlight || missile.Team == Team) continue;
            float d = FlatDistanceSquared(missile.transform.position, transform.position);
            if (d > nearest) continue;
            nearest = d; best = missile;
        }
        if (best == null) return;
        readyAt = Time.time + cooldown;
        Vector3 point = best.transform.position;
        best.Intercept();
        PlayIntercept(point);
    }

    public override void ApplyOnline(in OnlineStructure data)
    {
        if (shownShots >= 0 && data.Shots > shownShots) PlayIntercept(data.AimPosition);
        shownShots = data.Shots;
    }

    private void PlayIntercept(Vector3 point)
    {
        lastShotAt = Time.time;
        Vector3 flat = point - transform.position; flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f) { targetHeading = Quaternion.LookRotation(flat); hasHeading = true; }
        BuildVfx.Beam(transform.position + Vector3.up * muzzleHeight, point, Glow);
        ReleaseAudio.PlayAt(ReleaseAudioCue.Intercept, point, Random.Range(0.97f, 1.05f));
    }

    protected override void Present()
    {
        if (head != null)
        {
            // Snaps onto the intercept, otherwise a slow radar sweep.
            if (hasHeading && Time.time - lastShotAt < 0.8f)
                head.rotation = Quaternion.RotateTowards(head.rotation, targetHeading, 900f * Time.deltaTime);
            else
                head.Rotate(0f, 55f * Time.deltaTime, 0f, Space.World);
        }
        float since = Time.time - lastShotAt;
        float level = since < 0.2f ? 1.7f : Time.time < readyAt && !IsOnlineView ? 0.45f : 0.85f;
        TintGlow(level);
    }
}
