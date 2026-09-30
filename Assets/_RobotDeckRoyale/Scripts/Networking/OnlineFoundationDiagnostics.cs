using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Device evidence for the player foundation. Compiled only into development builds.</summary>
public static class OnlineFoundationDiagnostics
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static string path;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void StartCapture()
    {
        // Per-process name: two clients on one machine share a persistent data
        // path, so a single fixed file made the second one throw a sharing
        // violation on startup every run.
        path = Path.Combine(
            Application.persistentDataPath,
            $"online-foundation_{System.Diagnostics.Process.GetCurrentProcess().Id}.log");
        File.WriteAllText(path, "Foundation capture " + DateTime.UtcNow.ToString("O") + "\n");
        Application.logMessageReceived -= Capture;
        Application.logMessageReceived += Capture;
    }

    private static void Capture(string message, string stack, LogType type)
    {
        if (message.StartsWith("[STARTUP]") || message.StartsWith("[MENU STAGE]") ||
            message.StartsWith("[SKIN]") || message.StartsWith("[MATCH ENTRY]") || message.StartsWith("[MATCH STATE]") ||
            message.StartsWith("[COMBAT") || message.StartsWith("[MISSILE") || message.StartsWith("[DAMAGE") ||
            message.StartsWith("[HEIST") || message.StartsWith("[ECELL") || message.StartsWith("[SPIDY") || message.StartsWith("[RESULT") ||
            message.StartsWith("[INTRO]") || message.StartsWith("[LOCAL STATE]") ||
            message.StartsWith("[REMOTE STATE]") || message.StartsWith("[AUTHORITY]") ||
            message.StartsWith("[AVATAR]") || message.StartsWith("[SPAWN]") ||
            message.StartsWith("[RUNNER]") || message.StartsWith("[PRIVATE") ||
            message.StartsWith("[LOBBY") || type == LogType.Exception || type == LogType.Error)
            File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + message + "\n" +
                (type == LogType.Exception ? stack + "\n" : ""));
    }

    public static void Snapshot(NetworkedPlayerAvatar avatar)
    {
        var local = FortressDuelManager.ActiveArena?.LocalPlayer;
        var source = avatar.IsLocalAvatar && local != null ? local.gameObject : avatar.gameObject;
        var nodes = source.GetComponentsInChildren<Transform>(true)
            .Where(t => t == source.transform || t.name == "Visual" || t.name == "Spark")
            .Select(t => $"{t.name}: world={t.position:F3} local={t.localPosition:F3} scale={t.localScale:F3} active={t.gameObject.activeInHierarchy}; components=" +
                string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name)));
        File.AppendAllText(path, $"[HIERARCHY] Owner={avatar.OwnerPlayerRef} Local={avatar.IsLocalAvatar} Team={avatar.Team} StateAuthority={avatar.Object.StateAuthority}\n" + string.Join("\n", nodes) + "\n");
    }
#else
    public static void Snapshot(NetworkedPlayerAvatar avatar) { }
#endif
}
