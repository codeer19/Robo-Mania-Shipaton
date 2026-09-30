using UnityEngine;

/// <summary>
/// OVERDRIVE PAD - a speed strip you drive across.
///
/// Its owner's robot crossing the pad gets a short movement boost. Driving over it
/// again refreshes the timer instead of stacking, the multiplier is capped, and
/// the boost always runs out on its own. It never affects enemies or Spidys and
/// never changes aim, firing or damage. Offline this component is the authority;
/// online the match authority applies the same tuning (see
/// NetworkedMatchState.TickOverdrive) and every client shows it.
/// </summary>
public sealed class OverdrivePad : BuildStructure
{
    [Header("Boost")]
    [SerializeField, Min(0.5f)] private float radius = 2.3f;
    [SerializeField, Range(1f, 1.5f)] private float speedMultiplier = 1.3f;
    [SerializeField, Min(0.2f)] private float boostSeconds = 2f;

    [Header("Presentation")]
    [SerializeField] private Transform chevrons;

    public float Radius => radius;
    public float SpeedMultiplier => speedMultiplier;
    public float BoostSeconds => boostSeconds;

    private Damageable ownerRobot;
    private float nextOwnerLookup;
    private float lastBoostAt = -99f;
    private int shownBoosts = -1;
    private float chevronBaseY;

    protected override float AreaRadius => radius;
    protected override AreaIndicator.Kind AreaKind => AreaIndicator.Kind.Overdrive;

    protected override void Start()
    {
        base.Start();
        if (chevrons != null) chevronBaseY = chevrons.localPosition.y;
    }

    protected override void SimulateOffline()
    {
        Damageable robot = ResolveOwnerRobot();
        if (robot == null || robot.IsDead) return;
        if (FlatDistanceSquared(robot.transform.position, transform.position) > radius * radius) return;
        if (MovementBoost.Apply(robot.gameObject, speedMultiplier, boostSeconds)) PlayBoost();
    }

    /// <summary>Online: Shots counts fresh boosts, so each one bursts once here.</summary>
    public override void ApplyOnline(in OnlineStructure data)
    {
        if (shownBoosts >= 0 && data.Shots > shownBoosts) PlayBoost();
        shownBoosts = data.Shots;
    }

    private void PlayBoost()
    {
        lastBoostAt = Time.time;
        // One line per fresh boost (never per frame), like the Jammer and Interceptor events.
        Debug.Log($"[OVERDRIVE] {Team} boost x{speedMultiplier:F2} for {boostSeconds:F1}s");
        BuildVfx.Boost(transform.position, radius);
        ReleaseAudio.PlayAt(ReleaseAudioCue.OverdriveBoost, transform.position, Random.Range(0.96f, 1.04f));
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

    // A steady idle glow that races forward along the chevrons, flaring on a boost.
    protected override void Present()
    {
        float since = Time.time - lastBoostAt;
        float level = since < 0.3f ? 1.6f - since * 2f : 0.75f + 0.2f * Mathf.Sin(Time.time * 5f);
        if (!InCombat) level = 0.7f;
        TintGlow(level);
        if (chevrons != null)
        {
            // The chevron strip leans forward and back: reads as "go this way".
            var p = chevrons.localPosition;
            p.z = 0.05f * Mathf.Sin(Time.time * 5f);
            p.y = chevronBaseY;
            chevrons.localPosition = p;
        }
    }
}
