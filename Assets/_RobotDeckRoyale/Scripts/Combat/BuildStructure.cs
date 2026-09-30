using UnityEngine;

/// <summary>
/// Shared base for the placeable build cards (Pulse Tower, Healing Pad, Overdrive Pad,
/// Recovery Jammer, Missile Interceptor).
///
/// Offline (bot matches) the component is the authority and runs its own effect
/// on a fixed tick. Online the match authority simulates the card from the same
/// tuning and this component only presents the replicated record handed to it by
/// <see cref="OnlineStructureView"/>. Enemies are read from the
/// <see cref="FortressTarget.Active"/> registry - never a scene search.
/// </summary>
[RequireComponent(typeof(Damageable))]
public abstract class BuildStructure : MonoBehaviour
{
    [SerializeField, Min(0.05f), Tooltip("Seconds between offline effect checks.")]
    private float tickInterval = 0.1f;
    [SerializeField, Min(0.3f), Tooltip("World height of the health bar above the pivot.")]
    private float healthBarHeight = 2.2f;

    protected FortressDuelManager duel;
    protected FortressTarget identity;
    protected Damageable health;
    protected MaterialPropertyBlock block;
    private Renderer[] glowRenderers;
    private int appliedGlowKey = int.MinValue;
    private float nextTick;
    private bool onlineView;
    private bool preview;

    public bool IsOnlineView => onlineView;
    protected FortressTeam Team => identity != null ? identity.Team : FortressTeam.Blue;
    protected bool Friendly => BuildItemPresentation.IsFriendly(identity);
    protected Color Glow => BuildItemPresentation.GlowColour(Friendly);
    protected bool InCombat => duel != null && duel.CurrentPhase == FortressDuelPhase.Combat;

    /// <summary>Online: presentation only; the authority runs the effect.</summary>
    public void SetOnlineView(bool view) => onlineView = view;

    /// <summary>Placement ghost: never acts, never shows a health bar.</summary>
    public void SetPreview() { preview = true; enabled = false; }

    /// <summary>Online: the replicated record for this structure, every frame.</summary>
    public virtual void ApplyOnline(in OnlineStructure data) { }

    /// <summary>The gameplay radius shown as a translucent ground circle (0: none).</summary>
    protected virtual float AreaRadius => 0f;
    protected virtual AreaIndicator.Kind AreaKind => AreaIndicator.Kind.Pulse;

    protected virtual void Awake()
    {
        identity = GetComponent<FortressTarget>();
        health = GetComponent<Damageable>();
        block = new MaterialPropertyBlock();
        // Every renderer with a glow slot (body plus moving parts such as the
        // Pulse Tower core or the Jammer ring) is tinted together.
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        int count = 0;
        foreach (var r in renderers) if (r.sharedMaterials.Length > 1) count++;
        glowRenderers = new Renderer[count];
        count = 0;
        foreach (var r in renderers) if (r.sharedMaterials.Length > 1) glowRenderers[count++] = r;
        BuildItemPresentation.EnsureHealthBar(transform, healthBarHeight);
        // The area circle comes from the same tuning the effect uses, so the two
        // always agree; it also shows on the placement ghost before placing.
        if (AreaRadius > 0f) AreaIndicator.Create(transform, AreaKind, AreaRadius);
    }

    protected virtual void Start() => duel = FortressDuelManager.ActiveArena;

    private void Update()
    {
        if (preview) return;
        if (!onlineView && MatchSessionContext.Type != MatchType.HumanOnline && InCombat && Time.time >= nextTick)
        {
            nextTick = Time.time + tickInterval;
            SimulateOffline();
        }
        Present();
    }

    /// <summary>Offline authority tick (bot matches only).</summary>
    protected abstract void SimulateOffline();

    /// <summary>Per-frame visuals for both modes.</summary>
    protected virtual void Present() { }

    /// <summary>Enemy robot (player or duel bot) inside a flat radius, or null.</summary>
    protected Damageable EnemyRobotWithin(float radius)
    {
        float reach = radius * radius;
        var targets = FortressTarget.Active;
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target == null || target.Team == Team || !target.IsCombatRobot) continue;
            var victim = target.Health;
            if (victim == null || victim.IsDead || FlatDistanceSquared(target.transform.position, transform.position) > reach) continue;
            return victim;
        }
        return null;
    }

    /// <summary>Whether an enemy robot or Spidy is inside the radius.</summary>
    protected bool AnyEnemyWithin(float radius, bool includeSpidys)
    {
        float reach = radius * radius;
        var targets = FortressTarget.Active;
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target == null || target.Team == Team) continue;
            if (!target.IsCombatRobot && !(includeSpidys && target.IsSpidy)) continue;
            var victim = target.Health;
            if (victim == null || victim.IsDead) continue;
            if (FlatDistanceSquared(target.transform.position, transform.position) <= reach) return true;
        }
        return false;
    }

    /// <summary>Damages every enemy robot/Spidy inside the radius; returns how many were hit.</summary>
    protected int DamageEnemiesWithin(float radius, int damage, bool includeSpidys, float slowMultiplier = 1f, float slowSeconds = 0f)
    {
        float reach = radius * radius;
        int hits = 0;
        var targets = FortressTarget.Active;
        // Backwards: a kill can disable a target and remove it from the registry.
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (i >= targets.Count) continue;
            var target = targets[i];
            if (target == null || target.Team == Team) continue;
            if (!target.IsCombatRobot && !(includeSpidys && target.IsSpidy)) continue;
            var victim = target.Health;
            if (victim == null || victim.IsDead || FlatDistanceSquared(target.transform.position, transform.position) > reach) continue;
            if (slowSeconds > 0f) MovementSlow.Apply(target.gameObject, slowMultiplier, slowSeconds);
            victim.TakeDamage(damage, Team, gameObject);
            hits++;
        }
        return hits;
    }

    /// <summary>Team glow at an intensity; skipped when nothing visible would change.</summary>
    protected void TintGlow(float intensity)
    {
        bool friendly = Friendly;
        int key = (friendly ? 1 : 0) + Mathf.RoundToInt(intensity * 40f) * 2;
        if (key == appliedGlowKey) return;
        appliedGlowKey = key;
        Color colour = BuildItemPresentation.GlowColour(friendly) * intensity;
        colour.a = 1f;
        for (int i = 0; i < glowRenderers.Length; i++)
            BuildItemPresentation.Tint(glowRenderers[i], 1, colour, block);
    }

    protected static float FlatDistanceSquared(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).sqrMagnitude; }
}
