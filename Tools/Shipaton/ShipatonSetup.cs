// Dev only: Shipaton Android target setup + compile checks.
//   run_script Tools/Shipaton/ShipatonSetup.cs ShipatonSetup.Configure
//   run_script Tools/Shipaton/ShipatonSetup.cs ShipatonSetup.CompileCheck  ["Android"|"WebGL"]
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEngine;

public static class ShipatonSetup
{
    public const string AdMobTestAppId = "ca-app-pub-3940256099942544~3347511713";   // Google's public test App ID

    public static string Configure()
    {
        var target = NamedBuildTarget.Android;
        var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0).ToList();
        defines.Remove("CRAZYGAMES_BUILD");
        if (!defines.Contains("SHIPATON_ANDROID")) defines.Add("SHIPATON_ANDROID");
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));

        // AdMob App ID lives in the plugin's settings asset (written into the Android manifest).
        var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings")).FirstOrDefault(x => x != null);
        string admob = "settings type not found";
        if (t != null)
        {
            var load = t.GetMethod("LoadInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var inst = load.Invoke(null, null) as UnityEngine.Object;
            var prop = t.GetProperty("GoogleMobileAdsAndroidAppId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            prop.SetValue(inst, AdMobTestAppId);
            EditorUtility.SetDirty(inst);
            AssetDatabase.SaveAssets();
            admob = AssetDatabase.GetAssetPath(inst) + " androidAppId=" + prop.GetValue(inst);
        }
        return "android defines: " + PlayerSettings.GetScriptingDefineSymbols(target) + "\nwebgl defines: " +
               PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL) + "\n" + admob;
    }

    public static string CompileCheck(string platform)
    {
        var bt = platform == "WebGL" ? BuildTarget.WebGL : BuildTarget.Android;
        var group = BuildPipeline.GetBuildTargetGroup(bt);
        var s = new ScriptCompilationSettings { target = bt, group = group, options = ScriptCompilationOptions.None };
        var dir = "Temp/CompileCheck_" + platform;
        var result = PlayerBuildInterface.CompilePlayerScripts(s, dir);
        var names = result.assemblies != null ? string.Join(", ", result.assemblies.Where(a => a.Contains("Assembly-CSharp") || a.ToLower().Contains("revenuecat") || a.Contains("GoogleMobile"))) : "";
        return platform + " compiled assemblies: " + (result.assemblies?.Count ?? 0) + " | " + names;
    }
}

public static class ShipatonGradle
{
    // Turn on the custom Gradle templates (Android only) so EDM4U can patch in the RevenueCat and
    // Google Mobile Ads native dependencies, then run the Android resolver.
    public static string EnableTemplatesAndResolve()
    {
        var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        foreach (var name in new[] { "useCustomMainGradleTemplate", "useCustomGradlePropertiesTemplate", "useCustomGradleSettingsTemplate" })
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = true;
        }
        so.ApplyModifiedProperties();
        AssetDatabase.Refresh();
        var resolver = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GooglePlayServices.PlayServicesResolver")).FirstOrDefault(x => x != null);
        if (resolver == null) return "resolver not found";
        var m = resolver.GetMethod("ResolveSync", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(bool) }, null);
        bool ok = (bool)m.Invoke(null, new object[] { true });
        return "resolve ok=" + ok;
    }
}
