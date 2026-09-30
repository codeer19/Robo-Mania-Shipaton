using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-250)]
public class ArenaCombatSetup : MonoBehaviour
{
    [Header("Imported Map Collision")]
    [SerializeField] private float minimumObstacleHeight = 0.3f;
    [SerializeField] private float minimumObstacleWidth = 0.45f;
    [SerializeField] private float walkableFloorHeight = 0f;
    [SerializeField, Min(0f)] private float maximumObstacleBaseHeight = 0.65f;

    [Header("Arena Presentation Cleanup")]
    [SerializeField] private bool levelRaisedSideLanes = true;
    [SerializeField] private float importedFloorSurfaceHeight = 0.34f;
    [SerializeField] private bool hideBrokenCraneAssemblies = true;
    [SerializeField] private Vector2 playableHalfExtents = new Vector2(25f, 35f);

    [Header("Symmetric Arena Composition")]
    [SerializeField] private bool enforceSymmetricCoverLayout = true;
    [SerializeField] private bool mirrorExteriorMachinery = true;
    [SerializeField, Min(-0.25f)] private float arenaCoverGroundHeight = 0.02f;

    [Header("Robot Hitboxes")]
    [SerializeField] private float robotHitboxRadius = 0.85f;
    [SerializeField] private float robotHitboxHeight = 3.1f;
    [SerializeField] private float robotHitboxCentreHeight = 1.45f;

    [Header("Turret Visual")]
    // Imported FBX files are represented as Prefab assets in Unity 6, rather
    // than direct GameObject assets. Keep this reference generic so Unity can
    // deserialize either form safely.
    [SerializeField] private Object turretVisualPrefab;
    [SerializeField, Min(1f)] private float turretTargetFootprint = 2.8f;
    [SerializeField, Range(0.5f, 1.5f)] private float turretVisualScale = 1f;

    private readonly HashSet<MeshFilter> configuredMapMeshes = new HashSet<MeshFilter>();
    private readonly HashSet<Collider> configuredGameplayObstacles = new HashSet<Collider>();
    private bool importedPresentationConfigured;
    private bool symmetricCoverConfigured;
    private float nextRefreshTime;
    private float settleUntilTime;

    [Tooltip("Seconds after load during which the arena keeps re-scanning for late-created geometry.")]
    [SerializeField, Min(0f)] private float settleDuration = 5f;

    // Six authored slots on the blue half are rotated 180 degrees onto the red
    // half, so both teams read an identical set of routes.
    //
    // The layout resolves the arena into three lanes and gives the long
    // north/south sightline two breaks per half:
    //   * centre pocket (z +-6)      contests the objective without sealing it;
    //                                the ~6m gap between the pair is the direct
    //                                but exposed route through the middle.
    //   * lane separator (z +-14)    long axis runs along Z, which is what makes
    //                                the centre and the flank read as separate
    //                                routes instead of one open field.
    //   * spawn approach (z +-21)    immediate cover on respawn, offset from the
    //                                spawn point so stepping sideways breaks
    //                                line of sight from mid.
    private static readonly Vector3[] SymmetricCoverPositions =
    {
        new Vector3(-5f, 0f, -6f),
        new Vector3(5f, 0f, -6f),
        new Vector3(-12.5f, 0f, -14f),
        new Vector3(12.5f, 0f, -14f),
        new Vector3(-6f, 0f, -21f),
        new Vector3(6f, 0f, -21f),
        new Vector3(5f, 0f, 6f),
        new Vector3(-5f, 0f, 6f),
        new Vector3(12.5f, 0f, 14f),
        new Vector3(-12.5f, 0f, 14f),
        new Vector3(6f, 0f, 21f),
        new Vector3(-6f, 0f, 21f)
    };

    private static readonly float[] SymmetricCoverRotations =
    {
        0f, 0f, 90f, 90f, 0f, 0f,
        180f, 180f, 270f, 270f, 180f, 180f
    };

