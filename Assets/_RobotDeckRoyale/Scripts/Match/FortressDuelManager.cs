using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public enum FortressDuelPhase
{
    // Preserve the original numeric values because other scene tooling may
    // serialize or compare them.
    Build = 0,
    Combat = 1,
    Finished = 2,
    Intro = 3,
    Rumble = 4
}

[DefaultExecutionOrder(-150)]
public class FortressDuelManager : MonoBehaviour
{
    public static FortressDuelManager ActiveArena { get; private set; }
    [Header("Online Player Foundation")]
    [SerializeField] private Transform blueSpawn;
    [SerializeField] private Transform redSpawn;
    [SerializeField] private BotMatchBootstrap botMatchBootstrap;
    [SerializeField] private Behaviour[] offlineOnlyBehaviours;
    [SerializeField] private GameObject[] offlineOnlyObjects;
    public bool OnlineSceneReady { get; private set; }
    public RobotPlayerController LocalPlayer => playerMovement;
    public Camera MatchCamera => matchCamera;

    /// <summary>
    /// The match is over and a full-screen result or matchmaking screen now covers
    /// the arena: stop rendering it (the audio listener on the camera object stays
    /// on for UI sound). Presentation only - no match or network state changes.
    /// </summary>
    public void HidePresentation()
    {
        if (matchCamera != null) matchCamera.enabled = false;
        CombatFeedbackOverlay.GetOrCreate().HideRespawnCountdown();
    }
    public float OnlineIntroDuration => versusIntroDuration;
    public float OnlineCombatDuration => combatPhaseDuration;
    /// <summary>Shared by the online build phase and the offline one; one authored value.</summary>
    public float OnlineBuildDuration => buildPhaseDuration;
    public float OnlineDrawTolerance => timeoutDrawTolerance;
    private int lastOnlineSecond = -1;
    public void ShowOnlineTime(float seconds)
    {
        timeRemaining = seconds;
        int value = Mathf.CeilToInt(seconds);
        if (value == lastOnlineSecond) return;
        lastOnlineSecond = value;
        if (phaseText != null) phaseText.text = $"{value / 60}:{value % 60:00}";
    }
    public NetworkedPlayerAvatar OnlineOpponent => OnlineMatchDirector.Instance?.RemoteAvatar;
    public bool LocalSkinReady => localPlayerObject != null &&
        localPlayerObject.GetComponentInChildren<SparkSkinVisual>(true)?.AppliedSkinId == MatchSessionContext.LocalSkinId;

    /// <summary>Diagnostic only: whether the team spawn anchors are wired in the scene.</summary>
    public bool HasTeamSpawns => blueSpawn != null && redSpawn != null;

    /// <summary>Diagnostic only: the skin id actually applied to the local player, if any.</summary>
    public string AppliedLocalSkinId => localPlayerObject != null
        ? localPlayerObject.GetComponentInChildren<SparkSkinVisual>(true)?.AppliedSkinId
        : null;
    private Collider[][] onlineHeistColliders;
    public SwarmChargeController OnlineEconomy => GetComponent<SwarmChargeController>();
    public Damageable Heist(TeamSide side) => side == TeamSide.SideA ? blueVault : redVault;
    public Transform Spawn(TeamSide side) => side == TeamSide.SideA ? blueSpawn : redSpawn;
    public Collider[] HeistColliders(TeamSide side)
    {
        if (onlineHeistColliders == null) onlineHeistColliders = new[] {
            blueVault.GetComponentsInChildren<Collider>(), redVault.GetComponentsInChildren<Collider>() };
        return onlineHeistColliders[(int)side];
    }
    public void SetOnlinePlayerAlive(bool alive)
    {
        if (!IsOnline || CurrentPhase != FortressDuelPhase.Combat) return;
        SetPlayerControls(alive);
    }
    public void RespawnOnlinePlayer()
    {
        ArenaTeamPlacement.PlaceLocalPlayer(playerMovement, MatchSessionContext.LocalTeam, blueSpawn, redSpawn);
        SpawnPadPresentation.NotifySpawn(MatchSessionContext.LocalSide);
        playerBlaster.RestoreFullAmmo();
        cameraFollow.SetOnlinePerspective(MatchSessionContext.LocalSide, playerMovement.transform);
    }
    public void EndOnlineMatch(TeamSide winner, bool draw = false, bool timedOut = false)
    {
        if (!IsOnline) return;
        Debug.Log($"[RESULT VIEW] WinningSide={winner} LocalSide={MatchSessionContext.LocalSide} result={(TeamSides.IsFriendly(winner) ? "VICTORY" : "DEFEAT")}");
        // Existing result UI takes friendly/enemy presentation, never network side identity.
        FinishMatch(draw ? (FortressTeam?)null : TeamSides.PresentationTeam(winner), timedOut ? "TIME LIMIT" : "HEIST DESTROYED");
    }
    private bool IsOnline => MatchSessionContext.Type == MatchType.HumanOnline;
    private const int VictoryCoinReward = MatchRewards.Win;
    private const int DrawCoinReward = MatchRewards.Draw;
    private const int DefeatCoinReward = MatchRewards.Loss;
    private const int VictoryExperienceReward = 30;
    private const int DrawExperienceReward = 20;
    private const int DefeatExperienceReward = 12;

