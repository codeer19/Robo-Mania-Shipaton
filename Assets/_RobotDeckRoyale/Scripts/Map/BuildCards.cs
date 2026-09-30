using System;

/// <summary>
/// The six build cards and the three-card loadout a player takes into a match.
///
/// One place for card identity: menu, build HUD, placement, the bot and the
/// match authority all read names, order and the loadout rules from here, so a
/// card cannot be offered in one screen and refused in another for a different
/// reason. Tuning lives on each card's prefab component, not here.
/// </summary>
public static class BuildCards
{
    public const int LoadoutSize = 3;
    /// <summary>
    /// Each equipped card is one placement: three cards, three structures, no
    /// duplicates. Used by the HUD, the bot and the match authority alike.
    /// </summary>
    public const int MaxCopies = 1;

    /// <summary>Every card, in the order the loadout screen lists them.</summary>
    public static readonly BuildPlacementController.BuildableType[] All =
    {
        BuildPlacementController.BuildableType.Turret,
        BuildPlacementController.BuildableType.PulseTower,
        BuildPlacementController.BuildableType.HealingPad,
        BuildPlacementController.BuildableType.OverdrivePad,
        BuildPlacementController.BuildableType.RecoveryJammer,
        BuildPlacementController.BuildableType.MissileInterceptor,
    };

    /// <summary>Balanced first loadout: a defence, sustain and mobility.</summary>
    public static readonly BuildPlacementController.BuildableType[] DefaultLoadout =
    {
        BuildPlacementController.BuildableType.Turret,
        BuildPlacementController.BuildableType.HealingPad,
        BuildPlacementController.BuildableType.OverdrivePad,
    };

    public static bool IsDefined(BuildPlacementController.BuildableType type) =>
        (int)type >= 0 && (int)type < All.Length;

    public static string DisplayName(BuildPlacementController.BuildableType type) => type switch
    {
        BuildPlacementController.BuildableType.Turret => "TURRET",
        BuildPlacementController.BuildableType.PulseTower => "PULSE TOWER",
        BuildPlacementController.BuildableType.HealingPad => "HEALING PAD",
        BuildPlacementController.BuildableType.OverdrivePad => "OVERDRIVE PAD",
        BuildPlacementController.BuildableType.RecoveryJammer => "RECOVERY JAMMER",
        BuildPlacementController.BuildableType.MissileInterceptor => "MISSILE INTERCEPTOR",
        _ => type.ToString().ToUpperInvariant()
    };

    public static string Description(BuildPlacementController.BuildableType type) => type switch
    {
        BuildPlacementController.BuildableType.Turret => "Automatically attacks enemies.",
        BuildPlacementController.BuildableType.PulseTower => "Damages enemies around it with energy pulses.",
        BuildPlacementController.BuildableType.HealingPad => "Repairs your robot while you stand on it.",
        BuildPlacementController.BuildableType.OverdrivePad => "Drive through it for a short speed boost.",
        BuildPlacementController.BuildableType.RecoveryJammer => "Temporarily prevents enemy healing.",
        BuildPlacementController.BuildableType.MissileInterceptor => "Destroys incoming enemy missiles.",
        _ => string.Empty
    };

    /// <summary>Exactly three distinct, defined cards.</summary>
    public static bool IsValidLoadout(BuildPlacementController.BuildableType[] loadout)
    {
        if (loadout == null || loadout.Length != LoadoutSize) return false;
        for (int i = 0; i < loadout.Length; i++)
        {
            if (!IsDefined(loadout[i])) return false;
            for (int j = 0; j < i; j++) if (loadout[j] == loadout[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// A loadout as one int for the network and the save file: 4 bits per slot and
    /// a marker bit, so 0 (never written) is distinguishable from Turret in every slot.
    /// </summary>
    public static int Pack(BuildPlacementController.BuildableType[] loadout)
    {
        if (!IsValidLoadout(loadout)) loadout = DefaultLoadout;
        return 1 << 12 | (int)loadout[0] | (int)loadout[1] << 4 | (int)loadout[2] << 8;
    }

    /// <summary>Unpacks and validates; anything malformed becomes the default loadout.</summary>
    public static BuildPlacementController.BuildableType[] Unpack(int packed)
    {
        var loadout = new BuildPlacementController.BuildableType[LoadoutSize];
        if ((packed & 1 << 12) == 0) return (BuildPlacementController.BuildableType[])DefaultLoadout.Clone();
        for (int i = 0; i < LoadoutSize; i++) loadout[i] = (BuildPlacementController.BuildableType)(packed >> (4 * i) & 0xF);
        return IsValidLoadout(loadout) ? loadout : (BuildPlacementController.BuildableType[])DefaultLoadout.Clone();
    }

    public static bool Contains(BuildPlacementController.BuildableType[] loadout, BuildPlacementController.BuildableType type) =>
        loadout != null && Array.IndexOf(loadout, type) >= 0;

    /// <summary>Save-file form: "0,2,3".</summary>
    public static string Serialize(BuildPlacementController.BuildableType[] loadout)
    {
        if (!IsValidLoadout(loadout)) loadout = DefaultLoadout;
        return $"{(int)loadout[0]},{(int)loadout[1]},{(int)loadout[2]}";
    }

    public static BuildPlacementController.BuildableType[] Parse(string saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return (BuildPlacementController.BuildableType[])DefaultLoadout.Clone();
        string[] parts = saved.Split(',');
        if (parts.Length != LoadoutSize) return (BuildPlacementController.BuildableType[])DefaultLoadout.Clone();
        var loadout = new BuildPlacementController.BuildableType[LoadoutSize];
        for (int i = 0; i < LoadoutSize; i++)
        {
            if (!int.TryParse(parts[i], out int value)) return (BuildPlacementController.BuildableType[])DefaultLoadout.Clone();
            loadout[i] = (BuildPlacementController.BuildableType)value;
        }
        return IsValidLoadout(loadout) ? loadout : (BuildPlacementController.BuildableType[])DefaultLoadout.Clone();
    }
}
