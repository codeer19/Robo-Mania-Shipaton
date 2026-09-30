using Fusion;
using UnityEngine;

/// <summary>One adopted runner, one explicit local PlayerRoot, one publisher per PlayerRef.</summary>
[DisallowMultipleComponent]
public sealed class OnlineMatchDirector : MonoBehaviour
{
    public static OnlineMatchDirector Instance { get; private set; }
    private NetworkRunner runner;
    private NetworkedPlayerAvatar localAvatar;
    private FortressDuelManager arena;
    private bool spawnRequested, localPlaced, combatSpawnRequested;
    public NetworkRunner Runner => runner;
    public bool HasRunner => runner != null && runner.IsRunning;
    public Camera MatchCamera => arena != null ? arena.MatchCamera : null;
    public float IntroDuration => arena != null ? arena.OnlineIntroDuration : 2.8f;
    public NetworkedPlayerAvatar LocalAvatar => localAvatar != null && localAvatar.Object != null && localAvatar.Object.IsValid ? localAvatar : null;
    public NetworkedPlayerAvatar RemoteAvatar
    {
        get
        {
            if (!HasRunner) return null;
            foreach (PlayerRef owner in runner.ActivePlayers)
                if (owner != runner.LocalPlayer && NetworkedPlayerAvatar.TryGet(runner, owner, out var avatar))
                    return avatar;
            return null;
        }
    }
    public bool LocalDependenciesReady
    {
        get
        {
            NetworkedPlayerAvatar local = LocalAvatar, remote = RemoteAvatar;
            return localPlaced && arena != null && arena.LocalSkinReady &&
                local != null && local.ProfileReady && remote != null && remote.ProfileReady &&
                remote.Team != local.Team && CountPlayers() == 2;
        }
    }
    public bool BothPlayersReady
    {
        get
        {
            NetworkedPlayerAvatar local = LocalAvatar, remote = RemoteAvatar;
            return LocalDependenciesReady && local != null && remote != null &&
                local.ArenaReadyConfirmed && remote.ArenaReadyConfirmed;
        }
    }
    public NetworkedPlayerAvatar SideAAvatar
    {
        get
        {
            NetworkedPlayerAvatar local = LocalAvatar;
            if (local != null && local.Team == TeamSide.SideA) return local;
            NetworkedPlayerAvatar remote = RemoteAvatar;
            return remote != null && remote.Team == TeamSide.SideA ? remote : null;
        }
    }

    /// <summary>
    /// True once an adopted session has gone away underneath us.
    ///
    /// Distinguished from a deliberate teardown so the arena can leave for the
    /// menu the moment the peer drops, instead of polling a dead runner until a
    /// fixed timeout expires with nothing left to wait for.
    /// </summary>
    public bool ConnectionLost => runnerAdopted && !HasRunner;
    private bool runnerAdopted;
    private float adoptedAt;

