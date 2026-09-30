using Fusion;
using UnityEngine;

/// <summary>
/// TEMPORARY DIAGNOSTIC. Not gameplay.
///
/// The smallest possible replicated object: a NetworkObject, a NetworkTransform
/// and this script. No Spark, no skins, no animator, no PlayerRoot, no combat.
///
/// Its only job is to answer one question: does Runner.Spawn replicate between
/// two clients in this project's Fusion configuration at all? If the probe does
/// not replicate, the fault is in the Fusion/runner/scene setup and there is no
/// point looking at the Spark avatar. If it does replicate, Fusion is fine and
/// the fault is specific to NetworkedPlayerAvatar.
///
/// Delete once replication is proven.
/// </summary>
[DisallowMultipleComponent]
public sealed class FusionReplicationProbe : NetworkBehaviour
{
    [Networked] public int OwnerPlayerIndex { get; set; }

    public bool IsLocalProbe => HasStateAuthority;

    public override void Spawned()
    {
        // Captured here rather than in Awake: Fusion applies the spawn position
        // after the object is instantiated, so Awake would read the origin and both
        // probes would orbit the same point.
        spawnOrigin = transform.position;

        if (HasStateAuthority)
        {
            OwnerPlayerIndex = Runner.LocalPlayer.PlayerId;
        }

        // Colour by ownership so the two are distinguishable on screen at a glance.
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = HasStateAuthority
                ? new Color(0.2f, 0.9f, 0.3f)
                : new Color(1f, 0.35f, 0.2f);
        }

        Debug.Log(
            $"[PROBE] Spawned. id={Object.Id} local={HasStateAuthority} " +
            $"stateAuthority={Object.StateAuthority} inputAuthority={Object.InputAuthority} " +
            $"ownerIndex={OwnerPlayerIndex} runnerLocalPlayer={Runner.LocalPlayer}");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Debug.Log($"[PROBE] Despawned. local={HasStateAuthority}");
    }

    /// <summary>Moves the owned probe so movement replication is observable.</summary>
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
        {
            return;
        }

        // Slow orbit around the spawn point; purely so the remote client can see
        // whether transform updates arrive.
        float t = Runner.SimulationTime;
        transform.position = spawnOrigin + new Vector3(Mathf.Sin(t) * 3f, 0f, Mathf.Cos(t) * 3f);
    }

    /// <summary>
    /// Samples the probe's position once a second. On a remote probe a changing
    /// position is direct proof that transform updates are arriving, which spawn
    /// replication alone does not establish.
    /// </summary>
    private void Update()
    {
        if (Object == null || !Object.IsValid)
        {
            return;
        }

        if (Time.time < nextSampleTime)
        {
            return;
        }

        nextSampleTime = Time.time + 1f;

        Vector3 here = transform.position;
        float moved = Vector3.Distance(here, lastSample);
        lastSample = here;

        Debug.Log(
            $"[PROBE MOVE] id={Object.Id} local={HasStateAuthority} " +
            $"pos={here.ToString("F2")} movedSinceLastSample={moved:F3}");
    }

    private float nextSampleTime;
    private Vector3 lastSample;

    private Vector3 spawnOrigin;
}
