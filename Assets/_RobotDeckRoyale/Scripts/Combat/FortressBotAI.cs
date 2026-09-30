using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Damageable))]
public class FortressBotAI : MonoBehaviour
{
    private enum TacticalAction
    {
        EngagePlayer,
        DefendVault,
        PressureVault,
        CollectEnergyCell,
        Recover,
        Reposition
    }

    [Header("References")]
    [SerializeField] private FortressDuelManager duelManager;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private Damageable playerDamageable;
    [SerializeField] private Transform redVault;
    [SerializeField] private Damageable redVaultDamageable;
    [SerializeField] private Transform blueVault;
    [SerializeField] private Damageable blueVaultDamageable;
    [SerializeField] private SwarmChargeController swarmChargeController;

    [Header("Combat")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private BasicProjectile projectilePrefab;
    [SerializeField] private GameObject muzzleEffectPrefab;
    [SerializeField] private float muzzleEffectLifetime = 1.5f;
    [SerializeField] private float preferredCombatDistance = 14.5f;
    [SerializeField] private float attackRange = 20f;
    [SerializeField] private int damagePerShot = 22;
    [SerializeField] private float attackCooldown = 0.65f;
    [SerializeField, Min(1)] private int maxAmmo = 3;
    [SerializeField, Min(0.1f)] private float reloadSecondsPerAmmo = 1.15f;
    [SerializeField] private float aimHeight = 1f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;
    [SerializeField, Range(0f, 1.2f)] private float maximumLeadTime = 0.55f;
    [Tooltip("The bot must hold a firing solution this long before shooting, giving the player a readable window to break line of sight.")]
    [SerializeField, Range(0f, 1f)] private float shotWindUp = 0.22f;
    [Tooltip("Random cone applied to the bot's shots so it is not a frame-perfect marksman.")]
    [SerializeField, Range(0f, 8f)] private float aimErrorDegrees = 2.5f;

    [Header("Utility AI")]
    [SerializeField, Range(0.1f, 1f)] private float decisionInterval = 0.22f;
    [SerializeField, Range(0.2f, 2f)] private float minimumActionCommitment = 0.7f;
    [SerializeField, Range(0f, 30f)] private float actionSwitchMargin = 8f;
    [SerializeField] private float vaultThreatRadius = 14f;
    [SerializeField] private float recentDamageMemory = 2.5f;
    [SerializeField] private float lastSeenMemory = 2f;

    [Header("E-Cell Tactics")]
    [SerializeField, Min(0.1f)] private float energyCellPickupDistance = 0.9f;
    [SerializeField, Min(1f)] private float maximumEnergyCellPursuitDistance = 28f;

    [Header("Spawn Etiquette")]
    [Tooltip("The bot will not take a tactical position inside this radius of the enemy respawn point. Without it, pressuring the enemy vault parks the bot on top of the spawn and the player is killed before they can act.")]
    [SerializeField] private Transform enemySpawnPoint;
    [SerializeField, Min(0f)] private float enemySpawnKeepOut = 11f;

    [Header("Tactical Movement")]
    [SerializeField] private float orbitDistance = 7.5f;
    [SerializeField] private float strafeWidth = 3.25f;
    [SerializeField] private float tacticalPointRefresh = 0.75f;
    [SerializeField] private float navMeshSearchRadius = 4f;
    [SerializeField] private float retreatDistanceFromVault = 4f;

    [Header("Movement Polish")]
    [SerializeField] private float facingSmoothness = 14f;
    [SerializeField] private float destinationRefreshDistance = 0.35f;

    private NavMeshAgent agent;
    private Damageable damageable;
    private FortressTarget targetIdentity;
    private TacticalAction currentAction;
    private Transform currentTarget;
    private Damageable currentTargetDamageable;
    private EnergyCellPickup currentEnergyCell;
    private EnergyCellPickup scoredEnergyCell;
    private bool hasChosenAction;
    private float currentActionScore;
    private float actionCommittedUntil;
    private float nextDecisionTime;
    private float nextTacticalPointTime;
    private readonly WeaponCore weapon = new WeaponCore();
    private float nextAttackTime;
    private float firingSolutionSince = float.PositiveInfinity;
    private int orbitDirection = 1;
    private bool warnedAboutProjectile;
    private Vector3 tacticalDestination;
    private Vector3 lastDestination = new Vector3(float.PositiveInfinity, 0f, 0f);
    private Vector3 lastSeenPlayerPosition;
    private float lastSeenPlayerTime = float.NegativeInfinity;
    private Vector3 previousPlayerPosition;
    private float previousPlayerSampleTime;
    private Vector3 estimatedPlayerVelocity;
    private float lastDamagedTime = float.NegativeInfinity;
    private float recentDamageAmount;
    private float nextSwarmControllerLookupTime;
    private MuzzleFlashVFX muzzleFlash;
    private RobotMotionVisual motionVisual;
    // The bot fires through its own weapon rather than a RobotBlaster, so it has
    // no Fired event for RobotAnimationHooks to subscribe to. Notify it directly
    // or the bot never plays the authored attack the player does.
    private RobotAnimationHooks animationHooks;

    /// <summary>
    /// Retunes the existing utility scorer for a behaviour profile. This only moves
    /// numbers the AI already reads - no new decision logic - so a profile cannot
    /// give the bot knowledge or reactions a player could not have.
    /// </summary>
    public void ApplyBehaviourProfile(BotBehaviourProfile profile)
    {
        if (!BotMatchPolicy.RequireBotMatch("FortressBotAI.ApplyBehaviourProfile", this)) return;
        switch (profile)
        {
            case BotBehaviourProfile.Aggressive:
                preferredCombatDistance = 10.5f;
                attackCooldown = 0.55f;
                shotWindUp = 0.16f;
                aimErrorDegrees = 2.2f;
                orbitDistance = 6f;
                energyCellAppetite = 0.7f;
                break;

            case BotBehaviourProfile.Defensive:
                preferredCombatDistance = 17.5f;
                attackCooldown = 0.8f;
                shotWindUp = 0.3f;
                aimErrorDegrees = 3.2f;
                orbitDistance = 9f;
                energyCellAppetite = 0.85f;
                break;

            case BotBehaviourProfile.Collector:
                preferredCombatDistance = 15f;
                attackCooldown = 0.72f;
                shotWindUp = 0.26f;
                aimErrorDegrees = 3f;
                orbitDistance = 8f;
                energyCellAppetite = 1.6f;
                break;

            default:
                preferredCombatDistance = 14.5f;
                attackCooldown = 0.65f;
                shotWindUp = 0.22f;
                aimErrorDegrees = 2.5f;
                orbitDistance = 7.5f;
                energyCellAppetite = 1f;
                break;
        }

        // Reaction time varies within the profile so repeat matches against the
        // same profile still feel different.
        decisionInterval = Mathf.Clamp(decisionInterval * UnityEngine.Random.Range(0.85f, 1.25f), 0.1f, 1f);
        ConfigureReferenceWeapon();

        Debug.Log(
            $"[BOT_FALLBACK] Profile {profile}: distance={preferredCombatDistance} " +
            $"cooldown={attackCooldown} windUp={shotWindUp} aimError={aimErrorDegrees} " +
            $"cellAppetite={energyCellAppetite} decisionInterval={decisionInterval:F2}");
    }

    // Scales how attractive E-Cells look to the utility scorer. 1 keeps the
    // authored behaviour; the Collector profile raises it.
    private float energyCellAppetite = 1f;

    public int CurrentAmmo => weapon.CurrentAmmo;
    public int MaxAmmo => weapon.MaxAmmo;
    public float ReloadProgress => weapon.GetReloadProgress(Time.time);

    private void ConfigureReferenceWeapon()
    {
        if (duelManager == null) duelManager = FindAnyObjectByType<FortressDuelManager>();
        var reference = duelManager != null && duelManager.LocalPlayer != null
            ? duelManager.LocalPlayer.GetComponent<RobotBlaster>() : null;
        if (reference != null && reference.ProjectilePrefab != null)
        {
            projectilePrefab = reference.ProjectilePrefab;
        }
        weapon.Configure(maxAmmo, reloadSecondsPerAmmo, attackCooldown);
    }

    private void Awake()
    {
        if (!BotMatchPolicy.AllowActivation(this, "FortressBotAI.Awake")) return;
        agent = GetComponent<NavMeshAgent>();
        damageable = GetComponent<Damageable>();
        targetIdentity = GetComponent<FortressTarget>();
        motionVisual = GetComponentInChildren<RobotMotionVisual>(true);
        animationHooks = GetComponentInChildren<RobotAnimationHooks>(true);

        // The opponent drives itself through this component rather than through
        // RobotPlayerController, so it was never given a movement voice and the
        // enemy robot crossed the arena in total silence. Same cue and level as
        // the player: it is the same chassis on the same ground.
        MovementAudioVoice.Attach(gameObject, ReleaseAudioCue.RobotMovement, 0.50f);

        ConfigureReferenceWeapon();
        agent.updateRotation = false;

        if (duelManager == null)
            duelManager = FindFirstObjectByType<FortressDuelManager>();

        if (swarmChargeController == null)
            swarmChargeController = FindFirstObjectByType<SwarmChargeController>();

        if (muzzle != null)
        {
            muzzleFlash = muzzle.GetComponent<MuzzleFlashVFX>();
            if (muzzleFlash == null)
                muzzleFlash = muzzle.gameObject.AddComponent<MuzzleFlashVFX>();
        }

        if (playerTarget != null && playerDamageable == null)
            playerDamageable = playerTarget.GetComponentInParent<Damageable>();

        if (playerTarget != null)
        {
            previousPlayerPosition = playerTarget.position;
            lastSeenPlayerPosition = playerTarget.position;
        }

        previousPlayerSampleTime = Time.time;
    }

    private void OnEnable()
    {
        if (!BotMatchPolicy.AllowActivation(this, "FortressBotAI.OnEnable")) return;
        if (damageable != null)
            damageable.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        if (damageable != null)
            damageable.Damaged -= OnDamaged;
    }

    private void Update()
    {
        if (!MatchSessionContext.CanInitializeAI) return;
        weapon.Tick(Time.time);
        UpdatePlayerMemory();

        if (duelManager == null ||
            duelManager.CurrentPhase != FortressDuelPhase.Combat ||
            damageable == null || damageable.IsDead ||
            agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        if (Time.time >= nextDecisionTime)
        {
            EvaluateSituation();
            nextDecisionTime = Time.time + decisionInterval;
        }

        ActOnDecision();
        UpdateFacing();
    }

    private void EvaluateSituation()
    {
        bool playerAlive = IsAlive(playerTarget, playerDamageable);
        bool ownVaultAlive = IsAlive(redVault, redVaultDamageable);
        bool enemyVaultAlive = IsAlive(blueVault, blueVaultDamageable);
        float health = HealthPercent(damageable);
        float ammo = weapon.MaxAmmo > 0
            ? (float)weapon.CurrentAmmo / weapon.MaxAmmo
            : 0f;
        float damageRecency = Mathf.Clamp01(1f - (Time.time - lastDamagedTime) / recentDamageMemory);
        float pressure = Mathf.Clamp01(recentDamageAmount / Mathf.Max(1f, damageable.MaxHealth * 0.35f)) * damageRecency;
        float playerDistance = playerAlive ? FlatDistance(transform.position, playerTarget.position) : 999f;
        bool seesPlayer = playerAlive && HasLineOfSight(playerTarget, playerDamageable);
        float playerNearVault = playerAlive && ownVaultAlive
            ? 1f - Mathf.Clamp01(FlatDistance(playerTarget.position, redVault.position) / vaultThreatRadius)
            : 0f;
        float ownVaultDanger = ownVaultAlive ? 1f - HealthPercent(redVaultDamageable) : 0f;
        float enemyVaultDamage = enemyVaultAlive ? 1f - HealthPercent(blueVaultDamageable) : 1f;
        float playerVulnerability = playerAlive ? 1f - HealthPercent(playerDamageable) : 0f;
        float rangeQuality = 1f - Mathf.Clamp01(
            Mathf.Abs(playerDistance - preferredCombatDistance) / Mathf.Max(1f, preferredCombatDistance));
        float closePlayerPressure = playerAlive
            ? Mathf.Clamp01(1f - playerDistance / Mathf.Max(1f, attackRange * 1.8f)) *
              (seesPlayer ? 1f : 0.55f)
            : 0f;
        float combatPressure = Mathf.Max(pressure, closePlayerPressure);
        float vaultThreat = Mathf.Max(playerNearVault, ownVaultDanger * 0.7f);

        scoredEnergyCell = FindEnergyCellCandidate();
        float collectEnergy = ScoreEnergyCellCollection(
            scoredEnergyCell,
            combatPressure,
            vaultThreat);

        float engage = playerAlive
            ? 24f + rangeQuality * 17f + ammo * 16f + (seesPlayer ? 15f : -9f) +
              playerVulnerability * 24f - pressure * 12f - (1f - health) * 9f
            : -1000f;
        float defend = playerAlive && ownVaultAlive
            ? 13f + playerNearVault * 55f + ownVaultDanger * 34f + (seesPlayer ? 7f : 0f) + ammo * 8f
            : -1000f;
        float pressureVault = enemyVaultAlive
            ? 25f + health * 12f + ammo * 13f + enemyVaultDamage * 18f - playerNearVault * 32f - pressure * 10f
            : -1000f;
        float recover = ownVaultAlive
            ? 5f + (1f - health) * 72f + (1f - ammo) * 21f + pressure * 28f - playerVulnerability * 8f
            : -1000f;
        float reposition = playerAlive
            ? 12f + (seesPlayer ? 0f : 42f) + (1f - ammo) * 20f + pressure * 20f +
              (rangeQuality < 0.35f ? 13f : 0f)
            : -1000f;

        TacticalAction bestAction = TacticalAction.EngagePlayer;
        float bestScore = engage;
        Consider(TacticalAction.DefendVault, defend, ref bestAction, ref bestScore);
        Consider(TacticalAction.PressureVault, pressureVault, ref bestAction, ref bestScore);
        Consider(TacticalAction.CollectEnergyCell, collectEnergy, ref bestAction, ref bestScore);
        Consider(TacticalAction.Recover, recover, ref bestAction, ref bestScore);
        Consider(TacticalAction.Reposition, reposition, ref bestAction, ref bestScore);

        bool maySwitch = !hasChosenAction || Time.time >= actionCommittedUntil ||
                         bestScore >= currentActionScore + actionSwitchMargin;
        if (maySwitch && (!hasChosenAction || bestAction != currentAction))
            SelectAction(bestAction, bestScore);
        else
            currentActionScore = ScoreForAction(
                currentAction, engage, defend, pressureVault, collectEnergy, recover, reposition);

        recentDamageAmount = Mathf.MoveTowards(
            recentDamageAmount,
            0f,
            damageable.MaxHealth * decisionInterval / Mathf.Max(0.1f, recentDamageMemory));
    }

    private void SelectAction(TacticalAction action, float score)
    {
        currentAction = action;
        currentActionScore = score;
        hasChosenAction = true;
        actionCommittedUntil = Time.time + minimumActionCommitment;
        nextTacticalPointTime = 0f;

        if (action == TacticalAction.CollectEnergyCell)
        {
            currentEnergyCell = scoredEnergyCell;
            SetTarget(null, null);
        }
        else if (action == TacticalAction.PressureVault)
            SetTarget(blueVault, blueVaultDamageable);
        else if (action == TacticalAction.Recover)
            SetTarget(redVault, redVaultDamageable);
        else
            SetTarget(playerTarget, playerDamageable);

        if (action != TacticalAction.CollectEnergyCell)
            currentEnergyCell = null;

        if (action == TacticalAction.Reposition || Time.time - lastDamagedTime < 0.35f)
            orbitDirection *= -1;
    }

    private void ActOnDecision()
    {
        switch (currentAction)
        {
            case TacticalAction.DefendVault:
                MoveAndFightPlayer(true);
                break;
            case TacticalAction.PressureVault:
                PressureEnemyVault();
                break;
            case TacticalAction.CollectEnergyCell:
                CollectEnergyCell();
                break;
            case TacticalAction.Recover:
                RecoverNearVault();
                break;
            case TacticalAction.Reposition:
                RepositionForShot();
                break;
            default:
                MoveAndFightPlayer(false);
                break;
        }
    }

    private void CollectEnergyCell()
    {
        if (!IsAvailableEnergyCell(currentEnergyCell) ||
            swarmChargeController == null || targetIdentity == null ||
            swarmChargeController.GetCharge(targetIdentity.Team) >=
            swarmChargeController.MaximumCharge)
        {
            AbandonEnergyCellPursuit();
            return;
        }

        Vector3 cellPosition = currentEnergyCell.Position;
        if (FlatDistance(transform.position, cellPosition) <= energyCellPickupDistance)
        {
            agent.isStopped = true;
            EnergyCellPickup cell = currentEnergyCell;
            bool collected = cell.TryCollect(targetIdentity);

            if (collected || !IsAvailableEnergyCell(cell))
                AbandonEnergyCellPursuit();
            else
            {
                // Another collector may have filled the charge between decisions.
                nextDecisionTime = 0f;
                actionCommittedUntil = 0f;
            }
            return;
        }

        SetTarget(null, null);
        MoveToPosition(cellPosition, energyCellPickupDistance);
    }

    private EnergyCellPickup FindEnergyCellCandidate()
    {
        ResolveSwarmChargeController();
        if (swarmChargeController == null || targetIdentity == null ||
            swarmChargeController.GetCharge(targetIdentity.Team) >=
            swarmChargeController.MaximumCharge)
        {
            return null;
        }

        // Stay committed to a valid pickup instead of oscillating between nearby cells.
        if (currentAction == TacticalAction.CollectEnergyCell &&
            IsAvailableEnergyCell(currentEnergyCell))
        {
            return currentEnergyCell;
        }

        return swarmChargeController.FindClosestEnergyCell(
            targetIdentity.Team,
            transform.position);
    }

    private float ScoreEnergyCellCollection(
        EnergyCellPickup cell,
        float combatPressure,
        float vaultThreat)
    {
        if (!IsAvailableEnergyCell(cell) ||
            swarmChargeController == null || targetIdentity == null)
        {
            return -1000f;
        }

        float distance = FlatDistance(transform.position, cell.Position);
        if (distance > maximumEnergyCellPursuitDistance)
            return -1000f;

        float chargeNeed = 1f - swarmChargeController.GetChargeNormalized(targetIdentity.Team);
        float distanceAppeal = 1f - Mathf.Clamp01(
            distance / Mathf.Max(1f, maximumEnergyCellPursuitDistance));

        // A nearby cell is desirable while charge is low, but immediate combat and
        // pressure on the home vault remain more important than gathering.
        // Appetite scales only the appeal of gathering, never the combat and vault
        // penalties, so even a Collector still abandons a cell under real threat.
        float appeal = (8f + chargeNeed * 64f + distanceAppeal * 34f +
                        (1f - (float)weapon.CurrentAmmo / Mathf.Max(1, weapon.MaxAmmo)) * 8f) *
                       energyCellAppetite;

        return appeal - combatPressure * 55f - vaultThreat * 62f;
    }

    private void ResolveSwarmChargeController()
    {
        if (swarmChargeController != null || Time.time < nextSwarmControllerLookupTime)
            return;

        swarmChargeController = FindFirstObjectByType<SwarmChargeController>();
        nextSwarmControllerLookupTime = Time.time + 2f;
    }

    private void AbandonEnergyCellPursuit()
    {
        currentEnergyCell = null;
        scoredEnergyCell = null;
        currentTarget = null;
        currentTargetDamageable = null;
        hasChosenAction = false;
        currentActionScore = float.NegativeInfinity;
        actionCommittedUntil = 0f;
        nextDecisionTime = 0f;
        nextTacticalPointTime = 0f;
        lastDestination = new Vector3(float.PositiveInfinity, 0f, 0f);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private static bool IsAvailableEnergyCell(EnergyCellPickup cell)
    {
        return cell != null && cell.IsAvailable;
    }

    private void MoveAndFightPlayer(bool defendVault)
    {
        if (!IsAlive(playerTarget, playerDamageable))
            return;

        SetTarget(playerTarget, playerDamageable);
        if (Time.time >= nextTacticalPointTime)
        {
            Vector3 anchor = defendVault && redVault != null
                ? Vector3.Lerp(playerTarget.position, redVault.position, 0.38f)
                : playerTarget.position;
            tacticalDestination = FindCombatPosition(anchor, playerTarget, playerDamageable);
            nextTacticalPointTime = Time.time + tacticalPointRefresh;
        }

        MoveToPosition(tacticalDestination, 0.55f);
        TryAttack(playerTarget, playerDamageable);
    }

    private void PressureEnemyVault()
    {
        if (!IsAlive(blueVault, blueVaultDamageable))
            return;

        bool playerCanPunish = IsAlive(playerTarget, playerDamageable) &&
                               FlatDistance(transform.position, playerTarget.position) <= attackRange * 0.82f &&
                               HasLineOfSight(playerTarget, playerDamageable);
        if (playerCanPunish)
        {
            MoveAndFightPlayer(false);
            return;
        }

        SetTarget(blueVault, blueVaultDamageable);
        MoveToPosition(blueVault.position, preferredCombatDistance * 0.75f);
        TryAttack(blueVault, blueVaultDamageable);
    }

    private void RecoverNearVault()
    {
        if (redVault == null)
            return;

        if (Time.time >= nextTacticalPointTime)
        {
            Vector3 away = playerTarget != null
                ? (redVault.position - playerTarget.position).normalized
                : -redVault.forward;
            tacticalDestination = SampleNavMesh(
                redVault.position + away * retreatDistanceFromVault,
                redVault.position);
            nextTacticalPointTime = Time.time + tacticalPointRefresh;
        }

        MoveToPosition(tacticalDestination, 0.65f);
        if (IsAlive(playerTarget, playerDamageable))
        {
            SetTarget(playerTarget, playerDamageable);
            TryAttack(playerTarget, playerDamageable);
        }
    }

    private void RepositionForShot()
    {
        if (!IsAlive(playerTarget, playerDamageable))
            return;

        SetTarget(playerTarget, playerDamageable);
        if (Time.time >= nextTacticalPointTime ||
            FlatDistance(transform.position, tacticalDestination) < 0.8f)
        {
            tacticalDestination = FindBestFiringPosition(playerTarget, playerDamageable);
            nextTacticalPointTime = Time.time + tacticalPointRefresh * 1.25f;
        }

        MoveToPosition(tacticalDestination, 0.45f);
        TryAttack(playerTarget, playerDamageable);
    }

    private Vector3 FindCombatPosition(Vector3 anchor, Transform target, Damageable targetDamageable)
    {
        Vector3 fromTarget = transform.position - target.position;
        fromTarget.y = 0f;
        if (fromTarget.sqrMagnitude < 0.01f)
            fromTarget = -target.forward;

        fromTarget.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, fromTarget) * orbitDirection;
        Vector3 sampled = SampleNavMesh(
            anchor + fromTarget * orbitDistance + side * strafeWidth,
            transform.position);

        return HasLineOfSightFrom(sampled + Vector3.up * aimHeight, target, targetDamageable)
            ? sampled
            : FindBestFiringPosition(target, targetDamageable);
    }

    private Vector3 FindBestFiringPosition(Transform target, Damageable targetDamageable)
    {
        Vector3 best = transform.position;
        float bestScore = float.NegativeInfinity;
        float offset = orbitDirection > 0 ? 25f : -25f;

        for (int index = 0; index < 8; index++)
        {
            Vector3 radial = Quaternion.Euler(0f, offset + index * 45f, 0f) * Vector3.forward;
            Vector3 desired = target.position + radial * orbitDistance;
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas))
                continue;

            bool clear = HasLineOfSightFrom(hit.position + Vector3.up * aimHeight, target, targetDamageable);
            float travel = FlatDistance(transform.position, hit.position);
            float spacing = Mathf.Abs(FlatDistance(hit.position, target.position) - preferredCombatDistance);
            float vaultBias = redVault != null ? -FlatDistance(hit.position, redVault.position) * 0.08f : 0f;
            float score = (clear ? 30f : -20f) - travel * 0.55f - spacing + vaultBias;

            if (score > bestScore)
            {
                bestScore = score;
                best = hit.position;
            }
        }

        return best;
    }

