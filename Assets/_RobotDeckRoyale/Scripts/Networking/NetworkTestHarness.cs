using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Opt-in development driver. Uses real controller/fire/summon entry points; never writes player transforms.</summary>
public static class NetworkTestHarness
{
    public static bool IsActive { get; private set; }
    public static bool VerboseDiagnostics { get; set; }
    public static string NameOverride { get; private set; }
    public static string SkinOverride { get; private set; }
    public static string CommandPath { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { IsActive = false; NameOverride = SkinOverride = CommandPath = null; }
    public static void Configure(string name, string skin, string commandPath)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        IsActive = true; NameOverride = name; SkinOverride = skin; CommandPath = commandPath;
#endif
    }
    private static string Argument(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1];
        return null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string room = Argument("-roboAutoRoom");
        if (string.IsNullOrEmpty(room)) return;
        Configure(Argument("-roboName"), Argument("-roboSkin"), Argument("-roboCommands"));
        var go = new GameObject("DevelopmentAcceptanceDriver"); UnityEngine.Object.DontDestroyOnLoad(go);
        string action = Argument("-roboAction");
        // "QuickPlay" drives the Play button instead of a private room, so the
        // matchmaking path can be exercised from a build without a room code.
        if (action == "QuickPlay") go.AddComponent<NetworkTestHarnessRunner>().BeginQuickPlay();
        else go.AddComponent<NetworkTestHarnessRunner>().Begin(room, action == "Join" ? PrivateRoomAction.Join : PrivateRoomAction.Create);
#endif
    }
}
public sealed class NetworkTestHarnessRunner : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Serializable] private class Command { public int sequence; public string action; public float x,y,z,seconds; }
    [Serializable] private class AvatarState { public string owner,authority,name,skin,side; public bool local,visible,friendly; public Vector3 position,rendered,screen; }
    [Serializable] private class Snapshot
    {
        public string utc,frontend,session,localPlayer,phase,localSide,winner,commandResult;
        public int players,runners,offlineAI,hpA,hpB,heistA,heistB,chargeA,chargeB,cellId,shotCount,swarmCount,command;
        public bool cellAvailable,controls;
        public Vector3 localPosition,muzzle,cameraEuler,cellPosition;
        public AvatarState[] avatars;
        public OnlineSpidy[] spidys;
        public OnlineStructure[] structures;
        public float timeRemaining;
        public bool building, previewValid;
        public int turrets,pulseTowers,healingPads,overdrivePads,jammers,interceptors,buildPoints;
    }
    private int lastCommand;
    private string commandResult;
    private float nextPoll, driveUntil;
    private float profileUntil;
    private Vector2 drive;
    public void Begin(string roomCode, PrivateRoomAction action = PrivateRoomAction.Create) => StartCoroutine(Open(roomCode, action));
    private IEnumerator Open(string room, PrivateRoomAction action)
    {
        while (FrontendFlow.Instance == null || FrontendFlow.Instance.State != FrontendState.MainMenu) yield return null;
        FrontendFlow.Instance.StartPrivateRoom(room, action);
    }
    public void BeginQuickPlay() => StartCoroutine(OpenQuickPlay());
    private IEnumerator OpenQuickPlay()
    {
        while (FrontendFlow.Instance == null || FrontendFlow.Instance.State != FrontendState.MainMenu) yield return null;
        FrontendFlow.Instance.StartOnlineMatchmaking();
    }
    private void Update()
    {
        if (!NetworkTestHarness.IsActive) return;
        if (Time.unscaledTime < profileUntil) return;
        var arena = FortressDuelManager.ActiveArena;
        if (arena != null)
        {
            arena.LocalPlayer.SetExternalInputActive(true);
            arena.LocalPlayer.SetMoveInput(Time.unscaledTime < driveUntil ? drive : Vector2.zero);
        }
        if (Time.unscaledTime < nextPoll || string.IsNullOrEmpty(NetworkTestHarness.CommandPath)) return;
        nextPoll = Time.unscaledTime + 0.2f;
        try
        {
            if (File.Exists(NetworkTestHarness.CommandPath))
            {
                var command = JsonUtility.FromJson<Command>(File.ReadAllText(NetworkTestHarness.CommandPath));
                if (command != null && command.sequence > lastCommand) { lastCommand = command.sequence; Execute(command, arena); }
            }
            Capture(arena);
        }
        catch (IOException) { }
    }
    private void Execute(Command command, FortressDuelManager arena)
    {
        commandResult = command.action;
        if (command.action == "start") { PrivateLobbySession.Instance?.RequestStart(); return; }
        if (command.action == "leave") { FrontendFlow.Instance?.ReturnToMainMenu(); return; }
        if (command.action == "screenshot") { ScreenCapture.CaptureScreenshot(NetworkTestHarness.CommandPath + ".png"); return; }
        if (arena == null) { commandResult = "No arena"; return; }
        var aim = arena.LocalPlayer.GetComponent<RobotAimController>();
        var blaster = arena.LocalPlayer.GetComponent<RobotBlaster>();
        aim.SetExternalInputActive(true); blaster.SetExternalInputActive(true);
        switch (command.action)
        {
            case "move": drive = new Vector2(command.x, command.y); driveUntil = Time.unscaledTime + command.seconds; break;
            case "aim": aim.SnapAimTarget(new Vector3(command.x, command.y, command.z)); break;
            case "fire": commandResult = "fire=" + blaster.TryFire(); break;
            case "summon": NetworkedMatchState.Instance?.RequestSummon(); break;
            case "charge": NetworkedMatchState.Instance?.RPC_DevelopmentCharge(); break;
            case "build": arena.GetComponent<BuildPlacementController>().ToggleOnlineBuild(); break;
            case "select": arena.GetComponent<BuildPlacementController>().SelectBuildableType((BuildPlacementController.BuildableType)(int)command.x); break;
            case "undo": arena.GetComponent<BuildPlacementController>().UndoLast(); break;
            case "place":
                var builder = arena.GetComponent<BuildPlacementController>();
                var position = builder.SnapOnline(new Vector3(command.x,0,command.z));
                var rotation = builder.RotationOnline(builder.SelectedBuildable,Quaternion.Euler(0,command.y,0),MatchSessionContext.LocalSide);
                bool valid = builder.ValidateOnline(MatchSessionContext.LocalSide,builder.SelectedBuildable,position,rotation,NetworkedMatchState.Instance);
                commandResult = "preview=" + valid + "/sent=" + NetworkedMatchState.Instance.RequestPlacement(builder.SelectedBuildable,position,rotation);
                break;
            case "profile": profileUntil=Time.unscaledTime+21; gameObject.AddComponent<OnlinePerformanceCapture>().Begin(NetworkTestHarness.CommandPath+".profile.json"); break;
        }
        Debug.Log($"[ACCEPTANCE] command={lastCommand} result={commandResult}");
    }
    private void Capture(FortressDuelManager arena)
    {
        var director = OnlineMatchDirector.Instance;
        var runner = director != null && director.HasRunner ? director.Runner : PrivateLobbyDirector.Instance?.Runner;
        var match = NetworkedMatchState.Instance;
        var snapshot = new Snapshot { utc = DateTime.UtcNow.ToString("O"), frontend = FrontendFlow.Instance?.State.ToString(),
            session = runner?.SessionInfo?.Name, localPlayer = runner != null ? runner.LocalPlayer.ToString() : "none",
            players = runner != null ? runner.ActivePlayers.Count() : 0, runners = FindObjectsByType<Fusion.NetworkRunner>(FindObjectsSortMode.None).Count(r=>r.IsRunning),
            offlineAI = FindObjectsByType<FortressBotAI>(FindObjectsSortMode.None).Length,
            localSide = MatchSessionContext.LocalSide.ToString(), phase = arena?.CurrentPhase.ToString(),
            controls = arena != null && arena.LocalPlayer.enabled, command = lastCommand, commandResult = commandResult,
            localPosition = arena != null ? arena.LocalPlayer.transform.position : Vector3.zero,
            cameraEuler = arena != null ? arena.MatchCamera.transform.eulerAngles : Vector3.zero,
            muzzle = arena != null ? arena.LocalPlayer.GetComponent<RobotBlaster>().Muzzle.position : Vector3.zero,
            avatars = FindObjectsByType<NetworkedPlayerAvatar>(FindObjectsSortMode.None).Where(a=>a.Object!=null && a.Object.IsValid).Select(a=>new AvatarState {
                owner=a.OwnerPlayerRef.ToString(), authority=a.Object.StateAuthority.ToString(), name=a.DisplayName.ToString(), skin=a.SkinId.ToString(),
                side=a.Team.ToString(),local=a.IsLocalAvatar,visible=a.RemoteVisualRoot.activeInHierarchy,friendly=TeamSides.IsFriendly(a.Team),
                position=a.Position,rendered=a.transform.position,screen=arena!=null?arena.MatchCamera.WorldToViewportPoint(a.Position):Vector3.zero }).ToArray() };
        if (match != null && match.Object != null && match.Object.IsValid)
        {
            snapshot.hpA=match.PlayerHealth(TeamSide.SideA); snapshot.hpB=match.PlayerHealth(TeamSide.SideB);
            snapshot.heistA=match.HeistHealthA; snapshot.heistB=match.HeistHealthB;
            snapshot.chargeA=match.Charge(TeamSide.SideA); snapshot.chargeB=match.Charge(TeamSide.SideB);
            snapshot.cellId=match.CellId; snapshot.cellAvailable=match.CellAvailable; snapshot.cellPosition=match.CellPosition;
            snapshot.shotCount=match.MissileSequence; snapshot.swarmCount=match.SwarmSequence; snapshot.winner=match.WinningSide.ToString();
            snapshot.spidys=Enumerable.Range(0,NetworkedMatchState.SpidyCapacity).Select(i=>match.Spidys[i]).ToArray();
            snapshot.structures=Enumerable.Range(0,NetworkedMatchState.StructureCapacity).Select(i=>match.Structures[i]).ToArray();
            snapshot.timeRemaining=match.MatchEnd.RemainingTime(match.Runner) ?? 0;
            snapshot.building=match.IsBuilding(MatchSessionContext.LocalSide);
            var side=MatchSessionContext.LocalSide;
            snapshot.turrets=match.Remaining(side,BuildPlacementController.BuildableType.Turret);
            snapshot.pulseTowers=match.Remaining(side,BuildPlacementController.BuildableType.PulseTower);
            snapshot.healingPads=match.Remaining(side,BuildPlacementController.BuildableType.HealingPad);
            snapshot.overdrivePads=match.Remaining(side,BuildPlacementController.BuildableType.OverdrivePad);
            snapshot.jammers=match.Remaining(side,BuildPlacementController.BuildableType.RecoveryJammer);
            snapshot.interceptors=match.Remaining(side,BuildPlacementController.BuildableType.MissileInterceptor);
            snapshot.buildPoints=match.BuildPointsRemaining(side);
        }
        File.WriteAllText(NetworkTestHarness.CommandPath + ".state.json", JsonUtility.ToJson(snapshot,true));
    }
#else
    public void Begin(string roomCode, PrivateRoomAction action = PrivateRoomAction.Create) { }
    public void BeginQuickPlay() { }
#endif
}
