using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One command that runs every arena check that previously had to be done by
/// hand: rotational symmetry, route widths, prop height against the gameplay
/// camera, floor grounding, and navigation/spawn balance.
///
/// Run it before and after any layout change. It only reads the scene.
/// </summary>
public static class ArenaValidator
{
    private const float ProbeHeight = 1.2f;
    private const float RobotRadius = 1.0f;
    private const float FloorRejectHeight = 0.30f;

    /// <summary>
    /// How far a shot may travel down a lane without anything interrupting it.
    /// Sized so crossing the arena always means passing at least three separate
    /// pieces of cover rather than one clear run.
    /// </summary>
    private const float MaximumSightline = 16f;

    private class Issue
    {
        public string Category;
        public string Message;
        public Object Context;
    }

    [MenuItem("Robo Mania/Arena/Validate Arena %#v")]
    public static void Validate()
    {
        ArenaLayoutDefinition layout = FindLayout();
        float floorTop = layout != null ? layout.floorTopHeight : 0.341f;
        float maxHeight = layout != null ? layout.maximumPropHeight : 3.7f;
        float minRoute = layout != null ? layout.minimumRouteWidth : 2.4f;
        Vector2 half = layout != null ? layout.playableHalfExtents : new Vector2(25f, 35f);

        Physics.SyncTransforms();

        List<Issue> issues = new List<Issue>();
        List<(Transform t, Bounds b)> blockers = CollectBlockers();

        CheckSymmetry(blockers, issues);
        CheckOrientationConsistency(issues);
        CheckHeights(blockers, maxHeight, issues);
        CheckGrounding(blockers, floorTop, issues);
        CheckRouteWidths(half, minRoute, issues);
        CheckSightlines(half, issues);
        CheckPlayfieldBounds(blockers, half, issues);
        CheckNavigation(issues);

        Report(blockers.Count, issues);
    }

    private static List<(Transform, Bounds)> CollectBlockers()
    {
        List<(Transform, Bounds)> blockers = new List<(Transform, Bounds)>();

        foreach (Collider collider in Object.FindObjectsByType<Collider>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (collider.isTrigger)
                continue;

            if (collider.GetComponentInParent<CharacterController>() != null ||
                collider.GetComponentInParent<NavMeshAgent>() != null ||
                collider.GetComponentInParent<BasicProjectile>() != null)
            {
                continue;
            }

            // Vaults, turrets and player-placed barriers are gameplay entities,
            // not level geometry. They are allowed to be tall, to be asymmetric
            // (one team may have built something the other has not) and to sit
            // at their own height, so holding them to layout rules only produces
            // false positives.
            if (collider.GetComponentInParent<Damageable>() != null)
                continue;

            Bounds bounds = collider.bounds;

            // Floors and the outer containment walls are not layout pieces.
            if (bounds.max.y <= FloorRejectHeight)
                continue;

            if (collider.name.Contains("Boundary"))
                continue;

            if (bounds.size.x > 40f || bounds.size.z > 40f)
                continue;

            blockers.Add((collider.transform, bounds));
        }

        return blockers;
    }

    private static void CheckSymmetry(
        List<(Transform t, Bounds b)> blockers, List<Issue> issues)
    {
        foreach ((Transform t, Bounds b) in blockers)
        {
            Vector3 mirrored = new Vector3(-b.center.x, 0f, -b.center.z);
            bool found = false;

            foreach ((Transform _, Bounds other) in blockers)
            {
                Vector3 flat = new Vector3(other.center.x, 0f, other.center.z);

                if (Vector3.Distance(flat, mirrored) < 0.8f)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                issues.Add(new Issue
                {
                    Category = "SYMMETRY",
                    Message = $"'{t.name}' at {b.center:F1} has no 180-degree counterpart. " +
                              "One team has cover the other does not.",
                    Context = t
                });
            }
        }
    }