    // These imported mesh fragments are the two incomplete crane assemblies.
    // Their FBX transforms place long pieces through the arena and a single
    // BoxCollider around their combined bounds creates an invisible wall.
    private static readonly HashSet<string> BrokenCranePartNames =
        new HashSet<string>
        {
            "Cylinder.020", "Cylinder.034",
            "Cylinder.036", "Cylinder.037", "Cylinder.038", "Cylinder.039",
            "Cylinder.040", "Cylinder.041", "Cylinder.042", "Cylinder.043",
            "Cylinder.044", "Cylinder.045", "Cylinder.046", "Cylinder.047",
            "Cylinder.048", "Cylinder.049", "Cylinder.050", "Cylinder.051",
            "Cylinder.074", "Cylinder.075", "Cylinder.076", "Cylinder.077",
            "Cylinder.078", "Cylinder.079", "Cylinder.080", "Cylinder.081",
            "Cylinder.082", "Cylinder.083", "Cylinder.084", "Cylinder.085",
            "Cylinder.086", "Cylinder.087", "Cylinder.088", "Cylinder.089"
        };

    private void Awake()
    {
        settleUntilTime = Time.time + settleDuration;
        RefreshArenaSetup();
    }

    /// <summary>
    /// Re-applies the visual/collision pass immediately after a buildable is
    /// spawned.  Newly placed turrets should never render the legacy mesh for
    /// a frame while the periodic safety refresh catches up.
    /// </summary>
    public void RefreshNow()
    {
        settleUntilTime = Mathf.Max(settleUntilTime, Time.time + settleDuration);
        RefreshArenaSetup();
    }

    private void Update()
    {
        // The scene settles within the first moments of the match, and every
        // buildable placement already calls RefreshNow(). Polling a full scene
        // scan for the whole match cost a visible hitch twice a second.
        if (Time.time >= settleUntilTime || Time.time < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.time + 0.5f;
        RefreshArenaSetup();
    }

    private void RefreshArenaSetup()
    {
        ConfigureImportedMapPresentation();
        ConfigureSymmetricArenaCover();
        ConfigureMapCollision();
        ConfigureGameplayObstacleNavigation();
        ConfigureRobotHitboxes();
        ConfigureTeamIndicators();
        ConfigureTurretVisuals();
    }

    private void ConfigureImportedMapPresentation()
    {
        if (importedPresentationConfigured)
            return;

        GameObject mapRoot = GameObject.Find("map1");
        if (mapRoot == null)
            return;

        foreach (Transform child in
                 mapRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child == mapRoot.transform)
                continue;

            if (hideBrokenCraneAssemblies &&
                BrokenCranePartNames.Contains(child.name))
            {
                child.gameObject.SetActive(false);
                continue;
            }

            if (!levelRaisedSideLanes ||
                (child.name != "Cube.001" && child.name != "Cube.005"))
            {
                continue;
            }

            Renderer laneRenderer = child.GetComponent<Renderer>();
            if (laneRenderer == null)
                continue;

            // Both side-lane slabs were exported about 0.285 units above the
            // centre floor.  Match their top surface to the imported floor so
            // placed turrets and robot wheels can never disappear inside it.
            float heightCorrection =
                importedFloorSurfaceHeight - laneRenderer.bounds.max.y;
            if (Mathf.Abs(heightCorrection) > 0.001f)
            {
                child.position += Vector3.up * heightCorrection;
            }
        }

        if (mirrorExteriorMachinery)
        {
            ConfigureMirroredExteriorMachinery(mapRoot.transform);
        }

        importedPresentationConfigured = true;
    }

