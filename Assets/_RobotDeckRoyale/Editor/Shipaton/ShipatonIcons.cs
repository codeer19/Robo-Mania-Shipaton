// Android app icon for the Shipaton build: the Robo Mania key art (Spark vs Ninja), set for the
// Android legacy, round and adaptive icon kinds only (the WebGL build keeps its own icon).
//   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod ShipatonIcons.Apply
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

public static class ShipatonIcons
{
    const string Art = "Assets/_RobotDeckRoyale/Art/Icons/RoboMania_AppIcon.png";
    const string ClearForeground = "Assets/_RobotDeckRoyale/Art/Icons/RoboMania_AppIcon_AdaptiveForeground.png";

    [MenuItem("Robo Mania/Shipaton/Apply Android App Icon")]
    public static void Apply()
    {
        foreach (var path in new[] { Art, ClearForeground })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        var art = AssetDatabase.LoadAssetAtPath<Texture2D>(Art);
        var clear = AssetDatabase.LoadAssetAtPath<Texture2D>(ClearForeground);
        var target = NamedBuildTarget.Android;
        foreach (var kind in PlayerSettings.GetSupportedIconKinds(target))
        {
            var icons = PlayerSettings.GetPlatformIcons(target, kind);
            foreach (var icon in icons)
            {
                if (kind == AndroidPlatformIconKind.Adaptive)
                    icon.SetTextures(art, clear);       // background = key art, foreground = empty
                else
                    icon.SetTextures(art);
            }
            PlayerSettings.SetPlatformIcons(target, kind, icons);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[SHIPATON ICONS] applied: " + string.Join(", ", PlayerSettings.GetSupportedIconKinds(target).Select(k => k.ToString())));
    }
}
