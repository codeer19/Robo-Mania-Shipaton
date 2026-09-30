using UnityEngine;
using UnityEngine.InputSystem;

public class RobotAimController : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 1080f;

    [Header("Input")]
    [Tooltip("Disable this when aim is supplied by touch, networking, or AI.")]
    [SerializeField] private bool useMouseInput = true;

    [Header("Desktop Aim Indicator")]
    [SerializeField] private Color aimIndicatorColor = new Color(1f, 0.58f, 0.16f, 0.62f);

    private enum DesktopAimSource { None, Keys, Mouse }

    private const float KeyAimDistance = 12f;
    // Accumulated pointer travel that hands aim back to the mouse after the arrow
    // keys took it: deliberate movement, however slow, reclaims it; a knock of
    // the desk does not.
    private const float MouseReclaimPixels = 14f;
    private float mouseTravelSinceKeys;

    private Camera mainCamera;
    private readonly Plane aimPlane = new Plane(Vector3.up, Vector3.zero);
    private bool hasAimTarget;
    private Vector3 aimTarget;
    private bool externalInputActive;

    // Desktop aim is owned by whichever explicit aim input was used last - the
    // arrow keys or the mouse - and never by movement. lastExplicitAimDirection
    // is what the arrows set and what persists while the robot drives in any
    // direction, so holding Right then W moves up while still aiming right.
    // Movement only supplies a facing before the player has aimed at all, so
    // a WASD + Space player still shoots where they drive on their first shots.
    private DesktopAimSource desktopAimSource;
    private Vector3 lastExplicitAimDirection;
    private bool hasExplicitAim;
    private LineRenderer aimIndicator;

    public bool UsesExternalInput => externalInputActive;
    public bool HasAimTarget => hasAimTarget;
    public Vector3 AimTarget => aimTarget;

    private bool DesktopInput => useMouseInput && !externalInputActive && !Application.isMobilePlatform;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void OnDisable()
    {
        if (aimIndicator != null) aimIndicator.enabled = false;
    }

    private void Update()
    {
        if (DesktopInput) ReadDesktopAim();
        UpdateAimIndicator();

        if (!hasAimTarget)
        {
            return;
        }

        Vector3 direction = aimTarget - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private void ReadDesktopAim()
    {
        Keyboard keys = Keyboard.current;
        Mouse mouse = Mouse.current;

        Vector2 arrows = Vector2.zero;
        if (keys != null)
        {
            arrows = new Vector2(
                (keys.rightArrowKey.isPressed ? 1f : 0f) - (keys.leftArrowKey.isPressed ? 1f : 0f),
                (keys.upArrowKey.isPressed ? 1f : 0f) - (keys.downArrowKey.isPressed ? 1f : 0f));
        }

        if (arrows.sqrMagnitude > 0f)
        {
            Vector3 direction = TeamSides.CameraDirection(mainCamera, arrows.normalized);
            if (direction.sqrMagnitude > 0.0001f)
            {
                desktopAimSource = DesktopAimSource.Keys;
                lastExplicitAimDirection = direction.normalized;
                hasExplicitAim = true;
                mouseTravelSinceKeys = 0f;
            }
        }
        else if (mouse != null && desktopAimSource != DesktopAimSource.Mouse)
        {
            mouseTravelSinceKeys += mouse.delta.ReadValue().magnitude;
            if (mouseTravelSinceKeys > MouseReclaimPixels || mouse.leftButton.wasPressedThisFrame)
                desktopAimSource = DesktopAimSource.Mouse;
        }

        if (desktopAimSource == DesktopAimSource.Mouse && TryReadMousePoint(out Vector3 point))
        {
            // Re-read every frame: the cursor names a place on the ground, so the
            // robot keeps looking at it while it drives past.
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.04f)
            {
                lastExplicitAimDirection = flat.normalized;
                hasExplicitAim = true;
            }
            SetAimTarget(point);
            return;
        }

        if (hasExplicitAim)
        {
            // Direction, not a fixed point: the aim travels with the robot, so
            // moving can never swing the chassis back toward a stale spot.
            SetAimTarget(transform.position + lastExplicitAimDirection * KeyAimDistance);
            return;
        }

        if (keys != null)
        {
            Vector2 move = new Vector2(
                (keys.dKey.isPressed ? 1f : 0f) - (keys.aKey.isPressed ? 1f : 0f),
                (keys.wKey.isPressed ? 1f : 0f) - (keys.sKey.isPressed ? 1f : 0f));
            if (move.sqrMagnitude > 0f)
            {
                Vector3 fallback = TeamSides.CameraDirection(mainCamera, move.normalized);
                if (fallback.sqrMagnitude > 0.0001f)
                    SetAimTarget(transform.position + fallback.normalized * KeyAimDistance);
            }
        }
    }

    /// <summary>
    /// Firing does not steer the robot. Space shoots along whatever the player is
    /// already aiming at, and only supplies a direction of its own when there is
    /// no aim at all yet - which is the case for a player who has not touched the
    /// mouse or the arrows and has not moved. Anything more than that is the
    /// weapon taking ownership of the chassis, which is what made the robot snap
    /// back to its spawn facing on every shot.
    /// </summary>
    public void AimForKeyboardFire()
    {
        if (externalInputActive || !isActiveAndEnabled) return;
        if (hasAimTarget) return;

        // SetAimTarget, never SnapAimTarget: the rotation is left to Update's
        // RotateTowards so firing cannot teleport the facing.
        SetAimTarget(transform.position + transform.forward * KeyAimDistance);
    }

    public void SetAimTarget(Vector3 worldPosition)
    {
        aimTarget = worldPosition;
        hasAimTarget = true;
    }

    public void SnapAimTarget(Vector3 worldPosition)
    {
        // Touch input calls this directly, so it must respect the disabled
        // state used by death, respawn and the non-combat match phases.
        if (!isActiveAndEnabled)
            return;

        SetAimTarget(worldPosition);

        Vector3 direction = worldPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(
                direction.normalized,
                Vector3.up
            );
        }
    }

    public void ClearAimTarget()
    {
        hasAimTarget = false;
    }

    public void SetExternalInputActive(bool active)
    {
        externalInputActive = active;
        if (active && aimIndicator != null) aimIndicator.enabled = false;
    }

    private bool TryReadMousePoint(out Vector3 point)
    {
        point = default;
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null || Mouse.current == null)
        {
            return false;
        }

        // Clamped for the same reason the build preview is: a perspective camera
        // throws on a screen point outside its rect, and the pointer leaves the
        // game view constantly while aiming near an edge.
        Ray mouseRay = mainCamera.ScreenPointToRay(
            BuildPlacementController.ClampToViewport(
                mainCamera, Mouse.current.position.ReadValue()));

        if (!aimPlane.Raycast(mouseRay, out float distance)) return false;
        point = mouseRay.GetPoint(distance);
        return true;
    }

    /// <summary>
    /// A short tapered arrow on the floor in front of the robot, desktop only,
    /// so keyboard aim is readable without a permanent laser across the arena.
    /// Touch play keeps its own drag guide.
    /// </summary>
    private void UpdateAimIndicator()
    {
        bool show = DesktopInput && isActiveAndEnabled && hasAimTarget;
        if (!show)
        {
            if (aimIndicator != null && aimIndicator.enabled) aimIndicator.enabled = false;
            return;
        }

        Vector3 direction = aimTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.04f)
        {
            if (aimIndicator != null) aimIndicator.enabled = false;
            return;
        }

        if (aimIndicator == null) CreateAimIndicator();
        direction.Normalize();
        Vector3 floor = transform.position;
        floor.y += 0.08f;
        aimIndicator.SetPosition(0, floor + direction * 1.35f);
        aimIndicator.SetPosition(1, floor + direction * 2.9f);
        aimIndicator.enabled = true;
    }

    private void CreateAimIndicator()
    {
        var indicator = new GameObject("Desktop Aim Indicator");
        indicator.transform.SetParent(transform, false);
        aimIndicator = indicator.AddComponent<LineRenderer>();
        aimIndicator.positionCount = 2;
        aimIndicator.useWorldSpace = true;
        aimIndicator.numCapVertices = 2;
        aimIndicator.alignment = LineAlignment.View;
        aimIndicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimIndicator.receiveShadows = false;
        // Tapers to a point: reads as an arrow head without extra geometry.
        aimIndicator.widthCurve = new AnimationCurve(new Keyframe(0f, 0.26f), new Keyframe(0.72f, 0.2f), new Keyframe(1f, 0f));
        aimIndicator.material = VfxParticleMaterial.ResolveUnlitInstance(null, aimIndicatorColor);
        aimIndicator.startColor = new Color(aimIndicatorColor.r, aimIndicatorColor.g, aimIndicatorColor.b, aimIndicatorColor.a * 0.45f);
        aimIndicator.endColor = aimIndicatorColor;
        aimIndicator.enabled = false;
    }
}