    [Header("Phase Settings")]
    [Tooltip("The one build-phase length. Offline it drives the local countdown; online the " +
             "match authority starts the networked BuildEnd timer from it (OnlineBuildDuration).")]
    [SerializeField] private float buildPhaseDuration = 15f;
    [SerializeField, Min(0.2f)] private float fightBannerDuration = 1.4f;
    [SerializeField] private float combatPhaseDuration = 120f;
    [SerializeField, Min(0.5f)] private float versusIntroDuration = 2.8f;
    [SerializeField, Min(0.5f)] private float rumbleDuration = 1.45f;

    [Header("Match Identity")]
    [SerializeField] private string opponentDisplayName = "RIVAL 5678";

    [Header("Timeout Resolution")]
    [Tooltip("Vault-health percentage difference that is treated as a draw at the time limit.")]
    [SerializeField, Range(0f, 0.05f)]
    private float timeoutDrawTolerance = 0.005f;

    [Header("References")]
    [SerializeField] private TMP_Text phaseText;
    [SerializeField] private GameObject endMatchPanel;
    [SerializeField] private Damageable blueVault;
    [SerializeField] private Damageable redVault;
    [SerializeField] private Camera matchCamera;
    [SerializeField] private Transform localBuildAnchor;
    [SerializeField] private TopDownCameraFollow cameraFollow;
    [SerializeField] private GameObject localPlayerObject;
    [SerializeField] private GameObject centreSecurityGate;
    [SerializeField] private MatchPresentationUI matchPresentation;

    [Header("Local Player")]
    [SerializeField] private RobotPlayerController playerMovement;
    [SerializeField] private RobotAimController playerAim;
    [SerializeField] private RobotBlaster playerBlaster;

    [Header("Camera Sizes")]
    [SerializeField] private float buildViewSize = 12.5f;
    [SerializeField] private float combatViewSize = 11.5f;

    [Header("Opening Camera")]
    [Tooltip("A wider establishing view used behind the versus presentation.")]
    [SerializeField, Min(4f)] private float openingViewSize = 16.5f;
    [SerializeField, Range(48f, 72f)] private float openingCameraPitch = 60f;
    [SerializeField, Range(48f, 72f)] private float buildCameraPitch = 55f;
    [SerializeField, Min(4f)] private float openingCameraHeight = 24f;
    [SerializeField, Min(4f)] private float buildCameraHeight = 20f;
    [SerializeField] private Vector3 openingFocusOffset = new Vector3(0f, 0f, -4f);

    [Header("Combat Navigation")]
    [SerializeField] private Vector2 playableHalfExtents = new Vector2(25f, 35f);
    [Tooltip("Keeps agent centres far enough inside the arena for their full visible body and wheels.")]
    [SerializeField, Min(0.25f)] private float navigationEdgeInset = 1.65f;

    public FortressDuelPhase CurrentPhase { get; private set; }
    public float TimeRemaining => Mathf.Max(0f, timeRemaining);
    public string LocalPlayerDisplayName => ResolveLocalPlayerName();
    public string OpponentDisplayName => ResolveOpponentName();

    public event Action<FortressDuelPhase> PhaseChanged;

    private float timeRemaining;
    private NavMeshDataInstance combatNavMeshInstance;
    private bool hasCombatNavMeshInstance;
    private GameObject combatNavigationRoot;
    private Coroutine phaseSequence;
    private bool hasGrantedMatchReward;

    private void Awake()
    {
        ActiveArena = this;
        if (IsOnline)
        {
            if (offlineOnlyBehaviours != null)
                foreach (var behaviour in offlineOnlyBehaviours)
                    if (behaviour != null && behaviour is not BuildPlacementController && behaviour is not MobileBuildHUD) behaviour.enabled = false;
            if (offlineOnlyObjects != null)
                foreach (var obj in offlineOnlyObjects) if (obj != null) obj.SetActive(false);
            SetPlayerControls(false);
        }
        EnsureMatchPresentation();
        if (GetComponent<GameplayHints>() == null) gameObject.AddComponent<GameplayHints>();
    }

    private void OnDestroy()
    {
        if (ActiveArena == this) ActiveArena = null;
        if (hasCombatNavMeshInstance && combatNavMeshInstance.valid)
        {
            NavMesh.RemoveNavMeshData(combatNavMeshInstance);
        }

        if (combatNavigationRoot != null)
        {
            Destroy(combatNavigationRoot);
        }
    }

    private void Start()
    {
        switch (MatchSessionContext.Type)
        {
            case MatchType.HumanOnline:
                OnlineSceneReady = true;
                SetPlayerControls(false);
                phaseSequence = StartCoroutine(RunOnlineOpeningSequence());
                break;
            case MatchType.Bot:
                if (botMatchBootstrap != null && botMatchBootstrap.InitializeBotMatch())
                    phaseSequence = StartCoroutine(RunOpeningSequence());
                else WaitForMatchDecision();
                break;
            default:
                WaitForMatchDecision();
                break;
        }
    }

    private void WaitForMatchDecision()
    {
        SetPhase(FortressDuelPhase.Intro);
        SetPlayerControls(false);
        if (phaseText != null) phaseText.text = "WAITING...";
    }

    private float onlineOpponentMissingSince = -1f;
    private const float OnlineOpponentGraceSeconds = 4f;

