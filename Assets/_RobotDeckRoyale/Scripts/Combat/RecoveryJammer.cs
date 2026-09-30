using UnityEngine;

/// <summary>
/// RECOVERY JAMMER - anti-heal counterplay. An enemy robot inside its ring is
/// under NO HEAL (no Healing Pad, no regeneration), refreshed while it stays and
/// running out a few seconds after it leaves - long enough to spoil a retreat to
/// its own pad, which always sits in the other half. On a recharge the jammer
/// also fires an interference burst for a little damage. Movement and weapons
/// are never touched and the debuff always expires.
/// </summary>
public sealed class RecoveryJammer : BuildStructure
{
    [Header("Interference")]
    [SerializeField, Min(1f)] private float radius = 3.5f;
    [SerializeField, Min(1f), Tooltip("NO HEAL left after the robot leaves the ring.")] private float noHealSeconds = 5f;
    [SerializeField, Min(0.5f)] private float cooldown = 5f;
    [SerializeField, Min(0)] private int interferenceDamage = 6;

    [Header("Presentation")]
    [SerializeField] private Transform ring;

    public float Radius => radius;
    public float NoHealSeconds => noHealSeconds;
    public float Cooldown => cooldown;
    public int InterferenceDamage => interferenceDamage;

    private float readyAt, lastBurstAt = -99f;
    private int shownBursts = -1;

    protected override float AreaRadius => radius;
    protected override AreaIndicator.Kind AreaKind => AreaIndicator.Kind.Jammer;

    protected override void SimulateOffline()
    {
        Damageable victim = EnemyRobotWithin(radius);
        if (victim == null) return;
        victim.BlockHealing(noHealSeconds);        // refreshed every tick while inside
        if (Time.time < readyAt) return;
        readyAt = Time.time + cooldown;
        victim.TakeDamage(interferenceDamage, Team, gameObject);
        PlayBurst(victim.transform.position);
    }

    public override void ApplyOnline(in OnlineStructure data)
    {
        if (shownBursts >= 0 && data.Shots > shownBursts) PlayBurst(data.AimPosition);
        shownBursts = data.Shots;
    }

    private void PlayBurst(Vector3 victimPosition)
    {
        lastBurstAt = Time.time;
        BuildVfx.Jam(transform.position, radius, victimPosition, Glow);
    }

    protected override void Present()
    {
        float since = Time.time - lastBurstAt;
        if (ring != null) ring.Rotate(0f, (since < 0.6f ? -420f : -45f) * Time.deltaTime, 0f, Space.Self);
        float level = since < 0.3f ? 1.7f : 0.6f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 5.3f) * Mathf.Sin(Time.time * 2.1f));
        TintGlow(level);
    }
}
