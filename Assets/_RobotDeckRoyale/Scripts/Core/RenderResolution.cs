using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Keeps the 3D view sharp without over-filling phones.
///
/// The pipeline used a fixed 0.8 render scale everywhere, so every frame was drawn
/// at 80% and stretched back up with bilinear filtering: the soft look. Desktop now
/// renders at native resolution. Phones do too, unless the screen has more pixels
/// than a mid-range phone GPU can fill every frame (high pixel-ratio screens are
/// often 2.5+ megapixels); only then does the scale drop, just enough to stay
/// within the budget - never below the old 0.8 on a typical 1080p-class screen.
/// The UI is drawn at native resolution either way.
/// </summary>
public sealed class RenderResolution : MonoBehaviour
{
    private const float MobilePixelBudget = 1.8e6f;
    private const float MinimumScale = 0.7f;
    private const float CheckInterval = 0.5f;

    private int lastWidth, lastHeight;
    private float nextCheck;

    public static float CurrentScale { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject("RenderResolution");
        DontDestroyOnLoad(host);
        host.AddComponent<RenderResolution>();
    }

    private void Update()
    {
        // The canvas only changes on resize, rotation or fullscreen: poll twice a second.
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + CheckInterval;
        if (Screen.width == lastWidth && Screen.height == lastHeight) return;
        lastWidth = Screen.width;
        lastHeight = Screen.height;
        Apply();
    }

    public static float ScaleFor(int width, int height, bool mobile)
    {
        if (!mobile) return 1f;
        float pixels = (float)width * height;
        return pixels > MobilePixelBudget ? Mathf.Clamp(Mathf.Sqrt(MobilePixelBudget / pixels), MinimumScale, 1f) : 1f;
    }

    private void Apply()
    {
        CurrentScale = ScaleFor(lastWidth, lastHeight, Application.isMobilePlatform);
        var asset = UniversalRenderPipeline.asset;
        // Never write a runtime value into the project's pipeline asset in the Editor.
        if (asset == null || Application.isEditor) return;
        if (!Mathf.Approximately(asset.renderScale, CurrentScale)) asset.renderScale = CurrentScale;
        Debug.Log($"[RENDER] {lastWidth}x{lastHeight} mobile={Application.isMobilePlatform} renderScale={CurrentScale:F2}");
    }
}
