using UnityEngine;

/// <summary>
/// Renders the actual Spidy model once, for use as the summon button's glyph.
///
/// The button used to draw a spider out of UI circles and bars, which read as a
/// placeholder next to the rest of the HUD and looked nothing like the unit it
/// summons. The game already ships the model, so this photographs it instead of
/// approximating it: one offscreen stage, one render, then the stage is thrown
/// away and only the texture is kept. Nothing renders per frame afterwards,
/// which matters on the phone.
///
/// The render is issued explicitly rather than by leaving an enabled camera for
/// URP to pick up. The first attempt did the latter and produced an untouched
/// texture every time - the button showed a black square on device. Calling
/// Render directly is deterministic, and the result is checked before it is
/// handed out, so a failure falls back to the drawn glyph instead of shipping an
/// empty rectangle.
///
/// Deliberately mirrors RobotPreviewPresenter's single-sample target: the menu
/// stage rendered black on this Android device until multisampling was dropped,
/// and the same device runs this.
/// </summary>
public static class SpidyGlyphStage
{
    private const int PreviewLayer = 31;
    private const int GlyphSize = 192;

    // Rendered behind the model and then keyed out. URP writes an opaque alpha
    // channel, so a transparent clear colour does not survive - clearing to a
    // colour the model cannot contain and removing it afterwards is what turns
    // the render into a cut-out instead of a dark tile sitting on the button.
    private static readonly Color KeyColour = new Color(1f, 0f, 1f, 1f);

    private static Texture2D glyph;
    private static bool attempted;

    /// <summary>The rendered glyph as a cut-out, or null if it could not be produced.</summary>
    public static Texture2D Glyph()
    {
        if (attempted) return glyph;
        attempted = true;

        var prefab = Resources.Load<GameObject>("Networking/PF_Spidy_Visual");
        if (prefab == null)
        {
            Debug.LogWarning("[SPIDY GLYPH] PF_Spidy_Visual missing; keeping the drawn glyph.");
            return null;
        }

        var target = new RenderTexture(GlyphSize, GlyphSize, 24, RenderTextureFormat.ARGB32)
        {
            name = "SpidyGlyphTarget",
            antiAliasing = 1
        };
        if (!target.Create())
        {
            Debug.LogWarning("[SPIDY GLYPH] Could not allocate the glyph target; keeping the drawn glyph.");
            Object.Destroy(target);
            return null;
        }

        var host = new GameObject("SpidyGlyphStage") { hideFlags = HideFlags.HideAndDontSave };
        Compose(host.transform, prefab, target);
        Object.Destroy(host);

        Texture2D cutout = KeyOut(target, out int lit);
        target.Release();
        Object.Destroy(target);

        if (lit <= 0)
        {
            Debug.LogWarning("[SPIDY GLYPH] Render produced nothing; keeping the drawn glyph.");
            Object.Destroy(cutout);
            return null;
        }

        cutout.name = "SpidyGlyph";
        cutout.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Debug.Log($"[SPIDY GLYPH] Rendered {GlyphSize}x{GlyphSize} litPixels={lit}");
        glyph = cutout;
        return glyph;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { glyph = null; attempted = false; }

    private static void Compose(Transform parent, GameObject prefab, RenderTexture target)
    {
        // Far below the arena and the menu stage, which share this culling layer.
        parent.position = new Vector3(0f, -5000f, 0f);

        GameObject model = Object.Instantiate(prefab, parent);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, 152f, 0f);
        Quiesce(model);
        SetLayer(model, PreviewLayer);

        Bounds bounds = Measure(model);

        var lightObject = new GameObject("Glyph Light");
        lightObject.transform.SetParent(parent, false);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.5f;
        light.color = new Color(0.85f, 0.95f, 1f);
        light.cullingMask = 1 << PreviewLayer;
        lightObject.transform.rotation = Quaternion.Euler(38f, 205f, 0f);

        var cameraObject = new GameObject("Glyph Camera");
        cameraObject.transform.SetParent(parent, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        // Framed to fill the button without touching the corners, which have to
        // stay background for the key sample below to be meaningful.
        camera.orthographicSize = Mathf.Max(0.05f, bounds.extents.magnitude * 0.72f);
        camera.cullingMask = 1 << PreviewLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = KeyColour;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 60f;
        camera.targetTexture = target;
        camera.transform.position = bounds.center + new Vector3(0f, 3.1f, -6f);
        camera.transform.LookAt(bounds.center);
        camera.Render();
    }

    /// <summary>Silences gameplay behaviour on a throwaway instance without tearing up required components.</summary>
    private static void Quiesce(GameObject model)
    {
        foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var canvas in model.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
    }

    private static Bounds Measure(GameObject model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(model.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void SetLayer(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform) SetLayer(child.gameObject, layer);
    }

    /// <summary>
    /// Reads the render back, drops the key colour, and reports how much of the
    /// model survived - so a failed render is detected rather than assumed away.
    /// </summary>
    private static Texture2D KeyOut(RenderTexture target, out int lit)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
        RenderTexture.active = previous;

        Color[] pixels = readback.GetPixels();

        // The key is matched against a corner pixel rather than against the colour
        // that was requested. Testing for literal magenta failed: the value that
        // comes back has been through colour-space conversion, so the background
        // read as a dark purple that a fixed threshold let through and the glyph
        // shipped as a tinted box. A corner is outside the framed model, so
        // whatever it contains *is* the background, whatever the pipeline did to it.
        Color background = pixels[0];
        lit = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            bool isKey = Mathf.Abs(pixel.r - background.r) < 0.10f &&
                         Mathf.Abs(pixel.g - background.g) < 0.10f &&
                         Mathf.Abs(pixel.b - background.b) < 0.10f;
            if (isKey)
            {
                pixels[i] = new Color(0f, 0f, 0f, 0f);
                continue;
            }
            pixel.a = 1f;
            pixels[i] = pixel;
            lit++;
        }

        float coverage = lit / (float)pixels.Length;
        Debug.Log($"[SPIDY GLYPH] keyed background={background} coverage={coverage * 100f:F1}%");

        // A cut-out that kept almost everything means the key did not match, and a
        // near-empty one means nothing rendered. Neither is worth showing.
        if (coverage > 0.85f || coverage < 0.02f) lit = 0;

        readback.SetPixels(pixels);
        readback.Apply(false, false);
        return readback;
    }
}
