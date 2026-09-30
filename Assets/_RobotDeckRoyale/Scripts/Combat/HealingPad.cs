using UnityEngine;

/// <summary>
/// HEALING PAD - sustain you have to stand on.
///
/// Heals only its owner's robot, only while that robot is inside the pad's
/// ring, from a finite pool; once the pool is spent the pad goes dark. It never
/// heals the base, enemies or Spidys, stops the moment the robot steps off or the
/// pad is destroyed, and a Recovery Jammer's NO HEAL blocks it (enforced in
/// <see cref="Damageable.Heal"/> offline and by the match authority online).
/// </summary>
public sealed class HealingPad : BuildStructure
{
    [Header("Healing")]
    [SerializeField, Min(0.5f)] private float radius = 1.8f;
    [SerializeField, Min(0.5f)] private float healPerSecond = 8f;
    [SerializeField, Min(1)] private int healPool = 120;

    [Header("Presentation")]
    [SerializeField] private Transform emitter;

    public float Radius => radius;
    public float HealPerSecond => healPerSecond;
    public int HealPool => healPool;

    private Damageable ownerRobot;
    private float accumulator, lastHealAt = -99f, nextOwnerLookup, nextChimeAt;
    private int healed;

    protected override float AreaRadius => radius;
    protected override AreaIndicator.Kind AreaKind => AreaIndicator.Kind.Heal;

    protected override void SimulateOffline()
    {
        if (healed >= healPool) return;
        Damageable robot = ResolveOwnerRobot();
        if (robot == null || robot.IsDead || robot.CurrentHealth >= robot.MaxHealth || robot.HealingBlocked)
        { accumulator = 0f; return; }
        if (FlatDistanceSquared(robot.transform.position, transform.position) > radius * radius) { accumulator = 0f; return; }
        accumulator += healPerSecond * 0.1f;
        int amount = Mathf.Min(Mathf.FloorToInt(accumulator), healPool - healed);
        if (amount <= 0) return;
        accumulator -= amount;
        int before = robot.CurrentHealth;
        robot.Heal(amount);
        if (robot.CurrentHealth <= before) return;
        healed += robot.CurrentHealth - before;
        MarkHealed();
    }

    // A soft chime when repair starts (not on every tick).
    private void MarkHealed()
    {
        if (Time.time - lastHealAt > 0.5f && Time.time >= nextChimeAt)
        {
            nextChimeAt = Time.time + 1.2f;
            ReleaseAudio.PlayAt(ReleaseAudioCue.HealPulse, transform.position);
        }
        lastHealAt = Time.time;
    }

    /// <summary>Online: the authority's healed total, and whether it rose this frame.</summary>
    public override void ApplyOnline(in OnlineStructure data)
    {
        if (data.Shots > healed) MarkHealed();
        healed = data.Shots;
    }

    // The owner's robot, from the target registry (player on Blue, bot on Red).
    private Damageable ResolveOwnerRobot()
    {
        if (ownerRobot != null || Time.time < nextOwnerLookup) return ownerRobot;
        nextOwnerLookup = Time.time + 1f;
        var targets = FortressTarget.Active;
        for (int i = 0; i < targets.Count; i++)
            if (targets[i] != null && targets[i].Team == Team && targets[i].IsCombatRobot) { ownerRobot = targets[i].Health; break; }
        return ownerRobot;
    }

    // Idle glow, a brighter pulse while healing, dark once the pool is spent.
    protected override void Present()
    {
        bool healing = Time.time - lastHealAt < 0.35f;
        float level = healed >= healPool ? 0.12f
            : healing ? 1.15f + 0.3f * Mathf.Sin(Time.time * 14f)
            : 0.7f + 0.1f * Mathf.Sin(Time.time * 2.2f);
        TintGlow(level);
        if (emitter != null && healed < healPool)
        {
            emitter.Rotate(0f, (healing ? 160f : 30f) * Time.deltaTime, 0f, Space.Self);
            float bob = healing ? 0.05f * Mathf.Sin(Time.time * 10f) : 0f;
            var p = emitter.localPosition; p.y = emitterBaseY + bob; emitter.localPosition = p;
        }
    }

    private float emitterBaseY;
    protected override void Start()
    {
        base.Start();
        if (emitter != null) emitterBaseY = emitter.localPosition.y;
    }
}
