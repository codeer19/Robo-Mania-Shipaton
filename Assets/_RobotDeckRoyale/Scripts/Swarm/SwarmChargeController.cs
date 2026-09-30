using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class SwarmChargeController : MonoBehaviour
{
    [Header("Match References")]
    [SerializeField] private FortressDuelManager duelManager;
    [SerializeField] private Collider energyCellSpawnArea;
    [SerializeField] private Transform blueHeist;
    [SerializeField] private Transform redHeist;
    [SerializeField] private Transform blueSwarmSpawn;
    [SerializeField] private Transform redSwarmSpawn;
    [SerializeField] private Transform runtimeObjectsRoot;
    [SerializeField] private bool runWithoutDuelManager;

    [Header("Prefabs")]
    [SerializeField] private EnergyCellPickup energyCellPrefab;
    [SerializeField] private SwarmBotAI swarmBotPrefab;

    [Header("E-Cell Economy")]
    [SerializeField, Min(1)] private int maximumCharge = 100;
    [SerializeField, Min(1)] private int chargePerCell = 20;
    [SerializeField, Min(0f)] private float initialSpawnDelay = 0.8f;
    [SerializeField, Min(0.1f)] private float minimumSpawnInterval = 2.5f;
    [SerializeField, Min(0.1f)] private float maximumSpawnInterval = 4f;

    [Header("Random Spawn Validation")]
    [SerializeField] private Vector3 fallbackArenaCentre = Vector3.zero;
    [SerializeField] private Vector2 fallbackArenaSize = new Vector2(46f, 66f);
    [SerializeField, Min(0f)] private float arenaEdgePadding = 2f;
    [SerializeField, Min(0f)] private float pickupHeight = 0.35f;
    [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2.5f;
    [SerializeField, Min(1)] private int spawnPositionAttempts = 24;
    [SerializeField, Min(0f)] private float minimumCellSpacing = 3f;
    [SerializeField, Min(0f)] private float minimumHeistDistance = 6f;
    [SerializeField, Min(0f)] private float obstacleClearanceRadius = 0.55f;
    [SerializeField] private LayerMask spawnBlockingMask = ~0;

    [Header("Swarm Deployment")]
    [SerializeField, Min(1)] private int botsPerSwarm = 4;
    [SerializeField, Min(0f)] private float spawnFormationRadius = 1.25f;
    [SerializeField, Min(0f)] private float fallbackSpawnDistanceFromHeist = 4f;
    [SerializeField] private bool requirePreviousSwarmCleared = true;

    [Header("Enemy Automation")]
    [SerializeField, Min(0f)] private float redAutomaticSummonDelay = 0.9f;

    private readonly List<EnergyCellPickup> activeCells =
        new List<EnergyCellPickup>();
    private readonly List<SwarmBotAI>[] activeSwarmBots =
    {
        new List<SwarmBotAI>(),
        new List<SwarmBotAI>()
    };
    private readonly int[] teamCharge = new int[2];

    private bool combatActive;
    private float nextCellSpawnTime;
    private float redSummonAt = float.PositiveInfinity;
    private float nextMissingReferenceWarningTime;

    public EnergyCellPickup OnlineCellPrefab => energyCellPrefab;
    public SwarmBotAI OnlineSpidyPrefab => swarmBotPrefab;
    public int OnlineBotsPerSwarm => botsPerSwarm;
    public int OnlineChargePerCell => chargePerCell;
    public bool TryGetOnlineCellPosition(out Vector3 position)
    {
        if (!TryFindEnergyCellSpawnPoint(out position)) return false;
        position += Vector3.up * pickupHeight; return true;
    }
    public int MaximumCharge => maximumCharge;
    public bool CombatActive => combatActive;

    public event Action<FortressTeam, int, int> ChargeChanged;
    public event Action<FortressTeam> SwarmReady;
    public event Action<FortressTeam, IReadOnlyList<SwarmBotAI>> SwarmSummoned;
    public event Action<FortressTeam, Vector3> EnergyCellCollected;
    public event Action<EnergyCellPickup> EnergyCellSpawned;

    private void Awake()
    {
        if (duelManager == null)
            duelManager = FindFirstObjectByType<FortressDuelManager>();
    }

    private void Start()
    {
        if (MatchSessionContext.Type != MatchType.HumanOnline) EvaluateCombatState(true);
    }

    private void Update()
    {
        if (MatchSessionContext.Type == MatchType.HumanOnline) return;
        EvaluateCombatState(false);
        CleanupNullEntries();

        if (!combatActive)
            return;

        UpdateEnergyCellSpawning();
        UpdateRedAutomaticSummon();
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            CleanupRuntimeObjects();
    }

    private void OnValidate()
    {
        maximumCharge = Mathf.Max(1, maximumCharge);
        chargePerCell = Mathf.Clamp(chargePerCell, 1, maximumCharge);
        botsPerSwarm = Mathf.Max(1, botsPerSwarm);

        if (maximumSpawnInterval < minimumSpawnInterval)
            maximumSpawnInterval = minimumSpawnInterval;
    }

    public void ConfigureSceneReferences(
        FortressDuelManager matchManager,
        Collider spawnArea,
        Transform blueHeistRoot,
        Transform redHeistRoot,
        Transform blueSpawn = null,
        Transform redSpawn = null)
    {
        duelManager = matchManager;
        energyCellSpawnArea = spawnArea;
        blueHeist = blueHeistRoot;
        redHeist = redHeistRoot;
        blueSwarmSpawn = blueSpawn;
        redSwarmSpawn = redSpawn;
    }

    public void ConfigurePrefabs(
        EnergyCellPickup cellPrefab,
        SwarmBotAI botPrefab)
    {
        energyCellPrefab = cellPrefab;
        swarmBotPrefab = botPrefab;
    }

    public void Configure(
        FortressDuelManager matchManager,
        Collider spawnArea,
        Transform blueHeistRoot,
        Transform redHeistRoot,
        Transform blueSpawn,
        Transform redSpawn,
        EnergyCellPickup cellPrefab,
        SwarmBotAI botPrefab,
        Transform spawnedObjectsRoot = null)
    {
        ConfigureSceneReferences(
            matchManager,
            spawnArea,
            blueHeistRoot,
            redHeistRoot,
            blueSpawn,
            redSpawn);
        ConfigurePrefabs(cellPrefab, botPrefab);
        runtimeObjectsRoot = spawnedObjectsRoot;
    }

    public int GetCharge(FortressTeam team)
    {
        return teamCharge[TeamIndex(team)];
    }

    public float GetChargeNormalized(FortressTeam team)
    {
        return maximumCharge > 0
            ? (float)GetCharge(team) / maximumCharge
            : 0f;
    }

    public bool IsReady(FortressTeam team)
    {
        return GetCharge(team) >= maximumCharge &&
               (!requirePreviousSwarmCleared || GetActiveSwarmCount(team) == 0);
    }

    public int AddCharge(FortressTeam team, int amount)
    {
        TryAddCharge(team, amount, Vector3.zero);
        return GetCharge(team);
    }

    public int AddCharge(FortressTeam team, int amount, Vector3 collectionPoint)
    {
        TryAddCharge(team, amount, collectionPoint);
        return GetCharge(team);
    }

    public int GetActiveSwarmCount(FortressTeam team)
    {
        List<SwarmBotAI> teamBots = activeSwarmBots[TeamIndex(team)];
        teamBots.RemoveAll(bot => bot == null);
        return teamBots.Count;
    }

    public bool CanSummon(FortressTeam team)
    {
        if (!combatActive || GetCharge(team) < maximumCharge || swarmBotPrefab == null)
            return false;

        Transform enemyHeist = team == FortressTeam.Blue ? redHeist : blueHeist;
        if (enemyHeist == null)
            return false;

        return !requirePreviousSwarmCleared || GetActiveSwarmCount(team) == 0;
    }

    public bool TryAddCharge(FortressTeam team, int amount, Vector3 collectionPoint)
    {
        if (!combatActive || amount <= 0)
            return false;

        int index = TeamIndex(team);
        int previousCharge = teamCharge[index];
        if (previousCharge >= maximumCharge)
            return false;

        teamCharge[index] = Mathf.Min(maximumCharge, previousCharge + amount);
        ChargeChanged?.Invoke(team, teamCharge[index], maximumCharge);
        EnergyCellCollected?.Invoke(team, collectionPoint);
        if (team == FortressTeam.Blue) Funnel.Mark("first_ecell");
        // Reached only once the cell has actually been banked - spawning a cell,
        // approaching one or another team taking it never gets here.
        ReleaseAudio.PlayAt(ReleaseAudioCue.ECellPickup, collectionPoint);

        if (previousCharge < maximumCharge && teamCharge[index] >= maximumCharge)
        {
            SwarmReady?.Invoke(team);
            if (team == FortressTeam.Red)
                ScheduleRedSummon();
        }

        return true;
    }

    public bool TryAddCharge(FortressTeam team, int amount)
    {
        return TryAddCharge(team, amount, Vector3.zero);
    }

    public void RequestBlueSummon()
    {
        if (MatchSessionContext.Type == MatchType.HumanOnline) NetworkedMatchState.Instance?.RequestSummon();
        else TrySummon(FortressTeam.Blue);
    }

    public bool TrySummon(FortressTeam team)
    {
        if (!BotMatchPolicy.RequireBotMatch("SwarmChargeController.TrySummon", this)) return false;
        if (!CanSummon(team))
            return false;

        Transform enemyHeist = team == FortressTeam.Blue ? redHeist : blueHeist;
        Vector3 basePosition = GetSwarmSpawnPosition(team, enemyHeist);
        Vector3 forward = enemyHeist.position - basePosition;
        forward.y = 0f;
        Quaternion facing = forward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(forward.normalized)
            : Quaternion.identity;

        List<SwarmBotAI> spawnedThisCall = new List<SwarmBotAI>(botsPerSwarm);
        for (int index = 0; index < botsPerSwarm; index++)
        {
            float angle = index * 360f / Mathf.Max(1, botsPerSwarm);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) *
                             Vector3.forward * spawnFormationRadius;
            Vector3 spawnPosition = SampleNavMesh(basePosition + offset, basePosition);

            SwarmBotAI bot = Instantiate(
                swarmBotPrefab,
                spawnPosition,
                facing,
                runtimeObjectsRoot);
            bot.name = team + " Spidy " + (index + 1);
            bot.Initialize(team, enemyHeist, index, botsPerSwarm);
            bot.Removed += HandleSwarmBotRemoved;
            activeSwarmBots[TeamIndex(team)].Add(bot);
            spawnedThisCall.Add(bot);
        }

        if (spawnedThisCall.Count == 0)
            return false;

        SetCharge(team, 0, true);
        if (team == FortressTeam.Red)
            redSummonAt = float.PositiveInfinity;
        SwarmSummoned?.Invoke(team, spawnedThisCall);
        return true;
    }

    public EnergyCellPickup FindClosestEnergyCell(Vector3 position)
    {
        CleanupNullEntries();
        EnergyCellPickup closest = null;
        float closestSqrDistance = float.PositiveInfinity;

        foreach (EnergyCellPickup cell in activeCells)
        {
            if (cell == null || !cell.IsAvailable)
                continue;

            float sqrDistance = (cell.transform.position - position).sqrMagnitude;
            if (sqrDistance >= closestSqrDistance)
                continue;

            closest = cell;
            closestSqrDistance = sqrDistance;
        }

        return closest;
    }

    public EnergyCellPickup FindClosestEnergyCell(
        FortressTeam collectingTeam,
        Vector3 position)
    {
        return GetCharge(collectingTeam) < maximumCharge
            ? FindClosestEnergyCell(position)
            : null;
    }

    public EnergyCellPickup SpawnEnergyCellAt(Vector3 worldPosition)
    {
        if (energyCellPrefab == null)
            return null;

        CleanupNullEntries();
        if (activeCells.Count > 0)
            return activeCells[0];

        EnergyCellPickup cell = Instantiate(
            energyCellPrefab,
            worldPosition,
            Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f),
            runtimeObjectsRoot);
        cell.name = "E-Cell";
        cell.Initialize(this, chargePerCell);
        activeCells.Add(cell);
        EnergyCellSpawned?.Invoke(cell);
        return cell;
    }

    public void NotifyEnergyCellRemoved(EnergyCellPickup cell)
    {
        if (cell == null || !activeCells.Remove(cell))
            return;

        // A replacement is earned only by collection. Match cleanup and any
        // external destruction must not silently create another objective.
        if (combatActive && cell.WasCollected)
        {
            nextCellSpawnTime = Time.time + UnityEngine.Random.Range(
                minimumSpawnInterval,
                maximumSpawnInterval);
        }
    }

    private void EvaluateCombatState(bool force)
    {
        bool shouldBeActive = duelManager != null
            ? duelManager.CurrentPhase == FortressDuelPhase.Combat
            : runWithoutDuelManager;

        if (!force && shouldBeActive == combatActive)
            return;

        combatActive = shouldBeActive;
        if (combatActive)
            BeginCombat();
        else
            EndCombat();
    }

    private void BeginCombat()
    {
        CleanupRuntimeObjects();
        SetCharge(FortressTeam.Blue, 0, true);
        SetCharge(FortressTeam.Red, 0, true);
        nextCellSpawnTime = Time.time + initialSpawnDelay;
        redSummonAt = float.PositiveInfinity;
    }

    private void EndCombat()
    {
        CleanupRuntimeObjects();
        SetCharge(FortressTeam.Blue, 0, true);
        SetCharge(FortressTeam.Red, 0, true);
        redSummonAt = float.PositiveInfinity;
    }

    private void UpdateEnergyCellSpawning()
    {
        // There is exactly one contested centre objective at a time.
        if (Time.time < nextCellSpawnTime || activeCells.Count > 0)
            return;

        if (energyCellPrefab == null)
        {
            WarnAboutMissingReference("Energy-cell prefab is not assigned.");
            nextCellSpawnTime = Time.time + maximumSpawnInterval;
            return;
        }

        if (TryFindEnergyCellSpawnPoint(out Vector3 position))
        {
            SpawnEnergyCellAt(position + Vector3.up * pickupHeight);
            // Do not run another timer while this cell is active. Collection
            // starts the next replacement delay in NotifyEnergyCellRemoved.
            nextCellSpawnTime = float.PositiveInfinity;
        }
        else
        {
            // NavMesh may still be rebuilding when combat begins. Retry without
            // falling back to a random edge spawn.
            nextCellSpawnTime = Time.time + 0.5f;
        }
    }

    private bool TryFindEnergyCellSpawnPoint(out Vector3 position)
    {
        Bounds bounds = energyCellSpawnArea != null
            ? energyCellSpawnArea.bounds
            : new Bounds(
                fallbackArenaCentre,
                new Vector3(fallbackArenaSize.x, 1f, fallbackArenaSize.y));

        float minX = bounds.min.x + arenaEdgePadding;
        float maxX = bounds.max.x - arenaEdgePadding;
        float minZ = bounds.min.z + arenaEdgePadding;
        float maxZ = bounds.max.z - arenaEdgePadding;
        if (minX > maxX || minZ > maxZ)
        {
            position = bounds.center;
            return false;
        }

        Vector3 arenaCentre = bounds.center;
        arenaCentre.y = bounds.max.y + 0.1f;

        // Start at the exact centre. If centre geometry blocks that point, walk
        // a compact deterministic spiral so the pickup remains visibly central
        // and equally contestable instead of jumping to a random side lane.
        int attempts = Mathf.Max(1, spawnPositionAttempts);
        float centralSearchRadius = Mathf.Min(
            4f,
            Mathf.Max(0f, Mathf.Min(bounds.extents.x, bounds.extents.z) - arenaEdgePadding));

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float normalizedStep = attempts > 1
                ? (float)attempt / (attempts - 1)
                : 0f;
            float radius = centralSearchRadius * Mathf.Sqrt(normalizedStep);
            float angle = attempt * 137.50776f * Mathf.Deg2Rad;
            Vector3 candidate = arenaCentre + new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);

            if (!NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit navHit,
                    navMeshSampleRadius,
                    NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 sampled = navHit.position;
            if (sampled.x < minX || sampled.x > maxX || sampled.z < minZ || sampled.z > maxZ ||
                IsTooCloseToHeist(sampled) ||
                IsSpawnObstructed(sampled) || !IsReachableByBothTeams(sampled))
            {
                continue;
            }

            position = sampled;
            return true;
        }

        position = bounds.center;
        return false;
    }

    private bool IsTooCloseToHeist(Vector3 position)
    {
        return IsWithinFlatDistance(position, blueHeist, minimumHeistDistance) ||
               IsWithinFlatDistance(position, redHeist, minimumHeistDistance);
    }

    private bool IsTooCloseToCell(Vector3 position)
    {
        float minimumSqrDistance = minimumCellSpacing * minimumCellSpacing;
        foreach (EnergyCellPickup cell in activeCells)
        {
            if (cell != null &&
                FlatSqrDistance(position, cell.transform.position) < minimumSqrDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsSpawnObstructed(Vector3 position)
    {
        Collider[] overlaps = Physics.OverlapSphere(
            position + Vector3.up * (obstacleClearanceRadius + 0.08f),
            obstacleClearanceRadius,
            spawnBlockingMask,
            QueryTriggerInteraction.Ignore);

        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || overlap == energyCellSpawnArea ||
                (energyCellSpawnArea != null &&
                 overlap.transform.IsChildOf(energyCellSpawnArea.transform)))
            {
                continue;
            }

            // A sampled NavMesh point sits on its supporting floor. Ignore any
            // collider that ends at floor level so the arena floor (including
            // imported floor meshes) cannot reject every otherwise valid cell.
            if (overlap.bounds.max.y <= position.y + 0.15f)
                continue;

            return true;
        }

        return false;
    }

    private bool IsReachableByBothTeams(Vector3 destination)
    {
        Transform blueOrigin = blueSwarmSpawn != null ? blueSwarmSpawn : blueHeist;
        Transform redOrigin = redSwarmSpawn != null ? redSwarmSpawn : redHeist;
        return IsReachable(blueOrigin, destination) && IsReachable(redOrigin, destination);
    }

    private bool IsReachable(Transform origin, Vector3 destination)
    {
        if (origin == null)
            return true;

        if (!NavMesh.SamplePosition(
                origin.position,
                out NavMeshHit originHit,
                navMeshSampleRadius * 2f,
                NavMesh.AllAreas))
        {
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        return NavMesh.CalculatePath(
                   originHit.position,
                   destination,
                   NavMesh.AllAreas,
                   path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }

    private void UpdateRedAutomaticSummon()
    {
        if (!CanSummon(FortressTeam.Red))
        {
            if (GetCharge(FortressTeam.Red) < maximumCharge)
                redSummonAt = float.PositiveInfinity;
            return;
        }

        if (float.IsPositiveInfinity(redSummonAt))
            ScheduleRedSummon();

        if (Time.time < redSummonAt)
            return;

        if (!TrySummon(FortressTeam.Red))
            redSummonAt = Time.time + 1f;
    }

    private void ScheduleRedSummon()
    {
        redSummonAt = Time.time + redAutomaticSummonDelay;
    }

    private Vector3 GetSwarmSpawnPosition(FortressTeam team, Transform enemyHeist)
    {
        Transform explicitSpawn = team == FortressTeam.Blue
            ? blueSwarmSpawn
            : redSwarmSpawn;
        if (explicitSpawn != null)
            return SampleNavMesh(explicitSpawn.position, explicitSpawn.position);

        Transform ownHeist = team == FortressTeam.Blue ? blueHeist : redHeist;
        if (ownHeist == null)
            return SampleNavMesh(transform.position, transform.position);

        Vector3 direction = enemyHeist.position - ownHeist.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
            direction = ownHeist.forward;
        Vector3 desired = ownHeist.position +
                          direction.normalized * fallbackSpawnDistanceFromHeist;
        return SampleNavMesh(desired, ownHeist.position);
    }

    private Vector3 SampleNavMesh(Vector3 desired, Vector3 fallback)
    {
        return NavMesh.SamplePosition(
            desired,
            out NavMeshHit navHit,
            navMeshSampleRadius * 2f,
            NavMesh.AllAreas)
            ? navHit.position
            : fallback;
    }

    private void HandleSwarmBotRemoved(SwarmBotAI removedBot)
    {
        if (removedBot == null)
            return;

        removedBot.Removed -= HandleSwarmBotRemoved;
        activeSwarmBots[TeamIndex(removedBot.Team)].Remove(removedBot);
    }

    private void CleanupNullEntries()
    {
        activeCells.RemoveAll(cell => cell == null);
        activeSwarmBots[0].RemoveAll(bot => bot == null);
        activeSwarmBots[1].RemoveAll(bot => bot == null);
    }

    private void CleanupRuntimeObjects()
    {
        EnergyCellPickup[] cells = activeCells.ToArray();
        activeCells.Clear();
        foreach (EnergyCellPickup cell in cells)
        {
            if (cell != null)
                Destroy(cell.gameObject);
        }

        for (int teamIndex = 0; teamIndex < activeSwarmBots.Length; teamIndex++)
        {
            SwarmBotAI[] bots = activeSwarmBots[teamIndex].ToArray();
            activeSwarmBots[teamIndex].Clear();
            foreach (SwarmBotAI bot in bots)
            {
                if (bot == null)
                    continue;
                bot.Removed -= HandleSwarmBotRemoved;
                Destroy(bot.gameObject);
            }
        }
    }

    private void SetCharge(FortressTeam team, int amount, bool notify)
    {
        int index = TeamIndex(team);
        teamCharge[index] = Mathf.Clamp(amount, 0, maximumCharge);
        if (notify)
            ChargeChanged?.Invoke(team, teamCharge[index], maximumCharge);
    }

    private void WarnAboutMissingReference(string message)
    {
        if (Time.time < nextMissingReferenceWarningTime)
            return;

        Debug.LogWarning(message, this);
        nextMissingReferenceWarningTime = Time.time + 5f;
    }

    private static int TeamIndex(FortressTeam team)
    {
        return team == FortressTeam.Red ? 1 : 0;
    }

    private static bool IsWithinFlatDistance(
        Vector3 position,
        Transform target,
        float distance)
    {
        return target != null &&
               FlatSqrDistance(position, target.position) < distance * distance;
    }

    private static float FlatSqrDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return (first - second).sqrMagnitude;
    }
}
