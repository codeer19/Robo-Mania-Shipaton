using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporary verification harness for the matchmaking state machine.
/// Drives the controller with a scripted backend so the timeout/opponent race can
/// be forced deterministically. Not shipped - delete once the Fusion backend lands.
/// </summary>
public sealed class MatchmakingRaceHarness : MonoBehaviour
{
    private sealed class ScriptedBackend : IMatchmakingBackend
    {
        public string BackendName => "ScriptedTest";
        public bool IsAvailable => true;
        public int CancelCalls;
        private Action found;

        public void BeginSearch(MatchmakingConfig config, Action onOpponentFound, Action<string> onFailed)
        {
            found = onOpponentFound;
        }

        public void CancelSearch() => CancelCalls++;

        /// <summary>Fires the opponent-found callback on demand.</summary>
        public void FireOpponentFound() => found?.Invoke();
    }

    public static readonly List<string> Results = new List<string>();

    public IEnumerator RunAll()
    {
        Results.Clear();
        yield return Test_HumanBeforeTimeout();
        yield return Test_TimeoutFallsBackToBot();
        yield return Test_SimultaneousRace();
        yield return Test_LateOpponentAfterBotStart();
        yield return Test_CancelNeverStartsBot();
        yield return Test_RepeatedPlayPresses();
    }

    private static MatchmakingController NewController(out ScriptedBackend backend)
    {
        MatchSessionContext.BeginQuickPlay();
        GameObject host = new GameObject("MM_Test");
        var controller = host.AddComponent<MatchmakingController>();
        backend = new ScriptedBackend();
        controller.SetBackend(backend);
        return controller;
    }

    private static void Record(string test, bool passed, string detail)
    {
        Results.Add($"{(passed ? "PASS" : "FAIL")}  {test}  |  {detail}");
    }

    private IEnumerator Test_HumanBeforeTimeout()
    {
        var controller = NewController(out ScriptedBackend backend);
        int fires = 0;
        MatchmakingOutcome outcome = MatchmakingOutcome.None;
        controller.SearchResolved += (o, _) => { fires++; outcome = o; };

        controller.BeginMatchmaking();
        yield return null;
        backend.FireOpponentFound();
        yield return null;

        Record("human before timeout", fires == 1 && outcome == MatchmakingOutcome.Human,
            $"fires={fires} outcome={outcome} state={controller.State}");
        Destroy(controller.gameObject);
        yield return null;
    }

    private IEnumerator Test_TimeoutFallsBackToBot()
    {
        var controller = NewController(out ScriptedBackend backend);
        int fires = 0;
        MatchmakingOutcome outcome = MatchmakingOutcome.None;
        controller.SearchResolved += (o, _) => { fires++; outcome = o; };

        controller.BeginMatchmaking();
        float guard = 0f;
        while (fires == 0 && guard < 30f) { guard += Time.unscaledDeltaTime; yield return null; }

        Record("timeout falls back to bot", fires == 1 && outcome == MatchmakingOutcome.Bot,
            $"fires={fires} outcome={outcome} cancelCalls={backend.CancelCalls} (session must be torn down)");
        Destroy(controller.gameObject);
        yield return null;
    }

    /// <summary>The critical one: opponent arrives on the same frame the timer expires.</summary>
    private IEnumerator Test_SimultaneousRace()
    {
        int humanWins = 0, botWins = 0, doubleFires = 0;

        for (int attempt = 0; attempt < 25; attempt++)
        {
            var controller = NewController(out ScriptedBackend backend);
            int fires = 0;
            MatchmakingOutcome outcome = MatchmakingOutcome.None;
            controller.SearchResolved += (o, _) => { fires++; outcome = o; };

            controller.BeginMatchmaking();

            // Jitter the fire point across the timeout boundary so both orderings
            // actually occur: sometimes the human beats the timer, sometimes the
            // timer has already fired. Testing only one side proves nothing.
            float timeout = MatchmakingConfig.Load().HumanMatchTimeoutSeconds;
            float fireAt = timeout + UnityEngine.Random.Range(-0.08f, 0.08f);
            float guard = 0f;
            // Also break once the controller has resolved: SearchElapsedSeconds
            // stops advancing at that point, so waiting on it alone would spin
            // whenever the fire point lands past the timeout.
            while (controller.SearchElapsedSeconds < fireAt &&
                   controller.State == MatchmakingState.Searching &&
                   guard < 3f)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }

            backend.FireOpponentFound();

            float settle = 0f;
            while (fires == 0 && settle < 2f) { settle += Time.unscaledDeltaTime; yield return null; }
            // One extra frame so a wrongly-allowed second resolve would be caught.
            yield return null;

            if (fires > 1) doubleFires++;
            if (outcome == MatchmakingOutcome.Human) humanWins++;
            if (outcome == MatchmakingOutcome.Bot) botWins++;

            Destroy(controller.gameObject);
            yield return null;
        }

