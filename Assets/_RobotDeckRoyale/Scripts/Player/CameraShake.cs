using UnityEngine;

[DefaultExecutionOrder(10000)]
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Shake Feel")]
    [Tooltip("Peak sideways travel as a fraction of what the camera frames, not " +
             "in world units. Expressed this way so pulling the camera back or " +
             "changing the lens cannot quietly weaken the shake.")]
    [SerializeField] private float maxPositionShake = 0.12f;
    [SerializeField] private float maxRotationShake = 4f;
    [SerializeField] private float frequency = 31f;
    [SerializeField] private float recoverySpeed = 5.4f;

    private Camera shakeCamera;
    private TopDownCameraFollow follow;

    private float trauma;
    private float seed;
    private Vector3 appliedOffset;
    private Vector3 appliedRollAxis = Vector3.forward;
    private float appliedRoll;

    private void Awake()
    {
        Instance = this;
        seed = Random.Range(0f, 100f);
        shakeCamera = GetComponent<Camera>();
        follow = GetComponent<TopDownCameraFollow>();
    }

    /// <summary>
    /// Half the world height the camera currently frames. Shake is scaled by
    /// this so a given impulse covers the same fraction of the screen whatever
    /// the camera's distance or lens.
    /// </summary>
    private float ViewHalfHeight()
    {
        if (shakeCamera == null)
            return 1f;

        if (shakeCamera.orthographic)
            return Mathf.Max(0.01f, shakeCamera.orthographicSize);

        float distance = follow != null ? follow.FramingDistance : 20f;
        return Mathf.Max(
            0.01f,
            distance * Mathf.Tan(shakeCamera.fieldOfView * 0.5f * Mathf.Deg2Rad));
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Adds an impulse, where <paramref name="amount"/> is how strong the kick
    /// should be rather than how much trauma to bank.
    ///
    /// LateUpdate squares trauma so the shake decays with a snap instead of a
    /// linear ramp, but that squaring also crushed the impulse: a bullet hit
    /// asking for 0.035 became 0.0012 of the shake budget, which is nothing at
    /// all. Storing the root here cancels the square on the first frame, so a
    /// caller passing 0.035 actually gets 0.035 of the budget and the decay
    /// curve is left intact.
    /// </summary>
    public void Shake(float amount)
    {
        if (amount <= 0f)
            return;

        trauma = Mathf.Clamp01(
            Mathf.Sqrt(trauma * trauma + amount));
    }

    /// <summary>
    /// Runs before any follow rig's LateUpdate, so the previous frame's shake is
    /// removed and the follow always smooths from a clean pose. Leaving the
    /// offset baked into the transform made the follow lerp from a shaken
    /// position, which turned a sharp impulse into a lingering drift.
    /// </summary>
    private void Update()
    {
        ClearAppliedShake();
    }

    private void ClearAppliedShake()
    {
        if (appliedOffset != Vector3.zero)
        {
            transform.position -= appliedOffset;
            appliedOffset = Vector3.zero;
        }

        if (Mathf.Abs(appliedRoll) > 0.0001f)
        {
            transform.Rotate(appliedRollAxis, -appliedRoll, Space.World);
            appliedRoll = 0f;
        }
    }

    private void LateUpdate()
    {
        if (trauma <= 0.001f)
            return;

        float strength = trauma * trauma;
        float time = Time.unscaledTime * frequency;

        float x = (Mathf.PerlinNoise(seed, time) - 0.5f) * 2f;
        float y = (Mathf.PerlinNoise(seed + 21.7f, time) - 0.5f) * 2f;
        float roll = (Mathf.PerlinNoise(seed + 48.3f, time) - 0.5f) * 2f;

        // Screen-space movement, so it works with your angled top-down camera.
        float travel = maxPositionShake * strength * ViewHalfHeight();
        appliedOffset =
            transform.right * (x * travel) +
            transform.up * (y * travel);
        transform.position += appliedOffset;

        appliedRollAxis = transform.forward;
        appliedRoll = roll * maxRotationShake * strength;
        transform.Rotate(appliedRollAxis, appliedRoll, Space.World);

        trauma = Mathf.MoveTowards(
            trauma,
            0f,
            recoverySpeed * Time.unscaledDeltaTime
        );
    }
}
