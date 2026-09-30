using UnityEngine;

/// <summary>
/// Drives restrained camera emphasis from real combat events rather than from a
/// timer or ambient motion. Currently: a short framing tightening when the local
/// robot scores a kill, and a slightly wider frame while it is dead.
///
/// Deliberately conservative — the brief is polish, not spectacle, so there is
/// no per-hit zoom, no roll and no post-processing here.
/// </summary>
[RequireComponent(typeof(TopDownCameraFollow))]
public class CombatCameraDirector : MonoBehaviour
{
    [Header("Kill Emphasis")]
    [Tooltip("How much closer the camera frames when the local robot gets a kill. Kept small on purpose.")]
    [SerializeField, Range(0f, 0.25f)] private float killTightenAmount = 0.07f;
    [SerializeField, Min(0.05f)] private float killTightenAttack = 0.09f;
    [SerializeField, Min(0.05f)] private float killTightenRelease = 0.65f;

    [Header("Death Framing")]
    [Tooltip("How much wider the camera frames while the local robot is waiting to respawn.")]
    [SerializeField, Range(0f, 0.3f)] private float deathWidenAmount = 0.1f;
    [SerializeField, Min(0.05f)] private float deathWidenResponse = 2.2f;

    [Header("Local Robot")]
    [Tooltip("Leave empty to resolve the blue robot once on start.")]
    [SerializeField] private Damageable localRobot;

    private TopDownCameraFollow follow;
    private float killEmphasis;
    private float deathEmphasis;

    private void Awake()
    {
        follow = GetComponent<TopDownCameraFollow>();
    }

    private void Start()
    {
        if (localRobot != null)
            return;

        // Resolved once rather than every frame. A networked local player should
        // call SetLocalRobot from its own spawn callback instead.
        foreach (FortressTarget candidate in
                 FindObjectsByType<FortressTarget>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (candidate.Team != FortressTeam.Blue ||
                candidate.TargetType != FortressTargetType.Robot)
            {
                continue;
            }

            localRobot = candidate.GetComponent<Damageable>();
            break;
        }
    }

    private void OnEnable()
    {
        CombatEvents.Killed += HandleKilled;
    }

    private void OnDisable()
    {
        CombatEvents.Killed -= HandleKilled;
        follow.SetFramingBias(0f);
    }

    /// <summary>
    /// Assigned by whatever spawns the local robot. Left as a setter rather than
    /// a scene lookup so a Fusion local player can register itself on Spawned().
    /// </summary>
    public void SetLocalRobot(Damageable robot)
    {
        localRobot = robot;
    }

    private void HandleKilled(Damageable victim, GameObject attacker)
    {
        if (localRobot == null || attacker == null)
            return;

        // Only the local robot's own kills are emphasised.
        if (attacker != localRobot.gameObject &&
            !attacker.transform.IsChildOf(localRobot.transform))
        {
            return;
        }

        if (victim == localRobot)
            return;

        killEmphasis = 1f;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.unscaledDeltaTime;

        killEmphasis = Mathf.MoveTowards(
            killEmphasis,
            0f,
            deltaTime / Mathf.Max(0.05f, killTightenRelease));

        float targetDeath = localRobot != null && localRobot.IsDead ? 1f : 0f;
        deathEmphasis = Mathf.MoveTowards(
            deathEmphasis,
            targetDeath,
            deltaTime * deathWidenResponse);

        float smoothedKill = Mathf.SmoothStep(0f, 1f, killEmphasis);

        follow.SetFramingBias(
            deathEmphasis * deathWidenAmount -
            smoothedKill * killTightenAmount);
    }

    private void OnValidate()
    {
        killTightenAttack = Mathf.Max(0.05f, killTightenAttack);
    }
}
