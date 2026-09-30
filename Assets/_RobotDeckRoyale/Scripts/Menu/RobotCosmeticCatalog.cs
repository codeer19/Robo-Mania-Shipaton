using System;
using System.Collections.Generic;
using UnityEngine;

public enum RobotCosmeticCategory
{
    Skin,
    ColorCombo,
    EyeShape,
    ScreenEffect
}

public enum CosmeticPurchaseCurrency
{
    Coins,
    Premium
}

[Serializable]
public sealed class RobotCosmeticDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RobotCosmeticCategory Category { get; }
    public CosmeticPurchaseCurrency Currency { get; }
    public int CoinPrice { get; }
    public string StoreProductId { get; }
    public bool DefaultUnlocked { get; }
    public Color Primary { get; }
    public Color Secondary { get; }
    public Color Accent { get; }

    public RobotCosmeticDefinition(
        string id,
        string displayName,
        string description,
        RobotCosmeticCategory category,
        int coinPrice,
        bool defaultUnlocked,
        Color primary,
        Color secondary,
        Color accent,
        CosmeticPurchaseCurrency currency = CosmeticPurchaseCurrency.Coins,
        string storeProductId = "")
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        CoinPrice = Mathf.Max(0, coinPrice);
        DefaultUnlocked = defaultUnlocked;
        Primary = primary;
        Secondary = secondary;
        Accent = accent;
        Currency = currency;
        StoreProductId = storeProductId ?? string.Empty;
    }
}

/// <summary>
/// The shipping cosmetic catalogue. All entries currently use earned coins. Premium entries can
/// later provide a RevenueCat product identifier without changing the profile or menu APIs.
/// </summary>
public static class RobotCosmeticCatalog
{
    private static readonly List<RobotCosmeticDefinition> Items = new List<RobotCosmeticDefinition>
    {
        new RobotCosmeticDefinition("skin_core", "SPARK DEFAULT", "Spark's original cream, pink and mint finish.",
            RobotCosmeticCategory.Skin, 0, true, Hex("#F1E8D6"), Hex("#F33E8C"), Hex("#A4E4CB")),
        new RobotCosmeticDefinition("spark_robo_ranger", "ROBO RANGER", "The authored ranger hat, bandana and optics.",
            RobotCosmeticCategory.Skin, 550, false, Hex("#C89557"), Hex("#B23B31"), Hex("#FFD268")),
        new RobotCosmeticDefinition("spark_space_explorer", "SPACE EXPLORER", "Ready to explore, with backpack and antenna.",
            RobotCosmeticCategory.Skin, 850, false, Hex("#DCEAF4"), Hex("#246AC7"), Hex("#78EDFF")),
        new RobotCosmeticDefinition("spark_ninja", "NINJA", "Stealth armour, scarf and authored ninja eyes.",
            RobotCosmeticCategory.Skin, 1100, false, Hex("#252438"), Hex("#B83457"), Hex("#EDD45A")),
        new RobotCosmeticDefinition("spark_king", "KING SPARK", "A royal crown and cape for the arena.",
            RobotCosmeticCategory.Skin, 1100, false, Hex("#6B3BC7"), Hex("#DB376D"), Hex("#FFD268")),
        new RobotCosmeticDefinition("skin_ironclad", "IRONCLAD", "Dark forged plating with reinforced orange trim.",
            RobotCosmeticCategory.Skin, 550, false, Hex("#485065"), Hex("#171B26"), Hex("#FF8A32")),
        new RobotCosmeticDefinition("skin_solar", "SOLAR GUARD", "High-energy gold armour made for arena champions.",
            RobotCosmeticCategory.Skin, 850, false, Hex("#E3A827"), Hex("#6C271F"), Hex("#FFF08A")),
        new RobotCosmeticDefinition("skin_void", "VOID RUNNER", "Stealth plating with an ultraviolet power core.",
            RobotCosmeticCategory.Skin, 1100, false, Hex("#34255D"), Hex("#111222"), Hex("#B66CFF")),

        new RobotCosmeticDefinition("color_blue_orange", "PURPLE POP", "The original purple and orange Robo Mania colours.",
            RobotCosmeticCategory.ColorCombo, 0, true, Hex("#7434D7"), Hex("#FF7628"), Hex("#FFE0A6")),
        new RobotCosmeticDefinition("color_magma", "MAGMA", "Hot red armour with molten amber highlights.",
            RobotCosmeticCategory.ColorCombo, 280, false, Hex("#E43D32"), Hex("#FF8B22"), Hex("#FFE06A")),
        new RobotCosmeticDefinition("color_cyber_mint", "CYBER MINT", "Deep graphite panels powered by mint neon.",
            RobotCosmeticCategory.ColorCombo, 340, false, Hex("#263B42"), Hex("#31D6A6"), Hex("#A0FFE1")),
        new RobotCosmeticDefinition("color_royal", "ROYAL VOLT", "Royal violet with sharp electric-blue details.",
            RobotCosmeticCategory.ColorCombo, 420, false, Hex("#6B3BC7"), Hex("#245AD9"), Hex("#D799FF")),
        new RobotCosmeticDefinition("color_frost", "FROST BITE", "Ice-white panels and a saturated blue power core.",
            RobotCosmeticCategory.ColorCombo, 480, false, Hex("#DCEAF4"), Hex("#2477BD"), Hex("#8BF5FF")),

        new RobotCosmeticDefinition("eyes_round", "SCOUT OPTICS", "Friendly twin optical sensors.",
            RobotCosmeticCategory.EyeShape, 0, true, Hex("#72EAFF"), Color.white, Hex("#167DFF")),
        new RobotCosmeticDefinition("eyes_visor", "TACTICAL VISOR", "A focused combat visor with a crisp scan glow.",
            RobotCosmeticCategory.EyeShape, 220, false, Hex("#50E7FF"), Hex("#B7FAFF"), Hex("#1667E8")),
        new RobotCosmeticDefinition("eyes_tri", "TRI-SIGHT", "Three-point targeting optics.",
            RobotCosmeticCategory.EyeShape, 290, false, Hex("#FFB13B"), Hex("#FFF2A0"), Hex("#EF512F")),
        new RobotCosmeticDefinition("eyes_x", "X-RAY", "An aggressive crossed sensor array.",
            RobotCosmeticCategory.EyeShape, 360, false, Hex("#FF5475"), Hex("#FFC0CF"), Hex("#9C183F")),

        new RobotCosmeticDefinition("fx_none", "CLEAN SIGNAL", "No additional arena aura.",
            RobotCosmeticCategory.ScreenEffect, 0, true, Hex("#55D8FF"), Hex("#1672DD"), Color.white),
        new RobotCosmeticDefinition("fx_scanline", "SCAN PULSE", "A measured tactical scan rolls around the frame.",
            RobotCosmeticCategory.ScreenEffect, 260, false, Hex("#36D9FF"), Hex("#1358B8"), Color.white),
        new RobotCosmeticDefinition("fx_hologrid", "HOLO GRID", "Orbiting holographic data particles.",
            RobotCosmeticCategory.ScreenEffect, 380, false, Hex("#4AFFD5"), Hex("#157F80"), Hex("#C4FFF3")),
        new RobotCosmeticDefinition("fx_energy_rain", "ION RAIN", "Rising charged motes and an electric floor ring.",
            RobotCosmeticCategory.ScreenEffect, 520, false, Hex("#9A62FF"), Hex("#3A1B91"), Hex("#E5C8FF")),
        new RobotCosmeticDefinition("fx_prismatic", "PRISM CORE", "Rare multi-spectrum champion energy.",
            RobotCosmeticCategory.ScreenEffect, 700, false, Hex("#FF6DB3"), Hex("#43D6FF"), Hex("#FFF087"))
    };