    /// <summary>
    /// Ends an online match that can no longer finish on its own.
    ///
    /// The startup and build coroutines already leave on a dropped session, but
    /// nothing watched combat: when the opponent closed their tab, their avatar and
    /// (for SideA) the authoritative match state were destroyed with them, and the
    /// remaining player was left in a frozen match with no timer and no result. A
    /// lost local connection is reported as such; an opponent who is gone for a
    /// few seconds forfeits.
    /// </summary>
    private void UpdateOnlineWatchdog()
    {
        if (!IsOnline || (CurrentPhase != FortressDuelPhase.Build && CurrentPhase != FortressDuelPhase.Combat)) return;
        var director = OnlineMatchDirector.Instance;
        if (director == null) return;
        if (director.ConnectionLost)
        {
            Debug.LogWarning("[ONLINE MATCH] Session lost during the match.");
            if (phaseSequence != null) { StopCoroutine(phaseSequence); phaseSequence = null; }
            SetPhase(FortressDuelPhase.Finished);
            SetPlayerControls(false);
            CombatFeedbackOverlay.GetOrCreate().HideRespawnCountdown();
            FrontendFlow.Instance?.ReportDisconnected();
            return;
        }
        bool opponentPresent = director.RemoteAvatar != null && NetworkedMatchState.Instance != null;
        if (opponentPresent) { onlineOpponentMissingSince = -1f; return; }
        if (onlineOpponentMissingSince < 0f) { onlineOpponentMissingSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - onlineOpponentMissingSince < OnlineOpponentGraceSeconds) return;
        Debug.LogWarning("[ONLINE MATCH] Opponent left the match; awarding the forfeit.");
        FinishMatch(FortressTeam.Blue, "OPPONENT LEFT");
    }

    private void Update()
    {
        UpdateOnlineWatchdog();
        if (MatchSessionContext.Type != MatchType.Bot) return;
        if (CurrentPhase == FortressDuelPhase.Finished)
        {
            return;
        }

        if (CurrentPhase == FortressDuelPhase.Build)
        {
            timeRemaining -= Time.deltaTime;

            if (timeRemaining <= 0f)
            {
                BeginRumbleTransition();
                return;
            }

            UpdateBuildPhaseText();
        }
        else if (CurrentPhase == FortressDuelPhase.Combat)
        {
            timeRemaining -= Time.deltaTime;

            if (timeRemaining <= 0f)
            {
                EndMatchFromTimer();
                return;
            }

            UpdateCombatPhaseText();
        }
    }

    private void StartBuildPhase()
    {
        SetPhase(FortressDuelPhase.Build);
        Funnel.Event("build_started");
        timeRemaining = buildPhaseDuration;

        ConfigureBuildEnvironment(false);
        RaiseArenaSwarmHud(true);
        UpdateBuildPhaseText();

        if (matchPresentation != null)
        {
            matchPresentation.ShowBuildPhase(buildPhaseDuration);
        }
    }

    private void ConfigureBuildEnvironment(bool openingShot)
    {

        if (endMatchPanel != null)
        {
            endMatchPanel.SetActive(false);
        }

        SetPlayerControls(false);

        if (localPlayerObject != null)
        {
            localPlayerObject.SetActive(false);
        }

        if (centreSecurityGate != null)
        {
            // The old greybox gate visually cut the arena in half and its
            // colliders interrupted the opening/build flow.  It is retired in
            // every phase; the permanent arena walls provide containment.
            centreSecurityGate.SetActive(false);
        }


        if (cameraFollow != null)
        {
            cameraFollow.enabled = false;
        }

        if (matchCamera != null)
        {
            float cameraPitch = openingShot
                ? openingCameraPitch
                : buildCameraPitch;
            float cameraHeight = openingShot
                ? openingCameraHeight
                : buildCameraHeight;
            float cameraDistance = cameraHeight /
                Mathf.Max(0.01f, Mathf.Sin(cameraPitch * Mathf.Deg2Rad));

            ApplyCameraViewSize(
                matchCamera,
                openingShot ? openingViewSize : buildViewSize,
                cameraDistance);

            if (localBuildAnchor != null)
            {
                Vector3 focusPoint = localBuildAnchor.position +
                    (openingShot ? openingFocusOffset : Vector3.zero);
                Quaternion cameraRotation =
                    Quaternion.Euler(cameraPitch, 0f, 0f);

                matchCamera.transform.SetPositionAndRotation(
                    focusPoint + cameraRotation * Vector3.back * cameraDistance,
                    cameraRotation);
            }
        }

    }

    /// <summary>
    /// Frames the match camera to show <paramref name="viewSize"/> world units
    /// either side of centre, in whichever projection the camera is using.
    ///
    /// The view sizes here were authored against an orthographic camera, where
    /// the size is the framing. On a perspective camera framing is a product of
    /// the lens and the distance, so the lens is solved for the distance the shot
    /// is already being taken from. That keeps every authored view the same size
    /// on screen as before rather than making each one a new number to retune.
    /// </summary>
    private static void ApplyCameraViewSize(
        Camera camera, float viewSize, float distance)
    {
        if (camera == null)
            return;

        if (camera.orthographic)
        {
            camera.orthographicSize = viewSize;
            return;
        }

        if (distance <= 0.01f || viewSize <= 0.01f)
            return;

        camera.fieldOfView =
            2f * Mathf.Atan(viewSize / distance) * Mathf.Rad2Deg;
    }

