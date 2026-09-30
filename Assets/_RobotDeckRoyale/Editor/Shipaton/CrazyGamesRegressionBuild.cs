// Regression check for the CrazyGames WebGL target after the Shipaton (Android) integration:
// builds WebGL with the project's own settings (CRAZYGAMES_BUILD) into a separate test folder.
//   Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod CrazyGamesRegressionBuild.BuildCli
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class CrazyGamesRegressionBuild
{
    public const string Output = "Builds/WebGL_Regression_Shipaton";

    public static void BuildCli()
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
        bool ok = defines.Split(';').Contains("CRAZYGAMES_BUILD") && !defines.Split(';').Contains("SHIPATON_ANDROID");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = Output,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
        });
        var s = report.summary;
        Debug.Log($"[CRAZYGAMES REGRESSION] defines ok={ok} result={s.result} errors={s.totalErrors} time={s.totalTime}");
        EditorApplication.Exit(s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded && ok ? 0 : 1);
    }
}