    private void ConfigureSymmetricArenaCover()
    {
        if (!enforceSymmetricCoverLayout || symmetricCoverConfigured)
            return;

        GameObject coverRoot = GameObject.Find("FD_BreakableCover");
        if (coverRoot == null)
            return;

        ConfigureSymmetricIndustrialCover(coverRoot.transform);

        Transform themedRoot = coverRoot.transform.Find("ThemedArenaCover");
        if (themedRoot == null)
            return;

        List<Transform> covers = new List<Transform>();
        foreach (Transform child in themedRoot)
        {
            if (child != null && child.name.StartsWith("ArenaCover_Box_"))
                covers.Add(child);
        }

        covers.Sort((left, right) =>
            string.CompareOrdinal(left.name, right.name));

        if (covers.Count != SymmetricCoverPositions.Length)
        {
            Debug.LogWarning(
                "The symmetric arena layout expects exactly " +
                SymmetricCoverPositions.Length + " themed cover pieces, but found " +
                covers.Count + ". The authored layout was left untouched.",
                this);
            symmetricCoverConfigured = true;
            return;
        }

        for (int index = 0; index < covers.Count; index++)
        {
            Transform cover = covers[index];
            Vector3 slot = SymmetricCoverPositions[index];
            cover.SetPositionAndRotation(
                new Vector3(slot.x, cover.position.y, slot.z),
                Quaternion.Euler(0f, SymmetricCoverRotations[index], 0f));

            Renderer[] renderers =
                cover.GetComponentsInChildren<Renderer>(true);
            if (TryGetBounds(renderers, out Bounds bounds))
            {
                cover.position += Vector3.up *
                    (arenaCoverGroundHeight - bounds.min.y);
            }
        }

        symmetricCoverConfigured = true;
    }

    private static void ConfigureSymmetricIndustrialCover(Transform coverRoot)
    {
        if (coverRoot == null)
            return;

        // The four machines are the arena's mid-map landmarks. Moved out to the
        // flank corridors they give each side route a chunky silhouette to read
        // against, and split that route into a tight inside squeeze and a wider
        // outside run instead of leaving the map edge as empty floor.
        MirrorGameplayPair(
            coverRoot, "machine", "machine (2)",
            new Vector3(19f, 0f, -8f));
        MirrorGameplayPair(
            coverRoot, "machine (1)", "machine (3)",
            new Vector3(-19f, 0f, -8f));
    }

    private static void MirrorGameplayPair(
        Transform parent,
        string firstName,
        string secondName,
        Vector3 firstFlatPosition)
    {
        Transform first = parent.Find(firstName);
        Transform second = parent.Find(secondName);
        if (first == null || second == null)
            return;

        first.position = new Vector3(
            firstFlatPosition.x,
            first.position.y,
            firstFlatPosition.z);
        second.position = new Vector3(
            -firstFlatPosition.x,
            first.position.y,
            -firstFlatPosition.z);
        second.rotation = Quaternion.AngleAxis(180f, Vector3.up) *
                          first.rotation;
    }

    private static void ConfigureMirroredExteriorMachinery(Transform mapRoot)
    {
        if (mapRoot == null)
            return;

        // The imported scene placed these matching prop pairs at unrelated
        // corners.  Pairing them through a 180-degree arena rotation keeps the
        // visible world beyond the walls lively without visually favouring a
        // team or creating a random scatter of silhouettes.
        MirrorExteriorPair(
            mapRoot, "mesh.001", "mesh.002",
            new Vector3(-29f, 3.18f, -26f));
        MirrorExteriorPair(
            mapRoot, "mesh.003", "mesh.013",
            new Vector3(29f, 4.12f, -15f));
        MirrorExteriorPair(
            mapRoot, "Cylinder.005", "Cylinder.012",
            new Vector3(30f, -1.38f, -28f));

        // These three renderers form one small industrial assembly on each
        // side.  Keep both assemblies centred along the neutral midline.
        MirrorExteriorPair(
            mapRoot, "Cylinder.002", "Cylinder.003",
            new Vector3(-27f, 3.13f, 0f));
        MirrorExteriorPair(
            mapRoot, "Cylinder.008", "Cylinder.013",
            new Vector3(-27f, 3.13f, 0f));
        MirrorExteriorPair(
            mapRoot, "Cylinder.011", "Cylinder.016",
            new Vector3(-27f, 3.13f, 0f));

        // The north-only pipe pair had no matching geometry on the blue side
        // and dominated the establishing shot.  The neutral side-pipe pair is
        // retained, so the exterior remains dressed but compositionally fair.
        SetChildActive(mapRoot, "tube_corner.002", false);
        SetChildActive(mapRoot, "tube_corner.004", false);
    }

