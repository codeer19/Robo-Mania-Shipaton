using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if CRAZYGAMES_BUILD
using CrazyGames;
#endif

/// <summary>Distribution services only; never owns a runner, room, or gameplay authority.</summary>
public sealed class CrazyGamesPlatformService : MonoBehaviour
{
    private const string ProfileKey = "RoboMania.Profile.v1";
    private static CrazyGamesPlatformService instance;
    public static bool Ready { get; private set; }
    public static string AccountDisplayName { get; private set; }
    private bool accountChanged;
    private bool gameplayActive;
    private bool adBusy, adsFailed, pendingMatchAd;
    private FortressDuelManager arena;
    private bool basicLaunchDisabled, observedWorkingAds;
    private GameObject inputBlocker;
    public static event Action AvailabilityChanged;
    public static string EnvironmentName { get; private set; } = "disabled";
    public static bool IsLocalTest => EnvironmentName == "local" || EnvironmentName == "editor";
    public static bool InteractionBlocked => instance != null && instance.adBusy;
    public static int RewardedCooldownRemaining
    {
        get
        {
            if (!DateTime.TryParse(ProfileStore.GetString("RoboMania.Ads.LastRewarded"),null,
                System.Globalization.DateTimeStyles.RoundtripKind,out var last)) return 0;
            return Mathf.Max(0,(int)Math.Ceiling((last.ToUniversalTime().AddSeconds(CrazyGamesReleaseSettings.Current.rewardedCooldownSeconds)-DateTime.UtcNow).TotalSeconds));
        }
    }
    public static bool RewardedSupported
    {
        get
        {
#if CRAZYGAMES_BUILD
            return Ready && instance != null && !instance.adsFailed && !instance.basicLaunchDisabled &&
                (IsLocalTest || CrazyGamesReleaseSettings.Current.monetizationEnabled || instance.observedWorkingAds) &&
                CrazySDK.Ad.AdblockStatus == AdblockStatus.Missing && ProfileStore.PersistenceAvailable;
#elif SHIPATON_ANDROID
            // Android Shipaton build: AdMob rewarded ads, tracked by RevenueCat (ShipatonMonetization).
            return ShipatonMonetization.AdsInitialized && ProfileStore.PersistenceAvailable;
#else
            return false;
#endif
        }
    }
    public static void RequestDailyReward(Action<bool> completed)
    {
        if (!RewardedAvailable || PlayerProfileService.DailyRewardClaimed) { completed?.Invoke(false); return; }
        int revision = ProfileStore.Revision;
        instance.StartCoroutine(instance.RequestAd(true, success =>
        {
            completed?.Invoke(success && revision == ProfileStore.Revision && PlayerProfileService.ClaimVerifiedDailyReward());
        }, PlacementDaily));
    }
    private static void AdLog(string message)
    {
        if (IsLocalTest || Debug.isDebugBuild) Debug.Log("[CRAZYGAMES] " + message);
    }
    private void SetInputBlocked(bool blocked)
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        if (!blocked)
        {
            if (inputBlocker != null) Destroy(inputBlocker);
            inputBlocker = null;
            return;
        }
        inputBlocker = new GameObject("Ad Interaction Shield",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.GraphicRaycaster));
        DontDestroyOnLoad(inputBlocker);
        var canvas = inputBlocker.GetComponent<Canvas>();canvas.renderMode = RenderMode.ScreenSpaceOverlay;canvas.sortingOrder = 32760;
        FrontendUI.Panel("Input Shield",inputBlocker.transform,Vector2.zero,Vector2.one,Color.clear,false);
    }
    public static bool RewardedAvailable
    {
        get
        {
#if CRAZYGAMES_BUILD
            return RewardedSupported && !InteractionBlocked && RewardedCooldownRemaining == 0 &&
                FrontendFlow.Instance != null && FrontendFlow.Instance.State == FrontendState.MainMenu;
#elif SHIPATON_ANDROID
            return RewardedSupported && !InteractionBlocked && RewardedCooldownRemaining == 0 && ShipatonMonetization.RewardedReady &&
                FrontendFlow.Instance != null && FrontendFlow.Instance.State == FrontendState.MainMenu;
#else
            return false;
#endif
        }
    }
    public static void CompletedMatch()
    {
        // A brand-new player's first match never ends in an interstitial: they
        // should get to understand the game and reach Play Again first. Later
        // matches keep the existing return-to-menu midgame ad.
        if (PlayerProfileService.CompletedMatches <= 1)
        {
            AdLog("first completed match: no midgame ad");
            return;
        }
        if (instance != null) instance.pendingMatchAd = true;
    }

    // ---------------------------------------------------------------- rooms
    //
    // The portal's friends UI can only offer a room it has been told about, so
    // the private lobby publishes its code through here. The split is the same
    // as everywhere else in this file: the lobby owns the room, this owns the
    // platform, and no CrazySDK type escapes into gameplay code.

    /// <summary>
    /// Invite parameter carrying the room code, named the way the portal's own
    /// tooling expects. Shared by Room Data, the invite link and the join
    /// listener, so a friend arriving by any route lands in the same room.
    ///
    /// No region parameter: PhotonAppSettings pins FixedRegion to a single region
    /// for every client, so a room code already identifies one room globally and
    /// a region key would be dead weight.
    /// </summary>
    private const string RoomCodeParam = "roomName";

    private string publishedRoom;
    private string pendingInviteCode;
    private bool entryHandled;

    /// <summary>
    /// The frontend only accepts a room request from these states, and
    /// StartPrivateRoom is the live entry point - OnlineSessionService is legacy
    /// with no provider ever registered, so its Create/Join paths only ever
    /// report that online play is unavailable.
    /// </summary>
    private static bool CanStartRoom =>
        FrontendFlow.Instance != null &&
        (FrontendFlow.Instance.State == FrontendState.MainMenu ||
         FrontendFlow.Instance.State == FrontendState.Result ||
         FrontendFlow.Instance.State == FrontendState.Error);

    /// <summary>
    /// A fresh code for a party leader arriving through "play with friends".
    /// Creating and joining are the same named session, so a fixed code would
    /// drop every instant-multiplayer player into one shared room. Confusable
    /// characters are left out because players read these codes to each other.
    /// </summary>
    private static string NewRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new char[6];
        for (int i = 0; i < code.Length; i++) code[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
        return new string(code);
    }

    /// <summary>The portal asked to open straight into a room, because the player
    /// used "play with friends" rather than the game's own menu.</summary>
    public static bool WantsInstantMultiplayer
    {
        get
        {
#if CRAZYGAMES_BUILD
            try { return Ready && CrazySDK.Game.IsInstantMultiplayer; }
            catch { return false; }
#else
            return false;
#endif
        }
    }

    /// <summary>Room code from the invite link the player followed, or empty. Read
    /// once at the menu, because a link is only meaningful on the way in.</summary>
    public static string InvitedRoomCode
    {
        get
        {
#if CRAZYGAMES_BUILD
            try { return Ready ? CrazySDK.Game.GetInviteLinkParameter(RoomCodeParam) ?? string.Empty : string.Empty; }
            catch { return string.Empty; }
#else
            return string.Empty;
#endif
        }
    }

    /// <summary>Announces the room and shows the invite button. Called again
    /// whenever the seat count changes so the portal stops offering a full room.</summary>
    public static void PublishRoom(string roomCode, bool joinable)
    {
#if CRAZYGAMES_BUILD
        if (instance == null || !Ready || string.IsNullOrWhiteSpace(roomCode)) return;
        try
        {
            // Room Data, not the deprecated invite button: the portal builds its
            // own join/invite/friend UI from this, and ShowInviteButton is legacy.
            CrazySDK.Game.UpdateRoom(new UpdateRoomInput
            {
                RoomId = roomCode,
                IsJoinable = joinable,
                InviteParams = new Dictionary<string, string> { { RoomCodeParam, roomCode } }
            });
            instance.publishedRoom = roomCode;
            AdLog("room published id=" + roomCode + " joinable=" + joinable);
        }
        catch (Exception e) { Debug.LogWarning("Platform room update unavailable: " + e.Message); }
#endif
    }

    /// <summary>
    /// Puts a portal invite link for this room on the clipboard. Returns false
    /// when the platform cannot produce one, so the caller stays quiet rather
    /// than claiming a link was copied that the player has not got.
    /// </summary>
    public static bool CopyInviteLink(string roomCode)
    {
#if CRAZYGAMES_BUILD
        if (!Ready || string.IsNullOrWhiteSpace(roomCode)) return false;
        try
        {
            string link = CrazySDK.Game.InviteLink(
                new Dictionary<string, string> { { RoomCodeParam, roomCode } });
            if (string.IsNullOrEmpty(link)) return false;
            CrazySDK.Game.CopyToClipboard(link);
            AdLog("invite link copied " + link);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Platform invite link unavailable: " + e.Message);
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>Withdraws the room. Safe to call when none was published.</summary>
    public static void ClearRoom()
    {
#if CRAZYGAMES_BUILD
        if (instance == null || !Ready || instance.publishedRoom == null) return;
        instance.publishedRoom = null;
        try
        {
            CrazySDK.Game.LeftRoom();
            AdLog("room cleared");
        }
        catch (Exception e) { Debug.LogWarning("Platform room clear unavailable: " + e.Message); }
#endif
    }
    // Called only by the frontend after its existing cleanup reports success.
    public static IEnumerator AfterSuccessfulCleanup()
    {
        if (instance == null || !instance.pendingMatchAd) yield break;
        instance.pendingMatchAd = false;
        yield return instance.RequestAd(false, null);
    }
    public static void RequestMissionDouble(string missionId, Action<bool> completed)
    {
        var mission = MissionCatalog.Find(missionId);
        if (!RewardedAvailable || mission == null || !mission.Completed || mission.Claimed)
        { completed?.Invoke(false); return; }
        int revision = ProfileStore.Revision;
        instance.StartCoroutine(instance.RequestAd(true, success =>
        {
            bool granted = success && revision == ProfileStore.Revision &&
                PlayerProfileService.TryClaimVerifiedDoubleMission(missionId);
            completed?.Invoke(granted);
        }, PlacementMissionDouble));
    }
#if SHIPATON_ANDROID
    private const string PlacementDaily = ShipatonConfig.PlacementDailyCoins;
    private const string PlacementMissionDouble = ShipatonConfig.PlacementMissionDouble;
#else
    private const string PlacementDaily = "daily_coins_rewarded";
    private const string PlacementMissionDouble = "mission_double_rewarded";
#endif
    private IEnumerator RequestAd(bool rewarded, Action<bool> completed, string placement = null)
    {
#if CRAZYGAMES_BUILD
        bool safe = FrontendFlow.Instance != null && (rewarded ? FrontendFlow.Instance.State == FrontendState.MainMenu :
            FrontendFlow.Instance.State == FrontendState.Leaving);
        if (!safe || !Ready || adBusy || adsFailed || basicLaunchDisabled ||
            EnvironmentName == "disabled" || CrazySDK.Ad.AdblockStatus != AdblockStatus.Missing ||
            (rewarded && (!RewardedSupported || RewardedCooldownRemaining > 0)))
        { completed?.Invoke(false); yield break; }
        adBusy = true;
        SetInputBlocked(true);
        AvailabilityChanged?.Invoke();
        bool done = false, success = false, started = false;
        float audio = AudioListener.volume;
        bool background = Application.runInBackground;
        AdLog("REQUEST " + (rewarded ? "rewarded" : "midgame") + " environment=" + EnvironmentName);
        try
        {
            CrazySDK.Ad.RequestAd(rewarded ? CrazyAdType.Rewarded : CrazyAdType.Midgame,
                () => { if (done) return; started = true; AudioListener.volume = 0; AdLog("adStarted audio=" + AudioListener.volume); },
                error => {
                    if (done) return;
                    done = true;
                    if (error.code == "adsDisabledBasicLaunch" || error.code == "adblock") basicLaunchDisabled = true;
                    AdLog("adError code=" + error.code);
                },
                () => { if (done) return; done = true; success = true; observedWorkingAds = true; AdLog("adFinished"); });
            // SDK 5.33 mutes at request time; this game mutes only once adStarted arrives.
            if (!started) AudioListener.volume = audio;
        }
        catch (Exception e) { AdLog("adError exception=" + e.GetType().Name); done = true; }
        float deadline = Time.realtimeSinceStartup + 120f;
        while (!done && Time.realtimeSinceStartup < deadline) yield return null;
        if (!done) { done = true; adsFailed = true; success = false; AdLog("adError timeout"); }
        AudioListener.volume = PlayerAudioSettings.PlatformMuted ? 0 : audio;
        Application.runInBackground = background;
        if (success && rewarded)
        {
            ProfileStore.SetString("RoboMania.Ads.LastRewarded", DateTime.UtcNow.ToString("O"));
            ProfileStore.Save();
        }
        adBusy = false;
        SetInputBlocked(false);
        AdLog("CONTINUE audio=" + AudioListener.volume + " success=" + success);
        completed?.Invoke(success);
        AvailabilityChanged?.Invoke();
#elif SHIPATON_ANDROID
        // Rewarded only (no interstitials on Android). The reward is granted by the caller, and only
        // when ShipatonMonetization reports a completed ad - exactly once per show.
        if (!rewarded || adBusy || !RewardedAvailable) { completed?.Invoke(false); yield break; }
        adBusy = true;
        SetInputBlocked(true);
        AvailabilityChanged?.Invoke();
        bool done = false, success = false;
        ShipatonMonetization.ShowRewarded(placement, ok => { if (done) return; done = true; success = ok; });
        float deadline = Time.realtimeSinceStartup + 200f;
        while (!done && Time.realtimeSinceStartup < deadline) yield return null;
        if (success)
        {
            ProfileStore.SetString("RoboMania.Ads.LastRewarded", DateTime.UtcNow.ToString("O"));
            ProfileStore.Save();
        }
        adBusy = false;
        SetInputBlocked(false);
        completed?.Invoke(success);
        AvailabilityChanged?.Invoke();
#else
        completed?.Invoke(false);
        yield break;
#endif
    }
    public static IEnumerator Initialize()
    {
#if CRAZYGAMES_BUILD
        if (instance != null) yield break;
        instance = new GameObject("CrazyGames Platform").AddComponent<CrazyGamesPlatformService>();
        DontDestroyOnLoad(instance.gameObject);
        bool complete = false;
        try
        {
            if (CrazySDK.IsAvailable) CrazySDK.Init(() => complete = true);
            else complete = true;
        }
        catch (Exception e) { Debug.LogWarning("Platform initialization unavailable: " + e.Message); complete = true; }
        float deadline = Time.realtimeSinceStartup + 15;
        while (!complete && Time.realtimeSinceStartup < deadline) yield return null;
        Ready = complete && CrazySDK.IsInitialized;
        if (Ready)
        {
            EnvironmentName = CrazySDK.Environment;
            AdLog("SDK AVAILABLE: " + CrazySDK.IsAvailable + " ENVIRONMENT: " + EnvironmentName + " VERSION: " + CrazySDK.Version + " AD MODULE: " + (CrazySDK.Ad != null) + " CONFIG MONETIZATION: " + CrazyGamesReleaseSettings.Current.monetizationEnabled);
            ProfileStore.LoadPlatform();
            try
            {
                CrazySDK.User.GetUser(user => AccountDisplayName = user?.username);
                CrazySDK.User.AddAuthListener(instance.OnAccountChanged);
                CrazySDK.Ad.HasAdblock(_ => AvailabilityChanged?.Invoke());
                CrazySDK.Game.AddSettingsChangeListener(instance.OnGameSettings);
                instance.OnGameSettings(CrazySDK.Game.Settings);
                CrazySDK.Game.AddJoinRoomListener(instance.OnJoinRoomRequested);
            }
            catch (Exception e) { Debug.LogWarning("Platform account information unavailable: " + e.Message); }
        }
#elif SHIPATON_ANDROID
        // Android Shipaton build: RevenueCat + AdMob. Never blocks startup: both initialize in the background.
        if (instance != null) yield break;
        instance = new GameObject("Platform Service (Shipaton)").AddComponent<CrazyGamesPlatformService>();
        DontDestroyOnLoad(instance.gameObject);
        ShipatonMonetization.Initialize();
        ShipatonMonetization.AvailabilityChanged += () => AvailabilityChanged?.Invoke();
        yield break;
#else
        yield break;
#endif
    }
#if CRAZYGAMES_BUILD
    private void OnGameSettings(GameSettings settings)
    {
        if (settings != null) PlayerAudioSettings.SetPlatformMuted(settings.muteAudio);
    }
    /// <summary>
    /// A friend accepted an invite while this client was already running.
    ///
    /// Recorded rather than acted on: the callback can land mid-match, and
    /// yanking a player out of a live round to join a room would lose their
    /// match. Update applies it once the player is back at the menu.
    /// </summary>
    private void OnJoinRoomRequested(Dictionary<string, string> inviteParams)
    {
        if (inviteParams == null || !inviteParams.TryGetValue(RoomCodeParam, out string code) ||
            string.IsNullOrWhiteSpace(code)) return;
        pendingInviteCode = code;
        AdLog("join room requested code=" + code);
    }

    private void OnAccountChanged(PortalUser user)
    {
        AccountDisplayName = user?.username;
        // Do not write the prior account's in-memory profile into the new account.
        ProfileStore.SuspendWrites();
        accountChanged = true;
    }
    private void Update()
    {
        if (Ready)
        {
            arena = FortressDuelManager.ActiveArena;
            bool active = arena != null && FrontendFlow.Instance != null && FrontendFlow.Instance.State == FrontendState.Gameplay &&
                (arena.CurrentPhase == FortressDuelPhase.Build || arena.CurrentPhase == FortressDuelPhase.Combat || arena.CurrentPhase == FortressDuelPhase.Rumble);
            if (active != gameplayActive)
            {
                gameplayActive = active;
                if (active) Funnel.Event("gameplay_started");
                try { if (active) CrazySDK.Game.GameplayStart(); else CrazySDK.Game.GameplayStop(); }
                catch (Exception e) { Debug.LogWarning("Platform game event unavailable: " + e.Message); }
            }
        }
        // How the player arrived, resolved once the menu is actually up. A link
        // carrying a room code joins that room; "play with friends" with no code
        // opens a fresh one for the party leader.
        // How the player arrived, resolved once the frontend will actually accept a
        // room (every player has a display name now, so no naming gate). Held until
        // then rather than retried, so an invite is never silently dropped.
        if (!entryHandled && Ready && CanStartRoom)
        {
            entryHandled = true;
            string invited = InvitedRoomCode;
            if (!string.IsNullOrWhiteSpace(invited))
            {
                pendingInviteCode = invited;
                AdLog("entry invite code=" + invited);
            }
            else if (WantsInstantMultiplayer)
            {
                string code = NewRoomCode();
                AdLog("entry instant multiplayer; opening room " + code);
                FrontendFlow.Instance.StartPrivateRoom(code, PrivateRoomAction.Create);
            }
        }

        // Deferred invite. The frontend refuses a room request from anywhere but
        // these states, so acting earlier would throw the invite away.
        if (pendingInviteCode != null && CanStartRoom)
        {
            string code = pendingInviteCode;
            pendingInviteCode = null;
            AdLog("joining invited room " + code);
            FrontendFlow.Instance.StartPrivateRoom(code, PrivateRoomAction.Join);
        }

        if (!accountChanged || FrontendFlow.Instance == null || FrontendFlow.Instance.State != FrontendState.MainMenu) return;
        accountChanged = false;
        if (ProfileStore.LoadPlatform()) PlayerProfileService.NotifyReloaded();
    }
    private void OnDestroy()
    {
        if (Ready)
        {
            CrazySDK.User.RemoveAuthListener(OnAccountChanged);
            CrazySDK.Game.RemoveSettingsChangeListener(OnGameSettings);
            CrazySDK.Game.RemoveJoinRoomListener(OnJoinRoomRequested);
        }
        SetInputBlocked(false);
    }
#endif
    internal static bool TryReadProfile(out string json)
    {
        json = null;
#if CRAZYGAMES_BUILD
        if (!Ready) return false;
        try { json = CrazySDK.Data.GetString(ProfileKey, ""); return true; }
        catch (Exception e) { Debug.LogWarning("Platform data unavailable: " + e.Message); }
#endif
        return false;
    }
    internal static bool TryWriteProfile(string json)
    {
#if CRAZYGAMES_BUILD
        if (!Ready) return false;
        try { CrazySDK.Data.SetString(ProfileKey, json); return true; }
        catch (Exception e) { Debug.LogWarning("Platform save unavailable: " + e.Message); }
#endif
        return false;
    }
}
