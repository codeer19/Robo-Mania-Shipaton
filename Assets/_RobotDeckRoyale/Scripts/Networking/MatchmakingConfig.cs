using UnityEngine;

/// <summary>
/// Every tunable for the Play flow lives here so the timeout is defined once
/// rather than duplicated across the menu, the provider and the fallback path.
/// </summary>
[CreateAssetMenu(
    fileName = "MatchmakingConfig",
    menuName = "Robo Mania/Matchmaking Config",
    order = 0)]
public sealed class MatchmakingConfig : ScriptableObject
{
    [Header("Human Matchmaking")]
    [Tooltip("How long to look for a real opponent before falling back to the AI match.")]
    [SerializeField, Min(1f)] private float humanMatchTimeoutSeconds = 5f;

    [Tooltip("1v1. Fusion fills the session to this count before the match is considered ready.")]
    [SerializeField, Min(2)] private int playersPerMatch = 2;

    [Tooltip("Bumped when a change makes older clients incompatible. Keeps mismatched " +
             "builds out of each other's sessions.")]
    [SerializeField] private string queueVersion = "1";

    [Header("Fallback")]
    [Tooltip("Small delay after the online attempt is torn down, so the handoff reads as " +
             "deliberate rather than a glitch.")]
    [SerializeField, Min(0f)] private float botHandoffDelaySeconds = 0.6f;

    [Header("Scenes")]
    [SerializeField] private string arenaSceneName = "FortressDuelArena";

    public float HumanMatchTimeoutSeconds => humanMatchTimeoutSeconds;
    public int PlayersPerMatch => playersPerMatch;
    public string QueueVersion => string.IsNullOrWhiteSpace(queueVersion) ? "1" : queueVersion;
    public float BotHandoffDelaySeconds => botHandoffDelaySeconds;
    public string ArenaSceneName => arenaSceneName;

    private static MatchmakingConfig cached;

    /// <summary>
    /// Loads the shared asset from Resources, falling back to defaults so the game
    /// still runs if the asset has not been created yet.
    /// </summary>
    public static MatchmakingConfig Load()
    {
        if (cached != null)
        {
            return cached;
        }

        cached = Resources.Load<MatchmakingConfig>("Matchmaking/MatchmakingConfig");
        if (cached == null)
        {
            cached = CreateInstance<MatchmakingConfig>();
            cached.name = "MatchmakingConfig (defaults)";
            Debug.LogWarning(
                "[MATCHMAKING] No MatchmakingConfig asset found at " +
                "Resources/Matchmaking/MatchmakingConfig. Using built-in defaults.");
        }

        return cached;
    }
}
