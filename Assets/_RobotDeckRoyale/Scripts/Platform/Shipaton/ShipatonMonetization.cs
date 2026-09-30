#if SHIPATON_ANDROID
using System;
using GoogleMobileAds.Api;
using RevenueCat;
using UnityEngine;

/// <summary>
/// Android Shipaton monetization: RevenueCat SDK + AdMob rewarded ads, with every ad lifecycle
/// event reported to RevenueCat's AdTracker.
///
///   AdMob serves the ad. RevenueCat records it: loaded, failed to load, displayed (impression),
///   opened, and impression-level revenue (AdMob paid event, micros + currency + precision).
///
/// Monetization is a layer, never a dependency: if RevenueCat or AdMob fail to initialize, have no
/// fill or error, the game plays on and the reward buttons simply stay unavailable. Created once
/// (DontDestroyOnLoad) from CrazyGamesPlatformService.Initialize on the Shipaton target only.
/// </summary>
public sealed class ShipatonMonetization : MonoBehaviour
{
    private const string Tag = "[SHIPATON] ";
    private static ShipatonMonetization instance;

    public static bool RevenueCatConfigured { get; private set; }
    public static bool AdsInitialized { get; private set; }
    public static bool RewardedReady => instance != null && !instance.showing && instance.rewarded != null && instance.rewarded.CanShowAd();
    public static bool Busy => instance != null && instance.showing;
    public static event Action AvailabilityChanged;

    private Purchases purchases;
    private RewardedAd rewarded;
    private bool loading, showing, earned, finished;
    private float retryAt, closeGraceUntil = -1f, watchdogUntil;
    private int loadFailures;
    private string placement;
    private Action<bool> onFinished;
    private float savedVolume;

    public static void Initialize()
    {
        if (instance != null) return;
        var host = new GameObject("Shipaton Monetization");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<ShipatonMonetization>();
    }

    // ---------------------------------------------------------------- startup
    private void Awake()
    {
        Debug.Log(Tag + "init; testAds=" + ShipatonConfig.UsingTestAds);
        try
        {
            // Inactive while configuring, so Purchases.Start (which picks the Android wrapper)
            // sees useRuntimeSetup and waits for our Configure call.
            var go = new GameObject("RevenueCat Purchases");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            purchases = go.AddComponent<Purchases>();
            purchases.useRuntimeSetup = true;
            go.SetActive(true);
        }
        catch (Exception e) { Debug.LogWarning(Tag + "RevenueCat component unavailable: " + e.Message); purchases = null; }
    }

    private System.Collections.IEnumerator Start()
    {
        yield return null;          // Purchases.Start has run: its native wrapper exists.
        ConfigureRevenueCat();
        InitializeAds();
    }

    private void ConfigureRevenueCat()
    {
        if (purchases == null) return;
        if (!ShipatonConfig.HasRevenueCatKey) { Debug.LogWarning(Tag + "RevenueCat key not set; SDK not configured."); return; }
        try
        {
            purchases.Configure(Purchases.PurchasesConfiguration.Builder.Init(ShipatonConfig.RevenueCatPublicApiKey).Build());
            RevenueCatConfigured = true;
            Debug.Log(Tag + "RevenueCat configured");
            purchases.GetCustomerInfo((info, error) =>
            {
                if (error != null) Debug.LogWarning(Tag + "RevenueCat customer info error: " + error.Message);
                else Debug.Log(Tag + "RevenueCat ready; appUserId=" + info.OriginalAppUserId);
            });
        }
        catch (Exception e)
        {
            RevenueCatConfigured = false;
            Debug.LogWarning(Tag + "RevenueCat configure failed (game continues): " + e.Message);
        }
    }