        // Both orderings must actually have been exercised, or the test proved nothing.
        bool bothOrderingsSeen = humanWins > 0 && botWins > 0;
        Record("simultaneous race (25 runs)",
            doubleFires == 0 && humanWins + botWins == 25 && bothOrderingsSeen,
            $"humanWins={humanWins} botWins={botWins} doubleResolves={doubleFires} " +
            $"bothOrderingsExercised={bothOrderingsSeen} (doubleResolves MUST be 0)");
    }

    /// <summary>
    /// The genuinely dangerous case: the bot match has already started and a human
    /// callback lands afterwards. It must not start a second match, and it must
    /// release the session rather than leaving a live Photon room behind.
    /// </summary>
    private IEnumerator Test_LateOpponentAfterBotStart()
    {
        var controller = NewController(out ScriptedBackend backend);
        var outcomes = new List<MatchmakingOutcome>();
        controller.SearchResolved += (o, _) => outcomes.Add(o);

        controller.BeginMatchmaking();

        float guard = 0f;
        while (outcomes.Count == 0 && guard < 30f) { guard += Time.unscaledDeltaTime; yield return null; }

        int cancelsAfterBot = backend.CancelCalls;
        backend.FireOpponentFound();          // human arrives too late
        yield return null;
        yield return null;

        bool onlyBot = outcomes.Count == 1 && outcomes[0] == MatchmakingOutcome.Bot;
        bool releasedSession = backend.CancelCalls > cancelsAfterBot;
        Record("late opponent after bot start", onlyBot && releasedSession,
            $"outcomes=[{string.Join(",", outcomes)}] releasedSession={releasedSession} " +
            "(must stay a single Bot outcome AND release the session)");
        Destroy(controller.gameObject);
        yield return null;
    }

    private IEnumerator Test_CancelNeverStartsBot()
    {
        var controller = NewController(out ScriptedBackend backend);
        var outcomes = new List<MatchmakingOutcome>();
        controller.SearchResolved += (o, _) => outcomes.Add(o);

        controller.BeginMatchmaking();
        yield return null;
        controller.CancelMatchmaking();

        // Wait well past the timeout to prove no bot match sneaks in afterwards.
        float waited = 0f;
        float timeout = MatchmakingConfig.Load().HumanMatchTimeoutSeconds;
        while (waited < timeout + 1.5f) { waited += Time.unscaledDeltaTime; yield return null; }

        bool onlyCancelled = outcomes.Count == 1 && outcomes[0] == MatchmakingOutcome.Cancelled;
        Record("cancel never starts bot", onlyCancelled,
            $"outcomes=[{string.Join(",", outcomes)}] cancelCalls={backend.CancelCalls}");
        Destroy(controller.gameObject);
        yield return null;
    }

    private IEnumerator Test_RepeatedPlayPresses()
    {
        var controller = NewController(out ScriptedBackend backend);
        int fires = 0;
        controller.SearchResolved += (_, __) => fires++;

        bool first = controller.BeginMatchmaking();
        bool second = controller.BeginMatchmaking();
        bool third = controller.BeginMatchmaking();
        yield return null;
        backend.FireOpponentFound();
        yield return null;

        Record("repeated Play presses", first && !second && !third && fires == 1,
            $"accepted=[{first},{second},{third}] resolves={fires} (only the first press may start a search)");
        Destroy(controller.gameObject);
        yield return null;
    }
}
