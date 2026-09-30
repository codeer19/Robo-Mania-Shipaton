using System;
using System.Collections;
using UnityEngine;

public enum MatchmakingState
{
    Idle,
    Searching,
    MatchedHuman,
    StartingBot,
    InMatch,
    Cancelled,
    Failed
}

/// <summary>How a search ended. Exactly one of these is ever produced per search.</summary>
public enum MatchmakingOutcome
{
    None,
    Human,
    Bot,
    Cancelled,
    Failed
}

/// <summary>
/// Backend the controller drives. Implemented for real by Fusion; a stub keeps the
/// flow testable while the network layer is being built.
/// </summary>
public interface IMatchmakingBackend
{
    string BackendName { get; }
    bool IsAvailable { get; }

    /// <summary>
    /// Begins looking for a human opponent. Must invoke exactly one of the
    /// callbacks, or none at all if cancelled first.
    /// </summary>
    void BeginSearch(
        MatchmakingConfig config,
        Action onOpponentFound,
        Action<string> onFailed);

    /// <summary>
    /// Tears the attempt down: leave the session, stop the runner, drop callbacks.
    /// Must be safe to call repeatedly and when no search is running.
    /// </summary>
    void CancelSearch();
}

/// <summary>
/// Owns the Play flow: look for a human, and fall back to the AI match if none
/// arrives before the timeout.
///
/// The whole point of this class is that the timeout and the opponent-found
/// callback are racing. Both are funnelled through <see cref="TryClaimOutcome"/>,
/// which only the first caller can win, so a human joining on the same frame the
/// timer expires can never start two matches.
/// </summary>
[DisallowMultipleComponent]
public sealed class MatchmakingController : MonoBehaviour
{
    public static MatchmakingController Instance { get; private set; }

    public MatchmakingState State { get; private set; } = MatchmakingState.Idle;
    public float SearchElapsedSeconds { get; private set; }

    /// <summary>Raised on every state change, for the matchmaking overlay.</summary>
    public event Action<MatchmakingState> StateChanged;

    /// <summary>Raised once a search resolves, with the single winning outcome.</summary>
    public event Action<MatchmakingOutcome, string> SearchResolved;

    private IMatchmakingBackend backend;
    private MatchmakingConfig config;
    private Coroutine searchRoutine;

    // Guards the race. Incremented per search so a late callback from a previous
    // search cannot resolve the current one.
    private int searchGeneration;
    private bool outcomeClaimed;
    private int entryVersion;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        config = MatchmakingConfig.Load();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>The active backend, so gameplay can claim its connected runner.</summary>
    public IMatchmakingBackend Backend => backend;

    public void SetBackend(IMatchmakingBackend matchmakingBackend)
    {
        backend = matchmakingBackend;
        Debug.Log($"[MATCHMAKING] Backend set to {matchmakingBackend?.BackendName ?? "none"}.");
    }

    /// <summary>
    /// Entry point for the Play button. Repeated presses while a search is running
    /// are ignored rather than starting a second search.
    /// </summary>
    public bool BeginMatchmaking()
    {
        if (MatchSessionContext.EntryMode != MatchEntryMode.QuickPlay)
        {
            Debug.LogError($"[BOT BLOCKED] Quick Play search refused for EntryMode={MatchSessionContext.EntryMode}.");
            return false;
        }
        if (State == MatchmakingState.Searching ||
            State == MatchmakingState.MatchedHuman ||
            State == MatchmakingState.StartingBot ||
            State == MatchmakingState.InMatch)
        {
            Debug.Log($"[MATCHMAKING] Ignored Play press while state={State}.");
            return false;
        }

        config = config != null ? config : MatchmakingConfig.Load();
        searchGeneration++;
        entryVersion = MatchSessionContext.EntryVersion;
        outcomeClaimed = false;
        SearchElapsedSeconds = 0f;
        SetState(MatchmakingState.Searching);

        Debug.Log(
            $"[MATCHMAKING] Search started. generation={searchGeneration} " +
            $"timeout={config.HumanMatchTimeoutSeconds}s backend={backend?.BackendName ?? "none"}");
        Funnel.Event("matchmaking_started");

        searchRoutine = StartCoroutine(SearchRoutine(searchGeneration));
        return true;
    }

    /// <summary>
    /// Explicit user cancel. Never falls back to a bot match - that distinction
    /// matters, because a timeout and a cancel both stop the search but only one
    /// of them should start a game.
    /// </summary>
    public void CancelMatchmaking()
    {
        if (State != MatchmakingState.Searching)
        {
            Debug.Log($"[MATCHMAKING] Cancel ignored, state={State}.");
            return;
        }

        if (!TryClaimOutcome(searchGeneration))
        {
            // A human or the timeout resolved on the same frame; that result stands.
            Debug.Log("[MATCHMAKING] Cancel arrived after the search already resolved.");
            return;
        }

        StopSearchRoutine();
        backend?.CancelSearch();
        SetState(MatchmakingState.Cancelled);
        Debug.Log("[MATCHMAKING] Cancelled by user. No bot fallback.");
        SearchResolved?.Invoke(MatchmakingOutcome.Cancelled, "CANCELLED");
        SetState(MatchmakingState.Idle);
    }

