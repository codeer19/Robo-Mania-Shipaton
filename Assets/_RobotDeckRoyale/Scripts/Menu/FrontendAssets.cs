using System;
using UnityEngine;

/// <summary>Authored references shared by the home, result and loading presentations.</summary>
public sealed class FrontendAssets : ScriptableObject
{
    public GameObject RobotVisual;
    public GameObject Arena;
    public Texture2D LoadingArtwork;
    public SparkSkin[] Skins;
    /// <summary>
    /// The shared frontend asset set, loaded at most once per process.
    ///
    /// Every field here is a strong reference into the menu's art - the backdrop
    /// arena, the robot, and the source model of every skin - so Resources.Load
    /// has to deserialise that entire graph. Measured on the test device it costs
    /// ~26.5 seconds, and it was being paid twice: once to build the menu, then
    /// again the first time a replicated opponent's skin was applied in the arena.
    /// That second load blocked the main thread past Photon's tolerance, so the
    /// phone was dropped from the session ~24s into arena startup every time.
    ///
    /// Holding the result keeps the graph resident, which is what makes the
    /// arena's lookup free. DontUnloadUnusedAsset is the explicit half of that:
    /// loading a scene with LoadSceneMode.Single sweeps unused Resources assets,
    /// and this object has to survive as a root so its dependencies do too.
    /// </summary>
    public static FrontendAssets Load()
    {
        if (resident != null) return resident;

        float startedAt = Time.realtimeSinceStartup;
        resident = Resources.Load<FrontendAssets>("Frontend/FrontendAssets");
        if (resident == null)
        {
            Debug.LogError("[FRONTEND ASSETS] Resources/Frontend/FrontendAssets is missing.");
            return null;
        }

        resident.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Debug.Log($"[FRONTEND ASSETS] Loaded in {(Time.realtimeSinceStartup - startedAt) * 1000f:F0}ms " +
            $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} skins={resident.Skins?.Length ?? 0}");
        return resident;
    }

    private static FrontendAssets resident;

    // Domain reload in the Editor invalidates the asset but not the static field.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetResident() => resident = null;

    public SparkSkin FindSkin(string id)
    {
        if (Skins != null)
            foreach (SparkSkin skin in Skins)
                if (skin.Id == id) return skin;
        return Skins != null && Skins.Length > 0 ? Skins[0] : null;
    }
}

[Serializable]
public sealed class SparkSkin
{
    public string Id;
    public GameObject Source;
    public Texture2D Thumbnail;
    public string[] HiddenRenderers;
}
