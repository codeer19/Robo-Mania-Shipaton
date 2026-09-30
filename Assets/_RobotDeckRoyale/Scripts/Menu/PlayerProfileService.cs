using System;
using UnityEngine;

/// <summary>
/// Single persistence boundary for identity, earned currency, purchases and equipped cosmetics.
/// A future account/cloud layer can mirror these calls without changing gameplay or menu code.
/// </summary>
public static class PlayerProfileService
{
    /// <summary>
    /// Optional profile slot, from "-roboProfile &lt;name&gt;" on the command line.
    ///
    /// PlayerPrefs is keyed by company and product name, so two copies of the same
    /// build on one PC read and write a single shared profile: the same name, the
    /// same coins, the same equipped skin. Testing two players side by side on one
    /// machine is impossible without separating them, because both clients are
    /// literally the same account. Empty in normal play, where each device already
    /// has its own store.
    /// </summary>
    private static readonly string Slot = ReadProfileSlot();

    private static string ReadProfileSlot()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-roboProfile") return args[i + 1].Trim();
        }

        return string.Empty;
    }

    internal static string Key(string name) =>
        string.IsNullOrEmpty(Slot) ? "RoboMania." + name : $"RoboMania.{Slot}.{name}";

    public static string PlayerNameKey => Key("PlayerName");
    private static string NameConfirmedKey => Key("PlayerNameConfirmed");
    public static bool HasConfirmedName => ProfileStore.GetInt(NameConfirmedKey, 0) == 1 &&
        !string.IsNullOrWhiteSpace(ProfileStore.GetString(PlayerNameKey, string.Empty));
    public static string CoinsKey => Key("Coins");
    public static string LevelKey => Key("Level");
    public static string ExperienceKey => Key("Experience");
    private static string OwnedPrefix => Key("Cosmetic.Owned.");
    private static string EquippedPrefix => Key("Cosmetic.Equipped.");
    private const int DefaultCoins = 1250;

    public static event Action ProfileChanged;
    public static void NotifyReloaded() => ProfileChanged?.Invoke();
    /// <summary>True when matches show the player's CrazyGames username.</summary>
    public static bool UsesAccountName => ProfileStore.UsesPlatform && CrazyGamesReleaseSettings.Current.useAccountDisplayName &&
        !string.IsNullOrWhiteSpace(CrazyGamesPlatformService.AccountDisplayName);
    public static string DisplayName => UsesAccountName ? CrazyGamesPlatformService.AccountDisplayName : PlayerName;
    public static bool DailyRewardClaimed => DateTime.TryParse(ProfileStore.GetString(Key("lastDailyRewardClaim")), null,
        System.Globalization.DateTimeStyles.RoundtripKind, out var last) && last.ToUniversalTime().Date >= DateTime.UtcNow.Date;
    internal static bool ClaimVerifiedDailyReward()
    {
        if (DailyRewardClaimed) return false;
        using (ProfileStore.Batch())
        {
            ProfileStore.SetString(Key("lastDailyRewardClaim"), DateTime.UtcNow.ToString("O"));
            AddCoins(100);
        }
        return true;
    }

    public static string PlayerName
    {
        get
        {
            string saved = ProfileStore.GetString(PlayerNameKey, string.Empty);
            return string.IsNullOrWhiteSpace(saved) ? DefaultPlayerName : saved;
        }
    }

    /// <summary>
    /// The name a device uses before its owner picks one.
    ///
    /// Derived from the device id rather than a fixed string: every install used
    /// to fall back to the same "ROBO PILOT", so two real players faced each other
    /// with identical nameplates and no way to tell who was who. Deterministic, so
    /// it stays the same across launches without needing to be written to disk.
    /// </summary>
    public static string DefaultPlayerName
    {
        get
        {
            string device = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrEmpty(device) || device == SystemInfo.unsupportedIdentifier)
            {
                device = Environment.TickCount.ToString();
            }

            // The slot is folded in so two instances on one machine, which share a
            // device id, still get different names.
            int hash = (device + Slot).GetHashCode();
            return "PILOT-" + ((uint)hash % 10000u).ToString("0000");
        }
    }
    public static int Coins => Mathf.Max(0, ProfileStore.GetInt(CoinsKey, DefaultCoins));
    public static string CurrentEquippedSkinId => GetEquippedId(RobotCosmeticCategory.Skin);
    public static string CurrentEquippedSkin => CurrentEquippedSkinId;

    public static int Level => Mathf.Max(1, ProfileStore.GetInt(LevelKey, 1));
    public static int Experience => Mathf.Max(0, ProfileStore.GetInt(ExperienceKey, 0));
    public static int ExperienceToNextLevel => ExperienceForLevel(Level);

    /// <summary>
    /// Curve lives here so the menu, the level-up popup and the result screen all
    /// read one definition instead of each estimating its own.
    /// </summary>
    public static int ExperienceForLevel(int level)
    {
        return 100 + Mathf.Max(0, level - 1) * 40;
    }

    /// <summary>
    /// Adds XP and rolls levels, carrying the remainder forward. Prefer granting
    /// through <see cref="PlayerRewardLedger"/> so the award is de-duplicated;
    /// this is the raw mutation it delegates to.
    /// </summary>
    public static void AddExperience(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int level = Level;
        int experience = Experience + amount;

        // while, not if: a large award can cross more than one level at once.
        int required = ExperienceForLevel(level);
        while (experience >= required)
        {
            experience -= required;
            level++;
            required = ExperienceForLevel(level);
        }

        ProfileStore.SetInt(LevelKey, level);
        ProfileStore.SetInt(ExperienceKey, experience);
        SaveAndNotify();
    }

    public static void SetPlayerName(string value)
    {
        TryConfirmPlayerName(value, out _);
    }

    public static bool TryConfirmPlayerName(string value, out string error)
    {
        if (HasConfirmedName) { error = "YOUR PILOT NAME IS ALREADY FINAL."; return false; }
        if (!ValidatePlayerName(value, out string cleaned, out error)) return false;
        ProfileStore.SetString(PlayerNameKey, cleaned.ToUpperInvariant());
        ProfileStore.SetInt(NameConfirmedKey, 1);
        SaveAndNotify();
        error = string.Empty;
        return true;
    }

    public static bool ValidatePlayerName(string value, out string cleaned, out string error)
    {
        cleaned = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (cleaned.Length < 3 || cleaned.Length > 16)
        { error = "USE 3 TO 16 CHARACTERS."; return false; }
        foreach (char character in cleaned)
            if (!char.IsLetterOrDigit(character) && character != ' ' && character != '_' && character != '-')
            { error = "USE LETTERS, NUMBERS, SPACES, - OR _."; return false; }
        string compact = cleaned.Replace(" ", "").Replace("_", "").Replace("-", "");
        foreach (string word in new[] { "FUCK", "SHIT", "BITCH", "NIGGER", "CUNT", "NAZI" })
            if (compact.Contains(word)) { error = "PLEASE CHOOSE ANOTHER NAME."; return false; }
        error = string.Empty;
        return true;
    }

    public static void AddCoins(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        long updated = (long)Coins + amount;
        ProfileStore.SetInt(CoinsKey, (int)Math.Min(int.MaxValue, updated));
        SaveAndNotify();
    }

    public static bool TrySpendCoins(int amount)
    {
        if (amount < 0 || Coins < amount)
        {
            return false;
        }

        ProfileStore.SetInt(CoinsKey, Coins - amount);
        SaveAndNotify();
        return true;
    }

    public static bool IsOwned(string cosmeticId)
    {
        RobotCosmeticDefinition item = RobotCosmeticCatalog.Find(cosmeticId);
        return item != null && (item.DefaultUnlocked || ProfileStore.GetInt(OwnedPrefix + item.Id, 0) == 1);
    }

    public static bool TryPurchase(string cosmeticId, out string message)
    {
        RobotCosmeticDefinition item = RobotCosmeticCatalog.Find(cosmeticId);
        if (item == null)
        {
            message = "ITEM NOT FOUND";
            return false;
        }

        if (IsOwned(item.Id))
        {
            message = "ALREADY OWNED";
            return true;
        }

        if (item.Currency == CosmeticPurchaseCurrency.Premium)
        {
            message = "PREMIUM STORE NOT CONFIGURED";
            return false;
        }

        int currentCoins = Coins;
        if (currentCoins < item.CoinPrice)
        {
            message = "NOT ENOUGH COINS";
            return false;
        }

        // Commit the debit and ownership flag together. This prevents the UI from
        // observing an intermediate state where the coins have gone but the item
        // is not owned yet, and emits one profile refresh instead of two.
        ProfileStore.SetInt(CoinsKey, currentCoins - item.CoinPrice);
        ProfileStore.SetInt(OwnedPrefix + item.Id, 1);
        SaveAndNotify();
        message = "PURCHASED " + item.DisplayName;
        return true;
    }

    public static string GetEquippedId(RobotCosmeticCategory category)
    {
        string defaultId = RobotCosmeticCatalog.GetDefaultId(category);
        string savedId = ProfileStore.GetString(EquippedPrefix + category, defaultId);
        RobotCosmeticDefinition item = RobotCosmeticCatalog.Find(savedId);
        if (item == null || item.Category != category || !IsOwned(savedId))
        {
            return defaultId;
        }

        return savedId;
    }

    public static RobotCosmeticDefinition GetEquipped(RobotCosmeticCategory category)
    {
        return RobotCosmeticCatalog.Find(GetEquippedId(category));
    }

    public static bool TryEquip(string cosmeticId, out string message)
    {
        RobotCosmeticDefinition item = RobotCosmeticCatalog.Find(cosmeticId);
        if (item == null)
        {
            message = "ITEM NOT FOUND";
            return false;
        }

        if (!IsOwned(item.Id))
        {
            message = "BUY THIS ITEM FIRST";
            return false;
        }

        ProfileStore.SetString(EquippedPrefix + item.Category, item.Id);
        SaveAndNotify();
        message = "EQUIPPED " + item.DisplayName;
        return true;
    }

    private static string BuildLoadoutKey => Key("Build.Loadout");

    /// <summary>The three build cards this player takes into matches; the default until they change it.</summary>
    public static BuildPlacementController.BuildableType[] BuildLoadout =>
        BuildCards.Parse(ProfileStore.GetString(BuildLoadoutKey, string.Empty));

    /// <summary>Network form of <see cref="BuildLoadout"/>, published on the player's own avatar.</summary>
    public static int PackedBuildLoadout => BuildCards.Pack(BuildLoadout);

    /// <summary>Saves a loadout of exactly three different cards; anything else is refused.</summary>
    public static bool TrySetBuildLoadout(BuildPlacementController.BuildableType[] loadout)
    {
        if (!BuildCards.IsValidLoadout(loadout)) return false;
        ProfileStore.SetString(BuildLoadoutKey, BuildCards.Serialize(loadout));
        SaveAndNotify();
        return true;
    }

    /// <summary>In-gameplay coaching already shown to this player (see GameplayHints).</summary>
    public static bool IsTutorialDone(string id) => ProfileStore.GetInt(Key("Tutorial." + id), 0) == 1;
    public static void MarkTutorialDone(string id)
    {
        if (IsTutorialDone(id)) return;
        ProfileStore.SetInt(Key("Tutorial." + id), 1);
        ProfileStore.Save();
    }

    private static string MissionKey(string id) => Key("Mission." + id);
    public static int CompletedMatches => ProfileStore.GetInt(MissionKey("matches"), 0);
    public static int WonMatches => ProfileStore.GetInt(MissionKey("wins"), 0);
    public static bool MissionClaimed(string id) => ProfileStore.GetInt(MissionKey("claimed."+id),0)==1;
    public static void RecordFinishedMissionMatch(bool won)
    {
        ProfileStore.SetInt(MissionKey("matches"), (int)Math.Min(int.MaxValue,(long)CompletedMatches+1));
        if(won) ProfileStore.SetInt(MissionKey("wins"),(int)Math.Min(int.MaxValue,(long)WonMatches+1));
        SaveAndNotify();
    }
    public static bool TryClaimMission(string id)
    {
        return ClaimMission(id, 1);
    }
    internal static bool TryClaimVerifiedDoubleMission(string id)
    {
        return ClaimMission(id, 2);
    }
    private static bool ClaimMission(string id, int multiplier)
    {
        var mission = MissionCatalog.Find(id);
        if(mission == null || MissionClaimed(id) || mission.Progress < mission.Target) return false;
        // The durable claim marker and existing coin balance are committed together.
        ProfileStore.SetInt(MissionKey("claimed."+id),1);
        ProfileStore.SetInt(CoinsKey,(int)Math.Min(int.MaxValue,(long)Coins+(long)mission.Reward*multiplier));
        SaveAndNotify(); return true;
    }

    private static void SaveAndNotify()
    {
        ProfileStore.Save();
        ProfileChanged?.Invoke();
    }
}