    /// <summary>
    /// Flags instances of the same prefab that are presented on different axes.
    ///
    /// A 180-degree mirror keeps an asset's detailing pointing the same way; a
    /// 90-degree turn does not. When only some copies are turned, the authored
    /// accent lands on the top face for most pieces and on a side face for the
    /// rest, which is the "wrong rotation" that pure symmetry and route checks
    /// cannot see.
    /// </summary>
    private static void CheckOrientationConsistency(List<Issue> issues)
    {
        GameObject root = GameObject.Find(ArenaLayoutBuilder.GeneratedRootName);

        if (root == null)
            return;

        Dictionary<Object, List<Transform>> byPrefab =
            new Dictionary<Object, List<Transform>>();

        foreach (Transform child in root.transform)
        {
            Object source =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(child.gameObject);

            if (source == null)
                continue;

            if (!byPrefab.TryGetValue(source, out List<Transform> list))
            {
                list = new List<Transform>();
                byPrefab[source] = list;
            }

            list.Add(child);
        }

        foreach (KeyValuePair<Object, List<Transform>> entry in byPrefab)
        {
            if (entry.Value.Count < 2)
                continue;

            // Group by quarter turn: 0/180 are one presentation, 90/270 the other.
            List<Transform> onAxis = new List<Transform>();
            List<Transform> turned = new List<Transform>();

            foreach (Transform piece in entry.Value)
            {
                float yaw = Mathf.Repeat(piece.eulerAngles.y, 180f);
                bool isTurned = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 45f;

                if (isTurned)
                    turned.Add(piece);
                else
                    onAxis.Add(piece);
            }

            if (onAxis.Count == 0 || turned.Count == 0)
                continue;

            List<Transform> minority = turned.Count <= onAxis.Count ? turned : onAxis;

            foreach (Transform piece in minority)
            {
                issues.Add(new Issue
                {
                    Category = "ORIENTATION",
                    Message =
                        $"'{piece.name}' uses prefab '{entry.Key.name}' turned 90 degrees " +
                        $"while {(minority == turned ? onAxis.Count : turned.Count)} other " +
                        "copies are not. The asset's detailing will face a different way " +
                        "on these pieces.",
                    Context = piece
                });
            }
        }
    }

    private static void CheckHeights(
        List<(Transform t, Bounds b)> blockers, float maxHeight, List<Issue> issues)
    {
        foreach ((Transform t, Bounds b) in blockers)
        {
            if (b.size.y > maxHeight + 0.05f)
            {
                issues.Add(new Issue
                {
                    Category = "READABILITY",
                    Message = $"'{t.name}' is {b.size.y:F2}m tall (budget {maxHeight:F2}m). " +
                              "Tall props hide robots at the gameplay camera pitch.",
                    Context = t
                });
            }
        }
    }

    private static void CheckGrounding(
        List<(Transform t, Bounds b)> blockers, float floorTop, List<Issue> issues)
    {
        foreach ((Transform t, Bounds b) in blockers)
        {
            float gap = b.min.y - floorTop;

            if (gap < -0.12f)
            {
                issues.Add(new Issue
                {
                    Category = "GROUNDING",
                    Message = $"'{t.name}' is sunk {-gap:F2}m into the floor " +
                              $"(floor top {floorTop:F3}).",
                    Context = t
                });
            }
            else if (gap > 0.12f)
            {
                issues.Add(new Issue
                {
                    Category = "GROUNDING",
                    Message = $"'{t.name}' floats {gap:F2}m above the floor.",
                    Context = t
                });
            }
        }
    }

