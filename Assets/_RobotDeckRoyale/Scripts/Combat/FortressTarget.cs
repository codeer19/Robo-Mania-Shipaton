using System.Collections.Generic;
using UnityEngine;

public enum FortressTeam
{
    Blue,
    Red
}

public enum FortressTargetType
{
    Robot,
    Vault,
    Deployable
}

public class FortressTarget : MonoBehaviour
{
    [SerializeField] private FortressTeam team;
    [SerializeField] private FortressTargetType targetType;

    public FortressTeam Team => team;
    public FortressTargetType TargetType => targetType;

    // Every enabled target, so turrets and Spidys can scan candidates without
    // FindObjectsByType allocating a fresh array on every scan.
    private static readonly List<FortressTarget> active = new List<FortressTarget>();
    public static IReadOnlyList<FortressTarget> Active => active;

    // Role lookups resolved once per target instead of several GetComponent
    // calls per candidate per scan (and per frame in the Spidy AI).
    private Damageable health;
    private bool rolesCached, isVault, isCombatRobot, isSpidy, isTurret;

    public Damageable Health
    {
        get
        {
            // Re-queried while missing: online presentation can add the
            // Damageable after the identity.
            if (health == null) health = GetComponent<Damageable>();
            return health;
        }
    }

    public bool IsVault { get { CacheRoles(); return isVault; } }
    /// <summary>A player robot or the duel bot (not a Spidy).</summary>
    public bool IsCombatRobot { get { CacheRoles(); return isCombatRobot; } }
    public bool IsSpidy { get { CacheRoles(); return isSpidy; } }
    public bool IsTurret { get { CacheRoles(); return isTurret; } }

    private void CacheRoles()
    {
        if (rolesCached) return;
        rolesCached = true;
        isVault = GetComponent<FortressVault>() != null;
        isCombatRobot = GetComponent<RobotPlayerController>() != null || GetComponent<FortressBotAI>() != null;
        isSpidy = GetComponent<SwarmBotAI>() != null;
        isTurret = GetComponent<AutoTurret>() != null;
    }

    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();

    public void SetTeam(FortressTeam newTeam)
    {
        team = newTeam;
    }
}
