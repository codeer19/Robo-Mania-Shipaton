using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight conversion-funnel markers. No analytics SDK and no personal data:
/// each step is written to the log with seconds since launch, e.g.
/// "[FUNNEL] play_clicked t=12.4", so a release build's console shows exactly
/// how far a session got and how long each stage took.
/// </summary>
public static class Funnel
{
    private static readonly HashSet<string> Seen = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Seen.Clear();

    /// <summary>Logs a step the first time it happens in this session.</summary>
    public static void Mark(string step)
    {
        if (Seen.Add(step)) Debug.Log("[FUNNEL] " + step + " t=" + Time.realtimeSinceStartup.ToString("F1"));
    }

    /// <summary>Logs a step every time it happens (per-match stages).</summary>
    public static void Event(string step)
    {
        Seen.Add(step);
        Debug.Log("[FUNNEL] " + step + " t=" + Time.realtimeSinceStartup.ToString("F1"));
    }

    // CombatEvents clears its handlers at SubsystemRegistration, before this runs.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookCombat()
    {
        CombatEvents.DamageApplied -= OnDamageApplied;
        CombatEvents.DamageApplied += OnDamageApplied;
    }

    // Offline matches only: online damage is resolved by the match authority and
    // never raises local combat events.
    private static void OnDamageApplied(Damageable victim, DamageInfo info)
    {
        if (Seen.Contains("first_damage") || info.Source == null) return;
        var arena = FortressDuelManager.ActiveArena;
        if (arena == null || arena.LocalPlayer == null) return;
        Transform player = arena.LocalPlayer.transform;
        if (info.Source.transform == player || info.Source.transform.IsChildOf(player)) Mark("first_damage");
    }
}
