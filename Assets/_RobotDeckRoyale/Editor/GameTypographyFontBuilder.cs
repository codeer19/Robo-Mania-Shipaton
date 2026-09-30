using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

[InitializeOnLoad]
public static class GameTypographyFontBuilder
{
    private const string SourceFontPath = "Assets/Art/MapKit/you-re-gone.youre-regular.otf";
    private const string TargetDirectory = "Assets/_RobotDeckRoyale/Resources/Typography";
    private const string TargetFontPath = TargetDirectory + "/YouReGone-Regular SDF.asset";
    private const string RequiredCharacters =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    static GameTypographyFontBuilder()
    {
        EditorApplication.delayCall += EnsureFontAsset;
    }

    [MenuItem("Robo Mania/Refresh You Re Gone TMP Font")]
    public static void EnsureFontAsset()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceSynchronousImport);
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            Debug.LogWarning("Unable to import the game font at " + SourceFontPath + ".");
            return;
        }

        EnsureDirectory(TargetDirectory);

        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TargetFontPath);
        if (fontAsset == null)
        {
            fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);

            if (fontAsset == null)
            {
                Debug.LogError("TextMesh Pro could not generate the game font from " + SourceFontPath + ".");
                return;
            }

            fontAsset.name = "YouReGone-Regular SDF";
            Texture2D atlasTexture = fontAsset.atlasTextures[0];
            Material fontMaterial = fontAsset.material;
            atlasTexture.name = "YouReGone-Regular Atlas";
            fontMaterial.name = "YouReGone-Regular Atlas Material";
            atlasTexture.hideFlags = HideFlags.None;
            fontMaterial.hideFlags = HideFlags.None;

            AssetDatabase.CreateAsset(fontAsset, TargetFontPath);
            AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            AssetDatabase.AddObjectToAsset(fontMaterial, fontAsset);
        }

        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
        if (fallback != null && fallback != fontAsset)
            fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };

        fontAsset.TryAddCharacters(RequiredCharacters, out string missingCharacters, true);
        if (!string.IsNullOrEmpty(missingCharacters))
        {
            Debug.LogWarning(
                "The game font is missing these requested characters; TMP will use its fallback: " +
                missingCharacters);
        }

        EditorUtility.SetDirty(fontAsset);
        if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
            EditorUtility.SetDirty(fontAsset.atlasTextures[0]);
        if (fontAsset.material != null)
            EditorUtility.SetDirty(fontAsset.material);

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(TargetFontPath, ImportAssetOptions.ForceUpdate);
    }

    private static void EnsureDirectory(string directory)
    {
        string[] parts = directory.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }
}
