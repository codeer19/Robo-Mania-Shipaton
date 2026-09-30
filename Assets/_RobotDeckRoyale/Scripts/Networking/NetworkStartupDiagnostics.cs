using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Safe connection identity and slow startup-step evidence; never logs an AppId or credentials.</summary>
public static class NetworkStartupDiagnostics
{
    public static void Connection(NetworkRunner runner, string session, int attempt, string stage)
    {
        var settings = PhotonAppSettings.Global.AppSettings;
        string appHash;
        using (var hash = SHA256.Create())
            appHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(settings.AppIdFusion ?? ""))).Replace("-", "").Substring(0, 12);
        UnityEngine.Debug.Log($"[ANDROID PHOTON] Platform={Application.platform} Stage={stage} " +
            $"RunnerInstance={runner.GetEntityId()} IsRunning={runner.IsRunning} GameMode={runner.GameMode} " +
            $"SessionName={session} Region={settings.FixedRegion} Scene={SceneManager.GetActiveScene().name} " +
            $"StartAttemptId={attempt} AppIdPresent={!string.IsNullOrWhiteSpace(settings.AppIdFusion)} AppFingerprint={appHash} " +
            $"AppVersion={settings.AppVersion} Protocol={settings.Protocol} Fallback={settings.EnableProtocolFallback} " +
            $"Fusion={typeof(NetworkRunner).Assembly.GetName().Version} Unity={Application.unityVersion} Build={Application.buildGUID}");
    }

    // Longest step that finished during the frame currently being measured. The
    // watchdog reads and clears it, so a reported hitch always carries the worst
    // step from the frame that actually stalled.
    private static string worstName;
    private static double worstMilliseconds;

    public readonly struct Step : IDisposable
    {
        private readonly string name;
        private readonly long start;
        public Step(string name) { this.name = name; start = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            if (milliseconds > worstMilliseconds) { worstMilliseconds = milliseconds; worstName = name; }
            if (milliseconds > 100) UnityEngine.Debug.LogWarning($"[STARTUP TIMING] {name} took {milliseconds:F0}ms");
#endif
        }
    }

    /// <summary>
    /// Names the frame that stalled, and the slowest instrumented step inside it.
    ///
    /// The Android arena startup blocks the main thread outright, which means no
    /// per-frame diagnostic of any kind reaches the log while it is happening -
    /// the evidence is a silent gap, and a gap names nothing. Reporting after the
    /// fact is the only way to attribute it. A hitch with no step attached is just
    /// as informative: it proves the cost is outside every bracket below.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallHitchWatchdog()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var host = new GameObject("StartupHitchWatchdog") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<StartupHitchWatchdog>();
#endif
    }

    internal static void ReportFrame(float seconds)
    {
        // The opening frames measure engine and scene startup rather than a stall,
        // so reporting them only produces a guaranteed false positive.
        if (Time.frameCount > 3 && seconds > 0.5f)
            UnityEngine.Debug.LogWarning($"[FRAME HITCH] frame={Time.frameCount} blocked={seconds * 1000f:F0}ms " +
                $"slowestStep={(worstName ?? "<none instrumented>")}/{worstMilliseconds:F0}ms " +
                $"scene={SceneManager.GetActiveScene().name}");
        worstName = null;
        worstMilliseconds = 0d;
    }
}

/// <summary>Reports the previous frame's duration, so a stalled frame is still attributable.</summary>
internal sealed class StartupHitchWatchdog : MonoBehaviour
{
    private void Update() => NetworkStartupDiagnostics.ReportFrame(Time.unscaledDeltaTime);
}
