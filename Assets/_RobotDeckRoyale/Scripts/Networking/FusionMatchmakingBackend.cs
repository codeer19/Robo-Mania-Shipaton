using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// Photon Fusion 2 implementation of the matchmaking backend.
///
/// Topology: Shared Mode. Nothing in the project used Fusion before, so this is a
/// fresh choice rather than a change. Shared was picked because the existing
/// gameplay is client-authoritative MonoBehaviours - each robot moves and shoots
/// itself - and Shared lets each client keep StateAuthority over its own player.
/// Host Mode would mean relocating all movement and combat resolution onto the
/// host, which is a rewrite of exactly the systems that must not be rebuilt.
///
/// The trade-off, stated plainly: Shared trusts the client more, so it is easier
/// to cheat in than Host. For casual 1v1 that is an acceptable exchange for not
/// destabilising working gameplay.
///
/// This class owns the runner lifecycle only. It does not replicate gameplay -
/// that is the separate, larger piece of work.
/// </summary>
public sealed class FusionMatchmakingBackend : IMatchmakingBackend, INetworkRunnerCallbacks
{
    public string BackendName => "PhotonFusion2";

    /// <summary>
    /// When set, joins this exact session instead of quick-matching. Used by the
    /// private-room flow, where both players agree on a code up front - which
    /// removes the matchmaking race and the master-client ambiguity from testing.
    /// </summary>
    /// <summary>
    /// Marks a session as belonging to the Quick Play queue. Only sessions
    /// carrying this are considered when Play searches for an opponent.
    /// </summary>
    public const string QuickPlayMode = "quickplay1v1";

    public string SessionNameOverride { get; set; }

    /// <summary>
    /// Build index of the scene Fusion should own as the network scene, or -1 to
    /// start without one.
    ///
    /// This matters more than it looks. Starting a runner with no Scene makes
    /// Fusion log "no network scene will be loaded and no scene NetworkObjects
    /// will be spawned", and spawned objects then do not replicate between
    /// clients - each side only ever sees the one it spawned itself.
    /// </summary>
    public int NetworkSceneBuildIndex { get; set; } = -1;

    // Fusion is present and an App ID is configured; if either is missing the
    // controller falls straight through to the bot match rather than dead-ending.
    public bool IsAvailable => runnerPrefabAvailable;

    private readonly bool runnerPrefabAvailable = true;
    private readonly MonoBehaviour host;

    private NetworkRunner runner;
    private CancellationTokenSource cancellation;
    private Action opponentFound;
    private Action<string> failed;
    private MatchmakingConfig config;
    private bool searching;
    private bool notified;

    // The in-flight StartGame, when one is running. Cancelling is allowed at any
    // moment, including mid-connect, and a runner whose StartGame has not
    // returned yet must not be shut down or destroyed - doing that is what
    // produced the long Fusion async stack trace on the device. While this is
    // non-null, teardown belongs to the connect's own continuation.
    private Task<StartGameResult> connecting;
    private bool cancelRequested;

    // Runners currently being retired, so a shutdown can never be issued twice
    // for the same runner from two different paths.
    private readonly HashSet<NetworkRunner> retiring = new HashSet<NetworkRunner>();

    // Bounded, so a search can never spin creating and abandoning rooms.
    private const int MaxJoinAttempts = 3;
    private int joinAttempt;

    public FusionMatchmakingBackend(MonoBehaviour coroutineHost)
    {
        host = coroutineHost;
    }

    public void BeginSearch(MatchmakingConfig matchmakingConfig, Action onOpponentFound, Action<string> onFailed)
    {
        if (searching)
        {
            Debug.LogWarning("[PHOTON] BeginSearch called while already searching; ignoring.");
            return;
        }

        config = matchmakingConfig;
        opponentFound = onOpponentFound;
        failed = onFailed;
        searching = true;
        notified = false;
        cancelRequested = false;
        joinAttempt = 0;

        StartGameAsync();
    }

