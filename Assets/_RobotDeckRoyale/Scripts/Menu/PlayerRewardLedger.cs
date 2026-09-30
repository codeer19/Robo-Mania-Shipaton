using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Where a grant came from. Logged, and used to build transaction ids.</summary>
public enum RewardSource
{
    MatchResult,
    RewardedAd,
    Mission,
    Purchase,
    Debug,
    DailyBattles,
    ReturnStreak
}

/// <summary>
/// The single place coins and XP are granted.
///
/// Every grant carries a transaction id and is applied at most once, ever. That
/// matters because the callers are exactly the paths that fire twice in practice:
/// a match-end callback that can be raised again on scene reload, and a rewarded
/// ad SDK that retries its completion callback.
///
/// Processed ids are persisted, so the protection survives an app restart rather
/// than only holding for the current session.
/// </summary>
public static class PlayerRewardLedger
{
    private const string ProcessedPrefix = "RoboMania.Reward.Processed.";
    private const string ProcessedListKey = "RoboMania.Reward.ProcessedIds";
    private const int MaxRememberedTransactions = 256;

    /// <summary>Raised only when a grant is actually applied, never on a duplicate.</summary>
    public static event Action<RewardGrant> Granted;

    public readonly struct RewardGrant
    {
        public readonly int Coins;
        public readonly int Xp;
        public readonly RewardSource Source;
        public readonly string TransactionId;

        public RewardGrant(int coins, int xp, RewardSource source, string transactionId)
        {
            Coins = coins;
            Xp = xp;
            Source = source;
            TransactionId = transactionId;
        }
    }

    public static bool HasBeenProcessed(string transactionId)
    {
        return !string.IsNullOrEmpty(transactionId) &&
               ProfileStore.GetInt(ProcessedPrefix + transactionId, 0) == 1;
    }

    /// <summary>
    /// Grants coins and/or XP exactly once for the given transaction id.
    /// Returns false when the id was already processed, or the request was empty.
    /// </summary>
    public static bool TryGrant(int coins, int xp, RewardSource source, string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            Debug.LogWarning($"[REWARD] Refused grant from {source} with no transaction id. " +
                             "Every grant needs an id or it cannot be de-duplicated.");
            return false;
        }

        if (coins <= 0 && xp <= 0)
        {
            return false;
        }

        if (HasBeenProcessed(transactionId))
        {
            Debug.Log($"[REWARD] Blocked duplicate grant. source={source} id={transactionId}");
            return false;
        }

        // One platform document contains both the receipt and the granted balance.
        using (ProfileStore.Batch())
        {
            MarkProcessed(transactionId);
            if (coins > 0) PlayerProfileService.AddCoins(coins);
            if (xp > 0) PlayerProfileService.AddExperience(xp);
        }

        Debug.Log($"[REWARD] Granted coins={coins} xp={xp} source={source} id={transactionId}");
        Granted?.Invoke(new RewardGrant(coins, xp, source, transactionId));
        return true;
    }

    /// <summary>Stable id for a finished match, so a repeated result callback is a no-op.</summary>
    public static string MatchTransactionId(string matchId) => "match:" + matchId;

    /// <summary>Stable id for a verified rewarded ad.</summary>
    public static string AdTransactionId(string verifiedRewardId) => "ad:" + verifiedRewardId;

    private static void MarkProcessed(string transactionId)
    {
        ProfileStore.SetInt(ProcessedPrefix + transactionId, 1);

        // Keep a bounded ring of ids so PlayerPrefs cannot grow without limit over
        // the life of an install.
        List<string> ids = LoadProcessedList();
        ids.Add(transactionId);
        while (ids.Count > MaxRememberedTransactions)
        {
            ProfileStore.DeleteKey(ProcessedPrefix + ids[0]);
            ids.RemoveAt(0);
        }

        ProfileStore.SetString(ProcessedListKey, string.Join("|", ids));
        ProfileStore.Save();
    }

    private static List<string> LoadProcessedList()
    {
        string raw = ProfileStore.GetString(ProcessedListKey, string.Empty);
        return string.IsNullOrEmpty(raw)
            ? new List<string>()
            : new List<string>(raw.Split('|'));
    }
}