    private IEnumerator RunOpeningSequence()
    {
        SetPhase(FortressDuelPhase.Intro);
        timeRemaining = 0f;
        ConfigureBuildEnvironment(true);

        if (phaseText != null)
        {
            phaseText.text = string.Empty;
        }

        if (matchPresentation != null)
        {
            matchPresentation.ShowVersus(
                LocalPlayerDisplayName,
                OpponentDisplayName,
                versusIntroDuration);
        }

        yield return new WaitForSecondsRealtime(versusIntroDuration);

        phaseSequence = null;

        if (CurrentPhase == FortressDuelPhase.Intro)
        {
            StartBuildPhase();
        }
    }

    public bool PrepareOnlineLocalPlayer()
    {
        if (!OnlineSceneReady || !IsOnline || string.IsNullOrWhiteSpace(MatchSessionContext.LocalDisplayName) ||
            string.IsNullOrEmpty(MatchSessionContext.LocalSkinId)) return false;
        if (!ArenaTeamPlacement.PlaceLocalPlayer(playerMovement, MatchSessionContext.LocalTeam, blueSpawn, redSpawn))
            return false;
        localPlayerObject.SetActive(true);
        if (cameraFollow != null) cameraFollow.SetOnlinePerspective(MatchSessionContext.LocalSide, playerMovement.transform);
        SetPlayerControls(false);
        var loadout = localPlayerObject.GetComponent<PlayerCosmeticRuntimeLoadout>();
        if (loadout != null) loadout.ApplyMatchSkin(MatchSessionContext.LocalSkinId);
        else RobotCosmeticApplier.ApplySkin(localPlayerObject, MatchSessionContext.LocalSkinId);

        // The player is placed and identified, which is everything the network
        // avatar needs. The skin is reported but deliberately not required.
        //
        // This used to return LocalSkinReady, an exact string match against the
        // applied cosmetic. Both ApplySkin and SparkSkinVisual.Apply bail out
        // silently on an id they cannot resolve, so one unresolved skin meant the
        // avatar was never spawned at all - and because each client waits for the
        // other's avatar, that froze both players with their controls disabled.
        if (!LocalSkinReady)
        {
            Debug.LogWarning(
                $"[SKIN] Local skin '{MatchSessionContext.LocalSkinId}' has not applied " +
                $"(currently '{AppliedLocalSkinId}'). Continuing; the match must not wait on a cosmetic.");
        }

        return true;
    }

    private IEnumerator RunOnlineOpeningSequence()
    {
        SetPhase(FortressDuelPhase.Intro);
        timeRemaining = 0f;
        if (phaseText != null) phaseText.text = "WAITING FOR OPPONENT";
        if (centreSecurityGate != null) centreSecurityGate.SetActive(false);
        if (endMatchPanel != null) endMatchPanel.SetActive(false);
        SetPlayerControls(false);
        float deadline = Time.realtimeSinceStartup + 35f;
        OnlineMatchDirector director;
        while ((director = OnlineMatchDirector.Instance) == null || !director.BothPlayersReady ||
            director.SideAAvatar == null || !director.SideAAvatar.IntroStartedConfirmed || NetworkedMatchState.Instance == null)
        {
            // A dropped session is terminal, not slow. Waiting out the full
            // deadline against a dead runner only kept the arena polling network
            // state that no longer exists.
            if (director != null && director.ConnectionLost)
            {
                Debug.LogWarning("[INTRO] Session lost during arena startup. " + director.DescribeStartupState());
                if (phaseText != null) phaseText.text = "CONNECTION LOST";
                FrontendFlow.Instance?.ReturnToMainMenu();
                yield break;
            }
            if (Time.realtimeSinceStartup >= deadline)
            {
                // Names the conditions rather than reporting one combined failure,
                // so the blocking value is visible without another test run.
                Debug.LogError(
                    "[INTRO] Online startup failed. Returning to menu. " +
                    (director != null ? director.DescribeStartupState() : "director=null"));
                FrontendFlow.Instance?.ReturnToMainMenu();
                yield break;
            }
            yield return null;
        }
        Debug.Log("[INTRO] StartingVS");
        // Captured once: these are validity-gated properties, so re-reading them
        // per use would reintroduce the chance of one turning null mid-statement.
        var introOwner = director.SideAAvatar;
        var localAvatar = director.LocalAvatar;
        var remoteAvatar = director.RemoteAvatar;
        if (introOwner == null || localAvatar == null || remoteAvatar == null) yield break;
        float remaining = introOwner.IntroEnd.RemainingTime(director.Runner) ?? 0f;
        if (matchPresentation != null && remaining > 0f)
            matchPresentation.ShowVersus(localAvatar.DisplayName.ToString(), remoteAvatar.DisplayName.ToString(), remaining);
        while (NetworkedMatchState.Instance != null && NetworkedMatchState.Instance.Phase == OnlineCombatPhase.Waiting)
        {
            if (!director.HasRunner || Time.realtimeSinceStartup >= deadline)
            { FrontendFlow.Instance?.ReturnToMainMenu(); yield break; }
            yield return null;
        }
        if (NetworkedMatchState.Instance == null) yield break;
        Debug.Log("[INTRO] VSComplete");
        yield return RunOnlineBuildPhase(director);
        if (NetworkedMatchState.Instance == null) yield break;
        BeginOnlineCombat();
    }

