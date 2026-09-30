# Robo Mania

**Robo Mania is a 1v1 robot arena game built in Unity.** Two robots, two fortresses, one arena:
build your defences, fight the other robot, and destroy the enemy vault.

> **RevenueCat Shipaton (Next-Gen) submission.** This repository is the Android *Shipaton build* of
> Robo Mania, with the **RevenueCat SDK** and **RevenueCat ad-revenue tracking** integrated.
> Jump to **[RevenueCat Integration](#revenuecat-integration)**.
>
> **Android APK:** see the **[v1.0.0-shipaton GitHub Release](../../releases/tag/v1.0.0-shipaton)**.
> No store submission (Google Play, Galaxy Store or any other) is implied by this build.

---

## Gameplay

- **1v1 duels**: move, aim and fire missiles at the other robot; destroyed robots respawn.
- **Build phase (15 s)** before every fight: place three structures from your **3-card loadout**:
  **Turret**, **Pulse Tower**, **Healing Pad**, **Recovery Jammer** (NO HEAL on enemies),
  **Missile Interceptor** and **Overdrive Pad**.
- **E-Cells** on the field charge your **Spidy** swarm; summon it to storm the enemy vault.
- Destroy the enemy **vault** to win (Victory / Defeat / Draw on time).
- Progression: missions, XP, coins, shop, robot skins, settings, local save.
- Mobile touch controls (move stick, aim/fire, build placement).

## Multiplayer

Online play uses **Photon Fusion 2** (Shared Mode): **QuickPlay** matchmaking, **Create Room / Join
Room** with room codes, and a **RedBot** AI opponent as fallback when no human is found.

## Tech stack

| | |
|---|---|
| Engine | Unity **6000.5.4f1** (URP 17.5), C# |
| Multiplayer | Photon Fusion 2 (Shared Mode) |
| Monetization (Shipaton Android build) | **RevenueCat Unity SDK 9.11.1** (ad-revenue tracking via `AdTracker`) |
| Ad network (Shipaton Android build) | **Google AdMob** via Google Mobile Ads Unity plugin 11.5.0 (rewarded ads) |
| Android dependencies | External Dependency Manager for Unity (EDM4U) 1.2.190 |
| Platforms | **Android** (Shipaton build, IL2CPP ARM64) and **WebGL** (separate CrazyGames build) |

---

## RevenueCat Integration

The RevenueCat SDK is integrated into the **Android Shipaton build** and is initialized and used at
runtime. It is compiled only for that target (`SHIPATON_ANDROID`), so the WebGL/CrazyGames build
does not depend on it.

**Where it lives**

| File | What it does |
|---|---|
| [`Assets/_RobotDeckRoyale/Scripts/Platform/Shipaton/ShipatonMonetization.cs`](Assets/_RobotDeckRoyale/Scripts/Platform/Shipaton/ShipatonMonetization.cs) | RevenueCat initialization, AdMob rewarded ads, and the AdMob → RevenueCat `AdTracker` bridge |
| [`Assets/_RobotDeckRoyale/Scripts/Platform/Shipaton/ShipatonConfig.cs`](Assets/_RobotDeckRoyale/Scripts/Platform/Shipaton/ShipatonConfig.cs) | The single configuration point: RevenueCat public SDK key, rewarded ad unit, placements |
| [`Assets/_RobotDeckRoyale/Scripts/Menu/CrazyGamesPlatformService.cs`](Assets/_RobotDeckRoyale/Scripts/Menu/CrazyGamesPlatformService.cs) | The game's existing platform facade: `#elif SHIPATON_ANDROID` branches route the existing rewarded features to `ShipatonMonetization` |
| [`Packages/manifest.json`](Packages/manifest.json) | `com.revenuecat.purchases-unity` 9.11.1, `com.google.ads.mobile` 11.5.0, EDM4U (OpenUPM) |
| [`Assets/Plugins/Android/mainTemplate.gradle`](Assets/Plugins/Android/mainTemplate.gradle) | Native deps resolved by EDM4U: `purchases-hybrid-common`, `play-services-ads` |

### Initialization

`ShipatonMonetization` is created **once** at startup (`DontDestroyOnLoad`, from the platform
service's `Initialize`). It adds RevenueCat's `Purchases` component with `useRuntimeSetup = true`
and configures it programmatically:

```csharp
purchases.Configure(Purchases.PurchasesConfiguration.Builder.Init(ShipatonConfig.RevenueCatPublicApiKey).Build());
purchases.GetCustomerInfo((info, error) => ...);   // confirms the SDK is live (logs the app user id)
```

It survives scene changes, is never re-created, and registers no duplicate listeners. Failure is
non-fatal by design: a missing key, a configure exception, no network, AdMob init failure or no
fill all just leave the reward buttons unavailable. Startup, menus and matches never wait on
monetization.

### Ad tracking (AdMob → RevenueCat)

**AdMob serves the ads; RevenueCat does not serve ads.** RevenueCat records them through its ad
monetization API (`purchases.AdTracker`, RevenueCat Unity SDK ≥ 9.1.0), fed from the real Google
Mobile Ads callbacks:

| AdMob event | RevenueCat call | Data sent |
|---|---|---|
| `RewardedAd.Load` succeeded | `TrackAdLoaded(AdLoadedData)` | mediator `AdMob`, format `Rewarded`, ad unit, impression id (AdMob response id), network (adapter ad source) |
| `RewardedAd.Load` failed / no fill | `TrackAdFailedToLoad(AdFailedToLoadData)` | mediator, format, ad unit, AdMob error code |
| `OnAdImpressionRecorded` | `TrackAdDisplayed(AdDisplayedData)` | + placement |
| `OnAdFullScreenContentOpened` | `TrackAdOpened(AdOpenedData)` | + placement |
| `OnAdPaid(AdValue)` | `TrackAdRevenue(AdRevenueData)` | **impression-level revenue** in micros (`AdValue.Value`), currency, precision (`Precise→Exact`, `Estimated`, `PublisherProvided→PublisherDefined`), network, placement |

Placements are stable names per feature: `daily_coins_rewarded`, `mission_double_rewarded`.

### Impression-level ad revenue

`TrackAdRevenue` is driven by AdMob's **paid event** (`OnAdPaid`), i.e. impression-level ad revenue
(ILRD). For real revenue on your own AdMob app, turn on **Impression-level ad revenue** in AdMob
(Settings), and opt in to ad tracking on the **Ads** page of the RevenueCat dashboard.

### Rewarded ads (existing game features, kept)

The game's existing rewarded features are unchanged in design and now use AdMob on Android:

- **Daily reward: +100 coins**
- **Mission 2X reward**

A reward is granted **only after a completed rewarded ad**: the AdMob reward callback fired *and*
the ad closed. Load failure, no fill, show failure, early close, errors and duplicate callbacks
grant nothing. The completion callback is guarded to run **exactly once** per show, and the
existing profile checks (`ClaimVerifiedDailyReward`, `TryClaimVerifiedDoubleMission`) still apply.

There are **no in-app purchases**, products, subscriptions or paywalls in this build.

---

## Shipaton Build

This is the **Android Shipaton version** of Robo Mania. The **CrazyGames WebGL version** is a
separate platform target in the same project.

The Shipaton build adds RevenueCat monetization and ad tracking **without changing the core game**:
gameplay, Photon Fusion networking, matchmaking, progression and save are the same code on both
targets.

| | CrazyGames build | Shipaton build |
|---|---|---|
| Platform | WebGL | Android (IL2CPP, ARM64, landscape) |
| Scripting define | `CRAZYGAMES_BUILD` | `SHIPATON_ANDROID` |
| Ads | CrazyGames SDK ads | Google AdMob (rewarded) |
| Ad tracking | n/a | RevenueCat `AdTracker` |
| Platform services | CrazyGames SDK (account, data, rooms) | none (local save) |

The two paths are separated with scripting define symbols inside the game's existing platform
facade (`CrazyGamesPlatformService`): the CrazyGames build contains no RevenueCat/AdMob calls and
the Android build contains no CrazyGames calls.

Version: **1.0.0-shipaton** (Android versionCode 100).

---

## Build instructions

1. **Clone** this repository.
2. **Open** it in **Unity 6000.5.4f1** with the **Android Build Support** module (IL2CPP, SDK/NDK, OpenJDK).
3. **Restore packages**: Unity resolves `Packages/manifest.json` (Unity registry + OpenUPM for
   RevenueCat, Google Mobile Ads and EDM4U). Then run *Assets → External Dependency Manager →
   Android Resolver → Resolve*.
4. **Photon**: set your own Photon Fusion App ID in
   `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset` (`AppIdFusion`). The public repo ships
   the placeholder `YOUR_PHOTON_FUSION_APP_ID`.
5. **RevenueCat**: put your RevenueCat **public** SDK key in `ShipatonConfig.RevenueCatPublicApiKey`
   (`goog_...` for a Google Play app, or `test_...` for RevenueCat's Test Store). The repo contains a
   RevenueCat **Test Store** public key. Never put a RevenueCat *secret* key in the app.
6. **AdMob**: set your AdMob **App ID** in *Assets → Google Mobile Ads → Settings* and your rewarded
   ad unit in `ShipatonConfig.RewardedAdUnitId`. The repo uses **Google's official test IDs**
   (App ID `ca-app-pub-3940256099942544~3347511713`, rewarded unit `ca-app-pub-3940256099942544/5224354917`).
   Keep test IDs while developing.
7. **Android target**: File → Build Profiles → Android, with scripting define `SHIPATON_ANDROID`
   (already set in `ProjectSettings`).
8. **Signing**: create your own keystore **outside the repository** and set it in Player Settings
   → Publishing Settings (or via environment variables for `Tools/Shipaton/ShipatonBuild.cs`:
   `ROBOMANIA_KEYSTORE`, `ROBOMANIA_KEYSTORE_PASS`, `ROBOMANIA_KEY_ALIAS`, `ROBOMANIA_KEY_PASS`).
   No keystore or password is in this repository.
9. **Build** the APK (ARM64, IL2CPP).

Verify at runtime with `adb logcat -s Unity`; every monetization event logs with the `[SHIPATON]`
prefix (`RevenueCat configured`, `RevenueCat ready`, `AdMob initialized`, `rewarded loaded`,
`RevenueCat AdTracker revenue ...`, `rewarded finished success=True`).

## Repository layout

```
Assets/_RobotDeckRoyale/     game code, scenes, art, audio
Assets/_RobotDeckRoyale/Scripts/Platform/Shipaton/   RevenueCat + AdMob (Android Shipaton build)
Assets/Photon/               Photon Fusion 2 SDK
Assets/CrazySDK/             CrazyGames SDK (WebGL build only)
Assets/Plugins/Android/      Gradle templates (EDM4U-managed dependencies)
Packages/, ProjectSettings/  Unity project configuration
Tools/Shipaton/              editor scripts: target setup, compile checks, APK build
```

Third-party SDKs and assets included here (Photon Fusion, CrazyGames SDK, TextMesh Pro, fonts and
models) remain under their own licenses.
