using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

/// <summary>Option B: pose publication in FixedUpdateNetwork, interpolation only in Render.
/// The local PlayerRoot owns input. This prefab has no NetworkTransform or player/AI controller.</summary>
[DisallowMultipleComponent]
public sealed class NetworkedPlayerAvatar : NetworkBehaviour
{
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private TMP_Text nameplate;
    [Networked] public PlayerRef OwnerPlayerRef { get; set; }
    [Networked] public NetworkString<_32> SkinId { get; set; }
    [Networked] public NetworkString<_32> DisplayName { get; set; }
    /// <summary>This player's three build cards (BuildCards.Pack), written only by the owner.</summary>
    [Networked] public int BuildLoadout { get; set; }
    [Networked] public TeamSide Team { get; set; }
    [Networked] public Vector3 Position { get; set; }
    [Networked] public Quaternion Rotation { get; set; }
    [Networked] public Vector3 AimTarget { get; set; }
    [Networked] public float MoveSpeed { get; set; }
    [Networked] public NetworkBool AvatarPublished { get; set; }
    [Networked] public NetworkBool ArenaReady { get; set; }
    // Blue owns the shared intro deadline. Red only reads it.
    [Networked] public NetworkBool IntroStarted { get; set; }
    [Networked] public TickTimer IntroEnd { get; set; }

    public int Health => IsNetworkValid && NetworkedMatchState.Instance != null ? NetworkedMatchState.Instance.PlayerHealth(Team) : 0;
    public int MaxHealth => NetworkedMatchState.Instance != null ? NetworkedMatchState.Instance.PlayerMaximumHealth : 100;
    private static readonly Dictionary<NetworkRunner, Dictionary<PlayerRef, NetworkedPlayerAvatar>> registry = new();
    private Transform localSource;
    private RobotAimController localAim;
    private Animator remoteAnimator;
    private Vector3 previousPosition;
    private string appliedSkin;
    private bool registered, renderedPose, presentationBound;
    private PlayerRef registeredOwner;
    private float nextDiagnostic;
    private int diagnosticSamples;

    /// <summary>
    /// Whether this publisher's [Networked] properties may be read at all.
    ///
    /// Fusion's weaver throws InvalidOperationException the moment a networked
    /// getter runs with a cleared state pointer, and a despawned NetworkObject
    /// keeps its managed C# instance alive - so "the reference is not null" says
    /// nothing about whether reading through it is legal. NetworkObject.IsValid is
    /// the documented window: true from Spawned() until Despawned().
    /// </summary>
    public bool IsNetworkValid => Object != null && Object.IsValid;
    public bool IsLocalAvatar => IsNetworkValid && OwnerPlayerRef == Runner.LocalPlayer;
    public bool CanWriteState => IsLocalAvatar && HasStateAuthority && Object.StateAuthority == OwnerPlayerRef;
    public bool ArenaReadyConfirmed => IsNetworkValid && ArenaReady;
    public bool IntroStartedConfirmed => IsNetworkValid && IntroStarted;
    public bool ProfileReady => IsNetworkValid && AvatarPublished && OwnerPlayerRef.IsRealPlayer &&
        Object.StateAuthority == OwnerPlayerRef && (Team == TeamSide.SideA || Team == TeamSide.SideB) &&
        !string.IsNullOrWhiteSpace(DisplayName.ToString()) && !string.IsNullOrEmpty(SkinId.ToString()) &&
        (IsLocalAvatar || (renderedPose && appliedSkin == SkinId.ToString()));
    public GameObject RemoteVisualRoot => visualRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => registry.Clear();

    public static bool TryGet(NetworkRunner runner, PlayerRef owner, out NetworkedPlayerAvatar avatar)
    {
        avatar = null;
        return runner != null && registry.TryGetValue(runner, out var avatars) &&
            avatars.TryGetValue(owner, out avatar) && avatar != null && avatar.Object != null && avatar.Object.IsValid;
    }

