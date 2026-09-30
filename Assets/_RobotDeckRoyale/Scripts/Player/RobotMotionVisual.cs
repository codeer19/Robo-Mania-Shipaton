using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(210)]
public class RobotMotionVisual : MonoBehaviour
{
    [SerializeField] private Transform movementRoot;

    [Header("Movement Feel")]
    [SerializeField, Min(0.1f)] private float movementSpeedForFullAnimation = 5f;
    [SerializeField, Min(0.1f)] private float velocityResponse = 12f;
    // These amplitudes are what the motion actually reads as. They were reduced
    // to roughly a fiftieth of these values, which flattened the robot into a
    // sliding prop; anything spawned from a prefab without scene overrides
    // inherited that. Keep them in this range.
    [SerializeField] private float idleBobHeight = 0.055f;
    [SerializeField, Min(0.1f)] private float idleBobFrequency = 1.25f;
    [Tooltip("Small high-speed chassis lift. This is not a repeating walk bob.")]
    [SerializeField] private float speedLiftHeight = 0.045f;
    [SerializeField] private float suspensionCompression = 0.07f;
    [SerializeField] private float forwardLeanAngle = 7f;
    [SerializeField] private float sideLeanAngle = 8f;
    [SerializeField] private float turnLeanAngle = 6f;
    [SerializeField] private float accelerationLeanAngle = 6f;
    [SerializeField, Min(0.1f)] private float accelerationResponse = 10f;
    [SerializeField, Min(0.1f)] private float accelerationForFullReaction = 30f;
    [SerializeField, Min(1f)] private float turnRateForFullLean = 360f;
    [SerializeField, Min(0.1f)] private float animationSmoothness = 18f;
    [SerializeField, Min(1f)] private float teleportResetDistance = 6f;

    [Header("Ground Contact")]
    [SerializeField] private bool alignVisualToGround = true;
    [SerializeField, Range(0f, 0.2f)] private float groundClearance = 0.04f;

    [Header("Grounded Wheel Motion")]
    [SerializeField] private Transform leftWheel;
    [SerializeField] private Transform rightWheel;
    [SerializeField, Min(0.05f)] private float wheelRadius = 0.502f;
    [SerializeField, Min(0.05f)] private float wheelTrackHalfWidth = 0.913f;
    [Tooltip("Fallback only. The imported robot wheels use local Z as their axle.")]
    [SerializeField] private Vector3 wheelLocalAxis = Vector3.forward;

    [Tooltip("Extra spin when accelerating hard and drag when braking, as a " +
             "fraction of the ground roll. This is what makes the tyres look " +
             "driven rather than carried along.")]
    [SerializeField, Range(0f, 1.5f)] private float wheelSlip = 0.5f;

    [Tooltip("Fraction of a sideways slide that still turns the wheels. At zero " +
             "the tyres freeze mid-strafe while the robot keeps moving.")]
    [SerializeField, Range(0f, 1f)] private float lateralSkidRoll = 0.28f;

    [Tooltip("How far the wheels lean into a turn, in degrees.")]
    [SerializeField, Range(0f, 20f)] private float wheelCamberAngle = 6f;

    [Header("Combat Feel")]
    [SerializeField] private float fireKickAngle = 9f;
    [SerializeField] private float fireRecoilDistance = 0.18f;
    [SerializeField, Min(0.01f)] private float fireKickDuration = 0.16f;

    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 previousRootPosition;
    private Vector3 previousRootForward;
    private Vector3 smoothedWorldVelocity;
    private Vector3 smoothedWorldAcceleration;
    private Vector3 smoothedLocalVelocity;
    private Vector3 smoothedLocalAcceleration;
    private float smoothedTurnAmount;
    private float leftWheelRollDegrees;
    private float rightWheelRollDegrees;
    private float fireKickStartedAt = float.NegativeInfinity;
    private RobotBlaster blaster;
    private Quaternion leftWheelRestRotation;
    private Quaternion rightWheelRestRotation;
    private Vector3 leftWheelRollAxis;
    private Vector3 rightWheelRollAxis;
    private Vector3 leftWheelCamberAxis = Vector3.forward;
    private Vector3 rightWheelCamberAxis = Vector3.forward;
    private Transform chassisMotionRoot;
    private RobotExpressionVisual expressionVisual;
    private RobotRigBindings authoredRig;
    private RobotAnimationHooks animationHooks;
    private RobotHitReaction hitReaction;
    private RobotAimController aimSource;