    private static readonly Dictionary<string, RobotCosmeticDefinition> ById = BuildLookup();

    public static IReadOnlyList<RobotCosmeticDefinition> All => Items;

    public static RobotCosmeticDefinition Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        ById.TryGetValue(id, out RobotCosmeticDefinition definition);
        return definition;
    }

    public static string GetDefaultId(RobotCosmeticCategory category)
    {
        for (int index = 0; index < Items.Count; index++)
        {
            RobotCosmeticDefinition item = Items[index];
            if (item.Category == category && item.DefaultUnlocked)
            {
                return item.Id;
            }
        }

        return string.Empty;
    }

    public static List<RobotCosmeticDefinition> GetItems(RobotCosmeticCategory category)
    {
        List<RobotCosmeticDefinition> result = new List<RobotCosmeticDefinition>();
        for (int index = 0; index < Items.Count; index++)
        {
            if (Items[index].Category == category)
            {
                result.Add(Items[index]);
            }
        }

        return result;
    }

    private static Dictionary<string, RobotCosmeticDefinition> BuildLookup()
    {
        Dictionary<string, RobotCosmeticDefinition> lookup =
            new Dictionary<string, RobotCosmeticDefinition>(StringComparer.Ordinal);
        for (int index = 0; index < Items.Count; index++)
        {
            lookup[Items[index].Id] = Items[index];
        }

        return lookup;
    }

    private static Color Hex(string value)
    {
        return ColorUtility.TryParseHtmlString(value, out Color result) ? result : Color.white;
    }
}
