using UnityEngine;

[CreateAssetMenu(menuName = "Robo Mania/CrazyGames Release Settings")]
public sealed class CrazyGamesReleaseSettings : ScriptableObject
{
    [Tooltip("Keep false for Basic Launch. Enable only after CrazyGames enables monetization.")]
    public bool monetizationEnabled;
    [Tooltip("Full Launch can use the platform username without changing room or gameplay identity.")]
    public bool useAccountDisplayName;
    [Min(30)] public int rewardedCooldownSeconds = 60;
    private static CrazyGamesReleaseSettings current;
    public static CrazyGamesReleaseSettings Current
    {
        get
        {
            if (current == null) current = Resources.Load<CrazyGamesReleaseSettings>("CrazyGamesReleaseSettings");
            if (current == null) current = CreateInstance<CrazyGamesReleaseSettings>();
            return current;
        }
    }
}
