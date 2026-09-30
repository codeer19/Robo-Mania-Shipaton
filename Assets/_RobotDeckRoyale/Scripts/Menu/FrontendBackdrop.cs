using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one full-screen background for every screen that replaces the game view:
/// matchmaking, the private lobby, the build loadout and missions.
///
/// Fully opaque by design - nothing of the scene underneath (the menu stage, or
/// the arena of the match that just ended) can show through. The look is drawn
/// once at runtime into a small texture, so it costs no download: a royal-blue
/// to navy wash, a soft light from above, the arena's centre-ring motif and a
/// faint dot grid, all low-contrast so the screen's content stays the subject.
/// </summary>
public static class FrontendBackdrop
{
    private const int Width = 384, Height = 216;
    private static Texture2D texture;

    private static readonly Color Top = new Color(.13f, .38f, .80f);
    private static readonly Color Bottom = new Color(.035f, .08f, .22f);
    private static readonly Color Light = new Color(.42f, .72f, 1f);

    /// <summary>Adds the backdrop as the first child of <paramref name="parent"/>, covering it.</summary>
    public static RawImage Create(Transform parent, string name = "Backdrop")
    {
        var rect = FrontendUI.Rect(name, parent, Vector2.zero, Vector2.one);
        rect.SetAsFirstSibling();
        // Solid base under the image: guarantees opacity even while the aspect
        // fitter lays out, and on any frame the texture is not yet assigned.
        var solid = rect.gameObject.AddComponent<Image>();
        solid.color = Bottom;
        solid.raycastTarget = true;   // also stops clicks reaching anything behind

        var art = FrontendUI.Rect("Art", rect, Vector2.zero, Vector2.one);
        var image = art.gameObject.AddComponent<RawImage>();
        image.texture = Texture;
        image.raycastTarget = false;
        var fitter = art.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = (float)Width / Height;
        return image;
    }

    public static Texture2D Texture
    {
        get
        {
            if (texture != null) return texture;
            texture = new Texture2D(Width, Height, TextureFormat.RGB24, false)
            {
                name = "FrontendBackdrop",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[Width * Height];
            Vector2 centre = new Vector2(Width * .5f, Height * .46f);
            for (int y = 0; y < Height; y++)
            {
                float v = y / (Height - 1f);
                Color row = Color.Lerp(Bottom, Top, Mathf.SmoothStep(0f, 1f, v));
                for (int x = 0; x < Width; x++)
                {
                    Color c = row;
                    // Soft light from above, centred.
                    float dx = (x - Width * .5f) / (Width * .55f), dy = (y - Height * 1.05f) / (Height * .75f);
                    float glow = Mathf.Clamp01(1f - (dx * dx + dy * dy));
                    c = Color.Lerp(c, Light, glow * glow * .35f);

                    // The arena's centre mark: three thin, soft rings.
                    float r = Vector2.Distance(new Vector2(x, y), centre) / Height;
                    float ring = Ring(r, .30f, .006f) * .10f + Ring(r, .46f, .005f) * .07f + Ring(r, .68f, .005f) * .05f;
                    c = Color.Lerp(c, Color.white, ring);

                    // Faint dot grid, fading toward the bottom.
                    float gx = Mathf.Repeat(x, 12f) - 6f, gy = Mathf.Repeat(y, 12f) - 6f;
                    float dot = Mathf.Clamp01(1.3f - Mathf.Sqrt(gx * gx + gy * gy)) * .05f * (.35f + .65f * v);
                    c = Color.Lerp(c, Color.white, dot);

                    // Gentle vignette keeps the eye in the middle.
                    float vx = (x / (Width - 1f)) * 2f - 1f, vy = v * 2f - 1f;
                    float vignette = Mathf.Clamp01((vx * vx * .6f + vy * vy * .35f) - .15f);
                    c = Color.Lerp(c, Bottom * .8f, vignette * .45f);

                    pixels[y * Width + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }

    private static Texture2D stageTexture;

    /// <summary>
    /// A lit stage for a robot on a card: a soft cool spotlight fading into the
    /// card face and a floor disc the wheels stand on. Opaque at its core.
    /// </summary>
    public static RawImage Stage(Transform parent, Vector2 min, Vector2 max)
    {
        var rect = FrontendUI.Rect("Stage", parent, min, max);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = StageTexture;
        image.raycastTarget = false;
        return image;
    }

    private static Texture2D StageTexture
    {
        get
        {
            if (stageTexture != null) return stageTexture;
            const int w = 160, h = 200;
            stageTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "FrontendStage", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var face = new Color(.07f, .16f, .36f);
            var spot = new Color(.20f, .44f, .86f);
            var floor = new Color(.14f, .30f, .64f);
            var rim = new Color(.46f, .78f, 1f);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w, v = (y + .5f) / h;
                    // Spotlight: a tall soft ellipse, brightest behind the robot's body.
                    float sx = (u - .5f) / .46f, sy = (v - .52f) / .52f;
                    float light = Mathf.Clamp01(1f - (sx * sx + sy * sy));
                    Color c = Color.Lerp(face, spot, light * light);
                    // Floor disc under the wheels, with a bright rim on its far edge.
                    float fx = (u - .5f) / .40f, fy = (v - .13f) / .075f;
                    float disc = fx * fx + fy * fy;
                    if (disc < 1f)
                    {
                        c = Color.Lerp(c, floor, Mathf.Clamp01((1f - disc) * 6f));
                        float edge = Mathf.Clamp01(1f - Mathf.Abs(disc - .82f) * 7f) * (fy > 0f ? 1f : .45f);
                        c = Color.Lerp(c, rim, edge * .7f);
                    }
                    // Fade into the face at the rectangle edges so it has no hard border.
                    float ex = Mathf.Min(u, 1f - u) / .08f, ey = Mathf.Min(v, 1f - v) / .06f;
                    c = Color.Lerp(face, c, Mathf.Clamp01(Mathf.Min(ex, ey)));
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
            stageTexture.SetPixels32(pixels);
            stageTexture.Apply(false, true);
            return stageTexture;
        }
    }

    private static float Ring(float r, float radius, float width)
    {
        float d = Mathf.Abs(r - radius) / width;
        return Mathf.Clamp01(1f - d * d);
    }
}
