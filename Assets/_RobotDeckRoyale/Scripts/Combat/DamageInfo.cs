using UnityEngine;

/// <summary>
/// Everything a damage event needs to be presented and attributed.
///
/// The old API took a bare integer, so the attacker was thrown away at the point
/// of impact. That made kill attribution, directional damage feedback and any
/// future killfeed impossible without re-deriving information the shot already had.
/// </summary>
public readonly struct DamageInfo
{
    public readonly int Amount;

    /// <summary>The robot, turret or deployable that caused this damage.</summary>
    public readonly GameObject Source;

    public readonly bool HasTeam;
    public readonly FortressTeam Team;

    /// <summary>World point of impact. Zero when the damage had no impact point.</summary>
    public readonly Vector3 HitPoint;

    /// <summary>Normalised travel direction of the attack, if it had one.</summary>
    public readonly Vector3 Direction;

    public readonly bool IsSplash;

    public DamageInfo(
        int amount,
        GameObject source = null,
        bool hasTeam = false,
        FortressTeam team = FortressTeam.Blue,
        Vector3 hitPoint = default,
        Vector3 direction = default,
        bool isSplash = false)
    {
        Amount = amount;
        Source = source;
        HasTeam = hasTeam;
        Team = team;
        HitPoint = hitPoint;
        Direction = direction;
        IsSplash = isSplash;
    }

    public bool HasHitPoint => HitPoint.sqrMagnitude > 0.000001f;
}