    // Rest pose for the authored rig. ApplyAuthoredOffsets used to accumulate
    // onto whatever these transforms already held, which only stays bounded if
    // an Animator clip rewrites the same channels every frame. When it does not,
    // the chassis integrates downward until the robot sinks through the floor.
    private Vector3 authoredChassisBasePosition;
    private Quaternion authoredChassisBaseRotation;
    private Quaternion authoredHeadBaseRotation;
    private Quaternion authoredGunArmBaseRotation;
    private bool authoredBaseCaptured;

    private void Awake()
    {
        expressionVisual = GetComponent<RobotExpressionVisual>();
        authoredRig = GetComponentInChildren<RobotRigBindings>(true);
        animationHooks = GetComponent<RobotAnimationHooks>();
        hitReaction = GetComponentInParent<RobotHitReaction>();
        aimSource = GetComponentInParent<RobotAimController>();

        if (movementRoot == null)
            movementRoot = transform.parent;

        if (movementRoot != null)
            blaster = movementRoot.GetComponent<RobotBlaster>();

        if (authoredRig != null && authoredRig.IsValid)
        {
            leftWheel = authoredRig.LeftWheel;
            rightWheel = authoredRig.RightWheel;
            wheelRadius = authoredRig.WheelRadius;
            wheelTrackHalfWidth = authoredRig.WheelHalfTrack;
            chassisMotionRoot = authoredRig.Chassis;
            CaptureAuthoredBasePose();
        }
        else
        {
            authoredRig = null;
            ResolveWheelTransforms();
            BuildRuntimeMotionRig();
        }
        CacheWheelPose();
        CaptureOriginalPose();
        ResetMotionSamples();
    }

    private void Start()
    {
        if (alignVisualToGround && authoredRig == null)
            AlignVisualToGround();

        CaptureOriginalPose();
        ResetMotionSamples();
    }

    private void OnEnable()
    {
        RestoreVisualPose();
        ResetMotionSamples();

        if (blaster != null)
            blaster.Fired += HandleFired;
    }

    private void OnDisable()
    {
        if (blaster != null)
            blaster.Fired -= HandleFired;

        ResetWheelPose();
        RestoreVisualPose();
    }