    private void InitializeAds()
    {
        if (!ShipatonConfig.HasRewardedUnit) { Debug.LogWarning(Tag + "rewarded unit not set; ads off."); return; }
        try
        {
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            MobileAds.Initialize(status =>
            {
                AdsInitialized = true;
                Debug.Log(Tag + "AdMob initialized");
                LoadRewarded();
            });
        }
        catch (Exception e) { Debug.LogWarning(Tag + "AdMob init failed (game continues): " + e.Message); }
    }

    // ---------------------------------------------------------------- loading
    private void LoadRewarded()
    {
        if (loading || rewarded != null || !AdsInitialized) return;
        loading = true;
        try
        {
            RewardedAd.Load(ShipatonConfig.RewardedAdUnitId, new AdRequest(), (ad, error) =>
            {
                loading = false;
                if (error != null || ad == null)
                {
                    loadFailures++;
                    retryAt = Time.unscaledTime + Mathf.Min(120f, 5f * Mathf.Pow(2f, Mathf.Min(loadFailures, 5)));
                    Debug.Log(Tag + "rewarded load failed: " + (error != null ? error.GetMessage() : "no ad"));
                    Track(t => t.TrackAdFailedToLoad(new AdFailedToLoadData(
                        AdTracker.MediatorName.AdMob, AdTracker.Format.Rewarded, ShipatonConfig.RewardedAdUnitId,
                        placement: null, mediatorErrorCode: error != null ? error.GetCode() : (int?)null)), "failed_to_load");
                    AvailabilityChanged?.Invoke();
                    return;
                }
                loadFailures = 0;
                rewarded = ad;
                Hook(ad);
                Debug.Log(Tag + "rewarded loaded network=" + Network(ad));
                Track(t => t.TrackAdLoaded(new AdLoadedData(
                    AdTracker.MediatorName.AdMob, AdTracker.Format.Rewarded, ShipatonConfig.RewardedAdUnitId,
                    ImpressionId(ad), Network(ad))), "loaded");
                AvailabilityChanged?.Invoke();
            });
        }
        catch (Exception e) { loading = false; retryAt = Time.unscaledTime + 30f; Debug.LogWarning(Tag + "rewarded load exception: " + e.Message); }
    }

    private void Hook(RewardedAd ad)
    {
        ad.OnAdImpressionRecorded += () => Track(t => t.TrackAdDisplayed(new AdDisplayedData(
            AdTracker.MediatorName.AdMob, AdTracker.Format.Rewarded, ShipatonConfig.RewardedAdUnitId,
            ImpressionId(ad), Network(ad), placement)), "displayed");
        ad.OnAdFullScreenContentOpened += () =>
        {
            savedVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            Track(t => t.TrackAdOpened(new AdOpenedData(
                AdTracker.MediatorName.AdMob, AdTracker.Format.Rewarded, ShipatonConfig.RewardedAdUnitId,
                ImpressionId(ad), Network(ad), placement)), "opened");
        };
        // Impression-level ad revenue: AdMob reports AdValue.Value in micros on Android.
        ad.OnAdPaid += value => Track(t => t.TrackAdRevenue(new AdRevenueData(
            AdTracker.MediatorName.AdMob, AdTracker.Format.Rewarded, ShipatonConfig.RewardedAdUnitId,
            ImpressionId(ad), value.Value, string.IsNullOrEmpty(value.CurrencyCode) ? "USD" : value.CurrencyCode,
            PrecisionOf(value), Network(ad), placement)), "revenue micros=" + value.Value + " " + value.CurrencyCode + " precision=" + value.Precision);
        // Closed: the reward callback can land a moment after this on some devices, so finish after a short grace.
        ad.OnAdFullScreenContentClosed += () => { if (!finished) closeGraceUntil = Time.unscaledTime + 0.6f; };
        ad.OnAdFullScreenContentFailed += err => { Debug.Log(Tag + "show failed: " + err?.GetMessage()); Finish(false); };
    }

