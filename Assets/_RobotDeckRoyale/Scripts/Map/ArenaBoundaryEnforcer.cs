using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-300)]
public class ArenaBoundaryEnforcer : MonoBehaviour
{
    [SerializeField] private Vector2 playableHalfExtents = new Vector2(25f, 35f);
    [SerializeField] private float wallThickness = 1f;
    [SerializeField] private float wallHeight = 12f;
    [SerializeField] private float wallCentreHeight = 6f;
    [SerializeField, Min(0.05f)] private float containmentMargin = 0.12f;

    [Header("Octagonal Corner Cuts")]
    [Tooltip("Outward normal of the arena floor's diagonal corner edge, in XZ. " +
             "Leave the limit at 0 for a plain rectangular arena.")]
    [SerializeField] private Vector2 cornerEdgeNormal = new Vector2(0.8135f, 0.5815f);
    [Tooltip("Distance from the arena centre to that diagonal edge. 0 disables corner clipping.")]
    [SerializeField, Min(0f)] private float cornerEdgeLimit;

    public static ArenaBoundaryEnforcer Active { get; private set; }

    public Vector2 PlayableHalfExtents => playableHalfExtents;

    private readonly Dictionary<Transform, float> footprintCache =
        new Dictionary<Transform, float>();
    private readonly HashSet<Collider> registeredObstacles =
        new HashSet<Collider>();

    // Scanning the whole scene for movers every frame was the single most
    // expensive thing in the movement path. The set of robots changes only when
    // one spawns or despawns, so it is refreshed on a slow cadence instead.
    private const float MoverRefreshInterval = 1f;
    private CharacterController[] cachedControllers = new CharacterController[0];
    private NavMeshAgent[] cachedAgents = new NavMeshAgent[0];
    private float nextMoverRefreshTime;

    private void Awake()
    {
        Active = this;
        wallCentreHeight = Mathf.Max(wallHeight * 0.5f, wallCentreHeight);
        BuildBoundary("North Boundary", new Vector3(0f, wallCentreHeight, playableHalfExtents.y + wallThickness * 0.5f),
            new Vector3(playableHalfExtents.x * 2f + wallThickness * 2f, wallHeight, wallThickness));
        BuildBoundary("South Boundary", new Vector3(0f, wallCentreHeight, -playableHalfExtents.y - wallThickness * 0.5f),
            new Vector3(playableHalfExtents.x * 2f + wallThickness * 2f, wallHeight, wallThickness));
        BuildBoundary("East Boundary", new Vector3(playableHalfExtents.x + wallThickness * 0.5f, wallCentreHeight, 0f),
            new Vector3(wallThickness, wallHeight, playableHalfExtents.y * 2f));
        BuildBoundary("West Boundary", new Vector3(-playableHalfExtents.x - wallThickness * 0.5f, wallCentreHeight, 0f),
            new Vector3(wallThickness, wallHeight, playableHalfExtents.y * 2f));
    }

    private void LateUpdate()
    {
        // Physics colliders stop most movement, but CharacterController and
        // NavMeshAgent can still be pushed through a thin wall at high speed.
        // Correct the authoritative roots after movement so nothing can remain
        // embedded in a boundary or drift outside the playable rectangle.
        RefreshMoversIfNeeded();

        foreach (CharacterController controller in cachedControllers)
        {
            if (controller == null || !controller.enabled ||
                !controller.gameObject.activeInHierarchy)
                continue;

            float radius = GetContainmentRadius(
                controller.transform,
                Mathf.Max(0.35f, controller.radius));
            ClampTransform(controller.transform, radius, false);
        }

        foreach (NavMeshAgent agent in cachedAgents)
        {
            if (agent == null || !agent.isActiveAndEnabled)
                continue;

            float radius = GetContainmentRadius(
                agent.transform,
                Mathf.Max(0.35f, agent.radius));
            ClampTransform(agent.transform, radius, true);
        }
    }

