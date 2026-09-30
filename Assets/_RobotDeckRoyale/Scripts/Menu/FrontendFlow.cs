using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum FrontendState { Boot, MainMenu, Matchmaking, PrivateRoom, Gameplay, Result, Leaving, Loading, Error }

/// <summary>Session providers implement this when networking is added. Completion means all
/// callbacks and network objects from the previous attempt have been released.</summary>
public interface IOnlineSessionLifecycle
{
    void LeaveSession(Action<OnlineSessionResult> completed);
}

/// <summary>Shared orchestration for Play, Play Again and Return; scene-independent lifetime.</summary>
public sealed class FrontendFlow : MonoBehaviour
{
    public const string MainScene = "MainMenu";
    public const string GameplayScene = "FortressDuelArena";

    /// <summary>Build index of the arena, used when handing the scene to Fusion.</summary>
    public static int ArenaBuildIndex => SceneUtility.GetBuildIndexByScenePath("Assets/_RobotDeckRoyale/Scenes/30_FortressDuel/FortressDuelArena.unity");

    /// <summary>Build index of the menu, which Fusion adopts while a lobby is open.</summary>
    public static int MenuBuildIndex => SceneUtility.GetBuildIndexByScenePath("Assets/_RobotDeckRoyale/Scenes/01_MainMenu/MainMenu.unity");
    private static FrontendFlow instance;
    public static FrontendFlow Instance => instance;
    public FrontendState State { get; private set; } = FrontendState.Boot;
    public float LoadingProgress { get; private set; }
    public event Action<FrontendState> StateChanged;
    public event Action<float> ProgressChanged;
    private GameObject view;
    private int generation;
    private bool sessionTouched;
    private IOnlineSessionProvider sessionProvider;
    private Coroutine requestRoutine;
    private const float RequestTimeout = 45f;
    private const float CleanupTimeout = 15f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("FrontendFlow");
        instance = go.AddComponent<FrontendFlow>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
        generation++;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        if (State == FrontendState.Loading) return;
        if (scene.name == MainScene) { ClearView(); SetState(FrontendState.MainMenu); }
        else if (scene.name == GameplayScene) { ClearView(); SetState(FrontendState.Gameplay); }
    }

    /// <summary>
    /// Play. Looks for a real opponent first and falls back to the AI match when
    /// none arrives before the configured timeout.
    ///
    /// This deliberately does not go through BeginRequest/OnlineSessionService:
    /// that path waits for a provider to perform a synchronised scene handoff, and
    /// with no provider registered it simply dead-ends on an error. Create and Join
    /// still use it, because a named room genuinely needs that contract.
    /// </summary>
    public void StartOnlineMatchmaking()
    {
        if (!EnsurePilotName()) return;
        if (State != FrontendState.MainMenu && State != FrontendState.Result && State != FrontendState.Error)
        {
            return;
        }

        // Lock synchronously so a double tap cannot start two searches.
        SetState(FrontendState.Matchmaking);
        Funnel.Event("play_clicked");
        int token = ++generation;
        requestRoutine = StartCoroutine(MatchmakingFlow(token));
    }

    // A confirmed pilot name is no longer a gate: matches use DisplayName, which is
    // the CrazyGames username or the existing generated PILOT-#### guest name.
    private bool EnsurePilotName() => !CrazyGamesPlatformService.InteractionBlocked;

    private IEnumerator MatchmakingFlow(int token)
    {
        // One opaque screen for the whole search. Built first, so the old match
        // (Play Again) or the menu is covered from the very first frame, and the
        // finished arena stops rendering and ticking underneath it.
        MatchmakingController controller = null;
        MatchmakingScreen screen = ShowMatchmakingScreen(() => { if (controller != null) controller.CancelMatchmaking(); });
        RetireFinishedArena();

        // Play Again from an online result: the finished match's runner is still
        // adopted. It is retired before a new search starts a second runner, which
        // the director would otherwise refuse as "competing" once a human is found.
        if ((OnlineMatchDirector.Instance != null && OnlineMatchDirector.Instance.HasRunner) ||
            (PrivateLobbyDirector.Instance != null && PrivateLobbyDirector.Instance.IsActive))
        {
            bool cleaned = false;
            yield return Cleanup(result => cleaned = result, token);
            if (token != generation) yield break;
            if (!cleaned)
            {
                if (State == FrontendState.Matchmaking) ShowError("THE PREVIOUS MATCH COULD NOT BE CLOSED. PLEASE TRY AGAIN.");
                yield break;
            }
        }

        MatchSessionContext.BeginQuickPlay();
        controller = EnsureMatchmakingController();

        var resolved = false;
        var outcome = MatchmakingOutcome.None;
        Action<MatchmakingOutcome, string> onResolved = (result, reason) =>
        {
            if (resolved) return;
            resolved = true;
            outcome = result;
        };

        controller.SearchResolved += onResolved;
        controller.BeginMatchmaking();

        while (!resolved && token == generation)
        {
            if (controller.State == MatchmakingState.MatchedHuman && screen != null) screen.SetPhase(MatchmakingScreen.Phase.Found);
            yield return null;
        }

        controller.SearchResolved -= onResolved;

        if (token != generation)
        {
            yield break;
        }

        if (outcome == MatchmakingOutcome.Cancelled || outcome == MatchmakingOutcome.Failed)
        {
            controller.ResetToIdle();
            // From a finished match the arena is no longer usable; cancel goes home.
            if (SceneManager.GetActiveScene().name == GameplayScene) { ReturnToMainMenu(); yield break; }
            ClearView();
            SetState(FrontendState.MainMenu);
            yield break;
        }

        if (outcome == MatchmakingOutcome.Human)
        {
            if (screen != null) screen.SetPhase(MatchmakingScreen.Phase.Found);
            yield return new WaitForSecondsRealtime(.6f);
            yield return LoadArenaOnline(token, controller);
            yield break;
        }

        if (screen != null) screen.SetPhase(MatchmakingScreen.Phase.Finding);
        yield return LoadArena(token);
    }

    private MatchmakingScreen ShowMatchmakingScreen(UnityEngine.Events.UnityAction cancel)
    {
        RectTransform root = NewView("MatchmakingCanvas");
        var screen = root.gameObject.AddComponent<MatchmakingScreen>();
        screen.Build(root, Safe(root), cancel);
        return screen;
    }

    /// <summary>
    /// After a match ends the arena stays loaded under the result and matchmaking
    /// screens until the next scene replaces it. Its camera is switched off (the
    /// screens are opaque, so rendering it was pure waste - and on a phone, real
    /// GPU time) and its combat presentation stops. Nothing networked is touched.
    /// </summary>
    private static void RetireFinishedArena()
    {
        var arena = FortressDuelManager.ActiveArena;
        if (arena != null) arena.HidePresentation();
    }

    private MatchmakingController EnsureMatchmakingController(string sessionName = null)
    {
        MatchmakingController controller = MatchmakingController.Instance;
        if (controller == null)
        {
            var holder = new GameObject("MatchmakingController");
            controller = holder.AddComponent<MatchmakingController>();
        }

        controller.ResetToIdle();
        var backend = new FusionMatchmakingBackend(controller)
        {
            SessionNameOverride = sessionName,
            // Private rooms commit to the match immediately, so Fusion can own the
            // arena as the network scene from the start. That is what makes
            // spawned avatars replicate. Quick match still starts without a scene
            // because it may still fall back to a bot - see note below.
            NetworkSceneBuildIndex = sessionName != null ? ArenaBuildIndex : -1
        };
        controller.SetBackend(backend);
        return controller;
    }

    /// <summary>
    /// Private room over Fusion. Both players supply the same code, so they land
    /// in one named session with no quick-match race and no ambiguity about who
    /// becomes master. Uses the same arena handoff as quick match.
    /// </summary>
    public void StartPrivateRoom(string roomCode, PrivateRoomAction action = PrivateRoomAction.Create)
    {
        if (!EnsurePilotName()) return;
        if (State != FrontendState.MainMenu && State != FrontendState.Result && State != FrontendState.Error)
        {
            return;
        }

        string cleaned = (roomCode ?? string.Empty).Trim().ToUpperInvariant();
        if (cleaned.Length < 4 || cleaned.Length > 32 || !IsRoomCode(cleaned))
        {
            ShowError("ENTER A VALID ROOM CODE (4-32 LETTERS OR NUMBERS).");
            return;
        }

        SetState(FrontendState.Matchmaking);
        MatchSessionContext.BeginPrivateRoom();
        int token = ++generation;
        OpenPrivateRoom(token, cleaned, action);
    }

    /// <summary>
    /// Connects to the named room and opens the lobby.
    ///
    /// Deliberately does not touch MatchmakingController. That class races a human
    /// search against a timeout and falls back to the AI match, which is right for
    /// Play and wrong here: a private room is an agreement between two people, so
    /// a room holding one player waits instead of inventing an opponent. Reaching
    /// the session also does not start the match - the creator decides that.
    /// </summary>
    private PrivateRoomConnector pendingPrivateConnector;
    private void OpenPrivateRoom(int token, string roomCode, PrivateRoomAction action)
    {
        // Committed to the human path before the runner exists, so nothing
        // downstream can mistake an unclassified match for a bot one.
        MatchSessionContext.BeginHumanOnlineMatch();

        ShowStatus("PRIVATE ROOM", "CODE: " + roomCode + "\nCONNECTING...", true);

        var connector = new PrivateRoomConnector();
        pendingPrivateConnector = connector;
        connector.Connect(
            roomCode,
            runner =>
            {
                if (pendingPrivateConnector == connector) pendingPrivateConnector = null;
                if (token != generation)
                {
                    // The player backed out while we were connecting.
                    _ = runner.Shutdown(destroyGameObject: true);
                    return;
                }

                PrivateLobbyDirector.Ensure().EnterLobby(runner, roomCode);
                ShowPrivateLobby(roomCode);
            },
            reason =>
            {
                if (token != generation) return;
                ShowError(reason == "ROOM NOT FOUND" ? reason : FriendlyError(reason));
            }, action);
    }

    private void ShowPrivateLobby(string roomCode)
    {
        SetState(FrontendState.PrivateRoom);
        RectTransform root = NewView("PrivateLobbyCanvas");
        RectTransform safe = Safe(root);
        var screen = safe.gameObject.AddComponent<PrivateLobbyScreen>();
        screen.Build(safe, roomCode, LeavePrivateRoom);
    }

    private void LeavePrivateRoom()
    {
        generation++;
        if (PrivateLobbyDirector.Instance != null)
        {
            PrivateLobbyDirector.Instance.LeaveLobby();
        }

        MatchSessionContext.Clear();
        ReturnToMainMenu();
    }

    /// <summary>
    /// Human matches load the arena through Fusion, not Unity's SceneManager.
    /// Loading it locally would leave each client in its own copy of the scene
    /// with no NetworkObjects registered, so they would never see each other.
    /// In Shared Mode the master client drives the load and the rest follow.
    /// </summary>
    private IEnumerator LoadArenaOnline(int token, MatchmakingController controller)
    {
        SetState(FrontendState.Loading);

        var backend = controller.Backend as FusionMatchmakingBackend;
        Fusion.NetworkRunner runner = backend != null ? backend.ReleaseRunnerForMatch() : null;

        if (runner == null || !runner.IsRunning)
        {
            Debug.LogWarning("[ONLINE MATCH] No connected runner after match; falling back to a local load.");
            yield return LoadArena(token);
            yield break;
        }

        OnlineMatchDirector director = OnlineMatchDirector.Instance;
        if (director == null)
        {
            director = new GameObject("OnlineMatchDirector").AddComponent<OnlineMatchDirector>();
        }

        director.AdoptRunner(runner);

        // Quick Play has no lobby, so nothing has published who this player is or
        // which side they hold. The private-room path fills these from its lobby
        // records; without them PrepareOnlineLocalPlayer refuses and no avatar is
        // ever spawned, which leaves both players frozen at the intro.
        //
        // The shared-mode master is the session's first player, so both clients
        // derive opposite sides from the same replicated fact - no negotiation and
        // no chance of both claiming SideA.
        MatchSessionContext.SetLocalIdentity(
            // DisplayName, not PlayerName: the portal requires a player's own
            // account name to be visible to the people they are matched with.
            // It falls back to the pilot name when there is no account.
            PlayerProfileService.DisplayName,
            PlayerProfileService.CurrentEquippedSkinId,
            runner.IsSharedModeMasterClient ? TeamSide.SideA : TeamSide.SideB);

        Debug.Log(
            $"[QUICKPLAY] Entering human match. side={MatchSessionContext.LocalSide} " +
            $"name={MatchSessionContext.LocalDisplayName} skin={MatchSessionContext.LocalSkinId} " +
            $"master={runner.IsSharedModeMasterClient}");

        // Quick Play runners start without a network scene (NetworkSceneBuildIndex
        // is -1), so an arena that is already active can only be the previous
        // match's, left behind the result screen by Play Again. It is never adopted:
        // the master always loads a fresh arena through Fusion, and both peers wait
        // for a new arena instance rather than for the scene name, which the stale
        // arena already satisfies.
        FortressDuelManager staleArena = FortressDuelManager.ActiveArena;
        if (runner.IsSharedModeMasterClient)
        {
            Debug.Log($"[ONLINE MATCH] Master client loading the arena for the session. replacingStale={staleArena != null}");
            runner.LoadScene(GameplayScene, LoadSceneMode.Single, LocalPhysicsMode.None, true);
        }
        else
        {
            Debug.Log($"[ONLINE MATCH] Waiting for the master client to load the arena. replacingStale={staleArena != null}");
        }

        // Wait for a fresh arena to actually become active on this client.
        float deadline = Time.realtimeSinceStartup + RequestTimeout;
        while (!FreshArenaActive(staleArena) && Time.realtimeSinceStartup < deadline)
        {
            if (token != generation) yield break;
            yield return null;
        }

        if (!FreshArenaActive(staleArena))
        {
            Debug.LogWarning(
                "[ONLINE MATCH] Arena never became active on this client; no avatar will be " +
                $"spawned here. activeScene={SceneManager.GetActiveScene().name} " +
                $"master={runner.IsSharedModeMasterClient}");
            ShowError("THE MATCH COULD NOT START. PLEASE TRY AGAIN.");
            yield break;
        }

        // The arena exists now, so PlayerRoot is present for the avatar to follow.
        Debug.Log($"[ONLINE MATCH] Arena active; requesting avatar spawn. " +
                  $"master={runner.IsSharedModeMasterClient} localPlayer={runner.LocalPlayer}");
        director.SpawnLocalAvatar();

        ClearView();
        SetState(FrontendState.Gameplay);
    }

    // Unity's == treats a destroyed stale arena as null, so a live new instance
    // always differs from it while the in-between frame (neither alive) does not.
    private static bool FreshArenaActive(FortressDuelManager staleArena) =>
        SceneManager.GetActiveScene().name == GameplayScene &&
        FortressDuelManager.ActiveArena != null && FortressDuelManager.ActiveArena != staleArena;

    private IEnumerator LoadArena(int token)
    {
        SetState(FrontendState.Loading);
        AsyncOperation operation = null;
        try
        {
            operation = SceneManager.LoadSceneAsync(GameplayScene, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[MATCHMAKING] Arena load failed: " + exception.GetType().Name);
        }

        if (operation == null)
        {
            ShowError("THE MATCH COULD NOT START. PLEASE TRY AGAIN.");
            yield break;
        }

        while (!operation.isDone)
        {
            if (token != generation) yield break;
            yield return null;
        }

        ClearView();
        SetState(FrontendState.Gameplay);
    }

    private void LegacyQuickMatch() => BeginRequest(OnlineSessionRequest.QuickMatch, "");
    public void CreateRoom() => BeginRequest(OnlineSessionRequest.CreateRoom, "");
    public void JoinRoom(string code) => BeginRequest(OnlineSessionRequest.JoinRoom, code);

    private void BeginRequest(OnlineSessionRequest request, string code)
    {
        if (State != FrontendState.MainMenu && State != FrontendState.Result && State != FrontendState.Error) return;
        string cleaned = (code ?? "").Trim().ToUpperInvariant();
        if (request == OnlineSessionRequest.JoinRoom && (cleaned.Length < 4 || cleaned.Length > 32 || !IsRoomCode(cleaned)))
        {
            ShowError("ENTER A VALID ROOM CODE (4–12 LETTERS OR NUMBERS)."); return;
        }
        if (request == OnlineSessionRequest.CreateRoom || request == OnlineSessionRequest.JoinRoom)
            MatchSessionContext.BeginPrivateRoom();
        else MatchSessionContext.BeginQuickPlay();
        SetState(FrontendState.Matchmaking); // Lock synchronously before starting any callback/coroutine.
        int token = ++generation;
        requestRoutine = StartCoroutine(RunRequest(request, cleaned, token));
    }

    private static bool IsRoomCode(string value)
    {
        foreach (char c in value) if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')) return false;
        return true;
    }

    private IEnumerator RunRequest(OnlineSessionRequest request, string code, int token)
    {
        ShowStatus("PREPARING SESSION", "", true);
        bool cleaned = false;
        yield return Cleanup(result => cleaned = result, token);
        if (token != generation || !cleaned) yield break;
        if (!OnlineSessionService.IsConfigured)
        {
            ShowError("ONLINE PLAY IS NOT AVAILABLE YET."); yield break;
        }
        SetState(FrontendState.Matchmaking);
        ShowStatus(request == OnlineSessionRequest.QuickMatch ? "FINDING OPPONENT" : request == OnlineSessionRequest.CreateRoom ? "CREATING ROOM" : "JOINING ROOM", "CONNECTING…", true);
        bool done = false;
        OnlineSessionResult response = default;
        Action<OnlineSessionResult> completed = result =>
        {
            if (token != generation || done) return;
            response = result; done = true;
        };
        sessionProvider = OnlineSessionService.Provider;
        sessionTouched = true;
        try
        {
            if (request == OnlineSessionRequest.QuickMatch) OnlineSessionService.QuickMatch(completed);
            else if (request == OnlineSessionRequest.CreateRoom) OnlineSessionService.CreateRoom(completed);
            else OnlineSessionService.JoinRoom(code, completed);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Session request failed: " + exception.GetType().Name);
            completed(new OnlineSessionResult(false, "COULD NOT CONNECT. PLEASE TRY AGAIN."));
        }
        float deadline = Time.realtimeSinceStartup + RequestTimeout;
        while (!done && token == generation && State == FrontendState.Matchmaking && Time.realtimeSinceStartup < deadline) yield return null;
        if (token != generation || State == FrontendState.Gameplay) yield break;
        if (!done || !response.Success)
        {
            string message = !done ? "MATCHMAKING TIMED OUT. PLEASE TRY AGAIN." : FriendlyError(response.Message);
            yield return Cleanup(_ => { }, token);
            if (token == generation) ShowError(message);
            yield break;
        }
        if (request == OnlineSessionRequest.CreateRoom && !string.IsNullOrWhiteSpace(response.RoomCode))
        {
            SetState(FrontendState.PrivateRoom);
            RectTransform safe = ShowStatus("PRIVATE ROOM", "CODE: " + response.RoomCode + "\nWAITING FOR ANOTHER PLAYER", false);
            FrontendUI.Button("CopyRoomCode", safe, "COPY", new Vector2(.35f, .28f), new Vector2(.49f, .38f), FrontendUI.Blue,
                () => GUIUtility.systemCopyBuffer = response.RoomCode);
            FrontendUI.Button("LeaveRoom", safe, "RETURN", new Vector2(.51f, .28f), new Vector2(.66f, .38f), FrontendUI.Blue, ReturnToMainMenu);
        }
        else
        {
            // Existing provider contract owns synchronized scene handoff; never substitute a local match.
            ShowStatus("STARTING MATCH", "WAITING FOR THE SESSION…", true);
            float startDeadline = Time.realtimeSinceStartup + RequestTimeout;
            while (State == FrontendState.Matchmaking && token == generation && Time.realtimeSinceStartup < startDeadline) yield return null;
            if (State == FrontendState.Matchmaking && token == generation)
            {
                yield return Cleanup(_ => { }, token);
                if (token == generation) ShowError("THE MATCH COULD NOT START. PLEASE TRY AGAIN.");
            }
        }
    }

    private IEnumerator Cleanup(Action<bool> completed, int token)
    {
        pendingPrivateConnector?.Cancel();
        pendingPrivateConnector = null;
        System.Threading.Tasks.Task onlineCleanup = null;
        if (OnlineMatchDirector.Instance != null && OnlineMatchDirector.Instance.HasRunner)
            onlineCleanup = OnlineMatchDirector.Instance.ShutdownAsync();
        else if (PrivateLobbyDirector.Instance != null && PrivateLobbyDirector.Instance.IsActive)
            onlineCleanup = PrivateLobbyDirector.Instance.LeaveLobbyAsync();
        if (onlineCleanup != null)
        {
            while (!onlineCleanup.IsCompleted) yield return null;
            if (onlineCleanup.IsFaulted) { Debug.LogException(onlineCleanup.Exception); completed(false); yield break; }
        }
        MatchSessionContext.Clear();
        // Withdraw the room from the portal with the session that owned it, so the
        // friends UI never offers a room this client has already left.
        CrazyGamesPlatformService.ClearRoom();
        if (!sessionTouched) { completed(true); yield break; }
        if (!(sessionProvider is IOnlineSessionLifecycle lifecycle))
        {
            ShowError("THE ONLINE SESSION COULD NOT BE CLOSED. PLEASE RECONNECT FROM THE GAME START.");
            completed(false); yield break;
        }
        bool done = false, success = false;
        try { lifecycle.LeaveSession(result => { if (token == generation && !done) { success = result.Success; done = true; } }); }
        catch (Exception e) { Debug.LogWarning("Session cleanup failed: " + e.GetType().Name); done = true; }
        float deadline = Time.realtimeSinceStartup + CleanupTimeout;
        while (!done && token == generation && Time.realtimeSinceStartup < deadline) yield return null;
        if (token != generation) yield break;
        if (success) { sessionTouched = false; sessionProvider = null; }
        else ShowError("COULD NOT LEAVE THE SESSION. PLEASE TRY RETURN AGAIN.");
        completed(success);
    }

    public void ShowResult(FortressTeam? winner, int awardedCoins = 0)
    {
        if (State != FrontendState.Gameplay) return;
        CrazyGamesPlatformService.CompletedMatch();
        SetState(FrontendState.Result);
        RectTransform root = NewView("ResultCanvas");
        RetireFinishedArena();
        FrontendUI.Panel("InputBlocker", root, Vector2.zero, Vector2.one, Color.clear, false);
        var raw = FrontendUI.Rect("LiveBot", root, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
        var presenter = raw.gameObject.AddComponent<RobotPreviewPresenter>();
        presenter.Initialize(null, raw);
        MenuBotPose pose = !winner.HasValue ? MenuBotPose.Draw : winner.Value == FortressTeam.Blue ? MenuBotPose.Victory : MenuBotPose.Defeat;
        presenter.SetPose(pose);
        // Guarded by the FrontendState.Gameplay check at the top of this method,
        // so a duplicate result callback cannot stack a second sting.
        if (pose == MenuBotPose.Victory) ReleaseAudio.Play2D(ReleaseAudioCue.Victory);
        else if (pose == MenuBotPose.Defeat) ReleaseAudio.Play2D(ReleaseAudioCue.Defeat);
        RectTransform safe = Safe(root);

        // Gold / coral / silver faces over a darker extruded shadow, matching the
        // supplied references. No backing plate: the title floats over the scene.
        Color accent = pose == MenuBotPose.Victory ? new Color(1f, .79f, .15f)
            : pose == MenuBotPose.Defeat ? new Color(1f, .34f, .30f)
            : new Color(.95f, .97f, 1f);
        Color shadow = pose == MenuBotPose.Victory ? new Color(.76f, .35f, .05f)
            : pose == MenuBotPose.Defeat ? new Color(.55f, .07f, .12f)
            : new Color(.42f, .52f, .66f);

        var titleShadow = FrontendUI.Text("ResultTitleShadow", safe, pose.ToString().ToUpperInvariant(),
            new Vector2(.14f, .755f), new Vector2(.86f, .935f), 132, shadow);
        var title = FrontendUI.Text("ResultTitle", safe, pose.ToString().ToUpperInvariant(),
            new Vector2(.14f, .77f), new Vector2(.86f, .95f), 132, accent);

        // One progression panel beside the robot instead of a coin figure over its
        // wheels: what this match paid, and what it moved.
        CanvasGroup panelFade;
        ResultProgress progress = BuildResultProgress(safe, awardedCoins, out panelFade);

        // PLAY AGAIN is the one primary action; RETURN steps down beside it.
        RectTransform buttonGroup = FrontendUI.Rect("Buttons", safe, Vector2.zero, Vector2.one);
        var fade = buttonGroup.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        FrontendUI.Button("PlayAgain", buttonGroup, "PLAY AGAIN", new Vector2(.34f, .045f), new Vector2(.66f, .185f), FrontendUI.Gold, PlayAgain);
        var back = FrontendUI.Button("Return", buttonGroup, "RETURN", new Vector2(.70f, .06f), new Vector2(.84f, .15f), FrontendUI.Blue, ReturnToMainMenu);
        var backLabel = back.GetComponentInChildren<TextMeshProUGUI>();
        if (backLabel != null) { backLabel.fontSizeMax = 30f; backLabel.fontSize = 30f; }

        StartCoroutine(ResultEntrance(title, titleShadow, progress, panelFade, fade));
    }

    private void PlayAgain()
    {
        Funnel.Event("play_again_clicked");
        StartOnlineMatchmaking();
    }

    private sealed class ResultProgress
    {
        public TextMeshProUGUI Coins, Xp, Level;
        public RectTransform LevelFill;
        public int CoinTarget, XpTarget;
        public int LevelBefore, LevelAfter;
        public float FillFrom, FillTo;
    }

    private ResultProgress BuildResultProgress(RectTransform safe, int awardedCoins, out CanvasGroup fade)
    {
        var progress = new ResultProgress();
        bool summary = MatchResultSummary.Valid;
        progress.CoinTarget = summary ? MatchResultSummary.Coins : awardedCoins;
        progress.XpTarget = summary ? MatchResultSummary.Xp : 0;
        int levelAfter = summary ? MatchResultSummary.LevelAfter : PlayerProfileService.Level;
        int xpAfter = summary ? MatchResultSummary.XpAfter : PlayerProfileService.Experience;
        progress.LevelBefore = summary ? MatchResultSummary.LevelBefore : levelAfter;
        progress.LevelAfter = levelAfter;
        progress.FillFrom = summary && progress.LevelBefore == levelAfter
            ? (float)MatchResultSummary.XpBefore / PlayerProfileService.ExperienceForLevel(levelAfter) : 0f;
        progress.FillTo = (float)xpAfter / PlayerProfileService.ExperienceForLevel(levelAfter);
        Color muted = new Color(.72f, .82f, .93f);

        // One reward card in the same family as the build cards: ink outline, royal
        // blue frame, navy face. Every section sits on the face; nothing floats.
        var frame = FrontendUI.Panel("Rewards", safe, new Vector2(.635f, .205f), new Vector2(.975f, .765f), FrontendUI.Ink);
        frame.raycastTarget = false;
        fade = frame.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        var band = FrontendUI.Inset("Frame", frame.transform, FrontendUI.Blue, 5f, 5f, 5f, 5f);
        band.raycastTarget = false;
        var face = FrontendUI.Inset("Face", band.transform, FrontendUI.Navy, 7f, 7f, 7f, 7f);
        face.raycastTarget = false;
        Transform t = face.transform;

        var header = FrontendUI.Text("Header", t, "MATCH REWARDS", new Vector2(.05f, .885f), new Vector2(.95f, .975f), 24f, FrontendUI.Cream);
        header.alignment = TextAlignmentOptions.Center;
        FrontendUI.Panel("HeaderRule", t, new Vector2(.34f, .872f), new Vector2(.66f, .882f), FrontendUI.Gold).raycastTarget = false;

        // Coins and XP earned: two tiles, the headline of the card.
        var coinTile = FrontendUI.Panel("CoinTile", t, new Vector2(.04f, .60f), new Vector2(.49f, .85f), FrontendUI.Ink);
        coinTile.raycastTarget = false;
        FrontendUI.Rect("CoinIcon", coinTile.transform, new Vector2(.07f, .20f), new Vector2(.34f, .80f)).gameObject.AddComponent<FrontendCoinIcon>().raycastTarget = false;
        var coinCaption = FrontendUI.Text("CoinCaption", coinTile.transform, "COINS", new Vector2(.38f, .60f), new Vector2(.96f, .92f), 18f, muted);
        coinCaption.alignment = TextAlignmentOptions.Left;
        progress.Coins = FrontendUI.Text("CoinsValue", coinTile.transform, "+0", new Vector2(.38f, .08f), new Vector2(.96f, .66f), 40f, FrontendUI.Gold);
        progress.Coins.alignment = TextAlignmentOptions.Left;

        var xpTile = FrontendUI.Panel("XpTile", t, new Vector2(.51f, .60f), new Vector2(.96f, .85f), FrontendUI.Ink);
        xpTile.raycastTarget = false;
        var xpBadge = FrontendUI.Panel("XpBadge", xpTile.transform, new Vector2(.07f, .26f), new Vector2(.34f, .74f), FrontendUI.Cyan);
        xpBadge.raycastTarget = false;
        FrontendUI.Text("XpBadgeLabel", xpBadge.transform, "XP", Vector2.zero, Vector2.one, 24f, FrontendUI.Ink).alignment = TextAlignmentOptions.Center;
        var xpCaption = FrontendUI.Text("XpCaption", xpTile.transform, "EXPERIENCE", new Vector2(.38f, .60f), new Vector2(.96f, .92f), 18f, muted);
        xpCaption.alignment = TextAlignmentOptions.Left;
        progress.Xp = FrontendUI.Text("XpValue", xpTile.transform, "+0", new Vector2(.38f, .08f), new Vector2(.96f, .66f), 40f, FrontendUI.Cyan);
        progress.Xp.alignment = TextAlignmentOptions.Left;

        // Level: a gold level badge beside the bar that fills from before to after.
        var levelBadge = FrontendUI.Panel("LevelBadge", t, new Vector2(.04f, .405f), new Vector2(.19f, .545f), FrontendUI.Gold);
        levelBadge.raycastTarget = false;
        FrontendUI.Text("LevelNumber", levelBadge.transform, levelAfter.ToString(), Vector2.zero, Vector2.one, 32f, FrontendUI.Ink).alignment = TextAlignmentOptions.Center;
        progress.Level = FrontendUI.Text("Level", t, "LEVEL " + progress.LevelBefore, new Vector2(.23f, .48f), new Vector2(.62f, .555f), 22f, FrontendUI.Cream);
        progress.Level.alignment = TextAlignmentOptions.Left;
        var xpLabel = FrontendUI.Text("LevelXp", t, xpAfter + " / " + PlayerProfileService.ExperienceForLevel(levelAfter) + " XP",
            new Vector2(.60f, .48f), new Vector2(.96f, .555f), 18f, muted);
        xpLabel.alignment = TextAlignmentOptions.Right;
        var track = FrontendUI.Panel("LevelTrack", t, new Vector2(.23f, .415f), new Vector2(.96f, .46f), FrontendUI.Ink);
        track.raycastTarget = false;
        var fill = FrontendUI.Panel("LevelFill", track.transform, Vector2.zero, new Vector2(Mathf.Clamp01(progress.FillFrom), 1f), FrontendUI.Cyan);
        fill.raycastTarget = false;
        progress.LevelFill = fill.rectTransform;

        // Daily Battles: three pips, and the bonus when this match completed them.
        int battles = Mathf.Min(DailyProgress.DailyBattlesTarget, summary ? MatchResultSummary.DailyBattles : DailyProgress.DailyBattlesToday);
        var daily = FrontendUI.Text("Daily", t, "DAILY BATTLES", new Vector2(.04f, .285f), new Vector2(.50f, .365f), 20f, FrontendUI.Cream);
        daily.alignment = TextAlignmentOptions.Left;
        for (int i = 0; i < DailyProgress.DailyBattlesTarget; i++)
        {
            float left = .53f + i * .10f;
            FrontendUI.Panel("Battle " + (i + 1), t, new Vector2(left, .295f), new Vector2(left + .08f, .355f),
                i < battles ? FrontendUI.Gold : FrontendUI.Ink).raycastTarget = false;
        }
        var dailyValue = FrontendUI.Text("DailyValue", t, battles + "/" + DailyProgress.DailyBattlesTarget, new Vector2(.84f, .285f), new Vector2(.96f, .365f), 20f, FrontendUI.Gold);
        dailyValue.alignment = TextAlignmentOptions.Right;
        string dailyLine = summary && MatchResultSummary.DailyBonusGranted
            ? "DAILY BONUS  +" + DailyProgress.DailyBattlesCoins + " COINS  +" + DailyProgress.DailyBattlesXp + " XP"
            : battles >= DailyProgress.DailyBattlesTarget ? "DAILY BATTLES DONE - SEE YOU TOMORROW"
            : "PLAY " + (DailyProgress.DailyBattlesTarget - battles) + " MORE: +" + DailyProgress.DailyBattlesCoins + " COINS";
        var dailyNote = FrontendUI.Text("DailyNote", t, dailyLine, new Vector2(.04f, .215f), new Vector2(.96f, .285f), 16f,
            summary && MatchResultSummary.DailyBonusGranted ? FrontendUI.Gold : muted);
        dailyNote.alignment = TextAlignmentOptions.Left;

        // One mission line on an ink strip at the foot of the card.
        var missionStrip = FrontendUI.Panel("MissionStrip", t, new Vector2(.03f, .04f), new Vector2(.97f, .18f), FrontendUI.Ink);
        missionStrip.raycastTarget = false;
        var missionNote = FrontendUI.Text("Mission", missionStrip.transform, MissionLine(), new Vector2(.04f, .05f), new Vector2(.96f, .95f), 16f, muted);
        missionNote.alignment = TextAlignmentOptions.Left;
        return progress;
    }

    private static string MissionLine()
    {
        MissionDefinition next = null;
        foreach (var mission in MissionCatalog.Entries)
        {
            if (mission.Claimed) continue;
            if (mission.Completed) return "MISSION READY: " + mission.Title + " - CLAIM IN MISSIONS";
            if (next == null) next = mission;
        }
        if (next != null) return "MISSION: " + next.Title + "  " + next.Progress + "/" + next.Target;
        return DailyProgress.StreakClaimedToday
            ? "COME BACK TOMORROW: STREAK DAY " + DailyProgress.NextStreakDay + "  +" + DailyProgress.StreakRewardCoins(DailyProgress.NextStreakDay) + " COINS"
            : "ALL MISSIONS CLEARED";
    }

    /// <summary>Title pops, the panel fills in (coins, XP, level bar), then the buttons arrive.</summary>
    private IEnumerator ResultEntrance(TextMeshProUGUI title, TextMeshProUGUI titleShadow,
        ResultProgress progress, CanvasGroup panel, CanvasGroup buttons)
    {
        const float pop = .34f;
        for (float t = 0f; t < pop; t += Time.unscaledDeltaTime)
        {
            float k = t / pop;
            // overshoot then settle
            float scale = k < .6f ? Mathf.Lerp(.62f, 1.1f, k / .6f) : Mathf.Lerp(1.1f, 1f, (k - .6f) / .4f);
            if (title != null) title.rectTransform.localScale = Vector3.one * scale;
            if (titleShadow != null) titleShadow.rectTransform.localScale = Vector3.one * scale;
            if (panel != null) panel.alpha = k;
            yield return null;
        }
        if (title != null) title.rectTransform.localScale = Vector3.one;
        if (titleShadow != null) titleShadow.rectTransform.localScale = Vector3.one;
        if (panel != null) panel.alpha = 1f;

        if (progress != null)
        {
            const float count = .7f;
            for (float t = 0f; t < count; t += Time.unscaledDeltaTime)
            {
                float k = t / count;
                if (progress.Coins != null) progress.Coins.text = "+" + Mathf.RoundToInt(Mathf.Lerp(0f, progress.CoinTarget, k));
                if (progress.Xp != null) progress.Xp.text = "+" + Mathf.RoundToInt(Mathf.Lerp(0f, progress.XpTarget, k));
                if (progress.LevelFill != null)
                    progress.LevelFill.anchorMax = new Vector2(Mathf.Clamp01(Mathf.Lerp(progress.FillFrom, progress.FillTo, k)), 1f);
                yield return null;
            }
            if (progress.Coins != null) progress.Coins.text = "+" + progress.CoinTarget;
            if (progress.Xp != null) progress.Xp.text = "+" + progress.XpTarget;
            if (progress.LevelFill != null) progress.LevelFill.anchorMax = new Vector2(Mathf.Clamp01(progress.FillTo), 1f);
            if (progress.Level != null)
                progress.Level.text = progress.LevelAfter > progress.LevelBefore ? "LEVEL " + progress.LevelAfter + "  LEVEL UP!" : "LEVEL " + progress.LevelAfter;
            // Once, when the counter lands - not per interpolated frame.
            if (progress.CoinTarget > 0) ReleaseAudio.Play2D(ReleaseAudioCue.UiConfirm);
        }

        const float appear = .22f;
        for (float t = 0f; t < appear; t += Time.unscaledDeltaTime)
        {
            if (buttons != null) buttons.alpha = t / appear;
            yield return null;
        }
        if (buttons != null) buttons.alpha = 1f;
    }

    public void ReturnToMainMenu()
    {
        if (CrazyGamesPlatformService.InteractionBlocked) return;
        if (State == FrontendState.Leaving || State == FrontendState.Boot ||
            ((State == FrontendState.Loading || State == FrontendState.Gameplay) && MatchSessionContext.Type != MatchType.HumanOnline)) return;
        int token = ++generation;
        if (requestRoutine != null) { StopCoroutine(requestRoutine); requestRoutine = null; }
        SetState(FrontendState.Leaving);
        StartCoroutine(ReturnRoutine(token));
    }

    private IEnumerator ReturnRoutine(int token)
    {
        ShowStatus("LEAVING SESSION", "", false);
        bool clean = false;
        yield return Cleanup(result => clean = result, token);
        if (!clean || token != generation) yield break;
        yield return CrazyGamesPlatformService.AfterSuccessfulCleanup();
        if (token != generation) yield break;
        if (!Application.CanStreamedLevelBeLoaded(MainScene)) { ShowError("THE HOME SCREEN COULD NOT BE LOADED."); yield break; }
        SetState(FrontendState.Loading);
        RectTransform root = NewView("LoadingCanvas");
        var artwork = FrontendUI.Rect("SuppliedArtwork", root, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
        FrontendAssets assets = FrontendAssets.Load();
        artwork.texture = assets != null ? assets.LoadingArtwork : null;
        artwork.raycastTarget = true;
        if (artwork.texture != null)
        {
            var aspect = artwork.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = (float)artwork.texture.width / artwork.texture.height;
        }
        // The exact supplied art has a baked-in 42% gauge. Cover that region with the
        // live gauge's opaque backing, without editing or replacing the source pixels.
        FrontendUI.Panel("BakedGaugeCover", artwork.transform, new Vector2(.026f, .048f), new Vector2(.35f, .15f), FrontendUI.Ink);
        RectTransform safe = Safe(root);
        var gauge = FrontendUI.Panel("LoadingGauge", safe, new Vector2(.026f, .048f), new Vector2(.35f, .15f), FrontendUI.Ink);
        FrontendUI.Text("LoadingLabel", gauge.transform, "LOADING…", new Vector2(.035f, .51f), new Vector2(.50f, .95f), 32, Color.white).alignment = TextAlignmentOptions.Left;
        FrontendUI.Panel("Track", gauge.transform, new Vector2(.035f, .16f), new Vector2(.76f, .40f), new Color(.25f, .29f, .37f));
        var fill = FrontendUI.Panel("Progress", gauge.transform, new Vector2(.04f, .18f), new Vector2(.755f, .38f), new Color(1, .19f, .59f));
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
        fill.sprite = FrontendUI.Solid;
        var percentage = FrontendUI.Text("Percentage", gauge.transform, "0%", new Vector2(.78f, .10f), new Vector2(.99f, .48f), 32, Color.white);
        SetProgress(0, fill, percentage);
        yield return null; // Let the artwork render before initiating the asynchronous load.
        AsyncOperation operation = null;
        try { operation = SceneManager.LoadSceneAsync(MainScene, LoadSceneMode.Single); }
        catch (Exception e) { Debug.LogWarning("Home load failed: " + e.GetType().Name); }
        if (operation == null) { ShowError("THE HOME SCREEN COULD NOT BE LOADED. PLEASE TRY AGAIN."); yield break; }
        operation.allowSceneActivation = false;
        while (operation.progress < .9f)
        {
            SetProgress(Mathf.Clamp01(operation.progress / .9f), fill, percentage);
            yield return null;
        }
        SetProgress(1, fill, percentage);
        yield return null; // Present a real activation-ready 100% frame.
        operation.allowSceneActivation = true;
        while (!operation.isDone) yield return null;
        ClearView(); SetState(FrontendState.MainMenu);
    }

    private void SetProgress(float progress, Image fill, TMP_Text label)
    {
        LoadingProgress = progress; fill.fillAmount = progress;
        label.text = Mathf.RoundToInt(progress * 100) + "%";
        ProgressChanged?.Invoke(progress);
    }

    /// <summary>Provider hook for connection loss; existing match authority still decides outcomes.</summary>
    public void ReportDisconnected()
    {
        if (State == FrontendState.Loading || State == FrontendState.Leaving || State == FrontendState.Result) return;
        generation++;
        ShowError("THE CONNECTION WAS LOST. RETURN HOME TO TRY AGAIN.");
    }

    private void ShowError(string message)
    {
        SetState(FrontendState.Error);
        var safe = ShowStatus("UNABLE TO CONTINUE", message, false);
        FrontendUI.Button("ErrorReturn", safe, "RETURN", new Vector2(.39f, .25f), new Vector2(.61f, .37f), FrontendUI.Blue, ReturnToMainMenu);
    }

    private static string FriendlyError(string message)
    {
        string text = (message ?? "").ToLowerInvariant();
        if (text.Contains("full")) return "THIS ROOM IS FULL.";
        if (text.Contains("not found") || text.Contains("invalid") || text.Contains("not exist")) return "ROOM NOT FOUND. CHECK THE CODE.";
        if (text.Contains("timeout") || text.Contains("timed out")) return "THE CONNECTION TIMED OUT. PLEASE TRY AGAIN.";
        return "COULD NOT CONNECT. PLEASE TRY AGAIN.";
    }

    private RectTransform ShowStatus(string title, string message, bool cancel)
    {
        RectTransform root = NewView("SessionCanvas");
        FrontendBackdrop.Create(root);
        RectTransform safe = Safe(root);
        FrontendUI.Text("Title", safe, title, new Vector2(.18f, .57f), new Vector2(.82f, .74f), 62, Color.white);
        FrontendUI.Text("Status", safe, message, new Vector2(.20f, .40f), new Vector2(.8f, .55f), 28, Color.white);
        if (cancel) FrontendUI.Button("Cancel", safe, "RETURN", new Vector2(.39f, .25f), new Vector2(.61f, .37f), FrontendUI.Blue, ReturnToMainMenu);
        return safe;
    }

    private RectTransform NewView(string name)
    {
        ClearView();
        view = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        view.transform.SetParent(transform, false);
        var canvas = view.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        var scaler = view.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        return (RectTransform)view.transform;
    }

    private static RectTransform Safe(Transform root)
    {
        var safe = FrontendUI.Rect("SafeArea", root, Vector2.zero, Vector2.one);
        safe.gameObject.AddComponent<FrontendSafeArea>(); return safe;
    }

    private void ClearView() { if (view != null) { view.SetActive(false); Destroy(view); view = null; } }
    private void SetState(FrontendState next) { if (State == next) return; State = next; StateChanged?.Invoke(next); }
}
