using Fusion;
using UnityEngine;

/// <summary>Where a private room is in its lifecycle. One authoritative value.</summary>
public enum LobbyState
{
    WaitingForPlayers,
    Ready,
    Countdown,
    LoadingGame
}

/// <summary>
/// The single replicated object that governs a private room.
///
/// It exists so both clients read one shared truth instead of each keeping their
/// own booleans: who created the room, what state the lobby is in, and one timer
/// driving the countdown. Two independent local countdowns would drift, and a
/// per-client "am I starting" flag is what previously let one side march into the
/// arena while the other waited.
///
/// Spawned once by the shared-mode master client, which in a private room is the
/// player who created it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PrivateLobbySession : NetworkBehaviour
{
    public const float CountdownSeconds = 5f;

    [Networked] public PlayerRef CreatorPlayer { get; set; }
    [Networked] public LobbyState State { get; set; }
    [Networked] public TickTimer Countdown { get; set; }

    public static PrivateLobbySession Instance { get; private set; }

    private LobbyState lastLoggedState = (LobbyState)(-1);
    private bool arenaLoadRequested;
    private bool startRequested;

    /// <summary>True when this client created the room and may press Start.</summary>
    public bool IsLocalPlayerCreator =>
        Runner != null && Runner.LocalPlayer == CreatorPlayer;

    /// <summary>Seconds left on the shared countdown, or 0 when it is not running.</summary>
    public float CountdownRemaining
    {
        get
        {
            if (State != LobbyState.Countdown) return 0f;
            float? remaining = Countdown.RemainingTime(Runner);
            return remaining.HasValue ? Mathf.Max(0f, remaining.Value) : 0f;
        }
    }

    public override void Spawned()
    {
        Instance = this;

        if (HasStateAuthority)
        {
            // The spawner is the first player in the room, which is the creator.
            CreatorPlayer = Runner.LocalPlayer;
            State = LobbyState.WaitingForPlayers;
        }

        Debug.Log(
            $"[PRIVATE LOBBY] Session={Runner.SessionInfo?.Name} " +
            $"LocalPlayer={Runner.LocalPlayer} CreatorPlayer={CreatorPlayer} " +
            $"PlayerCount={CountPlayers()} isCreator={IsLocalPlayerCreator}");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
        {
            return;
        }

        if (startRequested)
        {
            startRequested = false;
            if (CanStart(out _))
            {
                Countdown = TickTimer.CreateFromSeconds(Runner, CountdownSeconds);
                State = LobbyState.Countdown;
                Debug.Log($"[LOBBY STATE] Countdown started ({CountdownSeconds}s) by {Runner.LocalPlayer}.");
            }
        }
        DriveState();
    }

    /// <summary>
    /// The team this client owns, derived from the replicated creator reference.
    ///
    /// Both clients run the same comparison against the same replicated value, so
    /// they cannot disagree: the creator is Blue and the joining player is Red on
    /// both devices. There is one world, not a mirrored one per client.
    ///
    /// Read by the director rather than applied from this object's
    /// FixedUpdateNetwork, because on the joining client this object is a proxy
    /// and Shared Mode does not tick FixedUpdateNetwork on proxies - which is why
    /// the joiner previously stayed Blue.
    /// </summary>
    public bool TryGetLocalTeam(out TeamSide team)
    {
        team = TeamSide.SideA;
        if (Runner == null || !CreatorPlayer.IsRealPlayer) return false;

        team = Runner.LocalPlayer == CreatorPlayer ? TeamSide.SideA : TeamSide.SideB;
        return true;
    }

    /// <summary>Advances the shared lobby state. Only the state authority runs this.</summary>
    private void DriveState()
    {
        // Once the arena has been asked for, this object's job is done.
        //
        // Without this latch it keeps running inside the arena: the lobby records
        // are gone, so it falls back to WaitingForPlayers, then sees two players
        // again and climbs back to Ready - and a second countdown would load the
        // arena a second time underneath a match already in progress.
        if (arenaLoadRequested)
        {
            return;
        }

        int players = CountPlayers();

        switch (State)
        {
            case LobbyState.WaitingForPlayers:
                if (players >= 2 && BothRecordsComplete()) State = LobbyState.Ready;
                break;

            case LobbyState.Ready:
                if (players < 2 || !BothRecordsComplete()) State = LobbyState.WaitingForPlayers;
                break;

            case LobbyState.Countdown:
                // Losing a player mid-countdown must not drop one human alone into
                // the arena, so the countdown is abandoned rather than finished.
                if (players < 2 || !BothRecordsComplete())
                {
                    Debug.Log("[LOBBY STATE] Countdown cancelled - a player left.");
                    Countdown = default;
                    State = LobbyState.WaitingForPlayers;
                    break;
                }

                if (Countdown.ExpiredOrNotRunning(Runner))
                {
                    State = LobbyState.LoadingGame;
                }

                break;
        }

        LogStateChange();

        if (State == LobbyState.LoadingGame && !arenaLoadRequested)
        {
            arenaLoadRequested = true;
            LoadArena();
        }
    }

    private void LogStateChange()
    {
        if (State == lastLoggedState) return;
        lastLoggedState = State;
        Debug.Log($"[LOBBY STATE] {State} players={CountPlayers()}");
    }

    /// <summary>
    /// Both players must have published a name and a skin before the match may
    /// start, so the arena never opens with half an identity.
    /// </summary>
    public bool BothRecordsComplete()
    {
        NetworkedLobbyPlayer local = NetworkedLobbyPlayer.Local;
        NetworkedLobbyPlayer remote = NetworkedLobbyPlayer.Remote;
        return local != null && remote != null && local.HasIdentity && remote.HasIdentity && local.SideAssigned && remote.SideAssigned &&
            local.Runner == Runner && remote.Runner == Runner && local.Team != remote.Team &&
            (local.Owner == CreatorPlayer ? local.Team == TeamSide.SideA : local.Team == TeamSide.SideB) &&
            (remote.Owner == CreatorPlayer ? remote.Team == TeamSide.SideA : remote.Team == TeamSide.SideB);
    }

    /// <summary>
    /// Everything that must hold before Start may be pressed. Re-checked on click
    /// as well as for the button's enabled state, because a player can leave in
    /// the moment between the two.
    /// </summary>
    public bool CanStart(out string reason)
    {
        if (MatchSessionContext.Type != MatchType.HumanOnline || MatchSessionContext.EntryMode != MatchEntryMode.PrivateRoom)
        { reason = "not a private human match"; return false; }
        if (Runner == null || !Runner.IsRunning) { reason = "no runner"; return false; }
        if (!IsLocalPlayerCreator) { reason = "not the room creator"; return false; }
        if (State != LobbyState.Ready) { reason = $"lobby is {State}"; return false; }
        if (CountPlayers() != 2) { reason = $"players={CountPlayers()}"; return false; }

        NetworkedLobbyPlayer remote = NetworkedLobbyPlayer.Remote;
        if (remote == null || remote.Runner != Runner || remote.Owner == Runner.LocalPlayer || !remote.Owner.IsRealPlayer)
        { reason = "no remote human record"; return false; }
        if (!remote.Object.StateAuthority.IsRealPlayer) { reason = "opponent has no PlayerRef"; return false; }
        if (!BothRecordsComplete()) { reason = "names or skins not received"; return false; }

        reason = null;
        return true;
    }

    /// <summary>Starts the shared countdown. Creator only.</summary>
    public void RequestStart()
    {
        if (!CanStart(out string reason))
        {
            Debug.LogWarning($"[PRIVATE LOBBY] Start refused: {reason}.");
            return;
        }

        // The creator holds state authority over this object, so it can write the
        // timer directly and every client reads the same expiry tick.
        startRequested = true;
    }

    /// <summary>
    /// Moves the whole session into the arena.
    ///
    /// Driven by the one client with state authority, through Fusion rather than
    /// Unity's SceneManager, so both peers transition together and the network
    /// scene stays consistent. A local load on one side is what leaves the other
    /// stranded in a scene the session does not know about.
    /// </summary>
    private void LoadArena()
    {
        if (!MatchSessionContext.AssertPrivateRoom("Before arena load")) return;
        Debug.Log($"[SCENE] Creator loading arena for the session. buildIndex={FrontendFlow.ArenaBuildIndex}");
        Runner.LoadScene(
            SceneRef.FromIndex(FrontendFlow.ArenaBuildIndex),
            UnityEngine.SceneManagement.LoadSceneMode.Single,
            UnityEngine.SceneManagement.LocalPhysicsMode.None,
            true);
    }

    public int CountPlayers()
    {
        if (Runner == null) return 0;
        int count = 0;
        foreach (PlayerRef _ in Runner.ActivePlayers) count++;
        return count;
    }
}