    public void InitializeLocal(NetworkRunner runner, Transform source, TeamSide team, string displayName, string skinId)
    {
        OwnerPlayerRef = runner.LocalPlayer;
        Team = team;
        DisplayName = displayName;
        SkinId = skinId;
        BuildLoadout = PlayerProfileService.PackedBuildLoadout;
        localSource = source;
        localAim = source.GetComponent<RobotAimController>();

        previousPosition = source.position;
        Position = source.position;
        Rotation = source.rotation;
        AvatarPublished = false;
        ArenaReady = false;
        IntroStarted = false;
        IntroEnd = default;
    }

    public override void Spawned()
    {
        visualRoot.SetActive(false);
        if (nameplate != null) nameplate.gameObject.SetActive(false);
        remoteAnimator = visualRoot.GetComponentInChildren<Animator>(true);
        if (remoteAnimator != null) remoteAnimator.applyRootMotion = false;
        RegisterOwner();
        Debug.Log($"[AUTHORITY] PlayerRef={OwnerPlayerRef} NetworkId={Object.Id} StateAuthority={Object.StateAuthority} IsLocal={IsLocalAvatar} CanWriteState={CanWriteState}");
    }

    private void RegisterOwner()
    {
        if (registered || !OwnerPlayerRef.IsRealPlayer) return;
        if (!registry.TryGetValue(Runner, out var avatars)) registry[Runner] = avatars = new();
        if (avatars.TryGetValue(OwnerPlayerRef, out var existing) && existing != null && existing != this)
        {
            Debug.LogError($"[AVATAR] Duplicate publisher for {OwnerPlayerRef}; refusing binding.");
            return;
        }
        avatars[OwnerPlayerRef] = this;
        registeredOwner = OwnerPlayerRef;
        registered = true;
    }

