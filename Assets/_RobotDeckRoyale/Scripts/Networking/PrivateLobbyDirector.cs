using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// Owns the connected runner while a private room sits in the lobby, and hands
/// the same runner to gameplay when the arena opens.
///
/// One runner covers menu, lobby, arena and back. Creating a second one for the
/// match would put the two clients in separate simulations while both still
/// reported a healthy session - which looks exactly like an opponent who never
/// appears.
/// </summary>
[DisallowMultipleComponent]
public sealed class PrivateLobbyDirector : MonoBehaviour, INetworkRunnerCallbacks
{
    public static PrivateLobbyDirector Instance { get; private set; }

    private NetworkRunner runner;
    private NetworkObject localRecord;
    private bool sessionSpawnAttempted;

    /// <summary>
    /// False once the match has left for the arena.
    ///
    /// Without it the director kept respawning lobby records and a second lobby
    /// session inside the arena, because its Update never stopped looking for
    /// them.
    /// </summary>
    private bool inLobby;

    /// <summary>Latched once the countdown begins; never rebuilds lobby objects after.</summary>
    private bool matchStarting;

    private void CloseLobby(string reason)
    {
        if (!inLobby) return;

        inLobby = false;
        localRecord = null;
        Debug.Log($"[PRIVATE LOBBY] Lobby closed ({reason}).");
    }

