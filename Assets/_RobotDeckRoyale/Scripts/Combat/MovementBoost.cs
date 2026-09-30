using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A short, capped movement boost (Overdrive Pad). The mirror of
/// <see cref="MovementSlow"/>: one component per body, added on first use.
/// Driving through the pad again refreshes the timer rather than stacking, the
/// multiplier is capped, and the boost always runs out on its own. Only travel
/// speed changes; aim, firing and damage are untouched.
///
/// Offline it scales the player's controller or the bot's NavMesh speed. Online
/// the match authority replicates the timer and each client applies it to its
/// own robot through <see cref="SetReplicated"/>; remote bodies only show it.
/// </summary>
[DisallowMultipleComponent]
public sealed class MovementBoost : MonoBehaviour
{
    public const float MaximumMultiplier = 1.5f;

    private RobotPlayerController player;
    private NavMeshAgent agent;
    private float multiplier = 1f;
    private float until;
    private BoostTrail trail;
    private Damageable health;

    public bool IsBoosted => Time.time < until;

    /// <summary>Starts or refreshes a boost; true only when it was not already running.</summary>
    public static bool Apply(GameObject target, float speedMultiplier, float duration)
    {
        if (target == null || duration <= 0f) return false;
        var root = target.GetComponentInParent<FortressTarget>();
        var host = root != null ? root.gameObject : target;
        var boost = host.GetComponent<MovementBoost>();
        if (boost == null) boost = host.AddComponent<MovementBoost>();
        bool fresh = !boost.IsBoosted;
        boost.Begin(speedMultiplier, duration);
        return fresh;
    }

    /// <summary>Online: the replicated timer is the only authority on whether this body is boosted.</summary>
    public static void SetReplicated(GameObject target, bool boosted, float speedMultiplier)
    {
        if (target == null) return;
        var boost = target.GetComponent<MovementBoost>();
        if (!boosted)
        {
            if (boost != null && boost.IsBoosted) boost.End();
            return;
        }
        if (boost == null) boost = target.AddComponent<MovementBoost>();
        if (!boost.IsBoosted || !Mathf.Approximately(boost.multiplier, speedMultiplier)) boost.Begin(speedMultiplier, 0.25f);
        else boost.until = Time.time + 0.25f;
    }

    private void Awake()
    {
        player = GetComponent<RobotPlayerController>();
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Damageable>();
    }

    private void Begin(float speedMultiplier, float duration)
    {
        multiplier = Mathf.Clamp(speedMultiplier, 1f, MaximumMultiplier);
        until = Time.time + duration;
        if (player != null) player.BoostMultiplier = multiplier;
        if (agent != null) AgentSpeedScale.For(agent).SetBoost(multiplier);
        ShowTrail(true);
        enabled = true;
    }

    private void Update()
    {
        // A robot that dies mid-boost loses it (and its streak) before it respawns.
        if (Time.time >= until || (health != null && health.IsDead)) End();
    }

    private void End()
    {
        until = 0f;
        multiplier = 1f;
        if (player != null) player.BoostMultiplier = 1f;
        if (agent != null) AgentSpeedScale.For(agent).SetBoost(1f);
        ShowTrail(false);
        enabled = false;
    }

    private void OnDisable()
    {
        // Destroyed or deactivated mid-boost: never leave a body permanently fast.
        if (player != null) player.BoostMultiplier = 1f;
        if (agent != null && agent.GetComponent<AgentSpeedScale>() != null) AgentSpeedScale.For(agent).SetBoost(1f);
        // Ending normally lets the streak fade; a deactivated body drops it at once.
        if (trail != null) { trail.SetEmitting(false); if (!gameObject.activeInHierarchy) trail.Clear(); }
    }

    private void ShowTrail(bool on)
    {
        if (on && trail == null) trail = BuildVfx.CreateBoostTrail(transform);
        if (trail != null) trail.SetEmitting(on);
    }
}