    private static void MirrorExteriorPair(
        Transform mapRoot,
        string firstName,
        string secondName,
        Vector3 firstPosition)
    {
        Transform first = mapRoot.Find(firstName);
        Transform second = mapRoot.Find(secondName);
        if (first == null || second == null)
            return;

        first.gameObject.SetActive(true);
        second.gameObject.SetActive(true);
        first.position = firstPosition;
        second.position = new Vector3(
            -firstPosition.x,
            firstPosition.y,
            -firstPosition.z);
        second.rotation = Quaternion.AngleAxis(180f, Vector3.up) *
                          first.rotation;
    }

    private static void SetChildActive(
        Transform root,
        string childName,
        bool isActive)
    {
        Transform child = root.Find(childName);
        if (child != null)
            child.gameObject.SetActive(isActive);
    }

    private void ConfigureMapCollision()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != "map" && root.name != "map1")
            {
                continue;
            }

            foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer meshRenderer = meshFilter != null
                    ? meshFilter.GetComponent<Renderer>()
                    : null;
                Bounds worldBounds = meshFilter != null &&
                                     meshFilter.sharedMesh != null
                    ? TransformBounds(
                        meshFilter.sharedMesh.bounds,
                        meshFilter.transform)
                    : default;

                if (meshFilter == null ||
                    meshFilter.sharedMesh == null ||
                    configuredMapMeshes.Contains(meshFilter) ||
                    meshRenderer == null ||
                    !meshRenderer.enabled ||
                    !meshRenderer.gameObject.activeInHierarchy ||
                    !ShouldBlockMovement(meshFilter.gameObject, worldBounds))
                {
                    continue;
                }

                AddMapObstacle(meshFilter);
                configuredMapMeshes.Add(meshFilter);
            }
        }
    }

    private bool ShouldBlockMovement(GameObject mapPiece, Bounds meshBounds)
    {
        float horizontalWidth = Mathf.Max(meshBounds.size.x, meshBounds.size.z);
        bool reachesWalkableFloor =
            meshBounds.min.y <= walkableFloorHeight + maximumObstacleBaseHeight &&
            meshBounds.max.y >= walkableFloorHeight + 0.05f;
        bool centredOutsideArena =
            Mathf.Abs(meshBounds.center.x) >= playableHalfExtents.x ||
            Mathf.Abs(meshBounds.center.z) >= playableHalfExtents.y;

        if (mapPiece == null || BrokenCranePartNames.Contains(mapPiece.name))
            return false;

        // Skip flat floor/base slabs, hidden meshes and overhead decorations.
        // Imported exterior machinery must not receive an AABB collider: its
        // sparse mesh bounds can extend several metres back into the arena and
        // behave like an invisible wall.  Authored gameplay walls and cover
        // have their own colliders outside of this import pass.
        return meshBounds.size.y >= minimumObstacleHeight &&
               horizontalWidth >= minimumObstacleWidth &&
               reachesWalkableFloor &&
               !centredOutsideArena &&
               !(meshBounds.size.x > 30f && meshBounds.size.z > 30f);
    }

    private void ConfigureGameplayObstacleNavigation()
    {
        ArenaBoundaryEnforcer boundary = ArenaBoundaryEnforcer.Active;

        foreach (Collider collider in
                 FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (!IsGameplayObstacle(collider))
                continue;

            if (!configuredGameplayObstacles.Contains(collider))
            {
                AddNavigationObstacle(collider);
                configuredGameplayObstacles.Add(collider);
            }

            // Small props (the flower pots) stop robots with their own collider. The
            // enforcer pads obstacles by a robot's whole arm reach, which around a
            // pot would read as a wide invisible wall, so they are left out of it.
            if (boundary != null && collider.GetComponentInParent<ArenaObstacle>() == null)
            {
                boundary.RegisterObstacle(collider);
            }
        }
    }

    private static bool IsGameplayObstacle(Collider collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger ||
            !collider.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (collider.GetComponentInParent<CharacterController>() != null ||
            collider.GetComponentInParent<NavMeshAgent>() != null ||
            collider.GetComponentInParent<BasicProjectile>() != null)
        {
            return false;
        }

        FortressTarget target = collider.GetComponentInParent<FortressTarget>();
        if (target != null)
        {
            return target.TargetType != FortressTargetType.Robot;
        }

        // Solid scenery props (the flower pots) block and are pathed around like cover.
        if (collider.GetComponentInParent<ArenaObstacle>() != null)
            return true;

        Transform current = collider.transform;
        while (current != null)
        {
            if (current.name == "FD_BreakableCover")
                return true;

            current = current.parent;
        }

        return false;
    }

    private static void AddNavigationObstacle(Collider sourceCollider)
    {
        NavMeshObstacle obstacle = sourceCollider.GetComponent<NavMeshObstacle>();
        if (obstacle == null)
        {
            obstacle = sourceCollider.gameObject.AddComponent<NavMeshObstacle>();
        }

        obstacle.shape = NavMeshObstacleShape.Box;

        if (sourceCollider is BoxCollider boxCollider)
        {
            obstacle.center = boxCollider.center;
            obstacle.size = boxCollider.size;
        }
        else
        {
            Bounds worldBounds = sourceCollider.bounds;
            obstacle.center = sourceCollider.transform.InverseTransformPoint(
                worldBounds.center);
            Vector3 scale = sourceCollider.transform.lossyScale;
            obstacle.size = new Vector3(
                worldBounds.size.x / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
                worldBounds.size.y / Mathf.Max(0.001f, Mathf.Abs(scale.y)),
                worldBounds.size.z / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
        }

        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
    }

    private static Bounds TransformBounds(Bounds localBounds, Transform source)
    {
        Vector3 worldCentre = source.TransformPoint(localBounds.center);
        Vector3 localExtents = localBounds.extents;
        Vector3 axisX = source.TransformVector(localExtents.x, 0f, 0f);
        Vector3 axisY = source.TransformVector(0f, localExtents.y, 0f);
        Vector3 axisZ = source.TransformVector(0f, 0f, localExtents.z);

        Vector3 worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

        return new Bounds(worldCentre, worldExtents * 2f);
    }

    private void AddMapObstacle(MeshFilter meshFilter)
    {
        GameObject mapPiece = meshFilter.gameObject;

        Bounds bounds = meshFilter.sharedMesh.bounds;
        if (mapPiece.GetComponent<Collider>() == null)
        {
            BoxCollider collider = mapPiece.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            collider.isTrigger = false;
        }

        NavMeshObstacle obstacle = mapPiece.GetComponent<NavMeshObstacle>();

        if (obstacle == null)
        {
            obstacle = mapPiece.AddComponent<NavMeshObstacle>();
        }

        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = bounds.center;
        obstacle.size = bounds.size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
    }

    private void ConfigureRobotHitboxes()
    {
        foreach (FortressTarget target in
                 FindObjectsByType<FortressTarget>(FindObjectsSortMode.None))
        {
            if (target == null ||
                target.TargetType != FortressTargetType.Robot ||
                target.GetComponent<Damageable>() == null)
            {
                continue;
            }

            if (target.transform.Find("ProjectileHitbox") != null)
            {
                continue;
            }

            GameObject hitboxObject = new GameObject("ProjectileHitbox");
            hitboxObject.layer = target.gameObject.layer;
            hitboxObject.transform.SetParent(target.transform, false);
            hitboxObject.transform.localPosition = Vector3.zero;
            hitboxObject.transform.localRotation = Quaternion.identity;

            CapsuleCollider hitbox = hitboxObject.AddComponent<CapsuleCollider>();
            hitbox.isTrigger = true;
            hitbox.direction = 1;
            hitbox.radius = robotHitboxRadius;
            hitbox.height = Mathf.Max(robotHitboxHeight, robotHitboxRadius * 2f);
            hitbox.center = new Vector3(0f, robotHitboxCentreHeight, 0f);
        }
    }

    private void ConfigureTeamIndicators()
    {
        foreach (FortressTarget target in
                 FindObjectsByType<FortressTarget>(FindObjectsSortMode.None))
        {
            if (target == null ||
                !TeamGroundRing.AllowsMarker(target) ||
                target.GetComponent<SwarmBotAI>() != null ||
                target.GetComponent<TeamGroundRing>() != null)
            {
                continue;
            }

            float radius = 1.15f;
            if (target.GetComponent<AutoTurret>() != null)
            {
                radius = turretTargetFootprint * 0.5f + 0.14f;
            }
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);

            if (target.GetComponent<AutoTurret>() == null &&
                TryGetBounds(renderers, out Bounds visualBounds))
            {
                radius = Mathf.Clamp(
                    Mathf.Max(visualBounds.extents.x, visualBounds.extents.z) + 0.16f,
                    0.75f,
                    2.5f);
            }

            TeamGroundRing ring = target.gameObject.AddComponent<TeamGroundRing>();
            ring.Configure(target, radius, 0.045f);
        }
    }

    private void ConfigureTurretVisuals()
    {
        if (turretVisualPrefab == null)
        {
            return;
        }

        foreach (AutoTurret turret in FindObjectsByType<AutoTurret>(FindObjectsSortMode.None))
        {
            if (turret == null || HasDescendantNamed(turret.transform, "TurretVisual_FBX"))
            {
                continue;
            }

            AttachTurretVisual(turret);
        }
    }

    private static bool HasDescendantNamed(Transform root, string objectName)
    {
        if (root == null)
            return false;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == objectName)
                return true;
        }

        return false;
    }

    private void AttachTurretVisual(AutoTurret turret)
    {
        Renderer[] oldRenderers = turret.GetComponentsInChildren<Renderer>(true);

        if (!TryGetBounds(oldRenderers, out Bounds oldBounds))
        {
            return;
        }

        Object spawnedAsset = Instantiate(turretVisualPrefab, turret.transform);
        GameObject visual = spawnedAsset as GameObject;

        if (visual == null)
        {
            Component component = spawnedAsset as Component;
            visual = component != null ? component.gameObject : null;
        }

        if (visual == null)
        {
            Destroy(spawnedAsset);
            Debug.LogWarning("Turret visual asset did not create a GameObject.", this);
            return;
        }

        visual.name = "TurretVisual_FBX";
        visual.SetActive(true);
        visual.transform.localPosition = Vector3.zero;
        // The imported cannon is authored along local +X, while Unity gameplay
        // aiming uses +Z as forward. Rotate the visual once so the barrel and
        // projectile direction agree.
        visual.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        visual.transform.localScale = Vector3.one;

        Renderer[] newRenderers = visual.GetComponentsInChildren<Renderer>(true);

        if (!TryGetBounds(newRenderers, out Bounds newBounds))
        {
            Destroy(visual);
            return;
        }

        float sourceFootprint = Mathf.Max(newBounds.size.x, newBounds.size.z);
        float horizontalScale =
            turretTargetFootprint /
            Mathf.Max(0.01f, sourceFootprint) *
            turretVisualScale;

        visual.transform.localScale = Vector3.one * horizontalScale;

        TryGetBounds(newRenderers, out newBounds);
        visual.transform.position += new Vector3(
            oldBounds.center.x - newBounds.center.x,
            oldBounds.min.y - newBounds.min.y,
            oldBounds.center.z - newBounds.center.z
        );

        TryGetBounds(newRenderers, out newBounds);
        AlignTurretMuzzle(turret, visual.transform, newBounds);

        foreach (Renderer oldRenderer in oldRenderers)
        {
            if (oldRenderer != null)
            {
                oldRenderer.enabled = false;
            }
        }
    }

    private void AlignTurretMuzzle(
        AutoTurret turret,
        Transform visual,
        Bounds visualBounds)
    {
        Transform muzzle = turret.Muzzle;

        if (muzzle == null)
            return;

        float forwardReach = Mathf.Max(
            visualBounds.extents.x,
            visualBounds.extents.z
        ) * 0.92f;

        muzzle.position =
            visualBounds.center +
            turret.transform.forward * forwardReach +
            Vector3.up * (visualBounds.extents.y * 0.28f);
        muzzle.rotation = turret.transform.rotation;
    }

    private bool TryGetBounds(Renderer[] renderers, out Bounds combinedBounds)
    {
        combinedBounds = default;
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }
}
