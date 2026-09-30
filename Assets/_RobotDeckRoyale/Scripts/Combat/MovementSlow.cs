using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A short, capped movement slow (Pulse Tower). One component per body, added on
/// first use. It never stuns: the body keeps full control of aim, firing and
/// abilities, only its travel speed is reduced, and the slow always expires on
/// its own. Re-applying refreshes the timer rather than stacking.
///
/// Offline it scales the player's controller or the agent's NavMesh speed.
/// Online the match authority replicates the timer and each client applies it
/// to its own robot through <see cref="SetReplicated"/>; remote bodies only show
/// the effect.
/// </summary>
[DisallowMultipleComponent]
public sealed class MovementSlow : MonoBehaviour
{
    private RobotPlayerController player;
    private NavMeshAgent agent;
    private float multiplier = 1f;
    private float until;
    private ParticleSystem sparks;

    public bool IsSlowed => Time.time < until;

    public static void Apply(GameObject target, float speedMultiplier, float duration)
    {
        if (target == null || duration <= 0f) return;
        var root = target.GetComponentInParent<FortressTarget>();
        var host = root != null ? root.gameObject : target;
        var slow = host.GetComponent<MovementSlow>();
        if (slow == null) slow = host.AddComponent<MovementSlow>();
        slow.Begin(speedMultiplier, duration);
    }

    /// <summary>Online: the replicated timer is the only authority on whether this body is slowed.</summary>
    public static void SetReplicated(GameObject target, bool slowed, float speedMultiplier)
    {
        if (target == null) return;
        var slow = target.GetComponent<MovementSlow>();
        if (!slowed)
        {
            if (slow != null && slow.IsSlowed) slow.End();
            return;
        }
        if (slow == null) slow = target.AddComponent<MovementSlow>();
        if (!slow.IsSlowed || !Mathf.Approximately(slow.multiplier, speedMultiplier)) slow.Begin(speedMultiplier, 0.25f);
        else slow.until = Time.time + 0.25f;
    }

    private void Awake()
    {
        player = GetComponent<RobotPlayerController>();
        agent = GetComponent<NavMeshAgent>();
    }

    private void Begin(float speedMultiplier, float duration)
    {
        multiplier = Mathf.Clamp(speedMultiplier, 0.2f, 1f);
        until = Time.time + duration;
        if (player != null) player.SpeedMultiplier = multiplier;
        // Shared with the Overdrive boost, so overlapping effects unwind cleanly.
        if (agent != null) AgentSpeedScale.For(agent).SetSlow(multiplier);
        ShowSparks(true);
        enabled = true;
    }

    private void Update()
    {
        if (Time.time >= until) End();
    }

    private void End()
    {
        until = 0f;
        multiplier = 1f;
        if (player != null) player.SpeedMultiplier = 1f;
        if (agent != null) AgentSpeedScale.For(agent).SetSlow(1f);
        ShowSparks(false);
        enabled = false;
    }

    private void OnDisable()
    {
        // Destroyed or deactivated mid-slow: never leave a body permanently slow.
        if (player != null) player.SpeedMultiplier = 1f;
        if (agent != null && agent.GetComponent<AgentSpeedScale>() != null) AgentSpeedScale.For(agent).SetSlow(1f);
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // A few cyan sparks crawling over the body while slowed. Tiny and capped.
    private void ShowSparks(bool on)
    {
        if (on && sparks == null) sparks = BuildVfx.CreateStatusSparks(transform);
        if (sparks == null) return;
        if (on && !sparks.isPlaying) sparks.Play(true);
        else if (!on) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