    // ---------------------------------------------------------------- showing
    /// <summary>Shows a rewarded ad. <paramref name="done"/> is called exactly once: true only when the
    /// ad played to its reward and closed. Load failure, no fill, show failure, early close: false.</summary>
    public static void ShowRewarded(string adPlacement, Action<bool> done)
    {
        if (instance == null || !RewardedReady) { done?.Invoke(false); return; }
        instance.Show(adPlacement, done);
    }

    private void Show(string adPlacement, Action<bool> done)
    {
        var ad = rewarded;
        rewarded = null;                 // one ad, one show
        showing = true; earned = false; finished = false; closeGraceUntil = -1f;
        placement = adPlacement;
        onFinished = done;
        watchdogUntil = Time.unscaledTime + 180f;
        AvailabilityChanged?.Invoke();
        Debug.Log(Tag + "show rewarded placement=" + placement);
        currentAd = ad;
        try { ad.Show(reward => { earned = true; Debug.Log(Tag + "reward earned " + reward?.Amount + " " + reward?.Type); }); }
        catch (Exception e) { Debug.LogWarning(Tag + "show exception: " + e.Message); Finish(false); }
    }

    private RewardedAd currentAd;

    private void Finish(bool success)
    {
        if (finished) return;            // exactly one completion, whatever callbacks follow
        finished = true;
        showing = false;
        closeGraceUntil = -1f;
        if (AudioListener.volume == 0f && savedVolume > 0f) AudioListener.volume = savedVolume;
        try { currentAd?.Destroy(); } catch { }
        currentAd = null;
        Debug.Log(Tag + "rewarded finished success=" + success + " placement=" + placement);
        var cb = onFinished; onFinished = null;
        try { cb?.Invoke(success); } catch (Exception e) { Debug.LogException(e); }
        retryAt = Time.unscaledTime + 1f;  // preload the next one
        AvailabilityChanged?.Invoke();
    }

    private void Update()
    {
        if (showing && !finished)
        {
            if (closeGraceUntil > 0f && Time.unscaledTime >= closeGraceUntil) Finish(earned);
            else if (Time.unscaledTime >= watchdogUntil) Finish(false);
        }
        if (AdsInitialized && !showing && rewarded == null && !loading && Time.unscaledTime >= retryAt) LoadRewarded();
    }

    private void OnApplicationPause(bool paused)
    {
        // Returning from the ad activity: if the close callback never came, the grace timer still ends the show.
        if (!paused && showing && !finished && closeGraceUntil < 0f) closeGraceUntil = Time.unscaledTime + 2f;
    }

    // ---------------------------------------------------------------- RevenueCat bridge
    private void Track(Action<AdTracker> call, string what)
    {
        if (!RevenueCatConfigured || purchases == null || purchases.AdTracker == null) return;
        try { call(purchases.AdTracker); Debug.Log(Tag + "RevenueCat AdTracker " + what); }
        catch (Exception e) { Debug.LogWarning(Tag + "RevenueCat AdTracker " + what + " failed: " + e.Message); }
    }

    private static string ImpressionId(RewardedAd ad)
    {
        try { return ad?.GetResponseInfo()?.GetResponseId() ?? string.Empty; } catch { return string.Empty; }
    }

    private static string Network(RewardedAd ad)
    {
        try { var n = ad?.GetResponseInfo()?.GetLoadedAdapterResponseInfo()?.AdSourceName; return string.IsNullOrEmpty(n) ? "AdMob" : n; } catch { return "AdMob"; }
    }

    private static AdTracker.Precision PrecisionOf(AdValue value)
    {
        switch (value.Precision)
        {
            case AdValue.PrecisionType.Precise: return AdTracker.Precision.Exact;
            case AdValue.PrecisionType.Estimated: return AdTracker.Precision.Estimated;
            case AdValue.PrecisionType.PublisherProvided: return AdTracker.Precision.PublisherDefined;
            default: return AdTracker.Precision.Unknown;
        }
    }
}
#endif
