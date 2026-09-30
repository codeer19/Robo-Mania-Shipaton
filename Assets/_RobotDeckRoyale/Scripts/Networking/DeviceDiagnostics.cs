using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// TEMPORARY DIAGNOSTIC. Mirrors the log to a file on the device.
///
/// Android is the only platform where the match cannot be inspected: managed
/// Debug.Log output does not reach logcat from this build, so a phone that
/// misbehaves gives back nothing at all. Writing our own file sidesteps logcat
/// entirely - pull it with:
///
///   adb pull /sdcard/Android/data/&lt;package&gt;/files/robomania_log.txt
///
/// Development builds only, so a shipped player never writes it. Delete once the
/// online flow is settled.
/// </summary>
public static class DeviceDiagnostics
{
    public const string LogFileName = "robomania_log.txt";

    private static StreamWriter writer;
    private static readonly object gate = new object();
    private static readonly System.Diagnostics.Stopwatch elapsed = new System.Diagnostics.Stopwatch();

    /// <summary>
    /// Per-process file name.
    ///
    /// The Editor and a standalone build on one machine share a persistent data
    /// path, so a single fixed name meant whichever started last silently wiped
    /// the other's log - and the surviving file looked like both clients.
    /// </summary>
    public static string LogPath => Path.Combine(
        Application.persistentDataPath,
        $"{Path.GetFileNameWithoutExtension(LogFileName)}_" +
        $"{System.Diagnostics.Process.GetCurrentProcess().Id}.txt");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (!Debug.isDebugBuild)
        {
            return;
        }

        try
        {
            elapsed.Restart();
            // Truncated per launch: a log that grows across sessions makes it far
            // too easy to read yesterday's run and think it is today's.
            writer = new StreamWriter(LogPath, false, Encoding.UTF8) { AutoFlush = true };
        }
        catch (IOException exception)
        {
            Debug.LogWarning($"[DEVICE LOG] Could not open {LogPath}: {exception.Message}");
            return;
        }

        Application.logMessageReceivedThreaded += Write;
        Application.quitting += Close;

        Write(
            $"session start {System.DateTime.Now:HH:mm:ss} device={SystemInfo.deviceModel} " +
            $"os={SystemInfo.operatingSystem} graphics={SystemInfo.graphicsDeviceType} " +
            $"screen={Screen.width}x{Screen.height}",
            string.Empty, LogType.Log);
    }

    private static void Write(string message, string stackTrace, LogType type)
    {
        if (writer == null) return;

        lock (gate)
        {
            if (writer == null) return;

            try
            {
                // logMessageReceivedThreaded also runs on Photon/worker threads.
                // Unity's Time API throws there, recursively logging another error.
                writer.WriteLine($"{elapsed.Elapsed.TotalSeconds:0.00} [{type}] {message}");

                // Stack traces only for the entries that need one; everything else
                // would bury the sequence we are actually reading the file for.
                if (type == LogType.Exception || type == LogType.Error)
                {
                    writer.WriteLine(stackTrace);
                }
            }
            catch (IOException)
            {
                // A failed diagnostic write must never take the game with it.
            }
        }
    }

    private static void Close()
    {
        lock (gate)
        {
            Application.logMessageReceivedThreaded -= Write;
            writer?.Flush();
            writer?.Dispose();
            writer = null;
        }
    }
}