    /// <summary>
    /// The opening build phase, counted off one networked deadline.
    ///
    /// Both peers read <see cref="NetworkedMatchState.BuildEnd"/> rather than
    /// running their own timer, so the number on screen is the same number on
    /// both devices and combat opens for both at the same tick. Movement, aim,
    /// firing and summoning stay held for the whole window - the authority drops
    /// fire and summon requests as well, so this is presentation backed by the
    /// simulation rather than a UI-only restriction.
    /// </summary>
    private IEnumerator RunOnlineBuildPhase(OnlineMatchDirector director)
    {
        var state = NetworkedMatchState.Instance;
        if (state == null || state.Phase != OnlineCombatPhase.Build) yield break;

        BeginOnlineBuildPresentation();
        if (matchPresentation != null) matchPresentation.ShowBuildPhase(buildPhaseDuration);
        Debug.Log($"[BUILD PHASE] Local presentation opened for {buildPhaseDuration:F0}s side={MatchSessionContext.LocalSide}");

        while (NetworkedMatchState.Instance != null &&
               NetworkedMatchState.Instance.Phase == OnlineCombatPhase.Build)
        {
            if (director.ConnectionLost)
            {
                Debug.LogWarning("[BUILD PHASE] Session lost during build.");
                if (phaseText != null) phaseText.text = "CONNECTION LOST";
                FrontendFlow.Instance?.ReturnToMainMenu();
                yield break;
            }

            timeRemaining = NetworkedMatchState.Instance.BuildEnd.RemainingTime(director.Runner) ?? 0f;
            if (phaseText != null) phaseText.text = "BUILD PHASE: " + Mathf.Max(1, Mathf.CeilToInt(timeRemaining));
            yield return null;
        }

        if (NetworkedMatchState.Instance == null) yield break;
        timeRemaining = 0f;
        // Written to the phase line as well as the banner: matchPresentation is
        // unassigned in the arena scene, so the banner alone would show nothing.
        if (phaseText != null) phaseText.text = "FIGHT!";
        if (matchPresentation != null) matchPresentation.ShowFight(fightBannerDuration);
        Debug.Log("[BUILD PHASE] Complete; showing FIGHT.");
        yield return new WaitForSecondsRealtime(fightBannerDuration);
        if (phaseText != null) phaseText.text = string.Empty;
    }

    /// <summary>
    /// Build-phase presentation for an online match.
    ///
    /// Deliberately not the offline build view, which hides the player and lifts
    /// the camera overhead: online both players are on the field for the whole
    /// match, so they place from the combat perspective they are about to fight
    /// from. Only the build HUD is granted; controls stay held.
    /// </summary>
    private void BeginOnlineBuildPresentation()
    {
        SetPhase(FortressDuelPhase.Build);
        Funnel.Event("build_started");
        if (matchPresentation != null) matchPresentation.HideTransientPresentation();
        if (centreSecurityGate != null) centreSecurityGate.SetActive(false);
        // The phase line lives under the swarm HUD, which online was only brought
        // up when combat opened - so the build countdown had nowhere to draw and
        // the phone showed the build HUD with no timer. It is raised here instead,
        // with its summon button held closed for the duration.
        RaiseArenaSwarmHud(true);
        if (cameraFollow != null && playerMovement != null)
        {
            cameraFollow.SetOnlinePerspective(MatchSessionContext.LocalSide, playerMovement.transform);
            cameraFollow.enabled = true;
        }
        SetPlayerControls(false);
    }

    /// <summary>Hands control to the player. The one path that enables input online.</summary>
    private void BeginOnlineCombat()
    {
        RebuildCombatNavMesh();
        RaiseArenaSwarmHud(false);
        if (cameraFollow != null) cameraFollow.SetOnlinePerspective(MatchSessionContext.LocalSide, playerMovement.transform);
        if (matchPresentation != null) matchPresentation.HideTransientPresentation();
        if (phaseText != null) phaseText.text = string.Empty;
        SetPhase(FortressDuelPhase.Combat);
        Funnel.Event("fight_started");
        SpawnPadPresentation.NotifyAll();
        if (cameraFollow != null) cameraFollow.enabled = true;
        Debug.Log("[INTRO] EnablingControls");
        SetPlayerControls(true);
        Debug.Log(
            $"[CONTROLS] LocalPlayerControlsEnabled={(playerMovement != null && playerMovement.enabled)} " +
            $"aim={(playerAim != null && playerAim.enabled)} blaster={(playerBlaster != null && playerBlaster.enabled)} " +
            $"side={MatchSessionContext.LocalSide}");
        Debug.Log("[INTRO] MatchStarted");
        phaseSequence = null;
    }

    private void BeginRumbleTransition()
    {
        if (CurrentPhase != FortressDuelPhase.Build)
        {
            return;
        }

        SetPhase(FortressDuelPhase.Rumble);
        timeRemaining = 0f;
        SetPlayerControls(false);

        // The same 12 ... 1 FIGHT! close as the online build phase.
        if (phaseText != null)
        {
            phaseText.text = "FIGHT!";
        }

        if (matchPresentation != null)
        {
            matchPresentation.ShowFight(rumbleDuration);
        }

        phaseSequence = StartCoroutine(RunRumbleTransition());
    }

