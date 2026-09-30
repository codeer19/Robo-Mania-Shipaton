using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Touch controls for the arena.
///
/// Fixed bases and independent movable handles share the existing touch input.
/// Stick values remain in canvas reference units, including the existing dead
/// zones, movement response and camera-relative aiming.
///
/// The attack stick follows the arena-brawler convention:
///   tap             -> snap to the nearest enemy and fire immediately
///   drag            -> show the shot the weapon would actually take
///   release outside -> fire along that line
///   release inside  -> cancel, no shot and no ammo spent
/// </summary>
public class MobileCombatInput : MonoBehaviour
{
    [Header("Gameplay References")]
    [SerializeField] private RobotPlayerController movement;
    [SerializeField] private RobotAimController aim;
    [SerializeField] private RobotBlaster blaster;
    [SerializeField] private Camera gameplayCamera;

    [Header("Joystick UI")]
    [SerializeField] private RectTransform moveStickBase;
    [SerializeField] private RectTransform moveStickKnob;
    [SerializeField] private RectTransform aimStickBase;
    [SerializeField] private RectTransform aimStickKnob;

    [Header("Touch Feel")]
    [Tooltip("Thumb travel, in 1920x1080 canvas units, that equals full stick deflection.")]
    [SerializeField] private float moveStickRadius = 150f;
    [SerializeField] private float aimStickRadius = 160f;

    // Resting positions as a fraction of the full screen: low and outboard, so
    // the two sticks frame the action instead of crowding the middle.
    //
    // Measured against the safe area instead, this device reports roughly a 20%
    // inset on each side, which dragged both sticks towards screen centre. The
    // rest of the HUD does not honour that inset either - the summon card sits
    // well outside it - so the screen rect is the consistent basis. PlaceStickAt
    // still clamps the whole ring on screen, which is the guarantee that matters.
    private const float MoveStickHorizontal = 0.11f;
    private const float AimStickHorizontal = 0.81f;
    private const float StickVertical = 0.22f;
    [Tooltip("Stick travel below this fraction is ignored, so resting-thumb jitter cannot drive the robot.")]
    [SerializeField, Range(0f, 0.5f)] private float moveDeadzone = 0.14f;
    [Tooltip("Aim travel below this fraction cancels the shot on release.")]
    [SerializeField, Range(0f, 0.5f)] private float aimDeadzone = 0.18f;
    [Tooltip("Seconds for the move input to reach a new direction. 0 is raw and twitchy.")]
    [SerializeField, Range(0f, 0.2f)] private float moveSmoothing = 0.055f;
    [Tooltip("Response curve on stick magnitude. 1 is linear; above 1 gives finer control near the centre.")]
    [SerializeField, Range(1f, 2.5f)] private float moveResponseExponent = 1.35f;
    [SerializeField] private float aimDistance = 12f;

    [Header("Attack Button Behaviour")]
    [Tooltip("A touch shorter than this that barely moved counts as a tap, and auto-aims.")]
    [SerializeField] private float tapMaximumDuration = 0.28f;
    [Tooltip("Thumb travel in canvas units that stops a touch counting as a tap.")]
    [SerializeField] private float tapMaximumMovement = 34f;
    [SerializeField] private bool tapToAutoAim = true;
    [SerializeField] private float autoAimRange = 24f;
    [Tooltip("Fire continuously while the attack stick is held instead of firing on release. " +
             "Off matches the arena-brawler aim-then-release feel.")]
    [SerializeField] private bool continuousFireWhileAiming = false;

    [Header("Aim Guide")]
    [Tooltip("Authored aim-line prefab. When assigned it replaces the code-built LineRenderer.")]
    [SerializeField] private LineRenderer aimLinePrefab;
    [Tooltip("Authored ground reticle prefab. When assigned it replaces the code-built ring.")]
    [SerializeField] private LineRenderer targetReticlePrefab;
    [SerializeField] private float aimLineWidth = 0.16f;
    [SerializeField] private float reticleRadius = 0.42f;
    [SerializeField] private Color aimGuideColor =
        new Color(1f, 0.5f, 0.12f, 0.72f);
    [SerializeField] private Color aimBlockedColor =
        new Color(1f, 0.16f, 0.12f, 0.72f);

    private const float ReferenceHeight = 1080f;

    private int moveTouchId = -1;
    private int aimTouchId = -1;