    private async void StartGameAsync()
    {
        // One runner per search, retired exactly once. Creating it on its own
        // GameObject keeps it clear of scene reloads. Held in a local as well as
        // the field, because a cancel clears the field while this connect is
        // still responsible for the instance.
        var runnerObject = new GameObject("FusionMatchmakingRunner");
        UnityEngine.Object.DontDestroyOnLoad(runnerObject);
        NetworkRunner owned = runnerObject.AddComponent<NetworkRunner>();
        owned.ProvideInput = true;

        // Without a scene manager the runner has nothing to drive network
        // scene loading, so LoadScene does not synchronise and spawned objects
        // never replicate between clients - each side only ever sees its own.
        // Photon's own sample attaches this for the same reason.
        var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
        sceneManager.IsSceneTakeOverEnabled = false;

        owned.AddCallbacks(this);
        runner = owned;
        cancellation = new CancellationTokenSource();

        StartGameResult result;
        try
        {
            var sessionProperties = new Dictionary<string, SessionProperty>
            {
                // Matchmaking metadata only. Gameplay state never travels here.
                //
                // "mode" is the filter that keeps Quick Play away from private
                // rooms: a random join only considers sessions carrying these
                // properties, and a private room is created by name with none of
                // them, so ABC123 can never be handed to someone pressing Play.
                { "mode", QuickPlayMode },
                { "map", config.ArenaSceneName },
                { "queue", config.QueueVersion }
            };

            var args = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                PlayerCount = config.PlayersPerMatch,
                SessionProperties = sessionProperties,
                MatchmakingMode = Photon.Realtime.MatchmakingMode.FillRoom,
                StartGameCancellationToken = cancellation.Token,
                // No SessionName: Fusion joins any compatible open session, or
                // creates one. That is what makes this a quick match rather than
                // a named room. A private room supplies the name explicitly.
                SessionName = string.IsNullOrWhiteSpace(SessionNameOverride)
                    ? null
                    : SessionNameOverride
            };

            if (NetworkSceneBuildIndex >= 0)
            {
                var sceneInfo = new NetworkSceneInfo();
                sceneInfo.AddSceneRef(
                    SceneRef.FromIndex(NetworkSceneBuildIndex),
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
                args.Scene = sceneInfo;
            }

            Debug.Log(
                $"[PHOTON] StartGame Shared/FillRoom players={config.PlayersPerMatch} " +
                $"queue=v{config.QueueVersion} session={(args.SessionName ?? "<quickmatch>")}");

            // Held so a cancel arriving mid-connect can defer teardown to this
            // continuation rather than pulling the runner out from under Fusion.
            connecting = owned.StartGame(args);
            result = await connecting;
        }
        catch (OperationCanceledException)
        {
            connecting = null;
            Debug.Log("[PHOTON] StartGame cancelled while connecting; retiring the runner.");
            await Retire(owned);
            return;
        }
        catch (Exception exception)
        {
            connecting = null;
            await Retire(owned);
            Report(false, exception.Message);
            return;
        }

        connecting = null;

        if (cancelRequested || !searching)
        {
            // Cancelled while connecting. The connect has now finished with the
            // runner, so this is the first moment it is safe to retire. No outcome
            // is reported and no bot is started: an explicit cancel is not a
            // timeout, and the controller must not be told a search resolved.
            Debug.Log("[PHOTON] Connect finished after cancellation; retiring without an outcome.");
            await Retire(owned);
            return;
        }

        if (!result.Ok)
        {
            await Retire(owned);
            Report(false, $"StartGame failed: {result.ShutdownReason}");
            return;
        }

        Debug.Log($"[PHOTON] Session joined: {owned.SessionInfo?.Name} " +
                  $"players={owned.SessionInfo?.PlayerCount}/{config.PlayersPerMatch}");

        // We may already be full if we joined a waiting opponent, in which case
        // OnPlayerJoined for them never fires for us.
        EvaluateSessionFull();

        await RejoinIfStillAlone(owned);
    }

