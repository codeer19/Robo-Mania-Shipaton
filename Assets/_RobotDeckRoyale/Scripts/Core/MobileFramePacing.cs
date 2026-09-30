using UnityEngine;

/// <summary>
/// Paces phones at the game's 60 fps design target.
///
/// Left at the default, the browser asks for a frame on every display refresh.
/// Many Android phones refresh at 90 or 120 Hz, and a mid-range phone cannot
/// finish this game's frame in 8-11 ms, so frames alternate between one and two
/// refreshes: uneven pacing that reads as lag, twice the work per second, and a
/// phone that heats up and throttles. Capped at 60 the frames arrive evenly.
/// Desktop keeps rendering at the display rate.
/// </summary>
public static class MobileFramePacing
{
    public const int TargetFramesPerSecond = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        if (!Application.isMobilePlatform) return;
        Application.targetFrameRate = TargetFramesPerSecond;
    }
}