    private IEnumerator RunRumbleTransition()
    {
        yield return new WaitForSecondsRealtime(rumbleDuration);

        phaseSequence = null;

        if (CurrentPhase == FortressDuelPhase.Rumble)
        {
            StartCombatPhase();
        }
    }

    private void StartCombatPhase()
    {
        SetPhase(FortressDuelPhase.Combat);
        Funnel.Event("fight_started");
        SpawnPadPresentation.NotifyAll();
        ArenaSwarmHud?.SetSummonLocked(false);
        timeRemaining = combatPhaseDuration;

        if (matchPresentation != null)
        {
            matchPresentation.HideTransientPresentation();
        }

        if (localPlayerObject != null)
        {
            localPlayerObject.SetActive(true);
        }

        if (centreSecurityGate != null)
        {
            centreSecurityGate.SetActive(false);
        }

        RebuildCombatNavMesh();

        SetPlayerControls(true);

        if (matchCamera != null)
        {
            ApplyCameraViewSize(
                matchCamera,
                combatViewSize,
                cameraFollow != null ? cameraFollow.FramingDistance : 0f);
        }

        if (cameraFollow != null)
        {
            cameraFollow.enabled = true;
        }

        UpdateCombatPhaseText();
    }

    public void EndMatch(FortressTeam winningTeam)
    {
        if (IsOnline) return;
        FinishMatch(winningTeam, "HEIST DESTROYED");
    }

    private void EndMatchFromTimer()
    {
        if (blueVault == null || redVault == null)
        {
            Debug.LogError(
                "Cannot resolve the timed result because a heist reference is missing. " +
                "The match will end as a draw rather than favouring either team.",
                this);

            FinishMatch(null, "TIME LIMIT // HEIST LINK LOST");
            return;
        }

        float blueHealthPercent = GetHealthPercent(blueVault);
        float redHealthPercent = GetHealthPercent(redVault);
        float difference = blueHealthPercent - redHealthPercent;

        Debug.Log(
            "TIME LIMIT // Blue heist " + blueVault.CurrentHealth + "/" +
            blueVault.MaxHealth + " (" + (blueHealthPercent * 100f).ToString("0.00") +
            "%) // Red heist " + redVault.CurrentHealth + "/" +
            redVault.MaxHealth + " (" + (redHealthPercent * 100f).ToString("0.00") +
            "%) // Draw tolerance " + (timeoutDrawTolerance * 100f).ToString("0.00") +
            "%.",
            this);

        if (Mathf.Abs(difference) <= timeoutDrawTolerance)
        {
            FinishMatch(null, "TIME LIMIT");
        }
        else if (difference > 0f)
        {
            FinishMatch(FortressTeam.Blue, "TIME LIMIT");
        }
        else
        {
            FinishMatch(FortressTeam.Red, "TIME LIMIT");
        }
    }

    private void FinishMatch(FortressTeam? winningTeam, string finishReason)
    {
        if (CurrentPhase == FortressDuelPhase.Finished)
        {
            return;
        }

        if (phaseSequence != null)
        {
            StopCoroutine(phaseSequence);
            phaseSequence = null;
        }

        SetPhase(FortressDuelPhase.Finished);
        Funnel.Event("match_completed");
        timeRemaining = 0f;
        SetPlayerControls(false);

        // Pushed as well as polled. The respawn routine drops itself the moment it
        // sees this phase, but a player who died in the same frame the match ended
        // could otherwise show one countdown frame on top of the result screen.
        CombatFeedbackOverlay.GetOrCreate().HideRespawnCountdown();
        foreach (WorldHealthBar bar in FindObjectsByType<WorldHealthBar>(FindObjectsSortMode.None))
            if (bar != null) bar.EndRespawnCountdown();

        int awardedCoins = GrantMatchRewardOnce(winningTeam);
        if (winningTeam.HasValue)
        {
            foreach (RobotAnimationHooks hooks in FindObjectsByType<RobotAnimationHooks>(FindObjectsSortMode.None))
            {
                FortressTarget identity = hooks.GetComponentInParent<FortressTarget>();
                if (identity != null) hooks.ShowResult((IsOnline ? TeamSides.PresentationTeam(TeamSides.FromSceneTeam(identity.Team)) : identity.Team) == winningTeam.Value);
            }
        }

        string resultLabel = !winningTeam.HasValue
            ? "DRAW"
            : winningTeam.Value == FortressTeam.Blue
                ? "BLUE WINS"
                : "RED WINS";

        if (phaseText != null)
        {
            phaseText.text = matchPresentation == null
                ? resultLabel
                : string.Empty;
        }

        if (endMatchPanel != null)
        {
            endMatchPanel.SetActive(FrontendFlow.Instance == null);
        }

        if (matchPresentation != null)
        {
            matchPresentation.ShowResult(
                winningTeam,
                LocalPlayerDisplayName,
                OpponentDisplayName,
                IsOnline ? Heist(MatchSessionContext.LocalSide).CurrentHealth : blueVault != null ? blueVault.CurrentHealth : 0,
                IsOnline ? Heist(MatchSessionContext.LocalSide).MaxHealth : blueVault != null ? blueVault.MaxHealth : 0,
                IsOnline ? Heist(TeamSides.Opponent(MatchSessionContext.LocalSide)).CurrentHealth : redVault != null ? redVault.CurrentHealth : 0,
                IsOnline ? Heist(TeamSides.Opponent(MatchSessionContext.LocalSide)).MaxHealth : redVault != null ? redVault.MaxHealth : 0,
                finishReason,
                awardedCoins);
        }

        Debug.Log(
            !winningTeam.HasValue
                ? "Fortress Duel ended in a draw // " + finishReason +
                  " // +" + awardedCoins + " coins."
                : winningTeam.Value + " wins Fortress Duel // " + finishReason +
                  " // local reward +" + awardedCoins + " coins.",
            this);
    }

