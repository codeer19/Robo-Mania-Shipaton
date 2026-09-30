// Builds the Android Shipaton APK (RevenueCat + AdMob, SHIPATON_ANDROID). Dev/editor only.
// Signing comes from a local file OUTSIDE the repository (never committed):
//   E:/RoboMania_Signing/keystore-credentials.properties  (keystore=, alias=, storepass=, keypass=)
// or from env vars ROBOMANIA_KEYSTORE / ROBOMANIA_KEYSTORE_PASS / ROBOMANIA_KEY_ALIAS / ROBOMANIA_KEY_PASS.
//   Editor:  menu Robo Mania > Shipaton > Build Android APK
//   CLI:     Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod ShipatonBuild.BuildCli
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ShipatonBuild
{
    [MenuItem("Robo Mania/Shipaton/Build Android APK")]
    public static void BuildMenu() => Debug.Log("[SHIPATON BUILD] " + Build());

    public static void BuildCli()
    {
        string msg = Build();
        Debug.Log("[SHIPATON BUILD] " + msg);
        EditorApplication.Exit(msg.StartsWith("result=Succeeded") ? 0 : 1);
    }

    public const string Version = "1.0.0-shipaton";
    public const int VersionCode = 100;
    public const string Output = "Builds/Shipaton/RoboMania-Shipaton-Android.apk";
    const string CredentialsFile = "E:/RoboMania_Signing/keystore-credentials.properties";

    public static string Build()
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        if (!defines.Split(';').Contains("SHIPATON_ANDROID")) return "SHIPATON_ANDROID missing: run ShipatonSetup.Configure first";

        string keystore = Env("ROBOMANIA_KEYSTORE"), storePass = Env("ROBOMANIA_KEYSTORE_PASS"),
               alias = Env("ROBOMANIA_KEY_ALIAS"), keyPass = Env("ROBOMANIA_KEY_PASS");
        if (keystore == null && File.Exists(CredentialsFile))
        {
            var kv = File.ReadAllLines(CredentialsFile).Where(l => l.Contains('=')).ToDictionary(l => l.Split('=')[0].Trim(), l => l.Substring(l.IndexOf('=') + 1).Trim());
            keystore = kv["keystore"]; alias = kv["alias"]; storePass = kv["storepass"]; keyPass = kv["keypass"];
        }
        if (keystore == null) return "no signing credentials found (see header)";

        // Shared player settings are set for this build only and restored afterwards (the WebGL
        // CrazyGames target keeps its own version and has no keystore).
        string oldVersion = PlayerSettings.bundleVersion;
        int oldCode = PlayerSettings.Android.bundleVersionCode;
        bool oldCustom = PlayerSettings.Android.useCustomKeystore;
        string oldKs = PlayerSettings.Android.keystoreName, oldAlias = PlayerSettings.Android.keyaliasName;
        try
        {
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = keyPass;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string msg = $"result={s.result} size={s.totalSize} errors={s.totalErrors} time={s.totalTime} output={s.outputPath}";
            File.WriteAllText("Temp/shipaton_build_result.txt", msg);
            return msg;
        }
        finally
        {
            PlayerSettings.bundleVersion = oldVersion;
            PlayerSettings.Android.bundleVersionCode = oldCode;
            PlayerSettings.Android.useCustomKeystore = oldCustom;
            PlayerSettings.Android.keystoreName = oldKs;
            PlayerSettings.Android.keyaliasName = oldAlias;
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasPass = "";
        }
    }

    static string Env(string name) { var v = Environment.GetEnvironmentVariable(name); return string.IsNullOrEmpty(v) ? null : v; }
}