    /// <summary>
    /// Asks again when this peer ends up alone in a room it created itself.
    ///
    /// A random join that matches nothing creates a room instead. Two players
    /// pressing Play at the same moment therefore both create, both sit alone at
    /// 1/2, and neither ever sees the other - measured on device, two peers
    /// pressing Play seconds apart still produced two separate rooms and two bot
    /// matches, because a freshly created room is not returned by matchmaking
    /// immediately. Abandoning an empty self-created room and querying again is
    /// what breaks that symmetry; the wait is jittered so two peers doing this at
    /// the same time do not stay in lockstep with each other.
    /// </summary>
    private async Task RejoinIfStillAlone(NetworkRunner owned)
    {
        if (joinAttempt >= MaxJoinAttempts) return;

        int waitMilliseconds = 1500 + UnityEngine.Random.Range(0, 1200);
        await Task.Delay(waitMilliseconds);

        if (!searching || notified || cancelRequested) return;
        if (owned == null || !owned.IsRunning) return;

        int required = config != null ? config.PlayersPerMatch : 2;
        if (owned.SessionInfo != null && owned.SessionInfo.PlayerCount >= required)
        {
            EvaluateSessionFull();
            return;
        }

        joinAttempt++;
        Debug.Log($"[PHOTON] Alone in {owned.SessionInfo?.Name} after {waitMilliseconds}ms; " +
                  $"leaving to look for an open Quick Play room (attempt {joinAttempt}/{MaxJoinAttempts}).");

        await Retire(owned);
        if (!searching || notified || cancelRequested) return;

        StartGameAsync();
    }

    private void EvaluateSessionFull()
    {
        if (!searching || runner == null || runner.SessionInfo == null)
        {
            return;
        }

        int required = config != null ? config.PlayersPerMatch : 2;
        int present = runner.SessionInfo.PlayerCount;

        if (present >= required)
        {
            Debug.Log($"[PHOTON] Session full ({present}/{required}); opponent present.");
            CloseToMatchmaking(runner);
            Report(true, null);
        }
    }

    /// <summary>
    /// Takes a matched session out of Quick Play for good.
    ///
    /// The session outlives the match: whoever is still on the result screen, or
    /// in a match the other player just left, keeps it at 1/2. Left open, the
    /// next player pressing Play (or Play Again) was handed that room as a
    /// "human opponent" and sat through a start that could never happen before
    /// being sent back to the menu. Only the Shared Mode master client may change
    /// the room, and a failure here must never block the match that was found.
    /// </summary>
    private static void CloseToMatchmaking(NetworkRunner owned)
    {
        if (owned == null || !owned.IsSharedModeMasterClient || owned.SessionInfo == null) return;
        try
        {
            owned.SessionInfo.IsOpen = false;
            owned.SessionInfo.IsVisible = false;
            Debug.Log($"[PHOTON] Session {owned.SessionInfo.Name} closed to matchmaking.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PHOTON] Could not close the session to matchmaking: {exception.Message}");
        }
    }

    /// <summary>
    /// Fires the controller callback at most once per search. The controller has
    /// its own single-winner gate too; this stops us calling into it repeatedly.
    /// </summary>
    private void Report(bool found, string reason)
    {
        if (notified || !searching)
        {
            return;
        }

        notified = true;

        if (found)
        {
            opponentFound?.Invoke();
        }
        else
        {
            Debug.LogWarning($"[PHOTON] Matchmaking failed: {reason}");
            failed?.Invoke(reason);
        }
    }

    /// <summary>The live runner, or null once it has been released or torn down.</summary>
    public NetworkRunner ActiveRunner => runner;

    /// <summary>
    /// Hands the connected runner to gameplay without shutting it down.
    ///
    /// Only used when a human opponent was found: the session has to survive the
    /// menu-to-arena transition, so the backend stops owning it. The timeout and
    /// cancel paths still go through CancelSearch and destroy the runner, which is
    /// what guarantees a bot match never leaves a session open.
    /// </summary>
    public NetworkRunner ReleaseRunnerForMatch()
    {
        NetworkRunner released = runner;

        searching = false;
        cancelRequested = false;
        opponentFound = null;
        failed = null;
        runner = null;

        if (released != null)
        {
            released.RemoveCallbacks(this);
            Debug.Log($"[PHOTON] Runner released to gameplay. session={released.SessionInfo?.Name}");
        }

        return released;
    }