    private int GrantMatchRewardOnce(FortressTeam? winningTeam)
    {
        if (hasGrantedMatchReward)
        {
            return 0;
        }

        hasGrantedMatchReward = true;

        int reward = !winningTeam.HasValue
            ? DrawCoinReward
            : winningTeam.Value == FortressTeam.Blue
                ? VictoryCoinReward
                : DefeatCoinReward;

        int experience = !winningTeam.HasValue
            ? DrawExperienceReward
            : winningTeam.Value == FortressTeam.Blue
                ? VictoryExperienceReward
                : DefeatExperienceReward;

        // The in-memory flag above only guards a repeated callback inside one
        // session; it resets when the scene reloads. The ledger keys on the match
        // id and persists, so a reload or a re-raised result cannot pay twice.
        MatchResultSummary.Clear();
        int levelBefore = PlayerProfileService.Level;
        int xpBefore = PlayerProfileService.Experience;
        using (ProfileStore.Batch())
        {
        bool awarded = PlayerRewardLedger.TryGrant(
            reward,
            experience,
            RewardSource.MatchResult,
            PlayerRewardLedger.MatchTransactionId(MatchId));
        if (awarded)
        {
            PlayerProfileService.RecordFinishedMissionMatch(winningTeam == FortressTeam.Blue);
            // Daily Battles counts the same de-duplicated match, and pays its
            // bonus in the same save when this match completes it.
            bool dailyBonus = DailyProgress.RecordMatch();
            MatchResultSummary.Record(reward, experience, levelBefore, xpBefore, dailyBonus);
        }

        return awarded ? reward : 0;
        }
    }

    /// <summary>
    /// Identifies this match for reward de-duplication. Generated once when the
    /// match starts so every result callback for the same match shares it.
    /// </summary>
    public string MatchId
    {
        get
        {
            if (string.IsNullOrEmpty(matchId))
            {
                matchId = System.Guid.NewGuid().ToString("N");
            }

            return matchId;
        }
    }

    private string matchId;

    public void RestartMatch()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void SetPlayerControls(bool controlsEnabled)
    {
        if (playerMovement != null)
        {
            playerMovement.enabled = controlsEnabled;
        }

        if (playerAim != null)
        {
            playerAim.enabled = controlsEnabled;
        }

        if (playerBlaster != null)
        {
            // Firing is allowed online now that shots replicate to the opponent.
            // It was held off while a shot was invisible to the other player.
            playerBlaster.enabled = controlsEnabled;
        }
    }

    private SwarmSummonHUD arenaSwarmHud;

    /// <summary>
    /// The swarm HUD this arena actually drives.
    ///
    /// The scene carries a second, stray SwarmHUD under the arena art root.
    /// Raising every HUD in the scene brought that one up as well, and since
    /// nothing ever writes to its labels it sat frozen on top of the live ones -
    /// a stale "BUILD PHASE" drawn over the running combat timer at identical
    /// coordinates, which is what made the readout look garbled. Ownership is
    /// defined by the phase label this manager writes to, so that is what is
    /// resolved here rather than "every HUD that exists".
    /// </summary>
    private SwarmSummonHUD ArenaSwarmHud
    {
        get
        {
            if (arenaSwarmHud != null) return arenaSwarmHud;
            if (phaseText != null) arenaSwarmHud = phaseText.GetComponentInParent<SwarmSummonHUD>(true);
            if (arenaSwarmHud == null)
                arenaSwarmHud = FindFirstObjectByType<SwarmSummonHUD>(FindObjectsInactive.Include);
            return arenaSwarmHud;
        }
    }

    private void RaiseArenaSwarmHud(bool summonLocked)
    {
        var hud = ArenaSwarmHud;
        if (hud == null) return;
        hud.gameObject.SetActive(true);
        hud.enabled = true;
        hud.SetSummonLocked(summonLocked);
    }

    private void UpdateBuildPhaseText()
    {
        if (phaseText == null)
        {
            return;
        }

        int seconds = Mathf.Max(1, Mathf.CeilToInt(timeRemaining));
        phaseText.text = "BUILD PHASE: " + seconds;
    }

    private void UpdateCombatPhaseText()
    {
        if (phaseText == null)
        {
            return;
        }

        int totalSeconds = Mathf.CeilToInt(timeRemaining);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        phaseText.text = "COMBAT: " + minutes + ":" + seconds.ToString("00");
    }

    private void SetPhase(FortressDuelPhase phase)
    {
        if (CurrentPhase == phase)
        {
            return;
        }

        CurrentPhase = phase;
        PhaseChanged?.Invoke(phase);
    }

    private void EnsureMatchPresentation()
    {
        if (matchPresentation == null)
        {
            matchPresentation = FindFirstObjectByType<MatchPresentationUI>();
        }

        if (matchPresentation != null)
        {
            matchPresentation.EnsureBuilt();
            return;
        }

        GameObject presentationObject = new GameObject("Match Presentation");
        presentationObject.transform.SetParent(transform, false);
        matchPresentation = presentationObject.AddComponent<MatchPresentationUI>();
        matchPresentation.EnsureBuilt();
    }

