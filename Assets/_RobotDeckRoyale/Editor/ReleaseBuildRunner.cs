using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CrazyGames;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Runs the CrazyGames SDK release build without its AutoRunPlayer step and into
/// a chosen folder, reusing the SDK builder's own settings, ASTC data variant,
/// 512/1024 MB wasm variants and crazygames_build_report.json via reflection, so
/// the output matches what the SDK's Build window produces.
///   ReleaseBuildRunner.StartFull("Builds/CrazyGamesRelease_v2")
///   ReleaseBuildRunner.StartQuick("Builds/RM_TestRelease")   (DXT variant only)
/// Progress and the result are written to Temp/release_build_status.txt.
/// </summary>
public static class ReleaseBuildRunner
{
    private const string StatusPath = "Temp/release_build_status.txt";

    public static string StartFull(string outputPath) => Schedule(outputPath, full: true);
    public static string StartQuick(string outputPath) => Schedule(outputPath, full: false);

    private static string Schedule(string outputPath, bool full)
    {
        File.WriteAllText(StatusPath, "queued " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        EditorApplication.delayCall += () => Run(outputPath, full);
        return "scheduled " + (full ? "full" : "quick") + " -> " + outputPath;
    }

    /// <summary>Synchronous variant for callers that cannot rely on delayCall ticking.</summary>
    public static string RunNow(string outputPath, bool full)
    {
        File.WriteAllText(StatusPath, "started " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        Run(outputPath, full);
        return File.ReadAllText(StatusPath);
    }

    private static void Status(string line) => File.AppendAllText(StatusPath, DateTime.Now.ToString("HH:mm:ss") + " " + line + "\n");

    private static void Run(string outputPath, bool full)
    {
        var start = DateTime.Now;
        var builder = new Builder();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        MethodInfo Method(string name) => typeof(Builder).GetMethod(name, flags);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);

        Method("StoreInitialSettings").Invoke(builder, null);
        var optimizations = (List<BuildOptimization>)Method("ConfigureWebGLSettings").Invoke(builder, new object[] { BuildVariant.Release });
        try
        {
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;
            Status("building main (DXT) scenes=" + string.Join(",", scenes.Select(Path.GetFileNameWithoutExtension)));
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Status($"main result={report.summary.result} size={report.summary.totalSize} time={report.summary.totalTime.TotalSeconds:F0}s errors={report.summary.totalErrors}");
            if (report.summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var message in step.messages.Where(m => m.type == LogType.Error))
                        Status("ERROR " + message.content);
                Status("FAILED");
                return;
            }
            SaveAssetReport(report, outputPath);
            if (!full)
            {
                Status("DONE quick");
                return;
            }
            Status("building ASTC data variant");
            var astc = (string)Method("DoASTCBuild").Invoke(builder, new object[] { options, report });
            Status("building 512/1024 MB wasm variants");
            var limited = (LimitedMemoryFiles)Method("DoLimitedMemoryBuilds").Invoke(builder, new object[] { options, report });
            new BuildReportGenerator().GenerateReport(report, BuildVariant.Release,
                new AdditionalBuildOptions { supportsMobile = true }, outputPath, start, optimizations, astc, limited);
            Status($"DONE full astc={astc} max512={limited?.max512mb} max1024={limited?.max1024mb} total={(DateTime.Now - start).TotalMinutes:F1}min");
        }
        catch (Exception exception)
        {
            Status("EXCEPTION " + exception);
        }
        finally
        {
            Method("RestoreWebGLSettings").Invoke(builder, null);
            AssetDatabase.SaveAssets();
        }
    }

    // Largest packed assets, for the size report (Unity Build Report data).
    private static void SaveAssetReport(BuildReport report, string outputPath)
    {
        var rows = report.packedAssets
            .SelectMany(p => p.contents)
            .GroupBy(c => c.sourceAssetPath)
            .Select(g => (path: g.Key, bytes: g.Aggregate(0UL, (s, c) => s + c.packedSize), type: g.First().type.Name))
            .OrderByDescending(r => r.bytes)
            .ToList();
        var lines = new List<string> { $"total packed {rows.Aggregate(0UL, (s, r) => s + r.bytes) / 1024} KB in {rows.Count} assets" };
        lines.AddRange(rows.Take(80).Select(r => $"{r.bytes / 1024,8} KB  {r.type,-14} {r.path}"));
        File.WriteAllLines(Path.Combine("Temp", "release_asset_report_" + Path.GetFileName(outputPath) + ".txt"), lines);
    }
}