    private static void CheckRouteWidths(
        Vector2 half, float minRoute, List<Issue> issues)
    {
        // Walk each lane and make sure a robot-sized capsule can actually pass.
        float[] lanes = { 0f, -6f, 6f, -12.5f, 12.5f, -19f, 19f };

        foreach (float lane in lanes)
        {
            int blockedRun = 0;
            float runStart = 0f;

            for (float z = -half.y + 3f; z <= half.y - 3f; z += 0.5f)
            {
                bool blocked = IsBlocked(new Vector3(lane, ProbeHeight, z), RobotRadius);

                if (blocked)
                {
                    if (blockedRun == 0)
                        runStart = z;

                    blockedRun++;
                }
                else
                {
                    if (blockedRun * 0.5f > 12f)
                    {
                        issues.Add(new Issue
                        {
                            Category = "ROUTE",
                            Message = $"Lane x={lane:F1} is blocked for " +
                                      $"{blockedRun * 0.5f:F1}m from z={runStart:F1}. " +
                                      "That reads as a wall, not a route.",
                            Context = null
                        });
                    }

                    blockedRun = 0;
                }
            }
        }

        // Lateral pinch check across the map at the cover rows.
        float[] rows = { -21f, -14f, -6f, 0f, 6f, 14f, 21f };

        foreach (float z in rows)
        {
            float widest = 0f;
            float current = 0f;

            for (float x = -half.x + 1f; x <= half.x - 1f; x += 0.5f)
            {
                if (IsBlocked(new Vector3(x, ProbeHeight, z), RobotRadius))
                {
                    widest = Mathf.Max(widest, current);
                    current = 0f;
                }
                else
                {
                    current += 0.5f;
                }
            }

            widest = Mathf.Max(widest, current);

            if (widest < minRoute)
            {
                issues.Add(new Issue
                {
                    Category = "ROUTE",
                    Message = $"Row z={z:F1} has no gap wider than {widest:F1}m " +
                              $"(need {minRoute:F1}m). The row is impassable.",
                    Context = null
                });
            }
        }
    }

    /// <summary>
    /// The counterpart to the route check, and the one that decides whether the
    /// arena plays like an arena or like a corridor.
    ///
    /// CheckRouteWidths only asks whether a lane is passable. A layout can pass it
    /// completely and still leave a firing line running the full length of the
    /// map, which is what happened here: the flanks were open for 70m end to end,
    /// so a robot on its own spawn could hit one on the opposite spawn and there
    /// was never a moment of breaking or regaining line of sight.
    ///
    /// Capping how far a shot can travel unobstructed is what creates that
    /// rhythm, so it is enforced rather than left to the eye.
    /// </summary>
    private static void CheckSightlines(Vector2 half, List<Issue> issues)
    {
        float[] lanes = { 0f, -6f, 6f, -12.5f, 12.5f, -19f, 19f };

        foreach (float lane in lanes)
        {
            float run = 0f;
            float longest = 0f;
            float longestEnd = 0f;

            for (float z = -half.y; z <= half.y; z += 0.5f)
            {
                // A shot is thinner than a robot, so this probes with a
                // projectile-sized radius rather than the movement radius.
                bool clear = !Physics.CheckSphere(
                    new Vector3(lane, ProbeHeight, z),
                    0.5f,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                if (clear)
                {
                    run += 0.5f;

                    if (run > longest)
                    {
                        longest = run;
                        longestEnd = z;
                    }
                }
                else
                {
                    run = 0f;
                }
            }

            if (longest > MaximumSightline)
            {
                issues.Add(new Issue
                {
                    Category = "SIGHTLINE",
                    Message = $"Lane x={lane:F1} is open for {longest:F1}m " +
                              $"(budget {MaximumSightline:F1}m), ending at " +
                              $"z={longestEnd:F1}. Nothing breaks line of sight " +
                              "along it, so the lane reads as a shooting gallery.",
                    Context = null
                });
            }
        }
    }

    private static void CheckPlayfieldBounds(
        List<(Transform t, Bounds b)> blockers, Vector2 half, List<Issue> issues)
    {
        foreach ((Transform t, Bounds b) in blockers)
        {
            if (Mathf.Abs(b.center.x) > half.x || Mathf.Abs(b.center.z) > half.y)
            {
                issues.Add(new Issue
                {
                    Category = "BOUNDS",
                    Message = $"'{t.name}' sits outside the playable rectangle at {b.center:F1}.",
                    Context = t
                });
            }
        }
    }

    private static void CheckNavigation(List<Issue> issues)
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();

        if (triangulation.vertices == null || triangulation.vertices.Length == 0)
        {
            issues.Add(new Issue
            {
                Category = "NAVIGATION",
                Message = "No NavMesh present. Enter play mode (the duel manager " +
                          "rebuilds it at combat start) or bake, then re-run.",
                Context = null
            });
            return;
        }

        Vector3 blue = new Vector3(0f, 0f, -24f);
        Vector3 red = new Vector3(0f, 0f, 24f);

        if (!TryPathLength(blue, red, out float crossing))
        {
            issues.Add(new Issue
            {
                Category = "NAVIGATION",
                Message = "No complete path from the blue spawn to the red spawn.",
                Context = null
            });
            return;
        }

        // Mirrored endpoint pairs isolate real layout bias from NavMesh
        // voxel noise at the spawn points themselves.
        (Vector3 a, Vector3 b, Vector3 ma, Vector3 mb, string label)[] pairs =
        {
            (blue, new Vector3(-21f, 0f, -5f), red, new Vector3(21f, 0f, 5f), "flank (west/east)"),
            (blue, new Vector3(21f, 0f, -5f), red, new Vector3(-21f, 0f, 5f), "flank (east/west)"),
            (blue, new Vector3(0f, 0f, 4f), red, new Vector3(0f, 0f, -4f), "centre"),
            (blue, new Vector3(0f, 0f, 30f), red, new Vector3(0f, 0f, -30f), "enemy vault")
        };

        foreach (var pair in pairs)
        {
            if (!TryPathLength(pair.a, pair.b, out float first) ||
                !TryPathLength(pair.ma, pair.mb, out float second))
            {
                issues.Add(new Issue
                {
                    Category = "NAVIGATION",
                    Message = $"Incomplete path on the {pair.label} route.",
                    Context = null
                });
                continue;
            }

            float delta = Mathf.Abs(first - second);
            float percent = delta / Mathf.Max(1f, Mathf.Max(first, second)) * 100f;

            if (percent > 8f)
            {
                issues.Add(new Issue
                {
                    Category = "BALANCE",
                    Message = $"{pair.label}: blue {first:F1}m vs red {second:F1}m " +
                              $"({percent:F1}% apart). One side reaches it faster.",
                    Context = null
                });
            }
        }

        Debug.Log($"[Arena] Spawn-to-spawn path: {crossing:F1}m.");
    }