    private void TryAttack(Transform target, Damageable targetDamageable)
    {
        if (!IsAlive(target, targetDamageable) || Time.time < nextAttackTime ||
            !weapon.HasAmmo ||
            FlatDistance(transform.position, target.position) > attackRange ||
            !HasLineOfSight(target, targetDamageable))
        {
            // Losing the shot resets the wind-up, so breaking line of sight is
            // a real defensive option rather than a cosmetic delay.
            firingSolutionSince = float.PositiveInfinity;
            return;
        }

        if (float.IsPositiveInfinity(firingSolutionSince))
            firingSolutionSince = Time.time;

        if (Time.time < firingSolutionSince + shotWindUp)
            return;

        Transform firePoint = muzzle != null ? muzzle : transform;
        Vector3 direction = PredictAimPoint(target, targetDamageable, firePoint.position) - firePoint.position;

        if (projectilePrefab != null && direction.sqrMagnitude > 0.01f)
        {
            ProjectileLauncher.Fire(
                projectilePrefab,
                firePoint.position,
                direction,
                gameObject,
                damagePerShot,
                aimErrorDegrees,
                muzzleEffectPrefab,
                muzzleEffectLifetime);

            if (muzzleFlash != null)
                muzzleFlash.PlayFlash();

            // The bot shoots through ProjectileLauncher directly rather than
            // through RobotBlaster, so it never reached the launch sound the
            // player gets and the opponent fired silently. Positional, so it
            // reads as coming from over there rather than from the player.
            ReleaseAudio.PlayAt(ReleaseAudioCue.MissileFire, firePoint.position,
                UnityEngine.Random.Range(0.97f, 1.03f));

            motionVisual?.NotifyFired();
            animationHooks?.NotifyFired();
        }
        else if (projectilePrefab == null)
        {
            if (!warnedAboutProjectile)
            {
                Debug.LogWarning("Red bot has no projectile assigned; using direct damage.", this);
                warnedAboutProjectile = true;
            }

            if (targetIdentity != null)
                targetDamageable.TakeDamage(damagePerShot, targetIdentity.Team, gameObject);
            else
                targetDamageable.TakeDamage(damagePerShot);

            motionVisual?.NotifyFired();
            animationHooks?.NotifyFired();
        }

        weapon.CommitShot(Time.time);
        nextAttackTime = Time.time + attackCooldown;
        firingSolutionSince = float.PositiveInfinity;
    }