    private static float GetHealthPercent(Damageable target)
    {
        return target != null && target.MaxHealth > 0
            ? Mathf.Clamp01((float)target.CurrentHealth / target.MaxHealth)
            : 0f;
    }

    private static string ResolveLocalPlayerName()
    {
        if (MatchSessionContext.Type == MatchType.HumanOnline)
            return MatchSessionContext.LocalDisplayName;
        // The same name online and in bot matches: the CrazyGames username, a
        // chosen pilot name, or the generated PILOT-#### guest name. Reading only
        // the saved pilot name showed "ROBO PILOT" to everyone who had not typed one.
        return NormalizeDisplayName(PlayerProfileService.DisplayName, "ROBO PILOT");
    }

    private string ResolveOpponentName()
    {
        if (IsOnline) return OnlineOpponent != null ? OnlineOpponent.DisplayName.ToString() : string.Empty;
        if (MatchSessionContext.Type != MatchType.Bot) return "WAITING...";
        return NormalizeDisplayName(opponentDisplayName, "RIVAL 5678");
    }

    /// <summary>
    /// Sets the opponent's shown name. Used by the fallback bot so AI matches do
    /// not all present the same rival.
    /// </summary>
    public void SetOpponentDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        opponentDisplayName = value;

        // Name plates read this once, when they are built. The bot is named in
        // Start, after those plates already exist, so they have to be told.
        WorldHealthBar[] plates = FindObjectsByType<WorldHealthBar>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < plates.Length; i++)
        {
            plates[i].RefreshDisplayName();
        }
    }

    private static string NormalizeDisplayName(string value, string fallback)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

        return normalized.ToUpperInvariant();
    }

    private void RebuildCombatNavMesh()
    {
        NavMeshAgent referenceAgent = FindAnyObjectByType<NavMeshAgent>();
        if (referenceAgent == null && !IsOnline) return;

        if (hasCombatNavMeshInstance && combatNavMeshInstance.valid)
        {
            NavMesh.RemoveNavMeshData(combatNavMeshInstance);
            hasCombatNavMeshInstance = false;
        }

        foreach (NavMeshSurface authoredSurface in FindObjectsByType<NavMeshSurface>())
        {
            authoredSurface.RemoveData();
        }

        if (combatNavigationRoot != null)
        {
            Destroy(combatNavigationRoot);
        }

        float floorHeight = localPlayerObject != null
            ? localPlayerObject.transform.position.y
            : 0f;

        int navigationLayer = LayerMask.NameToLayer("NavigationFloor");
        if (navigationLayer < 0)
        {
            navigationLayer = LayerMask.NameToLayer("BuildBlocker");
            Debug.LogWarning(
                "NavigationFloor layer is missing; using BuildBlocker as a fallback.",
                this);
        }

        combatNavigationRoot = new GameObject("Runtime Combat Navigation")
        {
            hideFlags = HideFlags.DontSave
        };

        NavMeshSurface combatSurface =
            combatNavigationRoot.AddComponent<NavMeshSurface>();
        combatSurface.agentTypeID = referenceAgent != null ? referenceAgent.agentTypeID : NavMesh.GetSettingsByIndex(0).agentTypeID;
        combatSurface.collectObjects = CollectObjects.All;
        combatSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        combatSurface.layerMask = 1 << navigationLayer;
        combatSurface.defaultArea = 0;
        combatSurface.ignoreNavMeshAgent = true;
        combatSurface.ignoreNavMeshObstacle = true;

        GameObject navigationFloor = new GameObject("Navigation Floor");
        navigationFloor.hideFlags = HideFlags.DontSave;
        navigationFloor.layer = navigationLayer;
        navigationFloor.transform.SetParent(combatNavigationRoot.transform, false);
        navigationFloor.transform.position =
            new Vector3(0f, floorHeight - 0.08f, 0f);

        BoxCollider floorCollider = navigationFloor.AddComponent<BoxCollider>();
        floorCollider.size = new Vector3(
            Mathf.Max(
                1f,
                playableHalfExtents.x * 2f - navigationEdgeInset * 2f),
            0.16f,
            Mathf.Max(
                1f,
                playableHalfExtents.y * 2f - navigationEdgeInset * 2f));

        Physics.SyncTransforms();
        combatSurface.BuildNavMesh();
        floorCollider.enabled = false;

        if (!NavMesh.SamplePosition(
                new Vector3(0f, floorHeight, 0f),
                out _,
                Mathf.Max(playableHalfExtents.x, playableHalfExtents.y),
                NavMesh.AllAreas))
        {
            Debug.LogError("Could not create the combat navigation floor.", this);
            return;
        }

        foreach (NavMeshAgent arenaAgent in FindObjectsByType<NavMeshAgent>())
        {
            if (arenaAgent == null || !arenaAgent.enabled)
            {
                continue;
            }

            if (NavMesh.SamplePosition(
                    arenaAgent.transform.position,
                    out NavMeshHit nearestPoint,
                    8f,
                    NavMesh.AllAreas))
            {
                arenaAgent.Warp(nearestPoint.position);
            }
        }
    }
}
