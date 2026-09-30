#if SHIPATON_ANDROID
/// <summary>
/// The one place the Android Shipaton build's monetization values live. Only client-side,
/// publicly embeddable values belong here (RevenueCat public SDK key, AdMob ad unit ids):
/// never a RevenueCat secret key, AdMob account credential or signing password.
///
/// The AdMob *App ID* is set separately, in Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings
/// (Assets > Google Mobile Ads > Settings), because the plugin writes it into the Android manifest.
///
/// The values below are TEST values: RevenueCat's Test Store key and Google's official public
/// test rewarded ad unit. To ship with your own, replace them (see README "Build instructions").
/// </summary>
public static class ShipatonConfig
{
    /// <summary>RevenueCat public SDK key (Project settings > API keys). "test_..." = Test Store,
    /// "goog_..." = Google Play app. Public by design; placeholder: YOUR_REVENUECAT_PUBLIC_KEY.</summary>
    public const string RevenueCatPublicApiKey = "test_PeYnKkOYPIbCDYBmzOpiNrZAJzF";

    /// <summary>AdMob rewarded ad unit. Google's official test unit; placeholder: YOUR_REWARDED_AD_UNIT_ID.</summary>
    public const string RewardedAdUnitId = "ca-app-pub-3940256099942544/5224354917";

    /// <summary>True while the ids above are Google's test ids (logged at startup).</summary>
    public const bool UsingTestAds = true;

    /// <summary>RevenueCat ad placements: stable names, one per rewarded feature.</summary>
    public const string PlacementDailyCoins = "daily_coins_rewarded";
    public const string PlacementMissionDouble = "mission_double_rewarded";

    public static bool HasRevenueCatKey =>
        !string.IsNullOrWhiteSpace(RevenueCatPublicApiKey) && !RevenueCatPublicApiKey.StartsWith("YOUR_");
    public static bool HasRewardedUnit =>
        !string.IsNullOrWhiteSpace(RewardedAdUnitId) && !RewardedAdUnitId.StartsWith("YOUR_");
}
#endif
