using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Damageable))]
[RequireComponent(typeof(FortressTarget))]
public sealed class SwarmBotAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FortressDuelManager duelManager;
    [SerializeField] private Transform muzzle;
    [SerializeField] private BasicProjectile bulletPrefab;
    [SerializeField] private GameObject muzzleEffectPrefab;
    [SerializeField] private float muzzleEffectLifetime = 0.8f;

    [Header("Movement")]
    [SerializeField, Min(0.5f)] private float movementSpeed = 4.6f;
    [SerializeField, Min(0.1f)] private float acceleration = 18f;
    [SerializeField, Min(30f)] private float turnSpeed = 720f;
    [SerializeField, Min(0.1f)] private float navMeshRecoveryRadius = 4f;
    [SerializeField, Min(0f)] private float heistFormationRadius = 1.8f;

    [Header("Navigation Recovery")]
    [SerializeField, Min(0.25f)] private float destinationSampleRadius = 2.5f;
    [SerializeField, Min(0.05f)] private float repathInterval = 0.45f;
    [SerializeField, Min(0.1f)] private float progressCheckInterval = 0.5f;
    [SerializeField, Min(0.01f)] private float minimumProgressDistance = 0.12f;
    [SerializeField, Min(0.25f)] private float stuckDurationBeforeRecovery = 2.25f;
    [SerializeField, Min(0.25f)] private float stuckRecoveryCooldown = 3f;
    [SerializeField, Min(0.1f)] private float recoveryWarpDistance = 1.2f;

    [Header("Opportunistic Targeting")]
    [SerializeField, Min(0.1f)] private float opportunityRange = 7.5f;
    [SerializeField, Min(0.1f)] private float opportunityDisengageRange = 10f;
    [SerializeField, Min(0.05f)] private float targetScanInterval = 0.3f;

    [Header("Low-Damage Attack")]
    [SerializeField, Min(0.1f)] private float attackRange = 7.2f;
    [SerializeField, Min(1)] private int damagePerShot = 6;
    [SerializeField, Min(0.05f)] private float attackCooldown = 0.68f;
    [SerializeField, Min(0f)] private float aimHeight = 0.45f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    [Header("Crawler Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip crawlerClip;
    [SerializeField, Min(0f)] private float animationPlaybackSpeed = 1f;
    [SerializeField] private string movingBoolParameter = "Moving";
    [SerializeField] private string speedFloatParameter = "Speed";

    [Header("Team Ring")]
    [SerializeField, Min(0.1f)] private float teamRingRadius = 0.86f;
    [SerializeField, Min(0.01f)] private float teamRingWidth = 0.16f;
    [SerializeField] private Color blueRingColor = new Color(0.08f, 0.55f, 1f, 0.95f);
    [SerializeField] private Color redRingColor = new Color(1f, 0.12f, 0.08f, 0.95f);

    private NavMeshAgent agent;
    private Damageable damageable;
    private FortressTarget identity;
    private Transform enemyHeist;
    private Damageable enemyHeistDamageable;
    private FortressTarget currentTarget;
    private Damageable currentTargetDamageable;
    private Vector3 heistFormationOffset;
    private float nextTargetScanTime;
    private float nextAttackTime;
    private float nextNavMeshRecoveryTime;
    private float nextDestinationRefreshTime;
    private float nextProgressCheckTime;
    private float lastProgressCheckTime;
    private float stalledDuration;
    private float nextStuckRecoveryTime;
    private float progressRemainingDistance;
    private float progressObjectiveDistance;
    private Vector3 lastObjectivePosition;
    private Transform navigationTarget;
    private bool hasObjectivePosition;
    private bool hasProgressSample;
    private int heistApproachVariant;
    private bool warnedAboutMissingProjectile;
    private bool isDying;
    private TeamGroundRing teamGroundIndicator;
    private MuzzleFlashVFX muzzleFlash;

    private NavMeshPath navigationPath;

    private PlayableGraph crawlerGraph;
    private AnimationClipPlayable crawlerPlayable;
    private bool usesCrawlerPlayable;
    private bool hasMovingParameter;
    private bool hasSpeedParameter;

    public float OnlineMovementSpeed => movementSpeed;
    public float OnlineAttackRange => attackRange;
    public float OnlineAttackCooldown => attackCooldown;
    public int OnlineDamage => damagePerShot;
    public AnimationClip OnlineCrawlerClip => crawlerClip;
    public FortressTeam Team => identity != null ? identity.Team : FortressTeam.Blue;
    public Transform EnemyHeist => enemyHeist;
    public bool IsAlive => damageable != null && !damageable.IsDead && !isDying;
    public Damageable Health => damageable;

    public event Action<SwarmBotAI> Removed;

    private void Awake()
    {
        if (!BotMatchPolicy.AllowActivation(this, "SwarmBotAI.Awake")) return;
        // Unity 6 forbids creating NavMeshPath while a MonoBehaviour is being
        // deserialized. Construct it at runtime so every spawned crawler owns
        // a valid path object before its first navigation update.
        navigationPath = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        damageable = GetComponent<Damageable>();
        SpidyHealthBar.Attach(gameObject);
        // Voice limited: a summoned swarm is many bodies, and only the few
        // nearest the camera are allowed to sound so the swarm stays listenable.
        MovementAudioVoice.Attach(gameObject, ReleaseAudioCue.SpidyMove, 0.85f, voiceLimited: true);
        identity = GetComponent<FortressTarget>();

        agent.speed = movementSpeed;
        agent.acceleration = acceleration;
        agent.angularSpeed = turnSpeed;
        agent.updateRotation = false;
        agent.autoRepath = true;
        agent.stoppingDistance = attackRange * 0.72f;

        if (duelManager == null)
            duelManager = FindFirstObjectByType<FortressDuelManager>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        ConfigureCrawlerAnimation();
        CreateTeamRing();
        UpdateTeamRingColor();

        if (muzzle != null)
        {
            muzzleFlash = muzzle.GetComponent<MuzzleFlashVFX>();
            if (muzzleFlash == null)
                muzzleFlash = muzzle.gameObject.AddComponent<MuzzleFlashVFX>();
        }
    }

    private void OnEnable()
    {
        if (!BotMatchPolicy.AllowActivation(this, "SwarmBotAI.OnEnable")) return;
        if (damageable != null)
            damageable.Died += HandleDeath;
    }

    private void OnDisable()
    {
        if (damageable != null)
            damageable.Died -= HandleDeath;
    }

    private void OnDestroy()
    {
        if (crawlerGraph.IsValid())
            crawlerGraph.Destroy();

        Removed?.Invoke(this);
    }

    private void Update()
    {
        if (!IsAlive ||
            (duelManager != null && duelManager.CurrentPhase != FortressDuelPhase.Combat))
        {
            StopAgent();
            UpdateAnimation(false);
            return;
        }

        if (!EnsureOnNavMesh())
        {
            UpdateAnimation(false);
            return;
        }

        if (!IsAliveTarget(enemyHeist, enemyHeistDamageable))
            FindEnemyHeist();

        if (Time.time >= nextTargetScanTime)
        {
            SelectTarget();
            nextTargetScanTime = Time.time + targetScanInterval;
        }

        // An opportunity can disappear between scans. Clear it immediately so
        // the crawler resumes its actual heist objective on this frame.
        if (!IsValidOpportunity(currentTarget, currentTargetDamageable))
        {
            currentTarget = null;
            currentTargetDamageable = null;
        }

        Transform targetTransform = GetCurrentTargetTransform();
        Damageable targetHealth = GetCurrentTargetDamageable();

        if (!IsAliveTarget(targetTransform, targetHealth))
        {
            StopAgent();
            ClearNavigationTarget();
            UpdateAnimation(false);
            return;
        }

        float targetDistance = FlatDistance(transform.position, targetTransform.position);
        bool hasFiringSolution = targetDistance <= attackRange &&
                                 HasLineOfSightThrottled(targetTransform, targetHealth);

        MoveToward(targetTransform, targetHealth, hasFiringSolution);
        FaceTargetOrVelocity(targetTransform);
        TryAttack(targetTransform, targetHealth, hasFiringSolution);
        UpdateAnimation(agent.velocity.sqrMagnitude > 0.04f);
    }

    public void Initialize(
        FortressTeam team,
        Transform heistTarget,
        int formationIndex,
        int formationCount)
    {
        if (!BotMatchPolicy.RequireBotMatch("SwarmBotAI.Initialize", this)) return;
        identity.SetTeam(team);
        enemyHeist = heistTarget;
        enemyHeistDamageable = heistTarget != null
            ? heistTarget.GetComponentInParent<Damageable>()
            : null;

        int safeCount = Mathf.Max(1, formationCount);
        float angle = formationIndex * 360f / safeCount;
        heistFormationOffset =
            Quaternion.Euler(0f, angle, 0f) * Vector3.forward * heistFormationRadius;

        agent.avoidancePriority = 25 + (formationIndex * 11) % 45;
        UpdateTeamRingColor();
        nextTargetScanTime = 0f;
        ClearNavigationTarget();
    }

    public void ConfigureWeapon(BasicProjectile projectile, Transform firePoint = null)
    {
        bulletPrefab = projectile;
        if (firePoint != null)
            muzzle = firePoint;
    }

    public void Configure(
        FortressTeam team,
        Transform heistTarget,
        BasicProjectile projectile,
        GameObject firingEffect = null,
        Transform firePoint = null,
        Animator targetAnimator = null,
        AnimationClip movementClip = null,
        int formationIndex = 0,
        int formationCount = 4)
    {
        muzzleEffectPrefab = firingEffect;
        ConfigureWeapon(projectile, firePoint);

        if (targetAnimator != null || movementClip != null)
            ConfigureAnimation(targetAnimator, movementClip);

        Initialize(team, heistTarget, formationIndex, formationCount);
    }

    public void ConfigureAnimation(Animator targetAnimator, AnimationClip movementClip)
    {
        if (crawlerGraph.IsValid())
            crawlerGraph.Destroy();

        animator = targetAnimator;
        crawlerClip = movementClip;
        ConfigureCrawlerAnimation();
    }

    private void SelectTarget()
    {
        // The vault remains the swarm's primary objective once reached. On the
        // route there, bots can break off briefly for actual combatants only.
        if (IsAliveTarget(enemyHeist, enemyHeistDamageable) &&
            FlatDistance(transform.position, enemyHeist.position) <= attackRange)
        {
            currentTarget = null;
            currentTargetDamageable = null;
            return;
        }

        if (IsValidOpportunity(currentTarget, currentTargetDamageable) &&
            FlatDistance(transform.position, currentTarget.transform.position) <=
            opportunityDisengageRange)
        {
            return;
        }

        currentTarget = null;
        currentTargetDamageable = null;
        float bestScore = float.PositiveInfinity;

        var candidates = FortressTarget.Active;
        for (int index = 0; index < candidates.Count; index++)
        {
            FortressTarget candidate = candidates[index];
            if (candidate == null || candidate == identity ||
                !candidate.isActiveAndEnabled || candidate.Team == Team ||
                candidate.IsVault ||
                !TryGetOpportunityPriority(candidate, out int targetPriority))
            {
                continue;
            }

            Damageable candidateHealth = candidate.Health;
            if (candidateHealth == null || candidateHealth.IsDead)
                continue;

            float distance = FlatDistance(transform.position, candidate.transform.position);
            if (distance > opportunityRange)
                continue;

            // Priority is deliberate and deterministic: player/bot, enemy
            // crawler, then turret. Distance decides within the same category.
            float score = targetPriority * (opportunityRange + 1f) + distance;
            if (score >= bestScore)
                continue;

            bestScore = score;
            currentTarget = candidate;
            currentTargetDamageable = candidateHealth;
        }
    }

    private static bool TryGetOpportunityPriority(
        FortressTarget candidate,
        out int priority)
    {
        if (candidate.IsCombatRobot)
        {
            priority = 0;
            return true;
        }

        if (candidate.IsSpidy)
        {
            priority = 1;
            return true;
        }

        if (candidate.IsTurret)
        {
            priority = 2;
            return true;
        }

        // Scenery, cover, pickups, neutral props and any future non-combat
        // FortressTarget types are intentionally excluded from swarm aggro.
        priority = int.MaxValue;
        return false;
    }

    private bool IsValidOpportunity(FortressTarget target, Damageable targetHealth)
    {
        return target != null && target != identity && target.isActiveAndEnabled &&
               target.Team != Team && !target.IsVault &&
               TryGetOpportunityPriority(target, out _) &&
               targetHealth != null && !targetHealth.IsDead;
    }

    private Transform GetCurrentTargetTransform()
    {
        return IsValidOpportunity(currentTarget, currentTargetDamageable)
            ? currentTarget.transform
            : enemyHeist;
    }

    private Damageable GetCurrentTargetDamageable()
    {
        return IsValidOpportunity(currentTarget, currentTargetDamageable)
            ? currentTargetDamageable
            : enemyHeistDamageable;
    }

    private void FindEnemyHeist()
    {
        enemyHeist = null;
        enemyHeistDamageable = null;

        var targets = FortressTarget.Active;
        for (int index = 0; index < targets.Count; index++)
        {
            FortressTarget vaultIdentity = targets[index];
            if (vaultIdentity == null || !vaultIdentity.IsVault) continue;
            Damageable vaultHealth = vaultIdentity.Health;
            if (vaultIdentity.Team == Team ||
                vaultHealth == null || vaultHealth.IsDead)
            {
                continue;
            }

            enemyHeist = vaultIdentity.transform;
            enemyHeistDamageable = vaultHealth;
            return;
        }
    }

    private void MoveToward(
        Transform target,
        Damageable targetHealth,
        bool hasFiringSolution)
    {
        bool attackingOpportunity =
            IsValidOpportunity(currentTarget, currentTargetDamageable);
        Vector3 desiredPosition = target.position +
            (attackingOpportunity ? Vector3.zero : heistFormationOffset);

        if (navigationTarget != target)
        {
            navigationTarget = target;
            hasObjectivePosition = false;
            heistApproachVariant = 0;
            nextDestinationRefreshTime = 0f;
            ResetProgressTracking();
        }

        // Do not stop merely because the crawler is close. If scenery blocks
        // its shot, it must keep pathing to a position with a firing lane.
        if (hasFiringSolution)
        {
            agent.stoppingDistance = attackRange * 0.72f;
            StopAgent();
            ResetProgressTracking();
            return;
        }

        agent.stoppingDistance = Mathf.Min(0.35f, attackRange * 0.1f);

        bool objectiveMoved = !hasObjectivePosition ||
            FlatDistance(lastObjectivePosition, desiredPosition) > 0.45f;
        if (objectiveMoved || Time.time >= nextDestinationRefreshTime)
        {
            TrySetReachableDestination(
                target,
                targetHealth,
                desiredPosition,
                attackingOpportunity);
        }

        if (HasCompleteAgentPath())
            agent.isStopped = false;
        else
            StopAgent();

        MonitorNavigationProgress(
            target,
            targetHealth,
            desiredPosition,
            attackingOpportunity);
    }

    private bool TrySetReachableDestination(
        Transform target,
        Damageable targetHealth,
        Vector3 desiredPosition,
        bool attackingOpportunity)
    {
        nextDestinationRefreshTime = Time.time + repathInterval;
        lastObjectivePosition = desiredPosition;
        hasObjectivePosition = true;

        bool hadCompletePath = HasCompleteAgentPath();
        if (!TryCalculateReachablePath(
                target,
                targetHealth,
                desiredPosition,
                attackingOpportunity,
                out _))
        {
            // Keep an already-valid route if a moving target produces one bad
            // sample. Partial paths are never accepted because they are a
            // common cause of crawlers waiting forever at a NavMesh boundary.
            if (!hadCompletePath)
            {
                agent.ResetPath();
                StopAgent();
            }

            return false;
        }

        if (!agent.SetPath(navigationPath))
        {
            if (!hadCompletePath)
                StopAgent();
            return false;
        }

        agent.isStopped = false;
        return true;
    }

    private bool TryCalculateReachablePath(
        Transform target,
        Damageable targetHealth,
        Vector3 desiredPosition,
        bool attackingOpportunity,
        out Vector3 resolvedDestination)
    {
        resolvedDestination = desiredPosition;

        if (attackingOpportunity)
        {
            return TryCalculateCompletePath(
                desiredPosition,
                destinationSampleRadius,
                out resolvedDestination);
        }

        // Prefer the assigned formation point, provided it also offers a clear
        // shot. If that point is behind cover, test deterministic positions
        // around the heist rather than idling at the obstructed endpoint.
        if (heistApproachVariant == 0 &&
            TryCalculateCompletePath(
                desiredPosition,
                destinationSampleRadius,
                out resolvedDestination) &&
            HasLineOfSightFromPosition(
                resolvedDestination,
                target,
                targetHealth))
        {
            return true;
        }

        Vector3 formationDirection = heistFormationOffset;
        if (formationDirection.sqrMagnitude <= 0.01f)
            formationDirection = transform.position - target.position;
        formationDirection.y = 0f;
        if (formationDirection.sqrMagnitude <= 0.01f)
            formationDirection = Vector3.forward;
        formationDirection.Normalize();

        float approachRadius = Mathf.Max(
            heistFormationRadius,
            attackRange * 0.62f);
        int firstVariant = heistApproachVariant > 0
            ? heistApproachVariant
            : 1;

        for (int step = 0; step < 8; step++)
        {
            int variant = ((firstVariant - 1 + step) % 8) + 1;
            Vector3 radialDirection =
                Quaternion.Euler(0f, (variant - 1) * 45f, 0f) * formationDirection;
            Vector3 candidate = target.position + radialDirection * approachRadius;

            if (TryCalculateCompletePath(
                    candidate,
                    Mathf.Min(destinationSampleRadius, 1.25f),
                    out resolvedDestination) &&
                HasLineOfSightFromPosition(
                    resolvedDestination,
                    target,
                    targetHealth))
            {
                return true;
            }
        }

        // A complete route is still preferable to standing still when arena
        // geometry gives no sampled position a clear predicted firing lane.
        if (TryCalculateCompletePath(
                desiredPosition,
                destinationSampleRadius,
                out resolvedDestination))
        {
            return true;
        }

        return TryCalculateCompletePath(
            target.position,
            navMeshRecoveryRadius,
            out resolvedDestination);
    }

    private bool TryCalculateCompletePath(
        Vector3 desiredPosition,
        float sampleRadius,
        out Vector3 resolvedDestination)
    {
        resolvedDestination = desiredPosition;
        if (!agent.isOnNavMesh ||
            !NavMesh.SamplePosition(
                desiredPosition,
                out NavMeshHit navHit,
                sampleRadius,
                agent.areaMask))
        {
            return false;
        }

        resolvedDestination = navHit.position;
        return agent.CalculatePath(resolvedDestination, navigationPath) &&
               navigationPath.status == NavMeshPathStatus.PathComplete;
    }

    private bool HasCompleteAgentPath()
    {
        return agent != null && agent.enabled && agent.isOnNavMesh &&
               !agent.pathPending && agent.hasPath &&
               agent.pathStatus == NavMeshPathStatus.PathComplete;
    }

    private void MonitorNavigationProgress(
        Transform target,
        Damageable targetHealth,
        Vector3 desiredPosition,
        bool attackingOpportunity)
    {
        if (Time.time < nextProgressCheckTime)
            return;

        float now = Time.time;
        float remainingDistance = GetNavigationRemainingDistance(desiredPosition);
        float objectiveDistance = FlatDistance(transform.position, desiredPosition);

        if (!hasProgressSample)
        {
            progressRemainingDistance = remainingDistance;
            progressObjectiveDistance = objectiveDistance;
            lastProgressCheckTime = now;
            nextProgressCheckTime = now + progressCheckInterval;
            hasProgressSample = true;
            return;
        }

        float elapsed = Mathf.Max(0f, now - lastProgressCheckTime);
        bool reducedPathDistance =
            IsFinite(progressRemainingDistance) && IsFinite(remainingDistance) &&
            remainingDistance <= progressRemainingDistance - minimumProgressDistance;
        bool approachedObjective =
            objectiveDistance <= progressObjectiveDistance - minimumProgressDistance;

        // Side-to-side avoidance jitter is not meaningful progress. Counting
        // any displacement here allowed a group of crawlers to shuffle around
        // each other forever in the middle of the arena without ever engaging
        // the vault. Only shortening the validated route or closing on the
        // current destination clears the stall timer.
        stalledDuration = reducedPathDistance || approachedObjective
            ? 0f
            : stalledDuration + elapsed;

        progressRemainingDistance = remainingDistance;
        progressObjectiveDistance = objectiveDistance;
        lastProgressCheckTime = now;
        nextProgressCheckTime = now + progressCheckInterval;

        if (stalledDuration < stuckDurationBeforeRecovery ||
            Time.time < nextStuckRecoveryTime)
        {
            return;
        }

        RecoverFromNavigationStall(
            target,
            targetHealth,
            desiredPosition,
            attackingOpportunity);
        ResetProgressTracking();
    }

    private float GetNavigationRemainingDistance(Vector3 desiredPosition)
    {
        if (HasCompleteAgentPath() && IsFinite(agent.remainingDistance))
            return agent.remainingDistance;

        return FlatDistance(transform.position, desiredPosition);
    }

    private void RecoverFromNavigationStall(
        Transform target,
        Damageable targetHealth,
        Vector3 desiredPosition,
        bool attackingOpportunity)
    {
        nextStuckRecoveryTime = Time.time + stuckRecoveryCooldown;
        nextDestinationRefreshTime = 0f;

        if (!attackingOpportunity)
        {
            heistApproachVariant = heistApproachVariant >= 8
                ? 1
                : heistApproachVariant + 1;
        }

        agent.isStopped = true;
        agent.ResetPath();

        bool calculatedPath = TryCalculateReachablePath(
            target,
            targetHealth,
            desiredPosition,
            attackingOpportunity,
            out _);
        bool warped = calculatedPath && TryWarpForwardAlongPath(navigationPath);

        if (!warped)
            warped = TryWarpToRecoveryNeighbor(desiredPosition);

        if (!warped)
            TryWarpToNearestNavMeshPoint();

        // Warp clears the agent's route, so always calculate one more validated
        // path from its recovered position.
        TrySetReachableDestination(
            target,
            targetHealth,
            desiredPosition,
            attackingOpportunity);
    }

    private bool TryWarpForwardAlongPath(NavMeshPath path)
    {
        // A single fixed recovery point can be occupied by the next member of
        // the swarm. Try progressively farther points on the already validated
        // path, then a shorter nudge. NavMesh.Raycast keeps a warp from cutting
        // straight through a wall when the path turns around a corner.
        float[] distances =
        {
            recoveryWarpDistance,
            recoveryWarpDistance * 1.5f,
            recoveryWarpDistance * 2f,
            recoveryWarpDistance * 0.55f
        };

        foreach (float distance in distances)
        {
            if (!TryGetPointAlongPath(path, distance, out Vector3 pathPoint) ||
                !NavMesh.SamplePosition(
                    pathPoint,
                    out NavMeshHit warpHit,
                    Mathf.Max(0.2f, agent.radius * 0.5f),
                    agent.areaMask) ||
                FlatDistance(transform.position, warpHit.position) <
                    minimumProgressDistance * 2f ||
                NavMesh.Raycast(
                    transform.position,
                    warpHit.position,
                    out _,
                    agent.areaMask) ||
                !IsRecoveryPositionClear(warpHit.position))
            {
                continue;
            }

            if (agent.Warp(warpHit.position))
                return true;
        }

        return false;
    }

    private bool TryWarpToRecoveryNeighbor(Vector3 desiredPosition)
    {
        Vector3 forward = desiredPosition - transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.01f)
            forward = transform.forward;
        if (forward.sqrMagnitude <= 0.01f)
            forward = Vector3.forward;
        forward.Normalize();

        float distance = Mathf.Max(
            agent.radius * 2.1f,
            recoveryWarpDistance * 0.72f);
        float[] angles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

        foreach (float angle in angles)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
            Vector3 candidate = transform.position + direction * distance;
            if (!NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit neighbor,
                    Mathf.Max(0.25f, agent.radius),
                    agent.areaMask) ||
                FlatDistance(transform.position, neighbor.position) <
                    minimumProgressDistance * 2f ||
                NavMesh.Raycast(
                    transform.position,
                    neighbor.position,
                    out _,
                    agent.areaMask) ||
                !IsRecoveryPositionClear(neighbor.position))
            {
                continue;
            }

            if (agent.Warp(neighbor.position))
                return true;
        }

        return false;
    }

    private bool TryWarpToNearestNavMeshPoint()
    {
        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit nearest,
                navMeshRecoveryRadius,
                agent.areaMask) ||
            Mathf.Abs(nearest.position.y - transform.position.y) >
                Mathf.Max(1f, agent.height))
        {
            return false;
        }

        return agent.Warp(nearest.position);
    }

    private bool IsRecoveryPositionClear(Vector3 position)
    {
        float radius = Mathf.Max(0.12f, agent.radius * 0.72f);
        float height = Mathf.Max(agent.height, radius * 2f);
        Vector3 bottom = position + Vector3.up * (radius + 0.05f);
        Vector3 top = position + Vector3.up * (height - radius + 0.05f);

        foreach (Collider overlap in Physics.OverlapCapsule(
                     bottom,
                     top,
                     radius,
                     ~0,
                     QueryTriggerInteraction.Ignore))
        {
            if (overlap == null || overlap.transform == transform ||
                overlap.transform.IsChildOf(transform) ||
                transform.IsChildOf(overlap.transform))
            {
                continue;
            }

            Vector3 closest = ColliderUtility.ClosestPointSafe(
                overlap, (bottom + top) * 0.5f);
            if (closest.y > position.y + 0.12f)
                return false;
        }

        return true;
    }

    private bool HasLineOfSightFromPosition(
        Vector3 position,
        Transform target,
        Damageable targetHealth)
    {
        return HasLineOfSightFrom(
            position + Vector3.up * aimHeight,
            target,
            targetHealth);
    }

    private static bool TryGetPointAlongPath(
        NavMeshPath path,
        float distance,
        out Vector3 point)
    {
        point = default;
        if (path == null || path.corners == null || path.corners.Length < 2)
            return false;

        float distanceLeft = Mathf.Max(0f, distance);
        Vector3[] corners = path.corners;
        for (int index = 1; index < corners.Length; index++)
        {
            Vector3 start = corners[index - 1];
            Vector3 end = corners[index];
            float segmentLength = Vector3.Distance(start, end);
            if (segmentLength <= 0.001f)
                continue;

            if (distanceLeft <= segmentLength)
            {
                point = Vector3.Lerp(start, end, distanceLeft / segmentLength);
                return true;
            }

            distanceLeft -= segmentLength;
        }

        point = corners[corners.Length - 1];
        return true;
    }

    private void ClearNavigationTarget()
    {
        navigationTarget = null;
        hasObjectivePosition = false;
        heistApproachVariant = 0;
        nextDestinationRefreshTime = 0f;
        ResetProgressTracking();
    }

    private void ResetProgressTracking()
    {
        hasProgressSample = false;
        stalledDuration = 0f;
        nextProgressCheckTime = Time.time + progressCheckInterval;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void FaceTargetOrVelocity(Transform target)
    {
        Vector3 direction = FlatDistance(transform.position, target.position) <= attackRange * 1.2f
            ? target.position - transform.position
            : agent.desiredVelocity;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion desired = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            desired,
            turnSpeed * Time.deltaTime);
    }

    private void TryAttack(
        Transform target,
        Damageable targetHealth,
        bool hasFiringSolution)
    {
        if (Time.time < nextAttackTime ||
            !hasFiringSolution)
        {
            return;
        }

        if (bulletPrefab == null)
        {
            if (!warnedAboutMissingProjectile)
            {
                Debug.LogWarning(
                    "Swarm bot needs a visible bullet prefab before it can attack.",
                    this);
                warnedAboutMissingProjectile = true;
            }

            nextAttackTime = Time.time + attackCooldown;
            return;
        }

        Vector3 origin = muzzle != null
            ? muzzle.position
            : transform.position + Vector3.up * aimHeight + transform.forward * 0.45f;
        Vector3 targetPoint = target.position + Vector3.up * aimHeight;
        Vector3 direction = targetPoint - origin;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        // Start the projectile just beyond the crawler's own capsule. Besides
        // looking like a fired shot, this avoids close-range tunnelling through
        // its muzzle/body overlap on mobile physics timesteps.
        Vector3 launchDirection = direction.normalized;
        origin += launchDirection * 0.18f;

        ProjectileLauncher.Fire(
            bulletPrefab,
            origin,
            launchDirection,
            gameObject,
            damagePerShot,
            0f,
            muzzleEffectPrefab,
            muzzleEffectLifetime);

        if (muzzleFlash != null)
            muzzleFlash.PlayFlash();

        nextAttackTime = Time.time + attackCooldown;
    }

    // The firing-lane raycast is refreshed at 10 Hz instead of every rendered
    // frame. Movement stays per-frame smooth; a 0.1 s older lane answer does not
    // change how hard a crawler hits.
    private Transform lineOfSightTarget;
    private bool lineOfSightResult;
    private float lineOfSightExpires;

    private bool HasLineOfSightThrottled(Transform target, Damageable targetHealth)
    {
        if (target == lineOfSightTarget && Time.time < lineOfSightExpires)
            return lineOfSightResult;
        lineOfSightTarget = target;
        lineOfSightResult = HasLineOfSight(target, targetHealth);
        lineOfSightExpires = Time.time + 0.1f;
        return lineOfSightResult;
    }

    private bool HasLineOfSight(Transform target, Damageable targetHealth)
    {
        Vector3 origin = muzzle != null
            ? muzzle.position
            : transform.position + Vector3.up * aimHeight;

        return HasLineOfSightFrom(origin, target, targetHealth);
    }

    private bool HasLineOfSightFrom(
        Vector3 origin,
        Transform target,
        Damageable targetHealth)
    {
        Vector3 direction = target.position + Vector3.up * aimHeight - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
            return true;

        return LineOfSightUtility.IsClear(
            origin,
            direction / distance,
            distance,
            lineOfSightMask,
            transform,
            targetHealth);
    }

    private bool EnsureOnNavMesh()
    {
        if (agent == null || !agent.enabled)
            return false;

        if (agent.isOnNavMesh)
            return true;

        if (Time.time < nextNavMeshRecoveryTime)
            return false;

        nextNavMeshRecoveryTime = Time.time + 0.5f;
        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit nearest,
                navMeshRecoveryRadius,
                agent.areaMask))
        {
            return false;
        }

        bool recovered = agent.Warp(nearest.position);
        if (recovered)
            ClearNavigationTarget();
        return recovered;
    }

    private void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
    }

    private void HandleDeath(Damageable deadBot)
    {
        if (isDying)
            return;

        isDying = true;
        StopAgent();
        UpdateAnimation(false);
        Destroy(gameObject);
    }

    private void ConfigureCrawlerAnimation()
    {
        usesCrawlerPlayable = false;
        hasMovingParameter = false;
        hasSpeedParameter = false;

        if (animator == null)
            return;

        animator.applyRootMotion = false;

        if (crawlerClip != null)
        {
            crawlerGraph = PlayableGraph.Create(name + " Crawler Animation");
            crawlerGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            crawlerPlayable = AnimationClipPlayable.Create(crawlerGraph, crawlerClip);
            crawlerPlayable.SetApplyFootIK(false);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                crawlerGraph,
                "Crawler",
                animator);
            output.SetSourcePlayable(crawlerPlayable);
            crawlerGraph.Play();
            crawlerPlayable.SetSpeed(0d);
            crawlerGraph.Evaluate(0f);
            usesCrawlerPlayable = true;
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool &&
                parameter.name == movingBoolParameter)
            {
                hasMovingParameter = true;
            }

            if (parameter.type == AnimatorControllerParameterType.Float &&
                parameter.name == speedFloatParameter)
            {
                hasSpeedParameter = true;
            }
        }
    }

    private void UpdateAnimation(bool moving)
    {
        if (usesCrawlerPlayable && crawlerPlayable.IsValid())
        {
            crawlerPlayable.SetSpeed(moving ? animationPlaybackSpeed : 0d);
            return;
        }

        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        if (hasMovingParameter)
            animator.SetBool(movingBoolParameter, moving);

        if (hasSpeedParameter)
            animator.SetFloat(speedFloatParameter, moving ? 1f : 0f, 0.12f, Time.deltaTime);

        if (!hasMovingParameter && !hasSpeedParameter)
            animator.speed = moving ? animationPlaybackSpeed : 0f;
    }

    private void CreateTeamRing()
    {
        teamGroundIndicator = GetComponent<TeamGroundRing>();
        if (teamGroundIndicator == null)
            teamGroundIndicator = gameObject.AddComponent<TeamGroundRing>();

        teamGroundIndicator.Configure(identity, teamRingRadius, 0.085f);
    }

    private void UpdateTeamRingColor()
    {
        if (teamGroundIndicator != null)
            teamGroundIndicator.RefreshVisual();
    }

    private static bool IsAliveTarget(Transform target, Damageable targetHealth)
    {
        return target != null && target.gameObject.activeInHierarchy &&
               targetHealth != null && !targetHealth.IsDead;
    }

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }
}
