using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the locally equipped loadout to the playable blue robot without coupling the profile
/// system to movement, combat, spawning or scene setup code.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public sealed class PlayerCosmeticRuntimeLoadout : MonoBehaviour
{
    private Transform visualRoot;
    private bool remote;
    private string remoteSkinId;
    public string AppliedSkinId => GetComponentInChildren<SparkSkinVisual>(true)?.AppliedSkinId;

    /// <summary>Consumer for a provider's replicated ID; this does not send network messages.</summary>
    public void ApplyRemoteSkin(string skinId)
    {
        remote = true;
        remoteSkinId = skinId;
        RobotCosmeticApplier.ApplySkin(gameObject, skinId);
    }

    public void BindLocalProfile()
    {
        remote = false;
        ApplyEquipped();
    }

    public void ApplyMatchSkin(string skinId)
    {
        // Freeze the identity captured in the lobby without altering the saved profile.
        remote = true;
        remoteSkinId = skinId;
        RobotCosmeticApplier.ApplySkin(gameObject, skinId);
    }

    private void OnEnable()
    {
        PlayerProfileService.ProfileChanged -= ApplyEquipped;
        PlayerProfileService.ProfileChanged += ApplyEquipped;
        ApplyEquipped();
    }

    private void OnDisable()
    {
        PlayerProfileService.ProfileChanged -= ApplyEquipped;
    }

    private void ApplyEquipped()
    {
        if (remote) { RobotCosmeticApplier.ApplySkin(gameObject, remoteSkinId); return; }
        if (visualRoot == null)
        {
            visualRoot = ResolveVisualRoot();
        }

        if (visualRoot != null)
        {
            RobotCosmeticApplier.ApplyEquipped(visualRoot.gameObject);
        }
    }

    private Transform ResolveVisualRoot()
    {
        Transform namedVisual = transform.Find("Visual");
        return namedVisual != null ? namedVisual : transform;
    }

    private static void AttachToLocalPlayers()
    {
        RobotPlayerController[] controllers = Object.FindObjectsByType<RobotPlayerController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int index = 0; index < controllers.Length; index++)
        {
            RobotPlayerController controller = controllers[index];
            if (controller == null)
            {
                continue;
            }

            FortressTarget identity = controller.GetComponent<FortressTarget>();
            if (identity != null && identity.Team != FortressTeam.Blue)
            {
                continue;
            }

            Attach(controller);
        }
    }

    public static void Attach(RobotPlayerController controller)
    {
        if (controller == null || controller.GetComponent<PlayerCosmeticRuntimeLoadout>() != null) return;
        // Compatibility for the scene-authored player. A provider configures ownership explicitly.
        var identity = controller.GetComponent<FortressTarget>();
        if (identity != null && identity.Team != FortressTeam.Blue) return;
        controller.gameObject.AddComponent<PlayerCosmeticRuntimeLoadout>();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AttachToLocalPlayers();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
}