    private void RefreshMoversIfNeeded()
    {
        if (Time.time < nextMoverRefreshTime)
            return;

        nextMoverRefreshTime = Time.time + MoverRefreshInterval;
        cachedControllers = FindObjectsByType<CharacterController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        cachedAgents = FindObjectsByType<NavMeshAgent>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    public float GetContainmentRadius(Transform target, float fallbackRadius)
    {
        if (target == null)
            return Mathf.Max(0.35f, fallbackRadius);

        if (footprintCache.TryGetValue(target, out float cachedRadius))
            return Mathf.Max(fallbackRadius, cachedRadius);

        float visibleRadius = CalculateVisibleFootprint(target);
        float result = Mathf.Clamp(
            Mathf.Max(fallbackRadius, visibleRadius),
            0.35f,
            2.75f);
        footprintCache[target] = result;
        return result;
    }

    public Vector3 ClampPosition(Vector3 position, float radius = 0.5f)
    {
        float safeRadius = Mathf.Max(0.05f, radius) + containmentMargin;
        position.x = Mathf.Clamp(
            position.x,
            -playableHalfExtents.x + safeRadius,
            playableHalfExtents.x - safeRadius);
        position.z = Mathf.Clamp(
            position.z,
            -playableHalfExtents.y + safeRadius,
            playableHalfExtents.y - safeRadius);
        return ClampToCornerCuts(position, safeRadius);
    }

    /// <summary>
    /// The arena floor is an octagon, but the rectangle above has square corners
    /// that hang past the cut edges - which is exactly where a robot walks into
    /// the corner fence. Push back along the diagonal edge normal so containment
    /// follows the floor's real silhouette.
    /// </summary>
    private Vector3 ClampToCornerCuts(Vector3 position, float safeRadius)
    {
        if (cornerEdgeLimit <= 0f)
        {
            return position;
        }

        Vector2 normal = cornerEdgeNormal.sqrMagnitude > 0.0001f
            ? cornerEdgeNormal.normalized
            : new Vector2(0.7071f, 0.7071f);

        float distance =
            normal.x * Mathf.Abs(position.x) +
            normal.y * Mathf.Abs(position.z);
        float limit = cornerEdgeLimit - safeRadius;
        if (distance <= limit)
        {
            return position;
        }

        float overshoot = distance - limit;
        position.x -= Mathf.Sign(position.x) * normal.x * overshoot;
        position.z -= Mathf.Sign(position.z) * normal.y * overshoot;
        return position;
    }

    /// <summary>
    /// Registers authored cover, turrets and vault colliders for the final
    /// movement safety pass. CharacterController collision and NavMesh carving
    /// remain the primary systems; this catches high-speed pushes, newly
    /// carved obstacles and agents that begin a frame already embedded.
    /// </summary>
    public void RegisterObstacle(Collider obstacle)
    {
        if (obstacle == null || obstacle.isTrigger)
            return;

        registeredObstacles.Add(obstacle);
    }

    private void ClampTransform(Transform target, float radius, bool useNavMeshWarp)
    {
        if (target == null)
            return;

        Vector3 clamped = ClampPosition(target.position, radius);
        clamped = ResolveObstacleOverlaps(target, clamped, radius);
        if ((clamped - target.position).sqrMagnitude <= 0.000001f)
            return;

        NavMeshAgent agent = useNavMeshWarp ? target.GetComponent<NavMeshAgent>() : null;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.Warp(clamped);
            agent.ResetPath();
            return;
        }

        CharacterController controller = target.GetComponent<CharacterController>();
        if (controller != null && controller.enabled)
        {
            Vector3 correction = clamped - target.position;

            controller.enabled = false;
            target.position = clamped;
            controller.enabled = true;

            RobotPlayerController movement =
                target.GetComponent<RobotPlayerController>();

            if (movement != null)
                movement.CancelVelocityAlong(correction);

            return;
        }

        target.position = clamped;
    }

