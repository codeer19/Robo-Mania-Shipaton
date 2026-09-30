using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class RobotPlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float acceleration = 38f;
    [SerializeField] private float deceleration = 52f;
    [Tooltip("How fast the travel direction can swing. Speed still ramps through acceleration, but changing direction must not have to pass through a standstill.")]
    [SerializeField, Min(90f)] private float turnRate = 1440f;
    [SerializeField] private float gravity = -25f;

    [Header("Input")]
    [Tooltip("Disable this when movement is supplied by touch, networking, or AI.")]
    [SerializeField] private bool useKeyboardInput = true;

    private CharacterController characterController;
    private Vector2 moveInput;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private bool externalInputActive;
    private bool reportedFirstMove;

    public bool UsesExternalInput => externalInputActive;

    /// <summary>Temporary speed scale (Shock Trap slow). Only MovementSlow writes it; 1 is normal.</summary>
    public float SpeedMultiplier { get; set; } = 1f;
    /// <summary>Overdrive Pad boost (1 when none); set only by <see cref="MovementBoost"/>.</summary>
    public float BoostMultiplier { get; set; } = 1f;

    public void ResetMovement()
    {
        moveInput = Vector2.zero;
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        PlayerCosmeticRuntimeLoadout.Attach(this);
        // Wheeled chassis loop. Silent while stationary, faded in and out by the
        // voice itself, and quiet enough to sit under combat.
        MovementAudioVoice.Attach(gameObject, ReleaseAudioCue.RobotMovement, 0.50f);
    }

    private void Update()
    {
        // Intro, death and respawn presentation temporarily disable the
        // CharacterController while keeping this input component alive. Do not
        // issue movement calls until the physical controller is active again.
        if (characterController == null ||
            !characterController.enabled ||
            !gameObject.activeInHierarchy)
        {
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            return;
        }

        if (useKeyboardInput && !externalInputActive && !Application.isMobilePlatform)
            moveInput = ReadKeyboardInput();

        Vector3 inputDirection = MatchSessionContext.Type == MatchType.HumanOnline
            ? TeamSides.CameraDirection(FortressDuelManager.ActiveArena?.MatchCamera, moveInput)
            : new Vector3(moveInput.x, 0f, moveInput.y);

        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();

        // Speed and heading are integrated separately. Driving the velocity
        // vector as a whole forced every direction change to decelerate through
        // zero first, which read as sliding on ice.
        float inputAmount = inputDirection.magnitude;
        float targetSpeed = inputAmount * moveSpeed * Mathf.Clamp(SpeedMultiplier, 0.2f, 1f) *
            Mathf.Clamp(BoostMultiplier, 1f, MovementBoost.MaximumMultiplier);
        float currentSpeed = horizontalVelocity.magnitude;

        Vector3 travelDirection = currentSpeed > 0.001f
            ? horizontalVelocity / currentSpeed
            : Vector3.zero;

        if (inputAmount > 0.001f)
        {
            if (!reportedFirstMove) { reportedFirstMove = true; Funnel.Mark("first_move"); }
            Vector3 desiredDirection = inputDirection / inputAmount;

            travelDirection = travelDirection == Vector3.zero
                ? desiredDirection
                : Vector3.RotateTowards(
                    travelDirection,
                    desiredDirection,
                    turnRate * Mathf.Deg2Rad * Time.deltaTime,
                    0f
                ).normalized;
        }

        float rate = inputAmount > 0.001f ? acceleration : deceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.deltaTime
        );

        horizontalVelocity = travelDirection * currentSpeed;

        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 finalVelocity = horizontalVelocity;
        finalVelocity.y = verticalVelocity;

        characterController.Move(finalVelocity * Time.deltaTime);

        // Containment is owned solely by ArenaBoundaryEnforcer.LateUpdate.
        // Clamping here as well meant two systems corrected the same transform
        // every frame and toggled the CharacterController twice against a wall.
    }

    /// <summary>
    /// Called by the containment pass after it corrects this robot, so the
    /// stored velocity cannot keep pushing into a surface it was just moved out of.
    /// </summary>
    public void CancelVelocityAlong(Vector3 correction)
    {
        if (Mathf.Abs(correction.x) > 0.0001f)
            horizontalVelocity.x = 0f;

        if (Mathf.Abs(correction.z) > 0.0001f)
            horizontalVelocity.z = 0f;
    }

    public void SetMoveInput(Vector2 input)
    {
        moveInput = Vector2.ClampMagnitude(input, 1f);
    }

    public void SetExternalInputActive(bool active)
    {
        externalInputActive = active;

        if (!active)
            moveInput = Vector2.zero;
    }

    private Vector2 ReadKeyboardInput()
    {
        if (Keyboard.current == null)
            return Vector2.zero;

        float horizontal = 0f;
        float vertical = 0f;

        // WASD only. The arrow keys aim (RobotAimController), independently of
        // movement, so keyboard-only players can drive one way and shoot another.
        if (Keyboard.current.aKey.isPressed)
            horizontal -= 1f;

        if (Keyboard.current.dKey.isPressed)
            horizontal += 1f;

        if (Keyboard.current.wKey.isPressed)
            vertical += 1f;

        if (Keyboard.current.sKey.isPressed)
            vertical -= 1f;

        return new Vector2(horizontal, vertical);
    }
}