    private IEnumerator SearchRoutine(int generation)
    {
        bool backendUsable = backend != null && backend.IsAvailable;

        if (backendUsable)
        {
            backend.BeginSearch(
                config,
                () => HandleOpponentFound(generation),
                reason => HandleBackendFailed(generation, reason));
        }
        else
        {
            Debug.Log(
                "[MATCHMAKING] No usable backend; going straight to the timeout so " +
                "the player still gets a match.");
        }

        float timeout = config.HumanMatchTimeoutSeconds;
        while (SearchElapsedSeconds < timeout)
        {
            if (generation != searchGeneration || outcomeClaimed || !IsCurrentQuickPlayEntry())
            {
                yield break;
            }

            SearchElapsedSeconds += Time.unscaledDeltaTime;
            yield return null;
        }

        // Timeout reached. This only wins if nothing else claimed the outcome first.
        if (!TryClaimOutcome(generation))
        {
            Debug.Log("[MATCHMAKING] Timeout lost the race; another outcome already won.");
            yield break;
        }

        Debug.Log($"[BOT_FALLBACK] No human within {timeout}s. Tearing down the online attempt.");
        Funnel.Event("bot_fallback_started");
        backend?.CancelSearch();
        SetState(MatchmakingState.StartingBot);

        if (config.BotHandoffDelaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(config.BotHandoffDelaySeconds);
        }

        if (generation != searchGeneration || !IsCurrentQuickPlayEntry() ||
            !MatchSessionContext.TryBeginBotAfterQuickPlayTimeout(entryVersion)) yield break;
        SetState(MatchmakingState.InMatch);
        SearchResolved?.Invoke(MatchmakingOutcome.Bot, "BOT_FALLBACK");
    }

    private void HandleOpponentFound(int generation)
    {
        if (!TryClaimOutcome(generation))
        {
            // Timeout or cancel already won. Do not start a second match; just make
            // sure we are not leaving a live session behind.
            Debug.Log("[MATCHMAKING] Opponent arrived after the search resolved; releasing session.");
            backend?.CancelSearch();
            return;
        }

        StopSearchRoutine();
        Debug.Log($"[MATCHMAKING] Human opponent found at {SearchElapsedSeconds:F2}s.");
        Funnel.Event("match_found");
        MatchSessionContext.BeginHumanOnlineMatch();
        SetState(MatchmakingState.MatchedHuman);
        SetState(MatchmakingState.InMatch);
        SearchResolved?.Invoke(MatchmakingOutcome.Human, "HUMAN");
    }

    private void HandleBackendFailed(int generation, string reason)
    {
        if (generation != searchGeneration || outcomeClaimed)
        {
            return;
        }

        // A backend failure is not fatal to the player's intent: they pressed Play
        // and should still get a game. Let the timeout carry them into the bot match
        // rather than dead-ending on an error.
        Debug.LogWarning($"[PHOTON] Matchmaking backend failed: {reason}. Falling through to bot timeout.");
    }

    /// <summary>
    /// The single-winner gate. Returns true to exactly one caller per search.
    /// </summary>
    private bool TryClaimOutcome(int generation)
    {
        if (generation != searchGeneration || outcomeClaimed || !IsCurrentQuickPlayEntry())
        {
            return false;
        }

        outcomeClaimed = true;
        return true;
    }

    private bool IsCurrentQuickPlayEntry() => MatchSessionContext.EntryMode == MatchEntryMode.QuickPlay &&
        MatchSessionContext.EntryVersion == entryVersion;

    private void StopSearchRoutine()
    {
        if (searchRoutine != null)
        {
            StopCoroutine(searchRoutine);
            searchRoutine = null;
        }
    }

    private void SetState(MatchmakingState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        StateChanged?.Invoke(next);
    }

    /// <summary>Called when a match ends so a new search can be started.</summary>
    public void ResetToIdle()
    {
        searchGeneration++;
        StopSearchRoutine();
        outcomeClaimed = false;
        SearchElapsedSeconds = 0f;
        SetState(MatchmakingState.Idle);
    }

    // Private entry cancels a pending Quick Play timer, including its handoff delay.
    // No SearchResolved callback: it must not navigate the old flow during private entry.
    public void AbandonForPrivateRoom()
    {
        bool pending = State == MatchmakingState.Searching || State == MatchmakingState.StartingBot;
        ResetToIdle();
        if (pending) backend?.CancelSearch();
    }
}