    private Vector2 moveStart;
    private Vector2 aimStart;
    private Vector2 aimTouchOrigin;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private PointerEventData uiPointer;
    private EventSystem pointerEvents;
    private Vector2 movePosition;
    private Vector2 aimPosition;

    private float aimTouchStartTime;
    private bool aimTouchWasDragged;
    private Vector2 smoothedMoveInput;
    private Vector2 moveInputVelocity;

    // Set when the aim touch lifts; consumed by ApplyAimAndFire on the same
    // frame so the shot is resolved in one place.
    private bool fireOnReleaseQueued;
    private bool autoAimQueued;
    private Vector3 queuedFireDirection;

    private LineRenderer aimLine;
    private LineRenderer targetReticle;
    private Canvas controlsCanvas;
    private RectTransform controlsCanvasRect;
    private Sprite stickBaseSprite;
    private Sprite stickKnobSprite;
    private bool ownsGameplayInput;

    public bool UsesTouchInput => ownsGameplayInput;

    /// <summary>
    /// Screen pixels per canvas reference unit. Every radius in this component
    /// is authored in reference units so it feels identical on every device.
    /// </summary>
    private float CanvasScale =>
        controlsCanvas != null && controlsCanvas.scaleFactor > 0.0001f
            ? controlsCanvas.scaleFactor
            : Mathf.Max(0.2f, Screen.height / ReferenceHeight);

    private void Awake()
    {
        if (movement == null)
            movement = GetComponent<RobotPlayerController>();

        if (aim == null)
            aim = GetComponent<RobotAimController>();

        if (blaster == null)
            blaster = GetComponent<RobotBlaster>();

        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        CreateMobileControlsIfNeeded();
        ConfigureAnaloguePresentation();
        UpdateFixedStickLayout();
        CreateAimGuide();
        SetStickVisible(moveStickBase, false);
        SetStickVisible(aimStickBase, false);
        SetGameplayInputOwnership(ShouldOwnTouchInput());
    }

    private void OnEnable()
    {
        SetGameplayInputOwnership(ShouldOwnTouchInput());
    }

    private void OnDisable()
    {
        if (movement != null)
            movement.SetMoveInput(Vector2.zero);

        if (blaster != null)
            blaster.SetFireHeld(false);

        ResetPendingAim();
        HideAimGuide();
        SetGameplayInputOwnership(false);
    }

    private void Update()
    {
        bool shouldOwnTouchInput = ShouldOwnTouchInput();

        if (shouldOwnTouchInput != ownsGameplayInput)
            SetGameplayInputOwnership(shouldOwnTouchInput);

        if (!ownsGameplayInput) return;
        bool canUseControls = Touchscreen.current != null &&
            movement != null && movement.isActiveAndEnabled &&
            FortressDuelManager.ActiveArena != null &&
            FortressDuelManager.ActiveArena.CurrentPhase == FortressDuelPhase.Combat;
        if (!canUseControls)
        {
            moveTouchId = aimTouchId = -1;
            ResetPendingAim();
            smoothedMoveInput = moveInputVelocity = Vector2.zero;
            if (movement != null) movement.SetMoveInput(Vector2.zero);
            if (blaster != null) blaster.SetFireHeld(false);
            SetStickVisible(moveStickBase, false);
            SetStickVisible(aimStickBase, false);
            HideAimGuide();
            return;
        }

        UpdateFixedStickLayout();
        ReadTouches();
        ApplyMovement();
        ApplyAimAndFire();
        UpdateJoystickVisuals();
    }

    private static bool touchEngaged;

    /// <summary>
    /// Whether the touch layer should take gameplay input away from the keyboard.
    ///
    /// A desktop browser advertises a Touchscreen device whether or not the
    /// machine has one, so keying ownership off its mere presence gave every
    /// desktop WebGL player the on-screen sticks and, because ownership also sets
    /// externalInputActive, silently switched off WASD and Space - the one input
    /// path the desktop build has to have.
    ///
    /// Presence is not use. A handheld still owns touch unconditionally, so the
    /// working analogue controls are untouched; anything else has to see a finger
    /// actually land, and hands the input straight back when the keyboard or
    /// mouse is used again.
    /// </summary>
    private static bool ShouldOwnTouchInput()
    {
        if (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld) return true;

        if (Touchscreen.current == null)
        {
            touchEngaged = false;
            return false;
        }

        if (Touchscreen.current.press.isPressed) touchEngaged = true;
        else if ((Keyboard.current != null && Keyboard.current.anyKey.isPressed) ||
                 (Mouse.current != null && Mouse.current.leftButton.isPressed)) touchEngaged = false;

        return touchEngaged;
    }

