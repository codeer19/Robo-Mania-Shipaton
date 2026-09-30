using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class EnergyCellPickup : MonoBehaviour
{
    [Header("Charge")]
    [SerializeField, Min(1)] private int chargeValue = 20;
    [SerializeField] private bool allowSwarmCollectors;

    [Header("Presentation")]
    [SerializeField] private Transform visualRoot;
    [SerializeField, Min(0f)] private float rotationSpeed = 80f;
    [SerializeField, Min(0f)] private float bobHeight = 0.14f;
    [SerializeField, Min(0f)] private float bobFrequency = 2.2f;

    [Header("Lifetime")]
    [SerializeField, Min(0f)] private float maximumLifetime = 24f;

    private SwarmChargeController controller;
    private Collider pickupCollider;
    private Rigidbody pickupRigidbody;
    private Vector3 visualStartPosition;
    private float spawnedAt;
    private bool collected;

    public int ChargeValue => chargeValue;
    public bool IsAvailable => !collected && isActiveAndEnabled;
    public bool WasCollected => collected;
    public Vector3 Position => transform.position;
    public SwarmChargeController Controller => controller;

    private void Awake()
    {
        pickupCollider = GetComponent<Collider>();
        pickupRigidbody = GetComponent<Rigidbody>();

        pickupCollider.isTrigger = true;
        pickupRigidbody.useGravity = false;
        pickupRigidbody.isKinematic = true;

        if (visualRoot == null)
            visualRoot = transform;

        visualStartPosition = visualRoot.localPosition;
        spawnedAt = Time.time;
    }

    private void Start()
    {
        if (controller == null)
            controller = FindFirstObjectByType<SwarmChargeController>();
    }

    private void Update()
    {
        if (visualRoot != null)
        {
            visualRoot.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);

            Vector3 localPosition = visualStartPosition;
            localPosition.y += Mathf.Sin(
                (Time.time - spawnedAt) * bobFrequency * Mathf.PI * 2f) * bobHeight;
            visualRoot.localPosition = localPosition;
        }

        if (maximumLifetime > 0f && Time.time >= spawnedAt + maximumLifetime)
            Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        TryCollect(other.GetComponentInParent<FortressTarget>());
    }

    private void OnDestroy()
    {
        if (controller != null)
            controller.NotifyEnergyCellRemoved(this);
    }

    public void Initialize(SwarmChargeController owner, int value)
    {
        controller = owner;
        chargeValue = Mathf.Max(1, value);
        // The centre pickup is an objective, not ambient loot. It must remain
        // available until one of the two robots actually collects it; otherwise
        // an unseen lifetime expiry can incorrectly advance the spawn cycle.
        maximumLifetime = 0f;
        spawnedAt = Time.time;
    }

    public bool TryCollect(FortressTarget collector)
    {
        if (collected || collector == null || !collector.isActiveAndEnabled)
            return false;

        Damageable collectorHealth = collector.GetComponent<Damageable>();
        if (collectorHealth == null || collectorHealth.IsDead || !IsEligibleRobot(collector))
            return false;

        if (controller == null)
            controller = FindFirstObjectByType<SwarmChargeController>();

        if (controller == null ||
            !controller.TryAddCharge(collector.Team, chargeValue, transform.position))
        {
            return false;
        }

        collected = true;
        pickupCollider.enabled = false;
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }

    private bool IsEligibleRobot(FortressTarget collector)
    {
        if (collector.GetComponent<RobotPlayerController>() != null ||
            collector.GetComponent<FortressBotAI>() != null)
        {
            return true;
        }

        return allowSwarmCollectors && collector.GetComponent<SwarmBotAI>() != null;
    }
}