    private void LateUpdate()
    {
        if (movementRoot == null)
            return;

        if (Time.deltaTime <= 0f) return;
        float deltaTime = Time.deltaTime;
        Vector3 rootDelta = movementRoot.position - previousRootPosition;

        if (rootDelta.sqrMagnitude > teleportResetDistance * teleportResetDistance)
        {
            smoothedWorldVelocity = Vector3.zero;
            smoothedWorldAcceleration = Vector3.zero;
            smoothedLocalVelocity = Vector3.zero;
            smoothedLocalAcceleration = Vector3.zero;
            rootDelta = Vector3.zero;
        }

        Vector3 worldVelocity = rootDelta / deltaTime;
        worldVelocity.y = 0f;
        Vector3 localDisplacement = movementRoot.InverseTransformDirection(
            rootDelta);
        localDisplacement.y = 0f;

        float velocityBlend = Damp(velocityResponse, deltaTime);
        Vector3 previousSmoothedWorldVelocity = smoothedWorldVelocity;
        smoothedWorldVelocity = Vector3.Lerp(
            smoothedWorldVelocity,
            worldVelocity,
            velocityBlend
        );

        Vector3 measuredAcceleration =
            (smoothedWorldVelocity - previousSmoothedWorldVelocity) / deltaTime;
        measuredAcceleration.y = 0f;
        smoothedWorldAcceleration = Vector3.Lerp(
            smoothedWorldAcceleration,
            measuredAcceleration,
            Damp(accelerationResponse, deltaTime)
        );
        smoothedLocalVelocity = movementRoot.InverseTransformDirection(
            smoothedWorldVelocity);
        smoothedLocalAcceleration = movementRoot.InverseTransformDirection(
            smoothedWorldAcceleration);
        smoothedLocalVelocity.y = 0f;
        smoothedLocalAcceleration.y = 0f;

        float speed = smoothedLocalVelocity.magnitude;
        float movementAmount = Mathf.Clamp01(
            speed / Mathf.Max(0.1f, movementSpeedForFullAnimation)
        );

        Vector3 rootForward = movementRoot.forward;
        rootForward.y = 0f;
        rootForward = rootForward.sqrMagnitude > 0.001f
            ? rootForward.normalized
            : previousRootForward;

        float turnDegrees = Vector3.SignedAngle(
            previousRootForward,
            rootForward,
            Vector3.up
        );
        float normalizedTurn = Mathf.Clamp(
            turnDegrees / deltaTime / Mathf.Max(1f, turnRateForFullLean),
            -1f,
            1f
        );
        smoothedTurnAmount = Mathf.Lerp(
            smoothedTurnAmount,
            normalizedTurn,
            Damp(10f, deltaTime)
        );

        bool suppress = authoredRig != null && animationHooks != null && animationHooks.SuppressProcedural;
        if (!suppress) UpdateWheelMotion(localDisplacement, turnDegrees);

        previousRootPosition = movementRoot.position;
        previousRootForward = rootForward;

        if (authoredRig != null)
        {
            if (!suppress) ApplyAuthoredOffsets();
            return;
        }

        float idleWave = Mathf.Sin(
            Time.time * idleBobFrequency * Mathf.PI * 2f);
        float accelerationAmount = Mathf.Clamp01(
            smoothedLocalAcceleration.magnitude /
            Mathf.Max(0.1f, accelerationForFullReaction));
        float verticalOffset =
            idleWave * idleBobHeight * (1f - movementAmount * 0.9f) +
            movementAmount * speedLiftHeight -
            accelerationAmount * suspensionCompression;

        float fireKick = GetFireKick();
        Vector3 targetPosition =
            originalLocalPosition +
            Vector3.up * verticalOffset +
            Vector3.back * (fireKick * fireRecoilDistance);

        Vector3 normalizedVelocity = Vector3.ClampMagnitude(
            smoothedLocalVelocity /
            Mathf.Max(0.1f, movementSpeedForFullAnimation),
            1f);
        Vector3 normalizedAcceleration = Vector3.ClampMagnitude(
            smoothedLocalAcceleration /
            Mathf.Max(0.1f, accelerationForFullReaction),
            1f);
        float pitch =
            -normalizedVelocity.z * forwardLeanAngle -
            normalizedAcceleration.z * accelerationLeanAngle -
            fireKick * fireKickAngle;
        float roll =
            -normalizedVelocity.x * sideLeanAngle -
            normalizedAcceleration.x * accelerationLeanAngle -
            smoothedTurnAmount * turnLeanAngle;

        Quaternion targetRotation =
            originalLocalRotation *
            Quaternion.Euler(pitch, 0f, roll);
        float animationBlend = Damp(animationSmoothness, deltaTime);

        // Position and rotation belong exclusively to this motion driver.
        // RobotHitReaction owns scale and material flash, so the two effects
        // layer without fighting over the same transform properties.
        Transform animatedRoot = chassisMotionRoot != null
            ? chassisMotionRoot
            : transform;
        animatedRoot.localPosition = Vector3.Lerp(
            animatedRoot.localPosition,
            targetPosition,
            animationBlend
        );
        animatedRoot.localRotation = Quaternion.Slerp(
            animatedRoot.localRotation,
            targetRotation,
            animationBlend
        );
    }

    private void ResetMotionSamples()
    {
        if (movementRoot == null)
            return;

        previousRootPosition = movementRoot.position;
        previousRootForward = movementRoot.forward;
        previousRootForward.y = 0f;

        if (previousRootForward.sqrMagnitude < 0.001f)
            previousRootForward = Vector3.forward;
        else
            previousRootForward.Normalize();

        smoothedWorldVelocity = Vector3.zero;
        smoothedWorldAcceleration = Vector3.zero;
        smoothedLocalVelocity = Vector3.zero;
        smoothedLocalAcceleration = Vector3.zero;
        smoothedTurnAmount = 0f;
    }