    private static bool TryPathLength(Vector3 from, Vector3 to, out float length)
    {
        length = 0f;

        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, 5f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(to, out NavMeshHit toHit, 5f, NavMesh.AllAreas))
        {
            return false;
        }

        NavMeshPath path = new NavMeshPath();

        if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        for (int index = 1; index < path.corners.Length; index++)
            length += Vector3.Distance(path.corners[index - 1], path.corners[index]);

        return true;
    }

    private static readonly Collider[] ProbeBuffer = new Collider[32];

    private static bool IsBlocked(Vector3 point, float radius)
    {
        int count = Physics.OverlapSphereNonAlloc(
            point, radius, ProbeBuffer, ~0, QueryTriggerInteraction.Ignore);

        for (int index = 0; index < count; index++)
        {
            Collider collider = ProbeBuffer[index];

            if (collider.bounds.max.y <= FloorRejectHeight)
                continue;

            if (collider.GetComponentInParent<CharacterController>() != null ||
                collider.GetComponentInParent<NavMeshAgent>() != null)
            {
                continue;
            }

            if (collider.name.Contains("Boundary"))
                continue;

            return true;
        }

        return false;
    }

    private static ArenaLayoutDefinition FindLayout()
    {
        string[] guids = AssetDatabase.FindAssets("t:ArenaLayoutDefinition");

        return guids.Length == 0
            ? null
            : AssetDatabase.LoadAssetAtPath<ArenaLayoutDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    private static void Report(int blockerCount, List<Issue> issues)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"=== ARENA VALIDATION ===  blockers scanned: {blockerCount}");

        if (issues.Count == 0)
        {
            builder.AppendLine("PASS - no issues found.");
            Debug.Log(builder.ToString());
            return;
        }

        Dictionary<string, int> counts = new Dictionary<string, int>();

        foreach (Issue issue in issues)
        {
            counts.TryGetValue(issue.Category, out int existing);
            counts[issue.Category] = existing + 1;
        }

        foreach (KeyValuePair<string, int> entry in counts)
            builder.AppendLine($"  {entry.Key}: {entry.Value}");

        Debug.LogWarning(builder.ToString());

        foreach (Issue issue in issues)
            Debug.LogWarning($"[{issue.Category}] {issue.Message}", issue.Context);
    }
}