    public NetworkRunner Runner => runner;
    public bool IsActive => runner != null && runner.IsRunning;
    public string RoomCode { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static PrivateLobbyDirector Ensure()
    {
        if (Instance == null)
        {
            new GameObject("PrivateLobbyDirector").AddComponent<PrivateLobbyDirector>();
        }

        return Instance;
    }

    /// <summary>Takes the connected runner and starts building the lobby on it.</summary>
    public void EnterLobby(NetworkRunner connectedRunner, string roomCode)
    {
        if (!MatchSessionContext.AssertPrivateRoom("Lobby open")) return;
        runner = connectedRunner;
        RoomCode = roomCode;
        sessionSpawnAttempted = false;
        inLobby = true;
        matchStarting = false;

        if (runner == null)
        {
            Debug.LogError("[PRIVATE LOBBY] Entered with no runner.");
            return;
        }

        runner.AddCallbacks(this);

        Debug.Log(
            $"[PRIVATE LOBBY] Session={runner.SessionInfo?.Name} " +
            $"LocalPlayer={runner.LocalPlayer} PlayerCount={CountPlayers()} " +
            $"master={runner.IsSharedModeMasterClient}");

        SpawnLobbyObjects();

        // Announce the room so the portal's friends UI can offer it. Without this
        // the room exists only inside the game and an invited friend has nothing
        // to join.
        PublishRoomState();
        PrivateRoomAIAudit.Capture("LobbyOpened");
    }

    private void Update()
    {
        if (!IsActive || !inLobby) return;

        // Closed as soon as the shared state says the match is starting, rather
        // than from a scene callback. OnSceneLoadStart did not arrive on either
        // client, so the director kept rebuilding lobby objects throughout the
        // arena load - the session was respawned ten times in one transition.
        PrivateLobbySession live = PrivateLobbySession.Instance;
        if (live != null && (live.State == LobbyState.Countdown || live.State == LobbyState.LoadingGame))
        {
            bool wasStarting = matchStarting;
            matchStarting = true;
            // The portal must stop offering this room the moment the countdown
            // begins, not merely when the seats fill.
            if (!wasStarting) PublishRoomState();
        }

        if (matchStarting)
        {
            MaintainLocalTeamAndIdentity();
            CloseLobby("match is starting");
            return;
        }

        // The master flag and the session object can both arrive a frame or two
        // after the join completes, so this keeps trying rather than depending on
        // the ordering of a single callback.
        SpawnLobbyObjects();
        MaintainLocalTeamAndIdentity();
    }

    /// <summary>
    /// Keeps this client's team and identity current while the lobby is open, and
    /// mirrors them into the match context.
    ///
    /// Runs here, in a plain Update, because it must happen on both clients. The
    /// joining client only ever holds a proxy of the lobby session, and Shared
    /// Mode does not run FixedUpdateNetwork on proxies, so driving this from the
    /// session object left the joiner permanently on the creator's team.
    ///
    /// The context copy matters because the lobby records are despawned by the
    /// arena scene load; without it the match would start with no idea who this
    /// player is or which side they are on.
    /// </summary>
    private void MaintainLocalTeamAndIdentity()
    {
        PrivateLobbySession session = PrivateLobbySession.Instance;
        NetworkedLobbyPlayer local = NetworkedLobbyPlayer.Local;
        if (session == null || local == null) return;

        if (session.TryGetLocalTeam(out TeamSide team))
        {
            local.AssignTeam(team);
            MatchSessionContext.SetLocalIdentity(
                local.DisplayName.Value, local.SkinId.Value, team);
        }
    }

    private void SpawnLobbyObjects()
    {
        if (runner == null || !runner.IsRunning) return;

        // Guarded on the object being absent rather than on a "tried once" flag, so
        // the session is rebuilt if it is ever lost. Spawned() sets Instance in the
        // same frame, so this cannot spawn two.
        if (runner.IsSharedModeMasterClient && PrivateLobbySession.Instance == null)
        {
            GameObject prefab = Resources.Load<GameObject>("Networking/PF_LobbySession");
            if (prefab == null)
            {
                if (!sessionSpawnAttempted)
                {
                    sessionSpawnAttempted = true;
                    Debug.LogError("[PRIVATE LOBBY] PF_LobbySession missing from Resources/Networking.");
                }

                return;
            }

            runner.Spawn(prefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
            Debug.Log("[PRIVATE LOBBY] Lobby session object spawned by the creator.");
        }

        if (localRecord == null)
        {
            GameObject prefab = Resources.Load<GameObject>("Networking/PF_LobbyPlayer");
            if (prefab == null)
            {
                Debug.LogError("[PRIVATE LOBBY] PF_LobbyPlayer missing from Resources/Networking.");
                return;
            }

            localRecord = runner.Spawn(prefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
            Debug.Log($"[PRIVATE LOBBY] Local lobby record spawned for {runner.LocalPlayer}.");
        }
    }

    /// <summary>
    /// Keeps the portal's view of this room in step with the seat count, so a
    /// full room stops being advertised as joinable.
    /// </summary>
    private void PublishRoomState()
    {
        if (string.IsNullOrEmpty(RoomCode)) return;
        bool joinable = !matchStarting && CountPlayers() < PrivateRoomConnector.RoomSize;
        CrazyGamesPlatformService.PublishRoom(RoomCode, joinable);
    }

    public int CountPlayers()
    {
        if (runner == null) return 0;
        int count = 0;
        foreach (PlayerRef _ in runner.ActivePlayers) count++;
        return count;
    }

    /// <summary>
    /// Leaves the room and tears the session down.
    ///
    /// The runner is destroyed rather than kept for reuse: Fusion expects a fresh
    /// runner for a new connection, and a shut-down one carries stale callbacks
    /// and session state into the next room.
    /// </summary>
    public void ForgetRunner(NetworkRunner value)
    {
        if (runner != value) return;
        runner.RemoveCallbacks(this);
        runner = null; localRecord = null; inLobby = false; matchStarting = false;
    }

    public async void LeaveLobby() => await LeaveLobbyAsync();
    public async System.Threading.Tasks.Task LeaveLobbyAsync()
    {
        NetworkRunner doomed = runner;
        runner = null;
        localRecord = null;
        sessionSpawnAttempted = false;
        RoomCode = null;

        if (doomed == null) return;

        try
        {
            doomed.RemoveCallbacks(this);
            if (doomed.IsRunning) await doomed.Shutdown();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PRIVATE LOBBY] Shutdown threw: {exception.Message}");
        }
        finally
        {
            if (doomed != null && doomed.gameObject != null) Destroy(doomed.gameObject);
            Debug.Log("[PRIVATE LOBBY] Left the room and destroyed the runner.");
        }
    }

    /// <summary>
    /// Hands the live runner to gameplay once Fusion has finished loading the
    /// arena on this client. Both clients arrive here through the same callback,
    /// so neither can start while the other is still loading its scene.
    /// </summary>
    public void OnSceneLoadDone(NetworkRunner networkRunner)
    {
        string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (active != FrontendFlow.GameplayScene)
        {
            Debug.Log($"[SCENE] Network scene load done: {active} (not the arena).");
            return;
        }

        // The lobby is over: stop rebuilding lobby objects, which would otherwise
        // reappear inside the arena.
        inLobby = false;
        localRecord = null;
        if (!MatchSessionContext.AssertPrivateRoom("Arena loaded")) return;
        PrivateRoomAIAudit.Capture("ArenaLoaded");

        Debug.Log(
            $"[SCENE] Arena Loaded. localPlayer={networkRunner.LocalPlayer} " +
            $"team={MatchSessionContext.LocalSide} name={MatchSessionContext.LocalDisplayName} " +
            $"skin={MatchSessionContext.LocalSkinId}");

        OnlineMatchDirector director = OnlineMatchDirector.Instance;
        if (director == null)
        {
            director = new GameObject("OnlineMatchDirector").AddComponent<OnlineMatchDirector>();
        }

        director.AdoptRunner(networkRunner);
        director.SpawnLocalAvatar();
    }

    public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef player)
    {
        Debug.Log($"[PRIVATE LOBBY] Player joined: {player}. PlayerCount={CountPlayers()}");
        PublishRoomState();
    }

    public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef player)
    {
        Debug.Log($"[PRIVATE LOBBY] Player left: {player}. PlayerCount={CountPlayers()}");
        PublishRoomState();
    }

    public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[PRIVATE LOBBY] Runner shutdown: {shutdownReason}");
    }

    public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[PRIVATE LOBBY] Disconnected: {reason}");
    }

    public void OnConnectedToServer(NetworkRunner networkRunner) { }
    public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken) { }
    public void OnInput(NetworkRunner networkRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner networkRunner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    /// <summary>
    /// Closes the lobby the moment a scene transition begins.
    ///
    /// Doing this only once the load finished left a window where the session and
    /// record had already been despawned by the transition but the director still
    /// thought it was in a lobby - so it respawned them, dozens of times, and the
    /// rebuilt session started climbing back towards another countdown.
    /// </summary>
    public void OnSceneLoadStart(NetworkRunner networkRunner)
    {
        if (!inLobby) return;

        inLobby = false;
        localRecord = null;
        Debug.Log("[PRIVATE LOBBY] Scene transition started; lobby closed.");
    }
    public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
    public void OnUserSimulationMessage(NetworkRunner networkRunner, SimulationMessagePtr message) { }
}
