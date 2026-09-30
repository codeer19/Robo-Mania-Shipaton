using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps every live TextMesh Pro label on the game's selected display font.
/// The font asset lives in Resources so this also works in player builds.
/// </summary>
public static class GameTypography
{
    private const string FontResourcePath = "Typography/YouReGone-Regular SDF";
    private static TMP_FontAsset displayFont;
    private static bool warnedAboutMissingFont;
    private static bool runtimeCreated;

    public static TMP_FontAsset Font
    {
        get
        {
            EnsureFontLoaded();
            return displayFont;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        displayFont = null;
        warnedAboutMissingFont = false;
        runtimeCreated = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureFontLoaded();

        if (runtimeCreated)
            return;

        GameObject runtimeObject = new GameObject("Game Typography Runtime")
        {
            hideFlags = HideFlags.HideInHierarchy
        };
        Object.DontDestroyOnLoad(runtimeObject);
        runtimeObject.AddComponent<GameTypographyRuntime>();
        runtimeCreated = true;
    }

    public static void Apply(TMP_Text label)
    {
        if (label == null)
            return;

        TMP_FontAsset resolvedFont = Font;
        if (resolvedFont != null && label.font != resolvedFont)
            label.font = resolvedFont;
    }

    public static void ApplyToLoadedLabels()
    {
        TMP_FontAsset resolvedFont = Font;
        if (resolvedFont == null)
            return;

        TMP_Text[] labels = Resources.FindObjectsOfTypeAll<TMP_Text>();
        for (int index = 0; index < labels.Length; index++)
        {
            TMP_Text label = labels[index];
            if (label == null || !label.gameObject.scene.IsValid())
                continue;

            if (label.font != resolvedFont)
                label.font = resolvedFont;
        }
    }

    private static void EnsureFontLoaded()
    {
        if (displayFont != null)
            return;

        displayFont = Resources.Load<TMP_FontAsset>(FontResourcePath);
        if (displayFont != null)
        {
            // Runtime-created TMP labels use this asset automatically. The scene
            // pass below also replaces fonts serialized on existing prefabs.
            TMP_Settings.defaultFontAsset = displayFont;
            return;
        }

        displayFont = TMP_Settings.defaultFontAsset;
        if (!warnedAboutMissingFont)
        {
            Debug.LogWarning(
                "Game typography font was not found at Resources/" + FontResourcePath +
                ". Falling back to the TextMesh Pro default font.");
            warnedAboutMissingFont = true;
        }
    }

}

[DefaultExecutionOrder(-10000)]
internal sealed class GameTypographyRuntime : MonoBehaviour
{
    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(HandleTextChanged);
        GameTypography.ApplyToLoadedLabels();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(HandleTextChanged);
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameTypography.ApplyToLoadedLabels();
    }

    private static void HandleTextChanged(Object changedObject)
    {
        if (changedObject is TMP_Text label && label.gameObject.scene.IsValid())
            GameTypography.Apply(label);
    }
}