    private void SetGameplayInputOwnership(bool shouldOwn)
    {
        ownsGameplayInput = shouldOwn;

        if (movement != null)
            movement.SetExternalInputActive(shouldOwn);

        if (aim != null)
            aim.SetExternalInputActive(shouldOwn);

        if (blaster != null)
            blaster.SetExternalInputActive(shouldOwn);

        if (!shouldOwn)
        {
            moveTouchId = -1;
            aimTouchId = -1;
            aimTouchWasDragged = false;
            smoothedMoveInput = Vector2.zero;
            moveInputVelocity = Vector2.zero;
            if (moveStickKnob != null) moveStickKnob.anchoredPosition = Vector2.zero;
            if (aimStickKnob != null) aimStickKnob.anchoredPosition = Vector2.zero;
            ResetPendingAim();

            if (movement != null)
                movement.SetMoveInput(Vector2.zero);

            if (blaster != null)
                blaster.SetFireHeld(false);

            SetStickVisible(moveStickBase, false);
            SetStickVisible(aimStickBase, false);
            HideAimGuide();
            return;
        }

        SetStickVisible(moveStickBase, false);
        SetStickVisible(aimStickBase, false);
    }

    private void ReadTouches()
    {
        bool foundMoveTouch = false;
        bool foundAimTouch = false;
        float dragLimit = tapMaximumMovement * CanvasScale;

        foreach (TouchControl touch in Touchscreen.current.touches)
        {
            if (!touch.press.isPressed)
                continue;

            int id = touch.touchId.ReadValue();
            Vector2 position = touch.position.ReadValue();

            if (id == moveTouchId)
            {
                movePosition = position;
                foundMoveTouch = true;
            }

            if (id == aimTouchId)
            {
                aimPosition = position;
                foundAimTouch = true;

                if (Vector2.Distance(aimPosition, aimTouchOrigin) > dragLimit)
                    aimTouchWasDragged = true;
            }
        }

        // On release each stick drifts back to its resting corner, so the player
        // always has a visible home to aim for without it being the only place
        // a press is accepted.
        if (!foundMoveTouch && moveTouchId != -1)
        {
            moveTouchId = -1;
            PlaceStickAtRest(moveStickBase, true);
        }

        if (!foundAimTouch && aimTouchId != -1)
        {
            ResolveAimRelease();
            PlaceStickAtRest(aimStickBase, false);
        }

        foreach (TouchControl touch in Touchscreen.current.touches)
        {
            if (!touch.press.isPressed)
                continue;

            int id = touch.touchId.ReadValue();
            Vector2 position = touch.position.ReadValue();

            // A finger already assigned to one stick must never be claimed by
            // the other when it is dragged across the screen midline.
            if (id == moveTouchId || id == aimTouchId)
                continue;

            // Each stick owns a side of the screen and springs to wherever that
            // side is first touched, rather than waiting to be hit at a fixed
            // spot. A thumb lands where it lands; asking the player to find a
            // painted circle first is what made the controls feel pinned.
            // UI buttons such as Spidy keep their own pointer, and a held finger
            // never migrates between sticks.
            if (!touch.press.wasPressedThisFrame || IsOverButton(position)) continue;

            if (moveTouchId == -1 && IsOnMoveSide(position))
            {
                moveTouchId = id;
                PlaceStickAt(moveStickBase, position);
                // Read the origin back from the placed base: near a screen edge
                // the clamp shifts it, and the knob must pivot about what is
                // actually drawn or the thumb and the visual disagree.
                moveStart = StickCentre(moveStickBase);
                movePosition = position;
                continue;
            }

            if (aimTouchId == -1 && !IsOnMoveSide(position))
            {
                aimTouchId = id;
                PlaceStickAt(aimStickBase, position);
                aimStart = StickCentre(aimStickBase);
                aimTouchOrigin = position;
                aimPosition = position;
                aimTouchStartTime = Time.unscaledTime;
                aimTouchWasDragged = false;
            }
        }
    }

