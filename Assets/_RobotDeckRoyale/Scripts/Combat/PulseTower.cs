using UnityEngine;

/// <summary>
/// PULSE TOWER - area control. On a fixed rhythm it releases an energy pulse:
/// every enemy robot and Spidy inside the ring takes a little damage and a short,
/// mild slow. It never stuns, the slow never stacks, and the pulse is telegraphed
/// (the core charges up, then an expanding ring), so standing next to it is a
/// choice rather than a trap.
/// </summary>
public sealed class PulseTower : BuildStructure
{
    [Header("Pulse")]
    [SerializeField, Min(1f)] private float radius = 4.5f;
    [SerializeField, Min(0.5f)] private float interval = 2.5f;
    [SerializeField, Min(0)] private int damage = 9;
    [SerializeField, Range(0.4f, 1f)] private float slowMultiplier = 0.75f;
    [SerializeField, Min(0f)] private float slowSeconds = 1f;

    [Header("Presentation")]
    [SerializeField] private Transform spinner;
    [SerializeField] private float spinDegreesPerSecond = 70f;

    public float Radius => radius;
    public float Interval => interval;
    public int Damage => damage;
    public float SlowMultiplier => slowMultiplier;
    public float SlowSeconds => slowSeconds;

    private float nextPulse = -1f;
    private float lastPulseAt = -99f;
    private int shownPulses = -1;

    protected override float AreaRadius => radius;
    protected override AreaIndicator.Kind AreaKind => AreaIndicator.Kind.Pulse;

    protected override void SimulateOffline()
    {
        if (nextPulse < 0f) nextPulse = Time.time + interval * 0.5f;
        if (Time.time < nextPulse) return;
        nextPulse = Time.time + interval;
        DamageEnemiesWithin(radius, damage, true, slowMultiplier, slowSeconds);
        PlayPulse();
    }

    public override void ApplyOnline(in OnlineStructure data)
    {
        if (shownPulses >= 0 && data.Shots > shownPulses) PlayPulse();
        shownPulses = data.Shots;
    }

    private void PlayPulse()
    {
        lastPulseAt = Time.time;
        BuildVfx.Pulse(transform.position, radius, Glow);
        ReleaseAudio.PlayAt(ReleaseAudioCue.PulseTower, transform.position, Random.Range(0.96f, 1.04f));
    }

    protected override void Present()
    {
        if (spinner != null) spinner.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        // Flash on the pulse, then the core slowly charges toward the next one.
        float since = Time.time - lastPulseAt;
        float level = since < 0.25f ? 1.5f : Mathf.Lerp(0.55f, 1.05f, Mathf.Clamp01((since - 0.25f) / Mathf.Max(0.5f, interval - 0.25f)));
        if (!InCombat) level = 0.7f;
        TintGlow(level);
    }
}
