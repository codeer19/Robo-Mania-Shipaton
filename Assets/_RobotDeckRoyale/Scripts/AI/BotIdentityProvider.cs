using System.Collections.Generic;
using UnityEngine;

/// <summary>How a fallback bot plays. Selected at random so repeat matches differ.</summary>
public enum BotBehaviourProfile
{
    Balanced,
    Aggressive,
    Defensive,
    Collector
}

/// <summary>Everything chosen for one fallback bot: who it appears to be, and how it plays.</summary>
public readonly struct BotIdentity
{
    public readonly string DisplayName;
    public readonly string SkinId;
    public readonly BotBehaviourProfile Profile;

    public BotIdentity(string displayName, string skinId, BotBehaviourProfile profile)
    {
        DisplayName = displayName;
        SkinId = skinId;
        Profile = profile;
    }
}

/// <summary>
/// Builds a varied identity for the AI opponent so fallback matches do not all
/// feel like the same robot.
///
/// Names are game-world flavoured and deliberately not presented as verified human
/// accounts - the UI is free to disclose that the opponent is AI, and nothing here
/// fabricates account IDs, clans or badges.
/// </summary>
public static class BotIdentityProvider
{
    // Two-part combinations give ~90 x 18 possibilities without a giant literal list,
    // plus a set of short standalone handles so not every name looks generated.
    private static readonly string[] Prefixes =
    {
        "Bolt", "Nova", "Gear", "Volt", "Pixel", "Hex", "Turbo", "Arc", "Zero", "Neon",
        "Byte", "Mecha", "Orbit", "Spark", "Iron", "Flux", "Robo", "Chrome", "Rivet", "Dash",
        "Cobalt", "Ember", "Quartz", "Delta", "Echo", "Fable", "Glitch", "Halo", "Ion", "Jolt",
        "Krypt", "Lumen", "Mag", "Nitro", "Onyx", "Pulse", "Quark", "Rust", "Solder", "Titan",
        "Umbra", "Vector", "Watt", "Xeno", "Yotta", "Zinc", "Amp", "Blitz", "Circuit", "Drift"
    };

    private static readonly string[] Suffixes =
    {
        "Fox", "Byte", "Ghost", "Kid", "Rush", "Drive", "Moss", "Nova", "Dash", "Bolt",
        "Rex", "Mint", "Jet", "Bee", "Nix", "Wing", "Cog", "Spin"
    };

    private static readonly string[] Standalone =
    {
        "Rivet", "Orbit7", "Cinder", "Static", "Wrench", "Pylon", "Cobble", "Vantage",
        "Tinker", "Lodestar", "Grommet", "Halcyon"
    };

    /// <summary>
    /// Only skins with an authored model are eligible. The cosmetic catalog also
    /// contains legacy colour-only entries (ironclad, solar, void) that have no
    /// mesh source, and handing one to the bot would spawn an unskinned robot.
    /// </summary>
    public static List<string> GetValidBotSkinIds()
    {
        var valid = new List<string>();
        FrontendAssets assets = FrontendAssets.Load();
        if (assets?.Skins == null)
        {
            return valid;
        }

        foreach (SparkSkin skin in assets.Skins)
        {
            if (skin != null && !string.IsNullOrEmpty(skin.Id) && skin.Source != null)
            {
                valid.Add(skin.Id);
            }
        }

        return valid;
    }

    /// <summary>
    /// Builds an identity, preferring a skin the player is not currently wearing so
    /// the two robots stay visually distinct. Falls back to matching only when the
    /// player owns the sole valid skin.
    /// </summary>
    public static BotIdentity Create(string playerSkinId)
    {
        if (!BotMatchPolicy.RequireBotMatch("BotIdentityProvider.Create")) return default;
        string name = CreateName();
        var profile = (BotBehaviourProfile)Random.Range(0, System.Enum.GetValues(typeof(BotBehaviourProfile)).Length);

        List<string> valid = GetValidBotSkinIds();
        if (valid.Count == 0)
        {
            Debug.LogWarning("[SKIN] No valid bot skins found; bot will use its authored default.");
            return new BotIdentity(name, string.Empty, profile);
        }

        var candidates = new List<string>(valid);
        if (candidates.Count > 1 && !string.IsNullOrEmpty(playerSkinId))
        {
            candidates.Remove(playerSkinId);
        }

        string skinId = candidates[Random.Range(0, candidates.Count)];
        Debug.Log($"[SKIN] Bot identity: name={name} skin={skinId} profile={profile} " +
                  $"(from {valid.Count} valid skins, player wearing {playerSkinId})");

        return new BotIdentity(name, skinId, profile);
    }

    private static string CreateName()
    {
        // Roughly one name in six is a standalone handle, so the set does not read
        // as uniformly two-part.
        if (Random.Range(0, 6) == 0)
        {
            return Standalone[Random.Range(0, Standalone.Length)];
        }

        return Prefixes[Random.Range(0, Prefixes.Length)] + Suffixes[Random.Range(0, Suffixes.Length)];
    }

    /// <summary>Total distinct names this can produce, for sanity-checking variety.</summary>
    public static int NameCombinationCount => Prefixes.Length * Suffixes.Length + Standalone.Length;
}
