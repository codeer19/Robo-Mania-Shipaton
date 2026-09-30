using UnityEngine;

public enum MatchType { None, HumanOnline, Bot }
public enum MatchEntryMode { None, QuickPlay, PrivateRoom }
// Compatibility view for existing presentation/tests, derived from Type; never separately writable.
public enum MatchOpponentKind { Unknown, Human, Bot }

/// <summary>The single persistent match decision. Unknown fails closed; private cannot select Bot.</summary>
public static class MatchSessionContext
{
    public static MatchType Type { get; private set; }
    public static MatchEntryMode EntryMode { get; private set; }
    public static int EntryVersion { get; private set; }
    public static MatchOpponentKind OpponentKind => Type == MatchType.HumanOnline ? MatchOpponentKind.Human :
        Type == MatchType.Bot ? MatchOpponentKind.Bot : MatchOpponentKind.Unknown;
    public static TeamSide LocalSide { get; private set; } = TeamSide.SideA;
    public static FortressTeam LocalTeam => TeamSides.SceneTeam(LocalSide);
    public static string LocalDisplayName { get; private set; } = string.Empty;
    public static string LocalSkinId { get; private set; } = string.Empty;
    public static bool CanInitializeAI => Type == MatchType.Bot && EntryMode == MatchEntryMode.QuickPlay;
    public static bool ShouldApplyBotIdentity => CanInitializeAI;

    public static void BeginPrivateRoom()
    {
        MatchmakingController.Instance?.AbandonForPrivateRoom();
        Clear();
        EntryMode = MatchEntryMode.PrivateRoom;
        Type = MatchType.HumanOnline;
        Debug.Log("[MATCH ENTRY] EntryMode=PrivateRoom MatchType=HumanOnline");
    }

    public static void BeginQuickPlay()
    {
        Clear();
        EntryMode = MatchEntryMode.QuickPlay;
        Debug.Log("[MATCH ENTRY] EntryMode=QuickPlay MatchType=None");
    }

    public static void BeginHumanOnlineMatch()
    {
        Type = MatchType.HumanOnline;
        Debug.Log($"[MATCH ENTRY] EntryMode={EntryMode} MatchType={Type}");
    }

    // Only the Quick Play timeout calls this; the entry ticket invalidates delayed callbacks.
    public static bool TryBeginBotAfterQuickPlayTimeout(int entryVersion)
    {
        if (EntryMode != MatchEntryMode.QuickPlay || Type != MatchType.None || entryVersion != EntryVersion)
        {
            Debug.LogError($"[BOT BLOCKED] Quick Play timeout rejected. MatchType={Type} EntryMode={EntryMode} ticket={entryVersion}/{EntryVersion}");
            return false;
        }
        Type = MatchType.Bot;
        Debug.Log("[MATCH ENTRY] EntryMode=QuickPlay MatchType=Bot (search timeout)");
        return true;
    }

    public static bool AssertPrivateRoom(string stage)
    {
        if (EntryMode == MatchEntryMode.PrivateRoom && Type == MatchType.HumanOnline) return true;
        Debug.LogError($"[PRIVATE ROOM VIOLATION] {stage}: EntryMode={EntryMode} MatchType={Type}");
        return false;
    }

    public static void SetLocalIdentity(string displayName, string skinId, TeamSide side)
    {
        LocalDisplayName = displayName;
        LocalSkinId = skinId;
        LocalSide = side;
    }
    public static void SetLocalTeam(FortressTeam team) => LocalSide = TeamSides.FromSceneTeam(team);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        EntryVersion++;
        EntryMode = MatchEntryMode.None;
        Type = MatchType.None;
        LocalSide = TeamSide.SideA;
        LocalDisplayName = string.Empty;
        LocalSkinId = string.Empty;
    }
}

