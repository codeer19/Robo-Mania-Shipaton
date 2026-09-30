using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fallback bot's build phase. It plays by the player's rules: a loadout of
/// three different cards chosen from a few predefined styles, each placed once
/// (3 build points, 1 per card), and every spot checked by the
/// same placement rules the player and the online authority use (Red side only,
/// clear of spawns and bases). Placements are spread across the build phase one
/// at a time and stop the moment it closes.
/// </summary>
public class RedBuildPlanner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FortressDuelManager duelManager;
    [SerializeField] private Transform redVault;
    [SerializeField] private Transform blueVault;
    [Tooltip("Red-painted turret; every other card uses the shared prefab tinted by team.")]
    [SerializeField] private GameObject redTurretPrefab;

    [Header("Placement")]
    [SerializeField] private int fallbackAttempts = 40;

    [Header("Match Randomness")]
    [SerializeField] private int matchSeed;

    private struct BotLoadout
    {
        public string Name;
        public BuildPlacementController.BuildableType[] Cards;
    }

    private static readonly BotLoadout[] Loadouts =
    {
        new BotLoadout { Name = "Balanced", Cards = new[] { BuildPlacementController.BuildableType.Turret, BuildPlacementController.BuildableType.OverdrivePad, BuildPlacementController.BuildableType.HealingPad } },
        new BotLoadout { Name = "Defensive", Cards = new[] { BuildPlacementController.BuildableType.Turret, BuildPlacementController.BuildableType.HealingPad, BuildPlacementController.BuildableType.MissileInterceptor } },
        new BotLoadout { Name = "Aggressive", Cards = new[] { BuildPlacementController.BuildableType.PulseTower, BuildPlacementController.BuildableType.OverdrivePad, BuildPlacementController.BuildableType.RecoveryJammer } },
        new BotLoadout { Name = "Control", Cards = new[] { BuildPlacementController.BuildableType.Turret, BuildPlacementController.BuildableType.PulseTower, BuildPlacementController.BuildableType.RecoveryJammer } },
        new BotLoadout { Name = "Fortress", Cards = new[] { BuildPlacementController.BuildableType.PulseTower, BuildPlacementController.BuildableType.HealingPad, BuildPlacementController.BuildableType.MissileInterceptor } },
    };

    private readonly List<GameObject> spawnedDefences = new List<GameObject>();
    private readonly int[] placedPerCard = new int[BuildCards.All.Length];
    private BuildPlacementController builder;
    private bool wasInBuildPhase;
    private bool hasBuiltThisPhase;
    private System.Random random;

    /// <summary>The loadout chosen for this match (for tests and diagnostics).</summary>
    public BuildPlacementController.BuildableType[] CurrentLoadout { get; private set; }

    private void Awake()
    {
        if (!BotMatchPolicy.AllowActivation(this, "RedBuildPlanner.Awake")) return;
        if (duelManager == null)
        {
            duelManager = FindFirstObjectByType<FortressDuelManager>();
        }
        if (duelManager != null) builder = duelManager.GetComponent<BuildPlacementController>();
    }

    private void Update()
    {
        if (!MatchSessionContext.CanInitializeAI) return;
        bool isBuildPhase =
            duelManager != null &&
            duelManager.CurrentPhase == FortressDuelPhase.Build;

        if (isBuildPhase && !wasInBuildPhase && !hasBuiltThisPhase)
        {
            StartCoroutine(BuildDefencesRoutine());
        }

        if (!isBuildPhase)
        {
            hasBuiltThisPhase = false;
        }

        wasInBuildPhase = isBuildPhase;
    }

    private IEnumerator BuildDefencesRoutine()
    {
        if (!BotMatchPolicy.RequireBotMatch("RedBuildPlanner.BuildDefencesRoutine", this)) yield break;
        hasBuiltThisPhase = true;

        if (matchSeed == 0)
        {
            matchSeed = System.Environment.TickCount;
        }

        random = new System.Random(matchSeed);
        BotLoadout loadout = Loadouts[random.Next(Loadouts.Length)];
        CurrentLoadout = loadout.Cards;
        BuildPlacementController.BuildableType[] plan = PlanPlacements(loadout.Cards);
        Debug.Log($"[BOT BUILD] loadout={loadout.Name} ({string.Join(",", loadout.Cards)}) plan={string.Join(",", plan)} seed={matchSeed}");

        // Spread across the phase like a player picking and placing, never all on
        // the first frame and always well before the phase closes.
        float firstDelay = 1.1f + (float)random.NextDouble() * 0.6f;
        yield return new WaitForSeconds(firstDelay);
        for (int i = 0; i < plan.Length; i++)
        {
            if (duelManager == null || duelManager.CurrentPhase != FortressDuelPhase.Build) yield break;
            TryPlace(plan[i]);
            yield return new WaitForSeconds(1.4f + (float)random.NextDouble() * 1.4f);
        }
    }

    /// <summary>Each card in the loadout once, in a shuffled order.</summary>
    private BuildPlacementController.BuildableType[] PlanPlacements(BuildPlacementController.BuildableType[] cards)
    {
        var plan = (BuildPlacementController.BuildableType[])cards.Clone();
        for (int i = plan.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (plan[i], plan[j]) = (plan[j], plan[i]);
        }
        return plan;
    }

    private GameObject CardPrefab(BuildPlacementController.BuildableType type) =>
        type == BuildPlacementController.BuildableType.Turret && redTurretPrefab != null
            ? redTurretPrefab
            : builder != null ? builder.Prefab(type) : null;

    // Where each card is useful, relative to the red base and the enemy approach.
    private Vector3 DesiredPosition(BuildPlacementController.BuildableType type)
    {
        Vector3 enemy = GetEnemyDirection();
        Vector3 side = new Vector3(enemy.z, 0f, -enemy.x);
        Vector3 vault = redVault.position;
        float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        float Sign() => random.NextDouble() < 0.5 ? -1f : 1f;
        switch (type)
        {
            case BuildPlacementController.BuildableType.PulseTower:
                // On the approach, where an attacker has to stand to hit the base.
                return vault + enemy * Range(7f, 10f) + side * Range(-4f, 4f);
            case BuildPlacementController.BuildableType.HealingPad:
                // Beside the base, off the main lane.
                return vault + enemy * Range(4f, 6f) + side * Sign() * Range(5f, 8f);
            case BuildPlacementController.BuildableType.OverdrivePad:
                // On the bot's own way out of the base, so it actually drives across it.
                return vault + enemy * Range(7f, 10f) + side * Range(-2.5f, 2.5f);
            case BuildPlacementController.BuildableType.RecoveryJammer:
                return vault + enemy * Range(8f, 12f) + side * Range(-6f, 6f);
            case BuildPlacementController.BuildableType.MissileInterceptor:
                // Close enough to the base to cover shots at it.
                return vault + enemy * Range(5f, 8f) + side * Range(-5f, 5f);
            default:
                return vault + enemy * Range(6f, 12f) + side * Range(-9f, 9f);
        }
    }

    private void TryPlace(BuildPlacementController.BuildableType type)
    {
        if (!BotMatchPolicy.RequireBotMatch("RedBuildPlanner.TryPlace", this)) return;
        if (duelManager == null || builder == null || duelManager.CurrentPhase != FortressDuelPhase.Build ||
            !BuildCards.Contains(CurrentLoadout, type) || redVault == null) return;
        int placed = 0;
        foreach (int count in placedPerCard) placed += count;
        if (placed >= BuildPlacementController.BuildPointsPerRound / BuildPlacementController.BuildItemCost ||
            placedPerCard[(int)type] >= BuildCards.MaxCopies) return;
        GameObject prefab = CardPrefab(type);
        if (prefab == null) return;

        Quaternion facing = BuildPlacementController.FacingFor(TeamSide.SideB);
        if (!TryFindValidPosition(type, DesiredPosition(type), facing, out Vector3 validPosition))
        {
            Debug.LogWarning("[BOT BUILD] No legal position for " + type);
            return;
        }

        GameObject defence = Instantiate(prefab, validPosition, facing);
        defence.name = "Red" + BuildCards.DisplayName(type).Replace(" ", "");
        var identity = defence.GetComponent<FortressTarget>();
        if (identity != null) identity.SetTeam(FortressTeam.Red);
        BuildableGrounding.SnapToGround(defence, builder.PlacementY);
        spawnedDefences.Add(defence);
        placedPerCard[(int)type]++;
        ReleaseAudio.PlayAt(ReleaseAudioCue.TurretDeploy, validPosition);
        Debug.Log($"[BOT BUILD] placed {type} at {validPosition:F1}");

        if (defence.GetComponent<AutoTurret>() != null)
        {
            ArenaCombatSetup setup = FindFirstObjectByType<ArenaCombatSetup>();
            if (setup != null)
                setup.RefreshNow();
        }
    }

    private Vector3 GetEnemyDirection()
    {
        if (blueVault == null)
        {
            return Vector3.back;
        }

        Vector3 direction = blueVault.position - redVault.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
        {
            return Vector3.back;
        }

        return direction.normalized;
    }

    private bool TryFindValidPosition(BuildPlacementController.BuildableType type, Vector3 desired, Quaternion facing, out Vector3 validPosition)
    {
        validPosition = Snap(desired);
        if (IsPositionValid(type, validPosition, facing)) return true;

        Bounds bounds = builder.BuildArea(TeamSide.SideB).bounds;
        for (int i = 0; i < fallbackAttempts; i++)
        {
            Vector3 candidate = Snap(new Vector3(
                Mathf.Lerp(bounds.min.x + 2f, bounds.max.x - 2f, (float)random.NextDouble()), 0f,
                Mathf.Lerp(bounds.min.z + 2f, bounds.max.z - 2f, (float)random.NextDouble())));
            if (IsPositionValid(type, candidate, facing)) { validPosition = candidate; return true; }
        }
        return false;
    }

    private Vector3 Snap(Vector3 position)
    {
        float grid = builder.GridSize;
        position.x = Mathf.Round(position.x / grid) * grid;
        position.z = Mathf.Round(position.z / grid) * grid;
        position.y = builder.PlacementY;
        return position;
    }

    // The player's rules on the Red side, plus spacing from the bot's own defences.
    private bool IsPositionValid(BuildPlacementController.BuildableType type, Vector3 position, Quaternion facing) =>
        builder.IsLegalPlacement(TeamSide.SideB, type, position, facing, p =>
        {
            foreach (GameObject defence in spawnedDefences)
            {
                if (defence == null) continue;
                Vector3 d = defence.transform.position - p; d.y = 0f;
                if (d.magnitude < builder.MinimumSpacing) return true;
            }
            return false;
        });
}