    private void ResolveWheelTransforms()
    {
        if (leftWheel != null && rightWheel != null)
            return;

        List<Transform> candidates = new List<Transform>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer is LineRenderer ||
                renderer is TrailRenderer || renderer.GetComponentInParent<Canvas>() != null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 localCentre = movementRoot != null
                ? movementRoot.InverseTransformPoint(bounds.center)
                : transform.InverseTransformPoint(bounds.center);
            bool sitsAtWheelHeight = localCentre.y <= 1.15f;
            bool sitsOnRobotSide = Mathf.Abs(localCentre.x) >= 0.55f;
            bool hasWheelShape =
                bounds.size.x <= 0.72f &&
                bounds.size.y >= 0.72f &&
                bounds.size.z >= 0.72f;

            if (sitsAtWheelHeight && sitsOnRobotSide && hasWheelShape)
                candidates.Add(renderer.transform);
        }

        if (candidates.Count < 2)
            return;

        candidates.Sort((first, second) =>
        {
            Vector3 firstPosition = movementRoot.InverseTransformPoint(first.position);
            Vector3 secondPosition = movementRoot.InverseTransformPoint(second.position);
            return firstPosition.x.CompareTo(secondPosition.x);
        });

        leftWheel = candidates[0];
        rightWheel = candidates[candidates.Count - 1];
    }

    private void CacheWheelPose()
    {
        if (leftWheel != null)
        {
            leftWheelRestRotation = leftWheel.localRotation;
            leftWheelRollAxis = ResolveWheelRollAxis(leftWheel);
            leftWheelCamberAxis = ResolveWheelCamberAxis(leftWheel);
        }
        if (rightWheel != null)
        {
            rightWheelRestRotation = rightWheel.localRotation;
            rightWheelRollAxis = ResolveWheelRollAxis(rightWheel);
            rightWheelCamberAxis = ResolveWheelCamberAxis(rightWheel);
        }
    }

    private void BuildRuntimeMotionRig()
    {
        // The source FBX is a flat collection of rigid meshes. Build two
        // mechanical wheel pivots at runtime so each tyre and its face details
        // rotate together instead of visibly separating from one another.
        List<Transform> leftParts = FindWheelAssemblyParts(leftWheel);
        List<Transform> rightParts = FindWheelAssemblyParts(rightWheel);

        if (leftWheel != null)
            leftWheel = CreateWheelPivot("Wheel_L_Motion", leftWheel, leftParts);
        if (rightWheel != null)
            rightWheel = CreateWheelPivot("Wheel_R_Motion", rightWheel, rightParts);

        // Body lean, suspension and recoil belong to the chassis only. Keeping
        // the wheel pivots outside this root preserves their ground contact.
        GameObject chassisObject = new GameObject("Chassis_Motion");
        chassisObject.hideFlags = HideFlags.DontSave;
        chassisMotionRoot = chassisObject.transform;
        chassisMotionRoot.SetParent(transform, false);

        List<Transform> chassisParts = new List<Transform>();
        foreach (Transform child in transform)
        {
            if (child == chassisMotionRoot || child == leftWheel ||
                child == rightWheel)
            {
                continue;
            }

            if (child.GetComponent<Renderer>() != null)
                chassisParts.Add(child);
        }

        foreach (Transform part in chassisParts)
            part.SetParent(chassisMotionRoot, true);
    }

    private List<Transform> FindWheelAssemblyParts(Transform tyre)
    {
        List<Transform> parts = new List<Transform>();
        if (tyre == null)
            return parts;

        Vector3 centre = transform.InverseTransformPoint(tyre.position);
        float assemblyRadius = Mathf.Max(0.38f, wheelRadius * 0.85f);

        foreach (Transform child in transform)
        {
            if (child.GetComponent<Renderer>() == null)
                continue;

            Vector3 localPosition = transform.InverseTransformPoint(
                child.position);
            if (Vector3.Distance(localPosition, centre) <= assemblyRadius)
                parts.Add(child);
        }

        if (!parts.Contains(tyre))
            parts.Add(tyre);

        return parts;
    }

    private Transform CreateWheelPivot(
        string pivotName,
        Transform tyre,
        List<Transform> parts)
    {
        GameObject pivotObject = new GameObject(pivotName);
        pivotObject.hideFlags = HideFlags.DontSave;
        Transform pivot = pivotObject.transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = tyre.localPosition;
        pivot.localRotation = tyre.localRotation;
        pivot.localScale = Vector3.one;

        foreach (Transform part in parts)
        {
            if (part != null)
                part.SetParent(pivot, true);
        }

        return pivot;
    }

    private Vector3 ResolveWheelRollAxis(Transform wheel)
    {
        if (wheel == null || movementRoot == null)
            return wheelLocalAxis.sqrMagnitude > 0.001f
                ? wheelLocalAxis.normalized
                : Vector3.forward;

        // Positive rotation about chassis-right moves the bottom of the wheel
        // rearward, which is the physically correct roll for forward travel.
        Vector3 localAxle = wheel.InverseTransformDirection(movementRoot.right);
        if (localAxle.sqrMagnitude <= 0.001f)
            localAxle = wheelLocalAxis;

        // Snap to the dominant imported local axis. This prevents tiny FBX
        // Euler offsets from turning a clean roll into a visible wheel wobble.
        Vector3 absolute = new Vector3(
            Mathf.Abs(localAxle.x),
            Mathf.Abs(localAxle.y),
            Mathf.Abs(localAxle.z));
        if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            return Vector3.right * Mathf.Sign(localAxle.x);
        if (absolute.y >= absolute.z)
            return Vector3.up * Mathf.Sign(localAxle.y);
        return Vector3.forward * Mathf.Sign(localAxle.z);
    }

    private void UpdateWheelMotion(
        Vector3 localDisplacement,
        float turnDegrees)
    {
        if (leftWheel == null || rightWheel == null)
            return;

        float planarDistance = localDisplacement.magnitude;
        float forwardRatio = planarDistance > 0.0001f
            ? localDisplacement.z / planarDistance
            : 0f;
        // Preserve clear forward/reverse roll while smoothly fading to a
        // controlled skid at a true 90-degree strafe. This is stateless, so
        // crossing the aim axis cannot abruptly reverse both wheels.
        const float directionSharpness = 2.5f;
        float driveResponse = (float)(
            System.Math.Tanh(forwardRatio * directionSharpness) /
            System.Math.Tanh(directionSharpness));
        float driveDistance = planarDistance * driveResponse;

        // Tyres break traction under hard acceleration and drag under braking.
        // Without this the wheels turn at exactly ground speed, which is the
        // one thing that never reads as a powered chassis.
        float slip = Mathf.Clamp(
            smoothedLocalAcceleration.z /
            Mathf.Max(0.1f, accelerationForFullReaction), -1f, 1f);
        driveDistance *= 1f + slip * wheelSlip;

        // A pure strafe left the wheels frozen while the robot kept sliding.
        // Scrubbing them forward a little sells the slide instead.
        driveDistance +=
            planarDistance * (1f - Mathf.Abs(driveResponse)) * lateralSkidRoll;

        float turnDistance =
            turnDegrees * Mathf.Deg2Rad * wheelTrackHalfWidth;
        float safeRadius = Mathf.Max(0.05f, wheelRadius);
        float leftDegrees =
            (driveDistance + turnDistance) / safeRadius * Mathf.Rad2Deg;
        float rightDegrees =
            (driveDistance - turnDistance) / safeRadius * Mathf.Rad2Deg;

        leftWheelRollDegrees = Mathf.Repeat(
            leftWheelRollDegrees + leftDegrees,
            360f);
        rightWheelRollDegrees = Mathf.Repeat(
            rightWheelRollDegrees + rightDegrees,
            360f);
        // Lean the wheels into the turn. The chassis already rolls; matching it
        // at the contact patch is what stops the robot looking like a body
        // sliding over two upright discs.
        float camber = smoothedTurnAmount * wheelCamberAngle;

        leftWheel.localRotation = leftWheelRestRotation *
            Quaternion.AngleAxis(camber, leftWheelCamberAxis) *
            Quaternion.AngleAxis(leftWheelRollDegrees, leftWheelRollAxis);
        rightWheel.localRotation = rightWheelRestRotation *
            Quaternion.AngleAxis(camber, rightWheelCamberAxis) *
            Quaternion.AngleAxis(rightWheelRollDegrees, rightWheelRollAxis);
    }

    /// <summary>
    /// The wheel's own axis that points along the robot's facing. Camber is a
    /// tilt about that axis, and it has to be resolved per wheel because the
    /// imported meshes each carry their own baked orientation.
    /// </summary>
    private Vector3 ResolveWheelCamberAxis(Transform wheel)
    {
        if (wheel == null || movementRoot == null)
            return Vector3.forward;

        Vector3 local = wheel.InverseTransformDirection(movementRoot.forward);

        if (local.sqrMagnitude <= 0.001f)
            return Vector3.forward;

        Vector3 absolute = new Vector3(
            Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));

        if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            return Vector3.right * Mathf.Sign(local.x);
        if (absolute.y >= absolute.z)
            return Vector3.up * Mathf.Sign(local.y);
        return Vector3.forward * Mathf.Sign(local.z);
    }

    private void ResetWheelPose()
    {
        leftWheelRollDegrees = 0f;
        rightWheelRollDegrees = 0f;
        if (leftWheel != null)
            leftWheel.localRotation = leftWheelRestRotation;
        if (rightWheel != null)
            rightWheel.localRotation = rightWheelRestRotation;
    }

    private void CaptureOriginalPose()
    {
        Transform animatedRoot = chassisMotionRoot != null
            ? chassisMotionRoot
            : transform;
        originalLocalPosition = animatedRoot.localPosition;
        originalLocalRotation = animatedRoot.localRotation;
    }

    private void RestoreVisualPose()
    {
        if (authoredRig != null) return; // Animator owns the base pose, including pooled reuse.
        Transform animatedRoot = chassisMotionRoot != null
            ? chassisMotionRoot
            : transform;
        animatedRoot.localPosition = originalLocalPosition;
        animatedRoot.localRotation = originalLocalRotation;
        fireKickStartedAt = float.NegativeInfinity;
    }

    private void AlignVisualToGround()
    {
        if (movementRoot == null)
            return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds visualBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                renderer is LineRenderer ||
                renderer is TrailRenderer ||
                renderer.GetComponentInParent<Canvas>() != null)
            {
                continue;
            }

            if (!hasBounds)
            {
                visualBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                visualBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
            return;

        float desiredMinimum = movementRoot.position.y + groundClearance;
        if (TryFindArenaVisualFloorHeight(
                movementRoot.position,
                out float visualFloorHeight))
        {
            desiredMinimum = Mathf.Max(
                desiredMinimum,
                visualFloorHeight + groundClearance);
        }

        transform.position += Vector3.up * (desiredMinimum - visualBounds.min.y);
    }

    private bool TryFindArenaVisualFloorHeight(
        Vector3 worldPosition,
        out float floorHeight)
    {
        floorHeight = float.NegativeInfinity;
        bool found = false;

        foreach (Renderer renderer in
                 FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (renderer == null ||
                !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
                renderer is LineRenderer ||
                renderer is TrailRenderer ||
                renderer.transform.IsChildOf(movementRoot) ||
                renderer.GetComponentInParent<Canvas>() != null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            bool containsPosition =
                worldPosition.x >= bounds.min.x &&
                worldPosition.x <= bounds.max.x &&
                worldPosition.z >= bounds.min.z &&
                worldPosition.z <= bounds.max.z;
            bool isLargeFlatSurface =
                bounds.size.x >= 8f &&
                bounds.size.z >= 8f &&
                bounds.size.y <= 0.25f;
            bool isNearGameplayPlane =
                bounds.max.y >= movementRoot.position.y - 0.5f &&
                bounds.max.y <= movementRoot.position.y + 1.25f;

            if (!containsPosition ||
                !isLargeFlatSurface ||
                !isNearGameplayPlane)
            {
                continue;
            }

            floorHeight = Mathf.Max(floorHeight, bounds.max.y);
            found = true;
        }

        return found;
    }

    private float GetFireKick()
    {
        float age = Time.time - fireKickStartedAt;

        if (age < 0f || age >= fireKickDuration)
            return 0f;

        float normalizedAge = age / Mathf.Max(0.01f, fireKickDuration);
        float remaining = 1f - normalizedAge;
        return remaining * remaining;
    }

    private void HandleFired(RobotBlaster weapon)
    {
        NotifyFired();
    }

    public void NotifyFired()
    {
        if (authoredRig != null)
        {
            // Player shots arrive directly at the bridge; the bot uses this existing seam.
            if (blaster == null && animationHooks != null) animationHooks.NotifyFired();
            return;
        }
        fireKickStartedAt = Time.time;

        // The bot AI drives this directly rather than raising RobotBlaster.Fired,
        // so forwarding here is what makes the arm kick on both robots from the
        // one call every shooter already makes.
        if (expressionVisual != null)
            expressionVisual.NotifyFired();
    }

    private static float Damp(float sharpness, float deltaTime)
    {
        return 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * deltaTime);
    }

    private void CaptureAuthoredBasePose()
    {
        if (authoredBaseCaptured || authoredRig == null)
            return;

        if (authoredRig.Chassis != null)
        {
            authoredChassisBasePosition = authoredRig.Chassis.localPosition;
            authoredChassisBaseRotation = authoredRig.Chassis.localRotation;
        }

        if (authoredRig.Head != null)
            authoredHeadBaseRotation = authoredRig.Head.localRotation;

        if (authoredRig.GunArm != null)
            authoredGunArmBaseRotation = authoredRig.GunArm.localRotation;

        authoredBaseCaptured = true;
    }

    private void ApplyAuthoredOffsets()
    {
        // Every offset below is applied from the captured rest pose rather than
        // added to the transform's current value. Accumulating instead only stays
        // bounded while some Animator clip rewrites these exact channels each
        // frame, and a rig whose body layer carries no motion has nothing to do
        // that - the chassis then sinks a little further every frame.
        CaptureAuthoredBasePose();

        Transform chassis = authoredRig.Chassis;
        Vector3 acceleration = Vector3.ClampMagnitude(smoothedLocalAcceleration / 30f, 1f);
        Vector3 right = chassis.parent.InverseTransformDirection(movementRoot.right);
        Vector3 forward = chassis.parent.InverseTransformDirection(movementRoot.forward);
        Quaternion delta = Quaternion.AngleAxis(-acceleration.z * 3f, right) *
            Quaternion.AngleAxis(-acceleration.x * 2f, forward);
        chassis.localRotation = delta * authoredChassisBaseRotation;
        Vector3 offset = Vector3.down * (acceleration.magnitude * 0.02f);
        if (hitReaction != null) offset += hitReaction.AuthoredWorldOffset;
        chassis.localPosition = authoredChassisBasePosition +
            chassis.parent.InverseTransformVector(offset);

        if (authoredRig.Head != null)
            authoredRig.Head.localRotation = authoredHeadBaseRotation;
        if (authoredRig.GunArm != null)
            authoredRig.GunArm.localRotation = authoredGunArmBaseRotation;

        if (aimSource == null || !aimSource.enabled || !aimSource.HasAimTarget) return;
        Vector3 direction = aimSource.AimTarget - movementRoot.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        float yaw = Vector3.SignedAngle(movementRoot.forward, direction, Vector3.up);
        authoredRig.Head.rotation = Quaternion.AngleAxis(Mathf.Clamp(yaw, -15f, 15f), Vector3.up) * authoredRig.Head.rotation;
        if (authoredRig.GunArm != null)
            authoredRig.GunArm.rotation = Quaternion.AngleAxis(Mathf.Clamp(yaw, -25f, 25f), Vector3.up) * authoredRig.GunArm.rotation;
    }
}