    /// <summary>Retracts a cached publisher handle as its network state is torn down.</summary>
    public void ForgetAvatar(NetworkedPlayerAvatar avatar)
    {
        if (avatar == null || localAvatar != avatar) return;
        localAvatar = null;
        spawnRequested = false;
        Debug.Log("[AVATAR] Local publisher handle retracted by despawn.");
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void AdoptRunner(NetworkRunner connectedRunner)
    {
        if (runner == connectedRunner) return;
        if (HasRunner) { Debug.LogError("[ONLINE MATCH] Refusing a competing runner."); return; }
        runner = connectedRunner;
        localAvatar = null;
        arena = null;
        localPlaced = false;
        combatSpawnRequested = false;
        spawnRequested = false;
        runnerAdopted = true;
        adoptedAt = Time.unscaledTime;
        Debug.Log($"[RUNNER] GameObject={runner?.name} SessionName={runner?.SessionInfo?.Name} PlayerCount={runner?.SessionInfo?.PlayerCount} LocalPlayer={runner?.LocalPlayer} GameMode={runner?.GameMode} IsRunning={HasRunner}");
    }

    // Scene callbacks may precede MonoBehaviour Start. Queue, then bind serialized references.
    public void SpawnLocalAvatar() => spawnRequested = true;

    private void Update()
    {
        ReportStartupBlockers();
        if (HasRunner && LocalAvatar != null && localAvatar.Team == TeamSide.SideA && !runner.IsSceneManagerBusy &&
            NetworkedMatchState.Instance == null && !combatSpawnRequested)
        {
            combatSpawnRequested = true;
            var combatPrefab = Resources.Load<GameObject>("Networking/PF_OnlineMatchState");
            if (combatPrefab != null) runner.Spawn(combatPrefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
            else Debug.LogError("[COMBAT] Network match state prefab missing.");
        }
        if (!HasRunner || !spawnRequested || localAvatar != null || runner.IsSceneManagerBusy) return;
        var readyArena = FortressDuelManager.ActiveArena;
        if (readyArena == null || !readyArena.OnlineSceneReady) return;
        arena = readyArena;
        if (!localPlaced)
        {
            localPlaced = arena.PrepareOnlineLocalPlayer();
            if (!localPlaced) return;
        }
        var prefab = Resources.Load<GameObject>("Networking/PF_Spark_NetworkAvatar");
        if (prefab == null) { Debug.LogError("[AVATAR] Registered avatar prefab missing."); spawnRequested = false; return; }
        Transform source = arena.LocalPlayer.transform;
        NetworkObject spawned = runner.Spawn(prefab, source.position, source.rotation, runner.LocalPlayer,
            (r, obj) => obj.GetComponent<NetworkedPlayerAvatar>().InitializeLocal(r, source,
                MatchSessionContext.LocalSide, MatchSessionContext.LocalDisplayName, MatchSessionContext.LocalSkinId));
        localAvatar = spawned.GetComponent<NetworkedPlayerAvatar>();
        runner.SetPlayerObject(runner.LocalPlayer, spawned);
        Debug.Log($"[AVATAR] Bound PlayerRef={runner.LocalPlayer} NetworkId={spawned.Id} PlayerRoot={source.name} StateAuthority={spawned.StateAuthority}");
    }

    public bool LocalPlaced => localPlaced;

    /// <summary>
    /// Every condition the online start waits on, reported separately.
    ///
    /// The startup used to fail with one combined message that named none of its
    /// inputs, so a single unmet condition looked identical to all of them being
    /// unmet. Each value is listed so the blocking one is visible directly.
    /// </summary>
    public string DescribeStartupState()
    {
        FortressDuelManager active = arena != null ? arena : FortressDuelManager.ActiveArena;
        NetworkedPlayerAvatar local = LocalAvatar, remote = RemoteAvatar, sideA = SideAAvatar;
        var matchState = NetworkedMatchState.Instance;

        return
            $"t={(runnerAdopted ? Time.unscaledTime - adoptedAt : 0f):F1}s " +
            $"RunnerReady={HasRunner} " +
            $"ConnectionLost={ConnectionLost} " +
            $"PlayerCount={CountPlayers()} " +
            $"LocalAvatar={(local != null)} " +
            $"RemoteAvatar={(remote != null)} " +
            $"LocalProfile={(local != null && local.ProfileReady)} " +
            $"RemoteProfile={(remote != null && remote.ProfileReady)} " +
            $"OnlineMatchState={(matchState != null)} " +
            $"LocalPlaced={localPlaced} " +
            $"LocalSkinReady={(active != null && active.LocalSkinReady)} " +
            $"RemoteSideValid={(local != null && remote != null && remote.Team != local.Team)} " +
            $"LocalReady={(local != null && local.ArenaReadyConfirmed)} " +
            $"RemoteReady={(remote != null && remote.ArenaReadyConfirmed)} " +
            $"CombatReady={BothPlayersReady} " +
            $"IntroReady={(sideA != null && sideA.IntroStartedConfirmed)}";
    }

    private float nextBlockerReport;

    /// <summary>
    /// Names whichever condition is still holding the match closed, once a second.
    ///
    /// Every gate on the way into a match is a silent "return false" that is
    /// retried forever, so a single unmet condition on one client leaves both
    /// players standing still with nothing in the log to explain it. This only
    /// reports; it changes no state and gates nothing.
    /// </summary>
    private void ReportStartupBlockers()
    {
        if (MatchSessionContext.Type != MatchType.HumanOnline) return;
        if (Time.unscaledTime < nextBlockerReport) return;
        nextBlockerReport = Time.unscaledTime + 1f;

        if (BothPlayersReady && NetworkedMatchState.Instance != null) return;

        Debug.Log("[ONLINE STARTUP] " + DescribeStartupState());
    }

    private int CountPlayers()
    {
        int count = 0;
        if (HasRunner) foreach (var player in runner.ActivePlayers) count++;
        return count;
    }

    public async void EndOnlineMatch() => await ShutdownAsync();
    public async System.Threading.Tasks.Task ShutdownAsync()
    {
        // Cleared before the await so a deliberate teardown never looks like a
        // dropped connection to anything polling ConnectionLost.
        runnerAdopted = false;
        spawnRequested = false;
        localPlaced = false;
        combatSpawnRequested = false;
        localAvatar = null;
        arena = null;
        NetworkRunner doomed = runner;
        runner = null;
        if (doomed == null) return;
        PrivateLobbyDirector.Instance?.ForgetRunner(doomed);
        try { if (doomed.IsRunning) await doomed.Shutdown(); }
        catch (System.Exception exception) { Debug.LogWarning($"[ONLINE MATCH] Shutdown: {exception.Message}"); }
        finally { if (doomed != null) Destroy(doomed.gameObject); }
    }
}