    private Vector3 ResolveObstacleOverlaps(
        Transform movingTarget,
        Vector3 position,
        float radius)
    {
        if (registeredObstacles.Count == 0)
            return position;

        float padding = Mathf.Max(0.05f, radius) + containmentMargin;

        // A few objects have more than one collider. Repeat a small bounded
        // number of times so leaving one collider cannot place the robot into
        // its neighbour, while keeping the cost deterministic on mobile.
        for (int pass = 0; pass < 3; pass++)
        {
            bool moved = false;

            foreach (Collider obstacle in registeredObstacles)
            {
                if (obstacle == null || !obstacle.enabled || obstacle.isTrigger ||
                    !obstacle.gameObject.activeInHierarchy ||
                    obstacle.transform.IsChildOf(movingTarget) ||
                    movingTarget.IsChildOf(obstacle.transform))
                {
                    continue;
                }

                Bounds bounds = obstacle.bounds;
                // Robot roots live at the floor plane. Ignore geometry wholly
                // above or below their usable body height.
                if (bounds.max.y < position.y + 0.05f ||
                    bounds.min.y > position.y + 3.5f)
                {
                    continue;
                }

                float minimumX = bounds.min.x - padding;
                float maximumX = bounds.max.x + padding;
                float minimumZ = bounds.min.z - padding;
                float maximumZ = bounds.max.z + padding;

                if (position.x <= minimumX || position.x >= maximumX ||
                    position.z <= minimumZ || position.z >= maximumZ)
                {
                    continue;
                }

                float toLeft = position.x - minimumX;
                float toRight = maximumX - position.x;
                float toBottom = position.z - minimumZ;
                float toTop = maximumZ - position.z;
                float shortest = Mathf.Min(
                    Mathf.Min(toLeft, toRight),
                    Mathf.Min(toBottom, toTop));

                if (shortest == toLeft)
                    position.x = minimumX;
                else if (shortest == toRight)
                    position.x = maximumX;
                else if (shortest == toBottom)
                    position.z = minimumZ;
                else
                    position.z = maximumZ;

                position = ClampPosition(position, radius);
                moved = true;
            }

            if (!moved)
                break;
        }

        return position;
    }

    private void OnDestroy()
    {
        footprintCache.Clear();
        registeredObstacles.Clear();

        if (Active == this)
            Active = null;
    }

    private static float CalculateVisibleFootprint(Transform target)
    {
        Transform visualRoot = target.Find("Visual");
        if (visualRoot == null)
            visualRoot = target.Find("SpiderVisual");
        if (visualRoot == null)
            visualRoot = target.Find("Model");

        Renderer[] renderers = (visualRoot != null ? visualRoot : target)
            .GetComponentsInChildren<Renderer>(true);
        float radius = 0f;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                renderer is LineRenderer ||
                renderer is TrailRenderer ||
                renderer.GetComponentInParent<Canvas>() != null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            float xReach = Mathf.Abs(bounds.center.x - target.position.x) +
                           bounds.extents.x;
            float zReach = Mathf.Abs(bounds.center.z - target.position.z) +
                           bounds.extents.z;
            radius = Mathf.Max(radius, xReach, zReach);
        }

        return radius;
    }

    private void BuildBoundary(string boundaryName, Vector3 position, Vector3 size)
    {
        Transform existing = transform.Find(boundaryName);
        GameObject boundary = existing != null
            ? existing.gameObject
            : new GameObject(boundaryName);

        boundary.layer = gameObject.layer;
        boundary.transform.SetParent(transform, false);
        boundary.transform.localPosition = position;
        boundary.transform.localRotation = Quaternion.identity;
        boundary.transform.localScale = Vector3.one;

        BoxCollider collider = boundary.GetComponent<BoxCollider>();
        if (collider == null)
            collider = boundary.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = size;
        collider.isTrigger = false;
    }
}