    /// <summary>
    /// Decides what a lifted attack touch meant. Kept separate from the touch
    /// scan so the tap/drag rules live in one readable place.
    /// </summary>
    private void ResolveAimRelease()
    {
        bool wasTap =
            !aimTouchWasDragged &&
            Time.unscaledTime - aimTouchStartTime <= tapMaximumDuration;

        Vector2 stick = GetStickInput(aimPosition, aimStart, aimStickRadius);

        if (wasTap && tapToAutoAim)
        {
            autoAimQueued = true;
        }
        else if (!continuousFireWhileAiming && stick.magnitude >= aimDeadzone)
        {
            queuedFireDirection = ConvertScreenInputToWorld(stick);
            fireOnReleaseQueued = true;
        }

        aimTouchId = -1;
        aimTouchWasDragged = false;
    }

    private void ApplyMovement()
    {
        if (movement == null)
            return;

        Vector2 target = Vector2.zero;

        if (moveTouchId != -1)
        {
            Vector2 stick = GetStickInput(movePosition, moveStart, moveStickRadius);
            float amount = stick.magnitude;

            if (amount > moveDeadzone)
            {
                // Rescale past the deadzone so the first responsive pixel of
                // travel still produces a small speed rather than a jump.
                amount = Mathf.InverseLerp(moveDeadzone, 1f, amount);
                amount = Mathf.Pow(amount, moveResponseExponent);

                Vector3 world = MatchSessionContext.Type == MatchType.HumanOnline
                    ? new Vector3(stick.normalized.x, 0f, stick.normalized.y) * amount
                    : ConvertScreenInputToWorld(stick) * amount;
                target = new Vector2(world.x, world.z);
            }
        }

        // Smoothing only shapes how fast the *direction* settles; a released
        // stick still stops the robot on the next frame so the controls never
        // feel like they are sliding.
        if (moveSmoothing > 0.001f && target.sqrMagnitude > 0.0001f)
        {
            smoothedMoveInput = Vector2.SmoothDamp(
                smoothedMoveInput, target, ref moveInputVelocity, moveSmoothing);
        }
        else
        {
            smoothedMoveInput = target;
            moveInputVelocity = Vector2.zero;
        }

        movement.SetMoveInput(smoothedMoveInput);
    }

    private void ApplyAimAndFire()
    {
        if (aim == null || blaster == null)
            return;

        if (autoAimQueued)
        {
            autoAimQueued = false;
            blaster.SetFireHeld(false);
            HideAimGuide();
            TryAutoAimAndFire();
            return;
        }

        if (fireOnReleaseQueued)
        {
            fireOnReleaseQueued = false;
            blaster.SetFireHeld(false);
            HideAimGuide();

            float range = ResolveGuideDistance();
            aim.SnapAimTarget(transform.position + queuedFireDirection * range);
            blaster.TryFire();
            return;
        }

        if (aimTouchId == -1)
        {
            blaster.SetFireHeld(false);
            HideAimGuide();
            return;
        }

        Vector2 stick = GetStickInput(aimPosition, aimStart, aimStickRadius);

        if (stick.magnitude < aimDeadzone)
        {
            // Inside the deadzone the shot is armed but not committed, which is
            // what makes dragging back to the centre a cancel.
            blaster.SetFireHeld(false);
            HideAimGuide();
            return;
        }

        Vector3 direction = ConvertScreenInputToWorld(stick);
        float guideDistance = ResolveGuideDistance();

        aim.SetAimTarget(transform.position + direction * guideDistance);
        blaster.SetFireHeld(continuousFireWhileAiming);

        ShowAimGuide(direction);
    }

    private float ResolveGuideDistance()
    {
        float projectileRange = blaster.ProjectileRange;
        return projectileRange > 0.01f ? projectileRange : aimDistance;
    }

    private void TryAutoAimAndFire()
    {
        FortressTarget ownTarget = GetComponent<FortressTarget>();

        if (ownTarget == null || aim == null || blaster == null)
            return;

        FortressTarget bestRobot = null;
        FortressTarget bestVault = null;
        float bestRobotDistance = float.MaxValue;
        float bestVaultDistance = float.MaxValue;

        foreach (FortressTarget candidate in
                 FindObjectsByType<FortressTarget>(FindObjectsSortMode.None))
        {
            if (candidate == null ||
                !candidate.isActiveAndEnabled ||
                candidate.Team == ownTarget.Team)
            {
                continue;
            }

            Damageable health = candidate.GetComponent<Damageable>();

            if (health == null || health.IsDead)
                continue;

            Vector3 offset = candidate.transform.position - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;

            if (distance > autoAimRange)
                continue;

            if (candidate.TargetType == FortressTargetType.Vault)
            {
                if (distance < bestVaultDistance)
                {
                    bestVault = candidate;
                    bestVaultDistance = distance;
                }
            }
            else if (distance < bestRobotDistance)
            {
                bestRobot = candidate;
                bestRobotDistance = distance;
            }
        }

        FortressTarget target = bestRobot != null ? bestRobot : bestVault;

        if (target == null)
        {
            // Nothing in range: fire straight ahead rather than swallowing the
            // tap, so the button always does something.
            aim.SnapAimTarget(
                transform.position + transform.forward * ResolveGuideDistance());
            blaster.TryFire();
            return;
        }

        aim.SnapAimTarget(target.transform.position + Vector3.up * 1.2f);
        blaster.TryFire();
    }

