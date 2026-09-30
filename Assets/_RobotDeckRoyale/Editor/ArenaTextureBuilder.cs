using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the arena's surface detail as tiling textures.
///
/// Every material in the scene was flat untextured colour, which is the main
/// reason the arena read as greybox. These are written as neutral, mostly-white
/// maps rather than coloured ones: URP multiplies _BaseMap by _BaseColor, so one
/// texture serves every painted surface and the existing palette keeps working
/// untouched.
///
/// The patterns are deliberately structural -- panel seams, plate borders,
/// bolts -- rather than noise. Noise would read as dirt at this camera distance
/// and fight the clean stylised look the project is going for.
/// </summary>
public static class ArenaTextureBuilder
{
    private const string OutputFolder =
        "Assets/_RobotDeckRoyale/Art/Textures/Generated";

    private const int Resolution = 512;

    [MenuItem("Robo Mania/Arena/Build Surface Textures")]
    public static void BuildAll()
    {
        EnsureFolder();

        WriteTexture("T_DeckPanel", BuildDeckPanel());
        WriteTexture("T_WallPanel", BuildWallPanel());
        WriteTexture("T_CratePlate", BuildCratePlate());

        AssetDatabase.Refresh();
        Debug.Log("Arena surface textures built into " + OutputFolder);
    }

    // ------------------------------------------------------------------
    // Patterns
    // ------------------------------------------------------------------

    /// <summary>
    /// Floor: a grid of large plates with recessed seams and a bolt at each
    /// corner. Two plates across the texture, so one repeat covers a readable
    /// span of floor rather than turning into a fine chequer at distance.
    /// </summary>
    private static Color[] BuildDeckPanel()
    {
        Color[] pixels = new Color[Resolution * Resolution];

        const int Plates = 2;
        int plate = Resolution / Plates;
        const int Seam = 7;

        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                int localX = x % plate;
                int localY = y % plate;

                // Distance to the nearest seam, wrapping at the texture edge so
                // the pattern stays seamless when tiled.
                int seamX = Mathf.Min(localX, plate - 1 - localX);
                int seamY = Mathf.Min(localY, plate - 1 - localY);

                float value = 1f;

                if (seamX < Seam || seamY < Seam)
                    value = 0.74f;

                // A soft bevel just inside each seam reads as a raised plate.
                else if (seamX < Seam + 5 || seamY < Seam + 5)
                    value = 0.92f;

                // Alternate plates sit a touch apart in value so the grid does
                // not read as one repeated stamp.
                int plateX = x / plate;
                int plateY = y / plate;
                if (((plateX + plateY) & 1) == 1)
                    value *= 0.965f;

                // Bolts near the plate corners.
                if (IsBolt(seamX, seamY, Seam))
                    value = 0.62f;

                pixels[y * Resolution + x] = new Color(value, value, value, 1f);
            }
        }

        return pixels;
    }

    /// <summary>
    /// Walls: horizontal banding, which reads as stacked armour plate and gives
    /// the long boundary run some rhythm without adding geometry.
    /// </summary>
    private static Color[] BuildWallPanel()
    {
        Color[] pixels = new Color[Resolution * Resolution];

        const int Bands = 4;
        int band = Resolution / Bands;
        const int Groove = 9;

        for (int y = 0; y < Resolution; y++)
        {
            int localY = y % band;
            int grooveDistance = Mathf.Min(localY, band - 1 - localY);

            float value = 1f;

            if (grooveDistance < Groove)
                value = 0.76f;
            else if (grooveDistance < Groove + 6)
                value = 0.93f;

            if (((y / band) & 1) == 1)
                value *= 0.97f;

            for (int x = 0; x < Resolution; x++)
                pixels[y * Resolution + x] = new Color(value, value, value, 1f);
        }

        return pixels;
    }

    /// <summary>
    /// Crates: a bordered plate with corner bolts, so a box reads as a built
    /// container rather than a primitive at gameplay distance.
    /// </summary>
    private static Color[] BuildCratePlate()
    {
        Color[] pixels = new Color[Resolution * Resolution];

        const int Border = 46;
        const int Inner = 92;

        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                int edgeX = Mathf.Min(x, Resolution - 1 - x);
                int edgeY = Mathf.Min(y, Resolution - 1 - y);
                int edge = Mathf.Min(edgeX, edgeY);

                float value = 1f;

                if (edge < Border)
                    value = 0.86f;

                // Recessed centre panel.
                if (edgeX > Inner && edgeY > Inner)
                    value = 0.93f;

                // Bolts inset from each corner.
                if (IsCornerBolt(edgeX, edgeY, Border))
                    value = 0.6f;

                pixels[y * Resolution + x] = new Color(value, value, value, 1f);
            }
        }

        return pixels;
    }

    // ------------------------------------------------------------------

    private static bool IsBolt(int seamX, int seamY, int seam)
    {
        int centre = seam + 13;
        int dx = seamX - centre;
        int dy = seamY - centre;
        return dx * dx + dy * dy <= 20;
    }

    private static bool IsCornerBolt(int edgeX, int edgeY, int border)
    {
        int centre = border / 2;
        int dx = edgeX - centre;
        int dy = edgeY - centre;
        return dx * dx + dy * dy <= 26;
    }

    // ------------------------------------------------------------------

    private static void WriteTexture(string textureName, Color[] pixels)
    {
        Texture2D texture = new Texture2D(
            Resolution, Resolution, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();

        string path = $"{OutputFolder}/{textureName}.png";
        File.WriteAllBytes(
            Path.Combine(
                Path.GetDirectoryName(Application.dataPath), path),
            texture.EncodeToPNG());

        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        // These are flat structural patterns, so they compress cleanly and do
        // not need to sit uncompressed in memory on a phone.
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(OutputFolder))
            return;

        string parent = Path.GetDirectoryName(OutputFolder)?.Replace('\\', '/');

        if (!AssetDatabase.IsValidFolder(parent))
        {
            string grandparent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(grandparent, Path.GetFileName(parent));
        }

        AssetDatabase.CreateFolder(parent, Path.GetFileName(OutputFolder));
    }
}
