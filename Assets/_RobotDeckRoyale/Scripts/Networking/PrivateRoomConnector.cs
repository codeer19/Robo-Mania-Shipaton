using System;
using System.Threading;
using Fusion;
using UnityEngine;

/// <summary>
/// Connects to a named private room and stops there.
///
/// Deliberately separate from MatchmakingController. That class exists to race a
/// human search against a timeout and fall back to the AI match, which is
/// correct for Play and wrong for a private room: CREATE and JOIN are agreements
/// between two people, so a room with one player waits rather than substituting a
/// bot. Keeping the two paths apart is what guarantees that.
///
/// Reaching the room means "connected to the session" and nothing more. The match
/// starts when the creator says so, not when the second player arrives.
/// </summary>
public sealed class PrivateRoomConnector
{
    /// <summary>A private room is always exactly two humans.</summary>
    public const int RoomSize = 2;

    private NetworkRunner runner;
    private CancellationTokenSource cancellation;
    private bool connecting;

    /// <summary>Uppercased, trimmed room code, so ' abc123 ' and 'ABC123' are one room.</summary>
    public static string Normalize(string roomCode) =>
        (roomCode ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// How many times a join is attempted before the room is reported as failed.
    ///
    /// Reaching Photon's cloud is not reliably a one-shot operation: an observed
    /// run failed with DisconnectException/ExceptionOnConnect and the very next
    /// attempt, same build and same room, connected normally. Without a retry that
    /// single blip ends the room, and on a phone - where a brief signal drop is
    /// routine - it is the difference between a match and a dead screen.
    /// </summary>
    private const int ConnectAttempts = 3;

    /// <summary>Pause between attempts, so a momentary outage has time to clear.</summary>
    private const int RetryDelayMilliseconds = 1200;

    /// <summary>Builds the runner this connector owns. One per attempt.</summary>
    private void CreateRunner()
    {
        var runnerObject = new GameObject("PrivateRoomRunner");
        UnityEngine.Object.DontDestroyOnLoad(runnerObject);
        runner = runnerObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;

        var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
        sceneManager.IsSceneTakeOverEnabled = true;
    }

    private async System.Threading.Tasks.Task<StartGameResult> StartWithRetries(
        StartGameArgs args, string session)
    {
        StartGameResult result = null;

        for (int attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            // A fresh runner per attempt. A failed StartGame leaves the runner shut
            // down, and Fusion expects a new instance for a new connection rather
            // than a restarted corpse carrying the previous attempt's state.
            await TearDown();
            cancellation.Token.ThrowIfCancellationRequested();
            CreateRunner();
            NetworkStartupDiagnostics.Connection(runner, session, attempt, "StartGame");
            result = await runner.StartGame(args);

            if (result.Ok)
            {
                if (attempt > 1)
                {
                    Debug.Log($"[PRIVATE ROOM] Connected to '{session}' on attempt {attempt}.");
                }

                return result;
            }

            // A cancelled join is the player leaving, not a failure to retry.
            if (result.ShutdownReason == ShutdownReason.GameNotFound || result.ShutdownReason == ShutdownReason.GameIsFull ||
                (cancellation != null && cancellation.IsCancellationRequested))
            {
                return result;
            }

            if (attempt < ConnectAttempts)
            {
                Debug.LogWarning(
                    $"[PRIVATE ROOM] Attempt {attempt}/{ConnectAttempts} failed " +
                    $"({result.ShutdownReason}); retrying in {RetryDelayMilliseconds}ms.");
                await System.Threading.Tasks.Task.Delay(RetryDelayMilliseconds, cancellation.Token);
            }
        }

        return result;
    }

    public async void Connect(string roomCode, Action<NetworkRunner> onConnected, Action<string> onFailed, PrivateRoomAction action = PrivateRoomAction.Join)
    {
        if (!MatchSessionContext.AssertPrivateRoom("Connect"))
        {
            onFailed?.Invoke("PRIVATE_ROOM_STATE_INVALID");
            return;
        }
        if (connecting)
        {
            Debug.LogWarning("[PRIVATE ROOM] Connect called while already connecting; ignoring.");
            return;
        }

        connecting = true;
        string session = Normalize(roomCode);

        try
        {
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();

            // Started with no network scene on purpose.
            //
            // Naming the menu here makes Fusion load it as the network scene, and
            // that load destroys and rebuilds the scene - taking the lobby objects
            // spawned moments earlier with it. Measured: the session object and
            // both player records were despawned immediately after spawning, so
            // the joining client never received the creator's session at all.
            //
            // Fusion's "no network scene" warning is about scene-placed
            // NetworkObjects, which this project has none of. Objects created with
            // Runner.Spawn replicate either way. The arena is loaded later through
            // Runner.LoadScene, which is what moves both clients together.
            var args = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = session,
                PlayerCount = RoomSize,
                EnableClientSessionCreation = action == PrivateRoomAction.Create,
                IsVisible = false,
                StartGameCancellationToken = cancellation.Token
            };

            Debug.Log($"[PRIVATE ROOM] Connecting to '{session}' (max {RoomSize}).");

            StartGameResult result = await StartWithRetries(args, session);

            if (!result.Ok)
            {
                // The reason is spelled out rather than reduced to "Error": the
                // shutdown reason alone is the same word for a cancelled join, a
                // wrong app id and a phone that briefly lost signal.
                Debug.LogWarning(
                    $"[PRIVATE ROOM] Connect failed after {ConnectAttempts} attempts. " +
                    $"reason={result.ShutdownReason} message={result.ErrorMessage}");
                await TearDown();
                connecting = false;
                onFailed?.Invoke(result.ShutdownReason == ShutdownReason.GameNotFound ? "ROOM NOT FOUND" : result.ShutdownReason.ToString());
                return;
            }

            Debug.Log(
                $"[PRIVATE ROOM] Connected. session={runner.SessionInfo?.Name} " +
                $"localPlayer={runner.LocalPlayer} " +
                $"players={runner.SessionInfo?.PlayerCount}/{RoomSize} " +
                $"master={runner.IsSharedModeMasterClient}");

            NetworkRunner connected = runner;
            runner = null;
            connecting = false;
            onConnected?.Invoke(connected);
        }
        catch (OperationCanceledException)
        {
            await TearDown();
            connecting = false;
            Debug.Log("[PRIVATE ROOM] Connect cancelled.");
        }
        catch (Exception exception)
        {
            connecting = false;
            await TearDown();
            onFailed?.Invoke(exception.Message);
        }
    }

    public void Cancel()
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        _ = TearDown();
    }

    private async System.Threading.Tasks.Task TearDown()
    {
        NetworkRunner doomed = runner;
        runner = null;
        if (doomed == null) return;

        try
        {
            if (doomed.IsRunning) await doomed.Shutdown();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PRIVATE ROOM] Shutdown threw: {exception.Message}");
        }
        finally
        {
            if (doomed != null && doomed.gameObject != null)
            {
                UnityEngine.Object.Destroy(doomed.gameObject);
            }
        }
    }
}