    private Vector2 GetStickInput(
        Vector2 currentPosition,
        Vector2 startPosition,
        float referenceRadius)
    {
        float screenRadius = Mathf.Max(1f, referenceRadius * CanvasScale);

        return Vector2.ClampMagnitude(
            (currentPosition - startPosition) / screenRadius, 1f);
    }

    private Vector3 ConvertScreenInputToWorld(Vector2 input)
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        if (gameplayCamera == null)
            return new Vector3(input.x, 0f, input.y).normalized;

        Vector3 cameraRight = gameplayCamera.transform.right;
        cameraRight.y = 0f;
        cameraRight.Normalize();

        Vector3 cameraForward = gameplayCamera.transform.forward;
        cameraForward.y = 0f;
        cameraForward.Normalize();

        return (cameraRight * input.x + cameraForward * input.y).normalized;
    }

    // ---------------------------------------------------------------------
    // Joystick presentation
    // ---------------------------------------------------------------------

    /// <summary>
    /// Positions a fixed base in canvas units when the screen or safe area changes.
    /// </summary>
    private void PlaceStickAt(RectTransform stickBase, Vector2 screenPoint)
    {
        if (stickBase == null || controlsCanvasRect == null)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                controlsCanvasRect, screenPoint, null, out Vector2 local))
        {
            return;
        }

        // Keep the whole ring on screen so a thumb near an edge still gets a
        // readable stick to push against.
        Vector2 half = controlsCanvasRect.rect.size * 0.5f;
        float margin = stickBase.sizeDelta.x * 0.5f;

        stickBase.anchoredPosition = new Vector2(
            Mathf.Clamp(local.x, -half.x + margin, half.x - margin),
            Mathf.Clamp(local.y, -half.y + margin, half.y - margin));
    }

    private void UpdateJoystickVisuals()
    {
        bool moving = moveTouchId != -1;
        bool aiming = aimTouchId != -1;

        SetStickVisible(moveStickBase, true);
        SetStickVisible(aimStickBase, true);

        if (moving)
        {
            MoveKnob(
                moveStickBase, moveStickKnob,
                GetStickInput(movePosition, moveStart, moveStickRadius),
                moveStickRadius);
        }
        else if (moveStickKnob != null)
        {
            ReturnKnob(moveStickKnob);
        }

        if (aiming)
        {
            MoveKnob(
                aimStickBase, aimStickKnob,
                GetStickInput(aimPosition, aimStart, aimStickRadius),
                aimStickRadius);
        }
        else if (aimStickKnob != null)
        {
            ReturnKnob(aimStickKnob);
        }
    }

    /// <summary>
    /// Places the knob at the thumb's actual offset. The travel is the same
    /// reference-unit radius the input uses, so the graphic and the input agree.
    /// </summary>
    private void MoveKnob(
        RectTransform stickBase,
        RectTransform stickKnob,
        Vector2 input,
        float referenceRadius)
    {
        if (stickBase == null || stickKnob == null)
            return;

        stickKnob.anchoredPosition = input * referenceRadius;
    }

    private void SetStickVisible(RectTransform stick, bool visible)
    {
        if (stick == null || stick.gameObject.activeSelf == visible)
            return;

        stick.gameObject.SetActive(visible);
    }

    // ---------------------------------------------------------------------
    // Aim guide
    // ---------------------------------------------------------------------

    private void CreateAimGuide()
    {
        // Prefer authored assets. The generated fallback below is placeholder
        // infrastructure and should be retired once real guide art exists.
        if (aimLinePrefab != null)
        {
            aimLine = Instantiate(aimLinePrefab, transform);
            aimLine.useWorldSpace = true;
            aimLine.positionCount = 2;
            aimLine.enabled = false;
        }

        if (targetReticlePrefab != null)
        {
            targetReticle = Instantiate(targetReticlePrefab, transform);
            targetReticle.useWorldSpace = true;
            targetReticle.loop = true;

            if (targetReticle.positionCount < 3)
                targetReticle.positionCount = 32;

            targetReticle.enabled = false;
        }

        if (aimLine == null)
        {
            GameObject lineObject = new GameObject("Mobile Aim Line");
            lineObject.transform.SetParent(transform, false);

            aimLine = lineObject.AddComponent<LineRenderer>();
            aimLine.positionCount = 2;
            aimLine.useWorldSpace = true;
            aimLine.widthMultiplier = aimLineWidth;
            aimLine.numCapVertices = 4;
            aimLine.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            aimLine.receiveShadows = false;
            aimLine.material = VfxParticleMaterial.ResolveUnlitInstance(null, aimGuideColor);
            aimLine.startColor = aimGuideColor;
            aimLine.endColor = aimGuideColor;
            aimLine.enabled = false;
        }

        if (targetReticle != null)
            return;

        GameObject reticleObject = new GameObject("Mobile Aim Reticle");
        reticleObject.transform.SetParent(transform, false);

        targetReticle = reticleObject.AddComponent<LineRenderer>();
        targetReticle.positionCount = 32;
        targetReticle.loop = true;
        targetReticle.useWorldSpace = true;
        targetReticle.widthMultiplier = aimLineWidth * 0.8f;
        targetReticle.numCapVertices = 3;
        targetReticle.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        targetReticle.receiveShadows = false;
        targetReticle.material = VfxParticleMaterial.ResolveUnlitInstance(null, aimGuideColor);
        targetReticle.startColor = aimGuideColor;
        targetReticle.endColor = aimGuideColor;
        targetReticle.enabled = false;
    }

    /// <summary>
    /// Draws the shot the weapon will actually take: it starts at the muzzle,
    /// stops at the first thing the projectile would hit, and sizes the reticle
    /// to the blast radius. The guide turns red when cover cuts the shot short,
    /// so a blocked angle reads before the ammo is spent.
    /// </summary>
    private void ShowAimGuide(Vector3 direction)
    {
        if (aimLine == null || targetReticle == null)
            return;

        Transform muzzle = blaster.Muzzle;
        Vector3 start = muzzle != null
            ? muzzle.position
            : transform.position + Vector3.up * 0.32f;

        float distance = ResolveGuideDistance();
        float sweepRadius = Mathf.Max(0.01f, blaster.ProjectileHitRadius);
        bool blocked = false;

        if (Physics.SphereCast(
                start,
                sweepRadius,
                direction,
                out RaycastHit blocker,
                distance,
                blaster.ProjectileHitMask,
                QueryTriggerInteraction.Ignore) &&
            !blocker.collider.transform.IsChildOf(transform))
        {
            distance = Mathf.Max(0.1f, blocker.distance);
            blocked = blocker.collider.GetComponentInParent<Damageable>() == null;
        }

        Color guideColour = blocked ? aimBlockedColor : aimGuideColor;
        Vector3 end = start + direction * distance;

        aimLine.SetPosition(0, start);
        aimLine.SetPosition(1, end);
        aimLine.startColor = guideColour;
        aimLine.endColor = new Color(guideColour.r, guideColour.g, guideColour.b, guideColour.a * 0.25f);
        aimLine.enabled = true;

        float blastRadius = blaster.ProjectileExplosionRadius;
        float ringRadius = blastRadius > 0.01f ? blastRadius : reticleRadius;

        for (int i = 0; i < targetReticle.positionCount; i++)
        {
            float angle = (float)i / targetReticle.positionCount * Mathf.PI * 2f;

            targetReticle.SetPosition(
                i,
                new Vector3(
                    end.x + Mathf.Cos(angle) * ringRadius,
                    transform.position.y + 0.06f,
                    end.z + Mathf.Sin(angle) * ringRadius));
        }

        targetReticle.startColor = guideColour;
        targetReticle.endColor = guideColour;
        targetReticle.enabled = true;
    }

    private void HideAimGuide()
    {
        if (aimLine != null)
            aimLine.enabled = false;

        if (targetReticle != null)
            targetReticle.enabled = false;
    }

    private void ResetPendingAim()
    {
        fireOnReleaseQueued = false;
        autoAimQueued = false;
        queuedFireDirection = Vector3.zero;
    }

    // ---------------------------------------------------------------------
    // Generated control art
    // ---------------------------------------------------------------------

    private void CreateMobileControlsIfNeeded()
    {
        if (moveStickBase != null &&
            moveStickKnob != null &&
            aimStickBase != null &&
            aimStickKnob != null)
        {
            controlsCanvas = moveStickBase.GetComponentInParent<Canvas>();
            controlsCanvasRect = controlsCanvas != null
                ? controlsCanvas.transform as RectTransform
                : null;
            return;
        }

        GameObject controlsObject = new GameObject(
            "MobileControls",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        controlsCanvas = controlsObject.GetComponent<Canvas>();
        controlsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        controlsCanvas.sortingOrder = 100;
        controlsCanvasRect = controlsObject.GetComponent<RectTransform>();

        CanvasScaler scaler = controlsObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        stickBaseSprite = CreateControlSprite(true);
        stickKnobSprite = CreateControlSprite(false);

        moveStickBase = CreateStickBase(
            controlsObject.transform,
            "MoveStickBase",
            moveStickRadius * 2f + 60f,
            new Color(0.30f, 0.55f, 1f, 0.42f));

        moveStickKnob = CreateStickKnob(
            moveStickBase,
            "MoveStickKnob",
            120f,
            new Color(0.42f, 0.78f, 1f, 0.95f));

        aimStickBase = CreateStickBase(
            controlsObject.transform,
            "AimStickBase",
            aimStickRadius * 2f + 60f,
            new Color(1f, 0.42f, 0.16f, 0.42f));

        aimStickKnob = CreateStickKnob(
            aimStickBase,
            "AimStickKnob",
            134f,
            new Color(1f, 0.60f, 0.20f, 0.96f));
    }

    private RectTransform CreateStickBase(
        Transform parent,
        string objectName,
        float size,
        Color color)
    {
        RectTransform rect = CreateUiImage(parent, objectName, stickBaseSprite, color);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);

        return rect;
    }

    private RectTransform CreateStickKnob(
        RectTransform parent,
        string objectName,
        float size,
        Color color)
    {
        RectTransform rect = CreateUiImage(parent, objectName, stickKnobSprite, color);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);

        return rect;
    }

    private RectTransform CreateUiImage(
        Transform parent,
        string objectName,
        Sprite sprite,
        Color color)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));

        imageObject.transform.SetParent(parent, false);

        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;

        return image.rectTransform;
    }

    /// <summary>
    /// Placeholder stick art. The base is a soft disc inside a heavy ring and
    /// the knob is a solid disc with a bright rim, which reads as a physical
    /// control at a glance instead of the flat wash the old sprite produced.
    /// Replace both with authored sprites when the UI art exists.
    /// </summary>
    private Sprite CreateControlSprite(bool isBase)
    {
        const int textureSize = 256;

        Texture2D texture = new Texture2D(
            textureSize, textureSize, TextureFormat.RGBA32, false);

        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[textureSize * textureSize];
        float centre = (textureSize - 1) * 0.5f;
        float radius = centre - 2f;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance =
                    Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre)) / radius;

                float alpha;
                float brightness = 1f;

                if (isBase)
                {
                    // soft fill, then a thick bright ring at the rim
                    float fill = Mathf.SmoothStep(0.30f, 0f, Mathf.InverseLerp(0.30f, 0.86f, distance));
                    float ring = Mathf.Max(
                        0f,
                        1f - Mathf.Abs(distance - 0.90f) / 0.10f);

                    alpha = Mathf.Clamp01(fill + ring);
                    brightness = Mathf.Lerp(0.75f, 1f, ring);
                }
                else
                {
                    // solid knob with a bright rim and a soft top highlight
                    float body = distance < 0.82f
                        ? 1f
                        : Mathf.Clamp01(1f - (distance - 0.82f) / 0.16f);

                    float rim = Mathf.Max(0f, 1f - Mathf.Abs(distance - 0.80f) / 0.14f);
                    float highlight =
                        Mathf.Clamp01(1f - Vector2.Distance(
                            new Vector2(x, y),
                            new Vector2(centre, centre + radius * 0.34f)) / (radius * 0.62f));

                    alpha = body;
                    brightness = Mathf.Clamp01(0.82f + rim * 0.20f + highlight * 0.22f);
                }

                pixels[y * textureSize + x] = new Color(brightness, brightness, brightness, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f);
    }
    private void ConfigureAnaloguePresentation()
    {
        // A base-only disc and a separate thumb stay independently drawable.
        // The previous whole-control PNG explicitly disabled both knob Images.
        if (stickBaseSprite == null) stickBaseSprite = CreateControlSprite(true);
        if (stickKnobSprite == null) stickKnobSprite = CreateControlSprite(false);
        ConfigureStick(moveStickBase, moveStickKnob);
        ConfigureStick(aimStickBase, aimStickKnob);
        if (aimStickKnob != null)
        {
            var horizontal = FrontendUI.Panel("Crosshair Horizontal", aimStickKnob,
                new Vector2(.23f,.47f), new Vector2(.77f,.53f), Color.white, false);
            var vertical = FrontendUI.Panel("Crosshair Vertical", aimStickKnob,
                new Vector2(.47f,.23f), new Vector2(.53f,.77f), Color.white, false);
            horizontal.raycastTarget = vertical.raycastTarget = false;
        }
    }

    private void ConfigureStick(RectTransform stick, RectTransform knob)
    {
        if (stick == null || knob == null) return;
        var image = stick.GetComponent<Image>();
        image.sprite = stickBaseSprite; image.type = Image.Type.Simple;
        image.preserveAspect = true; image.color = new Color(.53f,.57f,.63f,.85f);
        image.raycastTarget = false; image.enabled = true;
        var thumb = knob.GetComponent<Image>();
        thumb.sprite = stickKnobSprite; thumb.type = Image.Type.Simple;
        thumb.preserveAspect = true; thumb.color = new Color(.83f,.86f,.9f,.98f);
        thumb.raycastTarget = false; thumb.enabled = true;
        knob.gameObject.SetActive(true); knob.anchoredPosition = Vector2.zero;
    }

    private void UpdateFixedStickLayout()
    {
        var size = new Vector2Int(Screen.width, Screen.height);
        if (lastSafeArea == Screen.safeArea && lastScreenSize == size) return;
        lastSafeArea = Screen.safeArea; lastScreenSize = size;
        // Placed as a fraction of the safe area rather than as a margin derived
        // from the stick's own size. The size-derived version pushed both sticks
        // well inboard - they sat either side of screen centre instead of in the
        // bottom corners - and how far in they landed depended on the stick
        // radius and the canvas scale factor, so it drifted per device. Thumbs
        // rest at the bottom corners, so that is where the sticks belong.
        PlaceStickAtRest(moveStickBase, true);
        PlaceStickAtRest(aimStickBase, false);
        moveTouchId = aimTouchId = -1;
        ResetPendingAim();
    }

    /// <summary>Returns a stick to its idle corner. The resting spot is a hint, not a hit target.</summary>
    private void PlaceStickAtRest(RectTransform stick, bool isMoveStick)
    {
        if (stick == null) return;
        PlaceStickAt(stick, new Vector2(
            Mathf.Max(1, lastScreenSize.x) * (isMoveStick ? MoveStickHorizontal : AimStickHorizontal),
            Mathf.Max(1, lastScreenSize.y) * StickVertical));
    }

    /// <summary>
    /// Which stick owns a touch, by screen side.
    ///
    /// The split is the screen midline rather than either stick's drawn circle,
    /// which is what lets a stick be summoned anywhere on its own half.
    /// </summary>
    private bool IsOnMoveSide(Vector2 screenPoint) =>
        screenPoint.x < Mathf.Max(1, lastScreenSize.x) * 0.5f;

    private static Vector2 StickCentre(RectTransform stick) =>
        RectTransformUtility.WorldToScreenPoint(null, stick.position);

    private bool IsInsideStick(RectTransform stick, Vector2 point) => stick != null &&
        Vector2.Distance(point, StickCentre(stick)) <= stick.rect.width * .5f * CanvasScale;

    private bool IsOverButton(Vector2 point)
    {
        var events = EventSystem.current;
        if (events == null) return false;
        if (uiPointer == null || pointerEvents != events)
        { pointerEvents = events; uiPointer = new PointerEventData(events); }
        uiPointer.position = point; uiHits.Clear(); events.RaycastAll(uiPointer, uiHits);
        foreach (var hit in uiHits)
            if (hit.gameObject.GetComponentInParent<Selectable>() != null) return true;
        return false;
    }

    private static void ReturnKnob(RectTransform knob)
    {
        knob.anchoredPosition = Vector2.Lerp(knob.anchoredPosition, Vector2.zero,
            1f - Mathf.Exp(-30f * Time.unscaledDeltaTime));
        if (knob.anchoredPosition.sqrMagnitude < .01f) knob.anchoredPosition = Vector2.zero;
    }
}
