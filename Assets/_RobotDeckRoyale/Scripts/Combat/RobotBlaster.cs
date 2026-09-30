using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class RobotBlaster : MonoBehaviour
{
    [SerializeField] private Transform muzzle;
    [SerializeField] private BasicProjectile projectilePrefab;
    [SerializeField] private float fireInterval = 0.25f;

    [Header("Arena Attack")]
    [SerializeField, Min(1)] private int maxAmmo = 3;
    [SerializeField, Min(0.05f)] private float reloadSecondsPerAmmo = 1.15f;

    [Header("Input")]
    [Tooltip("Disable this when firing is supplied by touch, networking, or AI.")]
    [SerializeField] private bool useMouseInput = true;
    [Tooltip("A fire request made during cooldown is honoured for this long instead of being dropped.")]
    [SerializeField, Range(0f, 0.4f)] private float fireBufferSeconds = 0.12f;
    [SerializeField] private GameObject muzzleEffectPrefab;
    [SerializeField] private float muzzleEffectLifetime = 1.5f;

    // Ammunition and cadence rules are shared with every other shooter so the
    // player and the AI cannot drift onto different combat maths.
    private readonly WeaponCore weapon = new WeaponCore();

    private float bufferedFireUntil;
    private bool fireHeld;
    private bool externalInputActive;
    private RobotAimController aimSource;

    public int CurrentAmmo => weapon.CurrentAmmo;
    public int MaxAmmo => weapon.MaxAmmo;
    public Transform Muzzle => muzzle;
    public float FireInterval => fireInterval;
    public float ProjectileRange =>
        projectilePrefab != null
            ? projectilePrefab.MaxTravelDistance
            : 0f;

    public float ProjectileHitRadius =>
        projectilePrefab != null ? projectilePrefab.HitRadius : 0.1f;

    public float ProjectileExplosionRadius =>
        projectilePrefab != null ? projectilePrefab.ExplosionRadius : 0f;

    public LayerMask ProjectileHitMask =>
        projectilePrefab != null ? projectilePrefab.HitMask : (LayerMask)~0;

    // Death and the non-combat match phases disable this component. Firing is
    // also driven by direct TryFire calls from touch input, which keep working
    // on a disabled MonoBehaviour, so the guard belongs here rather than in Update.
    public bool CanFire =>
        isActiveAndEnabled &&
        projectilePrefab != null &&
        muzzle != null &&
        weapon.IsReady(Time.time);

    public float ReloadProgress => weapon.GetReloadProgress(Time.time);

    public float ReloadSecondsPerAmmo => reloadSecondsPerAmmo;
    public bool UsesExternalInput => externalInputActive;

    public event Action<int, int> AmmoChanged;
    public event Action<RobotBlaster> Fired;

    /// <summary>Raised with the origin and direction of a shot that was taken.</summary>
    public event Action<Vector3, Vector3> Shot;

    /// <summary>
    /// The round this blaster launches. Read by the online avatar so an opponent's
    /// shot can be reproduced locally without shipping prefab references over the
    /// wire; both clients already hold the same projectile.
    /// </summary>
    public BasicProjectile ProjectilePrefab => projectilePrefab;

    /// <summary>The muzzle effect that accompanies a shot, for the same reason.</summary>
    public GameObject MuzzleEffectPrefab => muzzleEffectPrefab;

    /// <summary>
    /// Raised when a fire request could not be served because the magazine is
    /// empty. The HUD uses this so a dropped input is never silent.
    /// </summary>
    public event Action<RobotBlaster> FireRejected;

    private void Awake()
    {
        weapon.Configure(maxAmmo, reloadSecondsPerAmmo, fireInterval);
        weapon.AmmoChanged += HandleAmmoChanged;
        aimSource = GetComponent<RobotAimController>();

        if (projectilePrefab != null)
            CombatPool.Prewarm(projectilePrefab.gameObject, maxAmmo + 2);
    }

    private void HandleAmmoChanged(int current, int capacity)
    {
        AmmoChanged?.Invoke(current, capacity);
    }

    private void Update()
    {
        weapon.Tick(Time.time);

        if (useMouseInput && !externalInputActive && !Application.isMobilePlatform)
        {
            bool keyboardFire = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
            if (keyboardFire && aimSource != null) aimSource.AimForKeyboardFire();
            // Space and the left button feed one held-trigger flag, so pressing both
            // is still one trigger on the single TryFire path - never two shots.
            fireHeld = keyboardFire || ReadMouseFire();
        }

        // A held trigger keeps requesting shots, and a request made during the
        // fire interval stays queued briefly instead of being thrown away.
        if (fireHeld || Time.time < bufferedFireUntil)
            TryFire();
    }

    // A click that lands on a HUD control (the Spidy card, a build card) belongs
    // to that control for the whole press, even if the pointer then drags onto
    // the arena.
    private bool mousePressStartedOnUI;
    private bool ReadMouseFire()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return false;
        if (mouse.leftButton.wasPressedThisFrame)
            mousePressStartedOnUI = UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        return mouse.leftButton.isPressed && !mousePressStartedOnUI;
    }

    public bool TryFire()
    {
        if (!CanFire)
        {
            if (isActiveAndEnabled && projectilePrefab != null && muzzle != null)
            {
                if (!weapon.HasAmmo)
                    FireRejected?.Invoke(this);
                else
                    bufferedFireUntil = Time.time + fireBufferSeconds;
            }

            return false;
        }

        bufferedFireUntil = 0f;

        Vector3 origin = muzzle.position;
        Vector3 direction = GetLaunchRotation() * Vector3.forward;

        if (MatchSessionContext.Type == MatchType.HumanOnline)
        {
            if (NetworkedMatchState.Instance == null || !NetworkedMatchState.Instance.RequestFire(origin, direction)) return false;
        }
        else
        {
            ProjectileLauncher.Fire(
                projectilePrefab,
                origin,
                direction,
                gameObject,
                -1,
                0f,
                muzzleEffectPrefab,
                muzzleEffectLifetime);

            // Offline only. In an online match every client raises its own launch
            // from the replicated missile in OnlineCombatPresentation, so firing
            // here as well would give the shooter two sounds for one press.
            ReleaseAudio.PlayAt(ReleaseAudioCue.MissileFire, origin, UnityEngine.Random.Range(0.97f, 1.03f));
        }

        weapon.CommitShot(Time.time);
        Funnel.Mark("first_shot");
        Fired?.Invoke(this);

        // Carries the exact origin and direction that were used, so an online
        // opponent reproduces the shot that was actually taken rather than one
        // recomputed a frame later from a chassis that has since rotated.
        Shot?.Invoke(origin, direction);

        return true;
    }

    /// <summary>
    /// The chassis eases toward the aim target, so the muzzle can still be
    /// rotating when the shot leaves. Launching along the requested aim keeps
    /// the projectile, the aim guide and the player's intent in agreement,
    /// regardless of script execution order.
    /// </summary>
    private Quaternion GetLaunchRotation()
    {
        if (aimSource == null || !aimSource.HasAimTarget)
            return muzzle.rotation;

        Vector3 direction = aimSource.AimTarget - muzzle.position;
        direction.y = 0f;

        return direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : muzzle.rotation;
    }

    public void RestoreFullAmmo()
    {
        weapon.RestoreFull();
        bufferedFireUntil = 0f;
    }

    public void SetFireHeld(bool shouldFire)
    {
        fireHeld = shouldFire;
    }

    public void SetExternalInputActive(bool active)
    {
        externalInputActive = active;

        if (!active)
        {
            fireHeld = false;
            bufferedFireUntil = 0f;
        }
    }

}
