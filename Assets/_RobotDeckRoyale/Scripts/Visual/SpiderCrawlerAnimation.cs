using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class SpiderCrawlerAnimation : MonoBehaviour
{
    private sealed class LegPose
    {
        public Transform Upper;
        public Transform Lower;
        public Quaternion UpperRestRotation;
        public Quaternion LowerRestRotation;
        public Vector3 HingeAxis;
        public float PhaseOffset;
    }

    private static readonly string[] UpperLegNames =
    {
        "leg_LF_upper",
        "leg_LM_upper",
        "leg_LB_upper",
        "leg_RF_upper",
        "leg_RM_upper",
        "leg_RB_upper"
    };

    private static readonly string[] LowerLegNames =
    {
        "leg_LF_lower",
        "leg_LM_lower",
        "leg_LB_lower",
        "leg_RF_lower",
        "leg_RM_lower",
        "leg_RB_lower"
    };

    // Alternating tripods: LF/RM/LB and RF/LM/RB.
    private static readonly float[] TripodPhase =
    {
        0f,
        Mathf.PI,
        0f,
        Mathf.PI,
        0f,
        Mathf.PI
    };

    [Header("Movement Source")]
    [SerializeField] private NavMeshAgent movementAgent;
    [SerializeField] private Rigidbody movementBody;
    [SerializeField] private Transform movementRoot;
    [SerializeField, Min(0.1f)] private float fallbackReferenceSpeed = 5f;
    [SerializeField, Min(0f)] private float movingThreshold = 0.08f;

    [Header("Imported Model")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Transform body;

    [Header("Crawler Gait")]
    [SerializeField, Min(0.1f)] private float strideFrequency = 3.1f;
    [SerializeField, Range(0f, 35f)] private float strideAngle = 16f;
    [SerializeField, Range(0f, 30f)] private float liftAngle = 11f;
    [SerializeField, Range(0f, 40f)] private float lowerLegFold = 18f;
    [SerializeField, Min(1f)] private float poseResponse = 18f;
    [SerializeField, Min(0.1f)] private float speedBlendResponse = 7f;

    [Header("Body Motion")]
    [SerializeField, Range(0f, 0.2f)] private float movingBobHeight = 0.055f;
    [SerializeField, Range(0f, 0.08f)] private float idleBobHeight = 0.012f;
    [SerializeField, Range(0f, 8f)] private float bodyRollAngle = 2.2f;
    [SerializeField, Range(0f, 6f)] private float bodyPitchAngle = 1.25f;
    [SerializeField, Min(0.1f)] private float idleBreathingFrequency = 0.65f;

    private readonly List<LegPose> legs = new List<LegPose>(6);
    private Vector3 bodyRestPosition;
    private Quaternion bodyRestRotation;
    private Vector3 previousRootPosition;
    private float gaitPhase;
    private float movementBlend;
    private float idlePhase;
    private bool isBound;

    private void Awake()
    {
        ResolveMovementSource();
        RebindModel();
    }

    private void OnEnable()
    {
        ResolveMovementSource();

        if (!isBound)
        {
            RebindModel();
        }

        Transform root = EffectiveMovementRoot();
        if (root != null)
        {
            previousRootPosition = root.position;
        }
    }

    private void LateUpdate()
    {
        if (!isBound)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        float speed = ResolvePlanarSpeed(deltaTime);
        float referenceSpeed = movementAgent != null && movementAgent.speed > 0.01f
            ? movementAgent.speed
            : fallbackReferenceSpeed;
        float targetMovementBlend = speed <= movingThreshold
            ? 0f
            : Mathf.InverseLerp(movingThreshold, Mathf.Max(movingThreshold + 0.01f, referenceSpeed), speed);

        float blendLerp = 1f - Mathf.Exp(-speedBlendResponse * deltaTime);
        movementBlend = Mathf.Lerp(movementBlend, targetMovementBlend, blendLerp);
        idlePhase += deltaTime * idleBreathingFrequency * Mathf.PI * 2f;

        if (movementBlend > 0.001f)
        {
            float cadence = strideFrequency * Mathf.Lerp(0.72f, 1.15f, movementBlend);
            gaitPhase = Mathf.Repeat(gaitPhase + deltaTime * cadence * Mathf.PI * 2f, Mathf.PI * 2f);
        }

        AnimateLegs(deltaTime);
        AnimateBody(deltaTime);
    }

    private void OnDisable()
    {
        RestoreRestPose();
        movementBlend = 0f;
    }

    private void OnValidate()
    {
        fallbackReferenceSpeed = Mathf.Max(0.1f, fallbackReferenceSpeed);
        movingThreshold = Mathf.Max(0f, movingThreshold);
        strideFrequency = Mathf.Max(0.1f, strideFrequency);
        poseResponse = Mathf.Max(1f, poseResponse);
        speedBlendResponse = Mathf.Max(0.1f, speedBlendResponse);
    }

    public void RebindModel()
    {
        if (isBound)
        {
            RestoreRestPose();
        }

        legs.Clear();

        if (body == null)
        {
            body = FindDescendant("body");
        }

        if (body != null)
        {
            bodyRestPosition = body.localPosition;
            bodyRestRotation = body.localRotation;
        }

        for (int index = 0; index < UpperLegNames.Length; index++)
        {
            Transform upper = FindDescendant(UpperLegNames[index]);
            Transform lower = FindDescendant(LowerLegNames[index]);

            if (upper == null || lower == null)
            {
                continue;
            }

            Vector3 radial = new Vector3(upper.localPosition.x, 0f, upper.localPosition.z);
            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = UpperLegNames[index].Contains("_L") ? Vector3.left : Vector3.right;
            }

            radial.Normalize();
            Vector3 hingeAxis = Vector3.Cross(Vector3.up, radial).normalized;

            legs.Add(new LegPose
            {
                Upper = upper,
                Lower = lower,
                UpperRestRotation = upper.localRotation,
                LowerRestRotation = lower.localRotation,
                HingeAxis = hingeAxis,
                PhaseOffset = TripodPhase[index]
            });
        }

        isBound = body != null && legs.Count > 0;

        if (!isBound)
        {
            Debug.LogWarning(
                "SpiderCrawlerAnimation could not find the imported spidy body/leg transforms. " +
                "Place it on the gameplay root above the spidy.fbx visual.",
                this);
        }
    }

    public void Configure(
        NavMeshAgent sourceAgent,
        Transform importedModelRoot,
        Transform sourceRoot = null)
    {
        movementAgent = sourceAgent;
        movementRoot = sourceRoot != null
            ? sourceRoot
            : sourceAgent != null
                ? sourceAgent.transform
                : movementRoot;
        modelRoot = importedModelRoot != null ? importedModelRoot : modelRoot;
        movementBody = movementRoot != null
            ? movementRoot.GetComponent<Rigidbody>()
            : movementBody;

        ResolveMovementSource();
        RefreshBindings();
    }

    public void RefreshBindings()
    {
        body = null;
        RebindModel();

        Transform root = EffectiveMovementRoot();
        if (root != null)
        {
            previousRootPosition = root.position;
        }
    }

    private void ResolveMovementSource()
    {
        if (movementAgent == null)
        {
            movementAgent = GetComponentInParent<NavMeshAgent>();
        }

        if (movementAgent == null)
        {
            movementAgent = GetComponentInChildren<NavMeshAgent>();
        }

        if (movementBody == null)
        {
            movementBody = GetComponentInParent<Rigidbody>();
        }

        if (movementRoot == null)
        {
            movementRoot = EffectiveMovementRoot();
        }
    }

    private Transform EffectiveMovementRoot()
    {
        if (movementRoot != null)
        {
            return movementRoot;
        }

        if (movementAgent != null)
        {
            return movementAgent.transform;
        }

        if (movementBody != null)
        {
            return movementBody.transform;
        }

        return transform;
    }

    private float ResolvePlanarSpeed(float deltaTime)
    {
        Vector3 velocity;

        if (movementAgent != null && movementAgent.enabled && movementAgent.gameObject.activeInHierarchy)
        {
            velocity = movementAgent.velocity;
        }
        else if (movementBody != null)
        {
            velocity = movementBody.linearVelocity;
        }
        else
        {
            Transform root = EffectiveMovementRoot();
            if (root == null)
            {
                return 0f;
            }

            velocity = (root.position - previousRootPosition) / Mathf.Max(0.0001f, deltaTime);
            previousRootPosition = root.position;
        }

        velocity.y = 0f;
        return velocity.magnitude;
    }

    private void AnimateLegs(float deltaTime)
    {
        float poseLerp = 1f - Mathf.Exp(-poseResponse * deltaTime);

        foreach (LegPose leg in legs)
        {
            float phase = gaitPhase + leg.PhaseOffset;
            float wave = Mathf.Sin(phase);
            float lift = Mathf.Max(0f, Mathf.Sin(phase + Mathf.PI * 0.5f));
            float swingDegrees = wave * strideAngle * movementBlend;
            float liftDegrees = lift * liftAngle * movementBlend;
            float foldDegrees = lift * lowerLegFold * movementBlend;

            Quaternion strideRotation = Quaternion.AngleAxis(swingDegrees, Vector3.up);
            Quaternion liftRotation = Quaternion.AngleAxis(-liftDegrees, leg.HingeAxis);
            Quaternion foldRotation = Quaternion.AngleAxis(-foldDegrees, leg.HingeAxis);

            Quaternion upperTarget = strideRotation * liftRotation * leg.UpperRestRotation;
            Quaternion lowerTarget = strideRotation * foldRotation * leg.LowerRestRotation;

            leg.Upper.localRotation = Quaternion.Slerp(leg.Upper.localRotation, upperTarget, poseLerp);
            leg.Lower.localRotation = Quaternion.Slerp(leg.Lower.localRotation, lowerTarget, poseLerp);
        }
    }

    private void AnimateBody(float deltaTime)
    {
        if (body == null)
        {
            return;
        }

        float idleBob = Mathf.Sin(idlePhase) * idleBobHeight * (1f - movementBlend);
        float movingBob = Mathf.Abs(Mathf.Sin(gaitPhase * 2f)) * movingBobHeight * movementBlend;
        float roll = Mathf.Sin(gaitPhase) * bodyRollAngle * movementBlend;
        float pitch = Mathf.Sin(gaitPhase * 2f) * bodyPitchAngle * movementBlend;

        Vector3 targetPosition = bodyRestPosition + Vector3.up * (idleBob + movingBob);
        Quaternion targetRotation = bodyRestRotation * Quaternion.Euler(pitch, 0f, roll);
        float poseLerp = 1f - Mathf.Exp(-poseResponse * deltaTime);

        body.localPosition = Vector3.Lerp(body.localPosition, targetPosition, poseLerp);
        body.localRotation = Quaternion.Slerp(body.localRotation, targetRotation, poseLerp);
    }

    private Transform FindDescendant(string objectName)
    {
        Transform searchRoot = modelRoot != null ? modelRoot : transform;

        foreach (Transform candidate in searchRoot.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == objectName)
            {
                return candidate;
            }
        }

        return null;
    }

    private void RestoreRestPose()
    {
        foreach (LegPose leg in legs)
        {
            if (leg.Upper != null)
            {
                leg.Upper.localRotation = leg.UpperRestRotation;
            }

            if (leg.Lower != null)
            {
                leg.Lower.localRotation = leg.LowerRestRotation;
            }
        }

        if (body != null)
        {
            body.localPosition = bodyRestPosition;
            body.localRotation = bodyRestRotation;
        }
    }
}