    /// <summary>
    /// Releases every handle to this publisher before its network state goes away.
    ///
    /// Un-registering alone is not enough. When Android stalled during arena
    /// startup and Photon dropped the peer, the director still held the local
    /// publisher in a plain field, and the opening-sequence coroutine read
    /// AvatarPublished straight off the despawned object on the very next frame.
    /// A validity check at each read site would have hidden that specific throw
    /// while leaving the dead reference in place forever, so the reference itself
    /// is retracted here and dependent systems are told to re-derive.
    /// </summary>
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (registry.TryGetValue(runner, out var avatars) && avatars.TryGetValue(registeredOwner, out var found) && found == this)
        {
            avatars.Remove(registeredOwner);
            if (avatars.Count == 0) registry.Remove(runner);
        }
        registered = false;
        renderedPose = false;
        presentationBound = false;
        localSource = null;
        localAim = null;
        if (visualRoot != null) visualRoot.SetActive(false);
        if (nameplate != null) nameplate.gameObject.SetActive(false);
        OnlineMatchDirector.Instance?.ForgetAvatar(this);
        Debug.Log($"[AVATAR] Despawned owner={registeredOwner} hasState={hasState}; references released.");
    }

    public override void FixedUpdateNetwork()
    {
        if (!CanWriteState || localSource == null) return;
        Position = localSource.position;
        Rotation = localSource.rotation;
        AimTarget = localAim != null && localAim.HasAimTarget ? localAim.AimTarget : Position + localSource.forward;
        Vector3 delta = Position - previousPosition;
        delta.y = 0f;
        MoveSpeed = delta.sqrMagnitude < 36f ? delta.magnitude / Runner.DeltaTime : 0f;
        previousPosition = Position;
        AvatarPublished = true;

        var director = OnlineMatchDirector.Instance;
        ArenaReady = director != null && director.Runner == Runner && director.LocalDependenciesReady;
        if (Team == TeamSide.SideA && !IntroStarted && director != null && director.BothPlayersReady)
        {
            IntroEnd = TickTimer.CreateFromSeconds(Runner, director.IntroDuration);
            IntroStarted = true;
            Debug.Log("[INTRO] BothPlayersReady");
        }
    }

    public override void Render()
    {
        using var startupTiming = new NetworkStartupDiagnostics.Step("Avatar.Render");
        RegisterOwner();
        if (IsLocalAvatar)
        {
            // Invisible diagnostic root; never drives the real local PlayerRoot.
            if (localSource != null) transform.SetPositionAndRotation(localSource.position, localSource.rotation);
            SampleDiagnostics();
            return;
        }
        if (!AvatarPublished || !registered || Object.StateAuthority != OwnerPlayerRef)
        {
            visualRoot.SetActive(false);
            if (nameplate != null) nameplate.gameObject.SetActive(false);
            return;
        }

        Vector3 position = Position;
        Quaternion rotation = Rotation;
        float speed = MoveSpeed;
        if (TryGetSnapshotsBuffers(out var from, out var to, out float alpha))
        {
            using var readerTiming = new NetworkStartupDiagnostics.Step("Avatar.PropertyReaders");
            var positions = GetPropertyReader<Vector3>(nameof(Position)).Read(from, to);
            var rotations = GetPropertyReader<Quaternion>(nameof(Rotation)).Read(from, to);
            var speeds = GetPropertyReader<float>(nameof(MoveSpeed)).Read(from, to);
            position = Vector3.Distance(positions.Item1, positions.Item2) > 6f ? Position : Vector3.Lerp(positions.Item1, positions.Item2, alpha);
            rotation = Quaternion.Slerp(rotations.Item1, rotations.Item2, alpha);
            speed = Mathf.Lerp(speeds.Item1, speeds.Item2, alpha);
        }
        // Face the direction this player is aiming rather than the direction they
        // are driving; without it the opponent shoots sideways out of its chassis.
        Vector3 aim = AimTarget - position;
        aim.y = 0f;
        if (aim.sqrMagnitude > 0.0001f)
        {
            rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(aim.normalized, Vector3.up), 0.65f);
        }

        transform.SetPositionAndRotation(position, rotation);
        renderedPose = true;
        string incoming = SkinId.ToString();
        if (!string.IsNullOrEmpty(incoming) && incoming != appliedSkin)
        {
            using var skinTiming = new NetworkStartupDiagnostics.Step("Avatar.ApplySkin");
            RobotCosmeticApplier.ApplySkin(visualRoot, incoming);
            var skin = visualRoot.GetComponentInChildren<SparkSkinVisual>(true);
            if (skin != null && skin.AppliedSkinId == incoming) appliedSkin = incoming;
        }
        visualRoot.SetActive(appliedSkin == incoming && !string.IsNullOrEmpty(appliedSkin) &&
            (NetworkedMatchState.Instance == null || Health > 0));
        if (visualRoot.activeSelf && !presentationBound)
        {
            presentationBound = true;
            using var presentationTiming = new NetworkStartupDiagnostics.Step("Avatar.BindPresentation");
            OnlineTeamPresentation.BindPlayer(visualRoot, Team, nameplate);
        }
        if (remoteAnimator != null && visualRoot.activeSelf)
        {
            remoteAnimator.SetFloat("MoveSpeed", Mathf.Clamp01(speed / 5.5f));
            remoteAnimator.SetBool("CombatActive", true);
        }
        if (nameplate != null)
        {
            nameplate.text = DisplayName.ToString();
            nameplate.gameObject.SetActive(ProfileReady);
            var camera = OnlineMatchDirector.Instance?.MatchCamera;
            if (camera != null) nameplate.transform.rotation = camera.transform.rotation;
        }
        SampleDiagnostics();
    }

    // Development builds only, once a second for 15 samples. No visible probes.
    private void SampleDiagnostics()
    {
        if (!NetworkTestHarness.VerboseDiagnostics || !AvatarPublished || diagnosticSamples >= 15 || Time.unscaledTime < nextDiagnostic) return;
        diagnosticSamples++;
        if (diagnosticSamples == 1) OnlineFoundationDiagnostics.Snapshot(this);
        nextDiagnostic = Time.unscaledTime + 1f;
        if (IsLocalAvatar)
            Debug.Log($"[LOCAL STATE] PlayerRef={OwnerPlayerRef} PlayerRootPos={localSource.position:F3} PublisherPos={Position:F3} Team={Team} Skin={SkinId} ArenaReady={ArenaReady}");
        else
            Debug.Log($"[REMOTE STATE] Owner={OwnerPlayerRef} ReceivedPos={Position:F3} VisualPos={transform.position:F3} VisualOffset={visualRoot.transform.localPosition:F3} Team={Team} Skin={appliedSkin} ArenaReady={ArenaReady}");
    }
}