    /// <summary>
    /// Stops the search. Safe at any point, including mid-connect.
    ///
    /// Cancelling used to cancel the token and immediately shut down and destroy
    /// the runner, even when StartGame had not returned yet. Fusion was then left
    /// connecting through an object whose GameObject had been destroyed, which is
    /// what produced the long async stack trace and the TaskCanceledException
    /// noise on the phone. Now cancellation is only signalled here; the connect's
    /// own continuation owns the teardown until it completes.
    /// </summary>
    public void CancelSearch()
    {
        if (!searching && runner == null && connecting == null)
        {
            return;
        }

        searching = false;
        cancelRequested = true;
        opponentFound = null;
        failed = null;

        // StartGame's token cancellation itself invokes Fusion shutdown and logs
        // StartGame Failed. Let the existing continuation retire a pending attempt
        // after it finishes; searching=false already prevents any late outcome.
        if (connecting != null)
        {
            Debug.Log("[PHOTON] Cancel requested while StartGame is connecting; teardown deferred to its completion.");
            return;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a previous cancel; nothing to do.
        }

        NetworkRunner doomed = runner;
        runner = null;
        _ = Retire(doomed);
        Debug.Log("[PHOTON] Search cancelled and runner retired.");
    }

    /// <summary>
    /// Leaves the session and destroys the runner, exactly once per runner.
    ///
    /// Both the cancel path and the connect continuation can reach a runner that
    /// needs retiring, and Shutdown is not safe to issue twice, so the set of
    /// in-flight retirements is the gate rather than a per-call flag.
    /// </summary>
    private async Task Retire(NetworkRunner doomed)
    {
        if (doomed == null || !retiring.Add(doomed))
        {
            return;
        }

        if (runner == doomed)
        {
            runner = null;
        }

        try
        {
            doomed.RemoveCallbacks(this);
            if (doomed.IsRunning)
            {
                // Leaves the Photon session so no room is left open behind us.
                await doomed.Shutdown();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PHOTON] Shutdown threw: {exception.Message}");
        }
        finally
        {
            retiring.Remove(doomed);
            if (doomed != null && doomed.gameObject != null)
            {
                UnityEngine.Object.Destroy(doomed.gameObject);
            }
            DisposeCancellation();
        }
    }

    /// <summary>Disposes the token source only once StartGame has stopped reading it.</summary>
    private void DisposeCancellation()
    {
        if (connecting != null)
        {
            return;
        }

        try
        {
            cancellation?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        cancellation = null;
    }

    // --- INetworkRunnerCallbacks -------------------------------------------------

    public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
    {
        Debug.Log($"[PHOTON] Player joined: {player}. " +
                  $"count={networkRunner.SessionInfo?.PlayerCount}");
        EvaluateSessionFull();
    }

    public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
    {
        Debug.Log($"[PHOTON] Player left: {player}.");
    }

    public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[PHOTON] Runner shutdown: {shutdownReason}");
        if (searching)
        {
            Report(false, shutdownReason.ToString());
        }
    }

    public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[PHOTON] Disconnected: {reason}");
        if (searching)
        {
            Report(false, reason.ToString());
        }
    }

    public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Report(false, reason.ToString());
    }

    public void OnConnectedToServer(NetworkRunner networkRunner) =>
        Debug.Log("[PHOTON] Connected to server.");

    public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken) { }
    public void OnInput(NetworkRunner networkRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner networkRunner) { }
    public void OnSceneLoadStart(NetworkRunner networkRunner) { }
    public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
    public void OnUserSimulationMessage(NetworkRunner networkRunner, SimulationMessagePtr message) { }
}