    private Vector3 PredictAimPoint(Transform target, Damageable targetDamageable, Vector3 origin)
    {
        Vector3 point = target.position + Vector3.up * aimHeight;
        if (targetDamageable == playerDamageable && projectilePrefab != null)
        {
            float travelTime = Vector3.Distance(origin, point) / Mathf.Max(1f, projectilePrefab.Speed);
            point += estimatedPlayerVelocity * Mathf.Min(maximumLeadTime, travelTime);
        }
        return point;
    }

    private bool HasLineOfSight(Transform target, Damageable targetDamageable)
    {
        Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * aimHeight;
        return HasLineOfSightFrom(origin, target, targetDamageable);
    }

    private bool HasLineOfSightFrom(Vector3 origin, Transform target, Damageable targetDamageable)
    {
        if (target == null)
            return false;

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
            targetDamageable);
    }

    /// <summary>
    /// Pushes a destination back out of the enemy spawn bubble so the bot can
    /// still pressure the vault without standing on the respawn point.
    /// </summary>
    private Vector3 KeepOutOfEnemySpawn(Vector3 destination)
    {
        if (enemySpawnPoint == null || enemySpawnKeepOut <= 0f)
            return destination;

        Vector3 spawn = enemySpawnPoint.position;
        Vector3 offset = destination - spawn;
        offset.y = 0f;

        float distance = offset.magnitude;

        if (distance >= enemySpawnKeepOut)
            return destination;

        Vector3 direction = distance > 0.01f
            ? offset / distance
            : (transform.position - spawn).normalized;

        if (direction.sqrMagnitude < 0.01f)
            direction = Vector3.forward;

        Vector3 pushed = spawn + direction * enemySpawnKeepOut;
        pushed.y = destination.y;

        return SampleNavMesh(pushed, destination);
    }

    private void MoveToPosition(Vector3 destination, float stoppingDistance)
    {
        destination = KeepOutOfEnemySpawn(destination);
        agent.stoppingDistance = stoppingDistance;
        if (FlatDistance(transform.position, destination) <= stoppingDistance + 0.15f)
        {
            agent.isStopped = true;
            return;
        }

        agent.isStopped = false;
        if (!agent.hasPath ||
            (destination - lastDestination).sqrMagnitude >=
            destinationRefreshDistance * destinationRefreshDistance)
        {
            agent.SetDestination(destination);
            lastDestination = destination;
        }
    }

    private void UpdateFacing()
    {
        bool trackTarget = currentTarget != null && currentTargetDamageable != null &&
                           !currentTargetDamageable.IsDead &&
                           FlatDistance(transform.position, currentTarget.position) <= attackRange * 1.15f;
        Vector3 direction = trackTarget
            ? currentTarget.position - transform.position
            : agent.desiredVelocity;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.02f)
            return;

        Quaternion rotation = Quaternion.LookRotation(direction.normalized);
        float blend = 1f - Mathf.Exp(-facingSmoothness * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation, blend);
    }

    private void UpdatePlayerMemory()
    {
        if (playerTarget == null)
            return;

        float elapsed = Time.time - previousPlayerSampleTime;
        if (elapsed > 0.02f)
        {
            Vector3 velocity = (playerTarget.position - previousPlayerPosition) / elapsed;
            estimatedPlayerVelocity = Vector3.Lerp(estimatedPlayerVelocity, velocity, 0.35f);
            estimatedPlayerVelocity.y = 0f;
            previousPlayerPosition = playerTarget.position;
            previousPlayerSampleTime = Time.time;
        }

        if (playerDamageable != null && !playerDamageable.IsDead &&
            HasLineOfSight(playerTarget, playerDamageable))
        {
            lastSeenPlayerPosition = playerTarget.position;
            lastSeenPlayerTime = Time.time;
        }
        else if (Time.time - lastSeenPlayerTime <= lastSeenMemory && currentAction == TacticalAction.Reposition)
        {
            tacticalDestination = lastSeenPlayerPosition;
        }
    }

    private void OnDamaged(Damageable target, int amount)
    {
        recentDamageAmount += amount;
        lastDamagedTime = Time.time;
        orbitDirection *= -1;
        nextDecisionTime = 0f;
        nextTacticalPointTime = 0f;
    }

    private void SetTarget(Transform target, Damageable targetDamageable)
    {
        currentTarget = target;
        currentTargetDamageable = targetDamageable;
    }

    private Vector3 SampleNavMesh(Vector3 desired, Vector3 fallback)
    {
        return NavMesh.SamplePosition(desired, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas)
            ? hit.position
            : fallback;
    }

    private static void Consider(
        TacticalAction action, float score, ref TacticalAction bestAction, ref float bestScore)
    {
        if (score <= bestScore)
            return;
        bestAction = action;
        bestScore = score;
    }

    private static float ScoreForAction(
        TacticalAction action, float engage, float defend, float pressure,
        float collectEnergy, float recover, float reposition)
    {
        switch (action)
        {
            case TacticalAction.DefendVault: return defend;
            case TacticalAction.PressureVault: return pressure;
            case TacticalAction.CollectEnergyCell: return collectEnergy;
            case TacticalAction.Recover: return recover;
            case TacticalAction.Reposition: return reposition;
            default: return engage;
        }
    }

    private static bool IsAlive(Transform target, Damageable targetDamageable)
    {
        return target != null && target.gameObject.activeInHierarchy &&
               targetDamageable != null && !targetDamageable.IsDead;
    }

    private static float HealthPercent(Damageable target)
    {
        return target == null || target.MaxHealth <= 0
            ? 0f
            : (float)target.CurrentHealth / target.MaxHealth;
    }

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }
}
