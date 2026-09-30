using UnityEngine;

public class TopDownCameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Framing")]
    [SerializeField, Min(2f)] private float cameraHeight = 18f;
    [SerializeField, Range(45f, 70f)] private float pitch = 55f;
    [SerializeField, Range(-30f, 30f)] private float yaw = 0f;
    [SerializeField, Min(0.1f)] private float followSpeed = 10f;
    [SerializeField, Min(0.1f)] private float rotationSpeed = 14f;

    [Header("Movement Look Ahead")]
    [SerializeField, Min(0f)] private float maximumLookAhead = 1.8f;
    [SerializeField, Min(0.1f)] private float speedForFullLookAhead = 5.5f;
    [SerializeField, Min(0.1f)] private float lookAheadResponse = 5.5f;
    [SerializeField, Min(1f)] private float teleportResetDistance = 8f;

    [Header("Arena Clamp")]
    [SerializeField] private bool clampToArena = true;
    [SerializeField] private Vector2 arenaMinimum = new Vector2(-25f, -35f);
    [SerializeField] private Vector2 arenaMaximum = new Vector2(25f, 35f);
    [SerializeField, Min(0f)] private float arenaPadding = 0.75f;
    [Tooltip("Extra framing beyond the playable walls. This keeps the outer arena art visible without allowing the target to leave the playfield.")]
    [SerializeField, Min(0f)] private float exteriorReveal = 7.5f;

    // Kept only so existing scene/prefab data from the original component can
    // deserialize without losing information. New framing uses cameraHeight
    // and pitch so position and view angle cannot drift apart.
    [SerializeField, HideInInspector]
    private Vector3 offset = new Vector3(0f, 9f, -9f);

    // Signed framing adjustment applied on top of the authored height, driven by
    // CombatCameraDirector from gameplay events. Positive widens, negative tightens.
    private float framingBias;
    private float smoothedFramingBias;

    private Camera gameplayCamera;
    private Vector3 previousTargetPosition;
    private Vector3 smoothedLookAhead;
    private Vector3 externalLookAheadDirection;
    private bool useExternalLookAhead;
    private bool hasTargetSample;

    public Transform Target => target;
    public float LocalYaw => yaw;
    public void SetOnlinePerspective(TeamSide side, Transform localPlayer)
    {
        yaw = side == TeamSide.SideA ? 0f : 180f;
        SetTarget(localPlayer, true);
    }

    /// <summary>
    /// How far the rig sits from its focus point. With a perspective camera this
    /// is what decides the framing, so callers that used to set an orthographic
    /// size need it to solve for the equivalent lens.
    /// </summary>
    public float FramingDistance =>
        cameraHeight * (1f + smoothedFramingBias) /
        Mathf.Max(0.01f, Mathf.Sin(pitch * Mathf.Deg2Rad));

    private void Awake()
    {
        gameplayCamera = GetComponent<Camera>();

        if (cameraHeight <= 0f)
            cameraHeight = Mathf.Max(2f, Mathf.Abs(offset.y));
    }

    private void OnEnable()
    {
        ResetTargetSample();
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 targetVelocity = SampleTargetVelocity(deltaTime);
        Vector3 desiredLookAhead = CalculateDesiredLookAhead(targetVelocity);
        smoothedLookAhead = Vector3.Lerp(
            smoothedLookAhead,
            desiredLookAhead,
            Damp(lookAheadResponse, deltaTime)
        );

        Vector3 focusPoint = target.position + smoothedLookAhead;
        focusPoint.y = target.position.y;
        focusPoint = ClampFocusPoint(focusPoint);

        smoothedFramingBias = Mathf.Lerp(
            smoothedFramingBias,
            framingBias,
            Damp(6f, deltaTime));

        Quaternion desiredRotation = Quaternion.Euler(pitch, yaw, 0f);
        float framedHeight = cameraHeight * (1f + smoothedFramingBias);
        float distance = framedHeight / Mathf.Max(0.01f, Mathf.Sin(pitch * Mathf.Deg2Rad));
        Vector3 desiredPosition =
            focusPoint +
            desiredRotation * Vector3.back * distance;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            Damp(followSpeed, deltaTime)
        );
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            Damp(rotationSpeed, deltaTime)
        );
    }

    public void SetTarget(Transform newTarget, bool snapImmediately = false)
    {
        target = newTarget;
        ResetTargetSample();

        if (snapImmediately)
            SnapToTarget();
    }

    /// <summary>
    /// Signed framing offset as a fraction of camera height. Negative tightens
    /// the frame, positive widens it. Smoothed internally so callers can set it
    /// every frame without introducing a pop.
    /// </summary>
    public void SetFramingBias(float bias)
    {
        framingBias = Mathf.Clamp(bias, -0.35f, 0.35f);
    }

    public void ConfigureArenaBounds(Vector2 minimum, Vector2 maximum)
    {
        arenaMinimum = Vector2.Min(minimum, maximum);
        arenaMaximum = Vector2.Max(minimum, maximum);
    }

    public void SetExternalLookAhead(Vector3 worldDirection)
    {
        worldDirection.y = 0f;
        externalLookAheadDirection = worldDirection.sqrMagnitude > 0.001f
            ? worldDirection.normalized
            : Vector3.zero;
        useExternalLookAhead = externalLookAheadDirection.sqrMagnitude > 0f;
    }

    public void ClearExternalLookAhead()
    {
        useExternalLookAhead = false;
        externalLookAheadDirection = Vector3.zero;
    }

    public void SnapToTarget()
    {
        if (target == null)
            return;

        smoothedLookAhead = Vector3.zero;
        smoothedFramingBias = framingBias;
        Vector3 focusPoint = ClampFocusPoint(target.position);
        Quaternion desiredRotation = Quaternion.Euler(pitch, yaw, 0f);
        float distance = cameraHeight * (1f + smoothedFramingBias) /
            Mathf.Max(0.01f, Mathf.Sin(pitch * Mathf.Deg2Rad));
        transform.SetPositionAndRotation(
            focusPoint + desiredRotation * Vector3.back * distance,
            desiredRotation
        );
        ResetTargetSample();
    }

    private Vector3 SampleTargetVelocity(float deltaTime)
    {
        if (!hasTargetSample)
        {
            previousTargetPosition = target.position;
            hasTargetSample = true;
            return Vector3.zero;
        }

        Vector3 delta = target.position - previousTargetPosition;
        previousTargetPosition = target.position;

        if (delta.sqrMagnitude > teleportResetDistance * teleportResetDistance)
        {
            smoothedLookAhead = Vector3.zero;
            return Vector3.zero;
        }

        Vector3 velocity = delta / deltaTime;
        velocity.y = 0f;
        return velocity;
    }

    private Vector3 CalculateDesiredLookAhead(Vector3 velocity)
    {
        float speed = velocity.magnitude;
        float amount = Mathf.InverseLerp(0.15f, speedForFullLookAhead, speed);
        Vector3 direction = useExternalLookAhead
            ? externalLookAheadDirection
            : (speed > 0.01f ? velocity / speed : Vector3.zero);
        return direction * (maximumLookAhead * amount);
    }

    private Vector3 ClampFocusPoint(Vector3 focusPoint)
    {
        if (!clampToArena)
            return focusPoint;

        float halfWidth = 0f;
        float halfDepth = 0f;

        if (gameplayCamera != null)
        {
            // Half the world height the camera frames. An orthographic camera
            // states it directly; a perspective one has to derive it from the
            // lens and how far back the rig is sitting, or the clamp lets the
            // view slide off the arena art it is meant to keep in shot.
            float halfViewHeight = gameplayCamera.orthographic
                ? gameplayCamera.orthographicSize
                : FramingDistance *
                  Mathf.Tan(gameplayCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            halfWidth = halfViewHeight * gameplayCamera.aspect;
            halfDepth = halfViewHeight /
                Mathf.Max(0.1f, Mathf.Sin(pitch * Mathf.Deg2Rad));
        }

        // Clamp against a slightly expanded framing rectangle. The robot stays
        // inside the real walls while the camera can breathe past them, which
        // gives the match the readable Brawl Stars-style arena silhouette.
        float minimumX = arenaMinimum.x - exteriorReveal + arenaPadding + halfWidth;
        float maximumX = arenaMaximum.x + exteriorReveal - arenaPadding - halfWidth;
        float minimumZ = arenaMinimum.y - exteriorReveal + arenaPadding + halfDepth;
        float maximumZ = arenaMaximum.y + exteriorReveal - arenaPadding - halfDepth;

        focusPoint.x = ClampOrCentre(
            focusPoint.x,
            minimumX,
            maximumX,
            (arenaMinimum.x + arenaMaximum.x) * 0.5f
        );
        focusPoint.z = ClampOrCentre(
            focusPoint.z,
            minimumZ,
            maximumZ,
            (arenaMinimum.y + arenaMaximum.y) * 0.5f
        );
        return focusPoint;
    }

    private void ResetTargetSample()
    {
        hasTargetSample = false;
        smoothedLookAhead = Vector3.zero;

        if (target != null)
        {
            previousTargetPosition = target.position;
            hasTargetSample = true;
        }
    }

    private static float ClampOrCentre(
        float value,
        float minimum,
        float maximum,
        float centre)
    {
        return minimum <= maximum
            ? Mathf.Clamp(value, minimum, maximum)
            : centre;
    }

    private static float Damp(float sharpness, float deltaTime)
    {
        return 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * deltaTime);
    }

    private void OnValidate()
    {
        cameraHeight = Mathf.Max(2f, cameraHeight);
        Vector2 minimum = Vector2.Min(arenaMinimum, arenaMaximum);
        Vector2 maximum = Vector2.Max(arenaMinimum, arenaMaximum);
        arenaMinimum = minimum;
        arenaMaximum = maximum;
    }
}
