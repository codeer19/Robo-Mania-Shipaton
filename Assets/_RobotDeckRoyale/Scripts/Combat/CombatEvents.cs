using System;
using UnityEngine;

/// <summary>
/// Presentation-only broadcast points for combat. Camera work, HUD, killfeed,
/// haptics and the audio pass that has not been authored yet can subscribe here
/// instead of each system having to discover every Damageable in the scene.
///
/// Nothing here affects simulation, so it is safe for these to remain local when
/// the project moves to Photon Fusion: the authority applies the damage, and each
/// client raises its own presentation events from replicated state.
/// </summary>
public static class CombatEvents
{
    /// <summary>Raised on every applied hit, after health has changed.</summary>
    public static event Action<Damageable, DamageInfo> DamageApplied;

    /// <summary>
    /// Raised when a Damageable dies. The attacker may be null for environmental
    /// or unattributed deaths.
    /// </summary>
    public static event Action<Damageable, GameObject> Killed;

    public static void RaiseDamageApplied(Damageable victim, in DamageInfo info)
    {
        DamageApplied?.Invoke(victim, info);
    }

    public static void RaiseKilled(Damageable victim, GameObject attacker)
    {
        Killed?.Invoke(victim, attacker);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        DamageApplied = null;
        Killed = null;
    }
}
