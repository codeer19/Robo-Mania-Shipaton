using UnityEngine;
using UnityEngine.UI;

public class AutoTurret : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private FortressTeam ownerTeam = FortressTeam.Blue;

    [Header("References")]
    [SerializeField] private FortressDuelManager duelManager;
    [SerializeField] private Transform rotatingHead;
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject projectilePrefab;

    [Header("Targeting")]
    [SerializeField, Min(1f)] private float detectionRange = 18f;
    [SerializeField, Min(1f)] private float firingRange = 17f;
    [SerializeField] private float targetScanInterval = 0.18f;
    [SerializeField] private float aimHeight = 1f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    [Header("Attack")]
    [SerializeField] private float rotationSpeed = 360f;
    [SerializeField] private float fireInterval = 0.65f;
    [SerializeField, Min(1)] private int damagePerShot = 8;

    private FortressTarget currentTarget;
    private float nextScanTime;
    private float nextFireTime;
    private MuzzleFlashVFX muzzleFlash;

    public Transform Muzzle => muzzle;
    public float DetectionRange => detectionRange;
    public float FiringRange => firingRange;
    public Transform RotatingHead => rotatingHead;
    public float TurnSpeed => rotationSpeed;
    public float FireInterval => fireInterval;
    public int DamagePerShot => damagePerShot;
    public float MuzzleHeight => muzzle.position.y - transform.position.y;
    public float MuzzleForward => Vector3.ProjectOnPlane(muzzle.position - transform.position, Vector3.up).magnitude;
    public BasicProjectile OnlineProjectile => projectilePrefab.GetComponent<BasicProjectile>();

    private void Awake()
    {
        if (duelManager == null)
        {
            duelManager = FindFirstObjectByType<FortressDuelManager>();
        }

        if (muzzle != null)
        {
            muzzleFlash = muzzle.GetComponent<MuzzleFlashVFX>();
            if (muzzleFlash == null)
                muzzleFlash = muzzle.gameObject.AddComponent<MuzzleFlashVFX>();
        }

        if (GetComponent<RobotHitReaction>() == null)
            gameObject.AddComponent<RobotHitReaction>();

        if (GetComponent<DamageNumberVFX>() == null)
            gameObject.AddComponent<DamageNumberVFX>();

        EnsureHealthBar();
    }

    private void EnsureHealthBar()
    {
        if (GetComponentInChildren<WorldHealthBar>(true) != null)
            return;

        GameObject barObject = new GameObject(
            "Turret Health Bar",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        barObject.transform.SetParent(transform, false);
        barObject.transform.localPosition = Vector3.up * 2.6f;
        barObject.transform.localScale = Vector3.one * 0.01f;

        Canvas canvas = barObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        // Add this after parenting so WorldHealthBar can resolve the turret's
        // Damageable/FortressTarget during Awake and subscribe immediately.
        barObject.AddComponent<WorldHealthBar>();
    }

    private void Update()
    {
        if (duelManager == null ||
            duelManager.CurrentPhase != FortressDuelPhase.Combat)
        {
            return;
        }

        if (Time.time >= nextScanTime)
        {
            currentTarget = FindBestTarget();
            nextScanTime = Time.time + targetScanInterval;
        }

        if (currentTarget == null)
        {
            return;
        }

        Damageable targetDamageable = currentTarget.Health;

        if (targetDamageable == null || targetDamageable.IsDead)
        {
            currentTarget = null;
            return;
        }

        float targetDistance = FlatDistance(
            transform.position,
            currentTarget.transform.position);

        if (targetDistance > detectionRange)
        {
            currentTarget = null;
            return;
        }

        RotateTowardsTarget(currentTarget.transform);

        if (targetDistance <= firingRange &&
            Time.time >= nextFireTime &&
            HasClearShot(currentTarget.transform, targetDamageable))
        {
            Fire(currentTarget.transform);
            nextFireTime = Time.time + fireInterval;
        }
    }

    private FortressTarget FindBestTarget()
    {
        // The shared registry, not FindObjectsByType: no array per scan.
        var allTargets = FortressTarget.Active;

        FortressTarget bestRobot = null;
        float bestRobotDistance = float.MaxValue;

        FortressTarget bestVault = null;
        float bestVaultDistance = float.MaxValue;

        for (int index = 0; index < allTargets.Count; index++)
        {
            FortressTarget possibleTarget = allTargets[index];
            if (possibleTarget == null ||
                !possibleTarget.isActiveAndEnabled ||
                possibleTarget.Team == ownerTeam)
            {
                continue;
            }

            Damageable damageable = possibleTarget.Health;

            if (damageable == null || damageable.IsDead)
            {
                continue;
            }

            float distance = FlatDistance(
                transform.position,
                possibleTarget.transform.position
            );

            if (distance > detectionRange)
            {
                continue;
            }

            bool isVault = possibleTarget.IsVault;

            if (isVault)
            {
                if (distance < bestVaultDistance)
                {
                    bestVault = possibleTarget;
                    bestVaultDistance = distance;
                }
            }
            else if (distance < bestRobotDistance)
            {
                bestRobot = possibleTarget;
                bestRobotDistance = distance;
            }
        }

        return bestRobot != null ? bestRobot : bestVault;
    }

    private void RotateTowardsTarget(Transform target)
    {
        Transform pivot =
            rotatingHead != null ? rotatingHead : transform;

        Vector3 direction = target.position - pivot.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.01f)
        {
            return;
        }

        Quaternion desiredRotation =
            Quaternion.LookRotation(direction.normalized);

        pivot.rotation = Quaternion.RotateTowards(
            pivot.rotation,
            desiredRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private bool HasClearShot(
        Transform target,
        Damageable targetDamageable)
    {
        Vector3 origin =
            muzzle != null
            ? muzzle.position
            : transform.position + Vector3.up * aimHeight;

        Vector3 targetPoint =
            target.position + Vector3.up * aimHeight;

        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;

        if (distance <= 0.01f)
        {
            return false;
        }

        return LineOfSightUtility.IsClear(
            origin,
            direction / distance,
            distance,
            lineOfSightMask,
            transform,
            targetDamageable);
    }

    private void Fire(Transform target)
    {
        if (projectilePrefab == null || target == null)
        {
            return;
        }

        Transform firePoint =
            muzzle != null ? muzzle : transform;

        Vector3 targetPoint = target.position + Vector3.up * aimHeight;
        Vector3 launchDirection = targetPoint - firePoint.position;

        if (launchDirection.sqrMagnitude <= 0.01f)
            return;

        BasicProjectile prefab =
            projectilePrefab.GetComponent<BasicProjectile>();

        if (prefab == null)
            return;

        ProjectileLauncher.Fire(
            prefab,
            firePoint.position,
            launchDirection,
            gameObject,
            damagePerShot);

        if (muzzleFlash != null)
            muzzleFlash.PlayFlash();
    }

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = new Color(1f, 0.7f, 0.12f, 0.75f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, firingRange);
    }
#endif
}
