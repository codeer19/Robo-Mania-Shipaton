using System;

/// <summary>
/// Daily Battles (play 3 matches today) and the 7-day return streak.
///
/// Both pay only in the existing currencies - coins and XP - through
/// <see cref="PlayerRewardLedger"/>, keyed by the UTC date, so a reward can be
/// granted once per day however often a callback, reload or claim repeats. Days
/// are UTC, matching the existing daily reward. State lives in the profile
/// (CrazyGames Data), so it follows the player's account across devices.
/// No ads are involved in either.
/// </summary>
public static class DailyProgress
{
    public const int DailyBattlesTarget = 3;
    public const int DailyBattlesCoins = 200;
    public const int DailyBattlesXp = 80;
    public const int StreakLength = 7;

    // Day 1 .. Day 7. Day 7 is the one worth coming back for.
    private static readonly int[] StreakCoins = { 50, 75, 100, 125, 150, 200, 400 };
    private static readonly int[] StreakXp = { 0, 20, 30, 40, 50, 60, 150 };

    private static string BattlesDateKey => PlayerProfileService.Key("Daily.Battles.Date");
    private static string BattlesCountKey => PlayerProfileService.Key("Daily.Battles.Count");
    private static string StreakLastKey => PlayerProfileService.Key("Streak.LastClaim");
    private static string StreakDayKey => PlayerProfileService.Key("Streak.Day");

    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static string Yesterday => DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");

    // ---------------------------------------------------------------- daily battles

    public static int DailyBattlesToday =>
        ProfileStore.GetString(BattlesDateKey) == Today ? Math.Max(0, ProfileStore.GetInt(BattlesCountKey, 0)) : 0;

    public static bool DailyBattlesComplete => DailyBattlesToday >= DailyBattlesTarget;

    /// <summary>
    /// Counts one finished, rewarded match. Grants the daily bonus when this match
    /// is the one that reaches the target. Call inside the match reward's batch.
    /// </summary>
    public static bool RecordMatch()
    {
        int count = DailyBattlesToday + 1;
        ProfileStore.SetString(BattlesDateKey, Today);
        ProfileStore.SetInt(BattlesCountKey, count);
        if (count != DailyBattlesTarget) return false;
        bool granted = PlayerRewardLedger.TryGrant(DailyBattlesCoins, DailyBattlesXp, RewardSource.DailyBattles, "daily-battles:" + Today);
        if (granted) Funnel.Event("daily_battle_completed");
        return granted;
    }

    // ---------------------------------------------------------------- streak

    private static string LastClaim => ProfileStore.GetString(StreakLastKey);
    private static int StoredDay => Math.Max(0, Math.Min(StreakLength, ProfileStore.GetInt(StreakDayKey, 0)));

    public static bool StreakClaimedToday => LastClaim == Today;
    public static bool StreakClaimable => ProfileStore.PersistenceAvailable && !StreakClaimedToday;

    /// <summary>The day that will be claimed next (today if unclaimed, else tomorrow).</summary>
    public static int NextStreakDay
    {
        get
        {
            string last = LastClaim;
            if (last == Today || last == Yesterday) return StoredDay % StreakLength + 1;
            return 1;
        }
    }

    /// <summary>The streak as it stands now: today's day once claimed, otherwise the run that is still alive.</summary>
    public static int CurrentStreakDay
    {
        get
        {
            string last = LastClaim;
            if (last == Today || last == Yesterday) return StoredDay;
            return 0;
        }
    }

    public static int StreakRewardCoins(int day) => StreakCoins[Math.Max(1, Math.Min(StreakLength, day)) - 1];
    public static int StreakRewardXp(int day) => StreakXp[Math.Max(1, Math.Min(StreakLength, day)) - 1];

    /// <summary>Claims today's streak reward once. The ledger id is the date, so a double tap or retry cannot pay twice.</summary>
    public static bool TryClaimStreak(out int day, out int coins, out int xp)
    {
        day = NextStreakDay;
        coins = StreakRewardCoins(day);
        xp = StreakRewardXp(day);
        if (!StreakClaimable) return false;
        using (ProfileStore.Batch())
        {
            // A receipt without the claim marker (a save interrupted between the two)
            // still counts as claimed, so the chip can never get stuck on CLAIM.
            string receipt = "streak:" + Today;
            if (!PlayerRewardLedger.HasBeenProcessed(receipt) &&
                !PlayerRewardLedger.TryGrant(coins, xp, RewardSource.ReturnStreak, receipt)) return false;
            ProfileStore.SetString(StreakLastKey, Today);
            ProfileStore.SetInt(StreakDayKey, day);
        }
        Funnel.Event("daily_reward_claimed");
        PlayerProfileService.NotifyReloaded();
        return true;
    }
}

/// <summary>What the last finished match paid and moved, for the result screen.</summary>
public static class MatchResultSummary
{
    public static bool Valid { get; private set; }
    public static int Coins { get; private set; }
    public static int Xp { get; private set; }
    public static int LevelBefore { get; private set; }
    public static int XpBefore { get; private set; }
    public static int LevelAfter { get; private set; }
    public static int XpAfter { get; private set; }
    public static int DailyBattles { get; private set; }
    public static bool DailyBonusGranted { get; private set; }

    public static void Clear() => Valid = false;

    public static void Record(int coins, int xp, int levelBefore, int xpBefore, bool dailyBonus)
    {
        Valid = true;
        Coins = coins; Xp = xp;
        LevelBefore = levelBefore; XpBefore = xpBefore;
        LevelAfter = PlayerProfileService.Level; XpAfter = PlayerProfileService.Experience;
        DailyBattles = DailyProgress.DailyBattlesToday;
        DailyBonusGranted = dailyBonus;
    }
}
