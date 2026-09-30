using UnityEngine;

/// <summary>Explicit contract for authored robot visuals; never resolves generic mesh names.</summary>
[DisallowMultipleComponent]
public class RobotRigBindings : MonoBehaviour
{
    public Animator Animator;
    public Transform Chassis;
    public Transform Head;
    public Transform GunArm;
    public Transform LeftWheel;
    public Transform RightWheel;
    public Transform LeftEye;
    public Transform RightEye;
    public Transform ScreenPanel;
    public Transform FaceSocket;
    public Transform MuzzleVisual;
    public Vector3 WheelAxis = Vector3.right;
    public Vector3 EyeScaleAxis = Vector3.up;
    [Tooltip("The model draws its own eyes/screen. Cosmetics must recolour them " +
             "rather than building runtime optics on top of the authored face.")]
    public bool HasAuthoredOptics;

    [Tooltip("The model's own materials are the finished look. Runtime surface " +
             "recolouring is skipped, because the role classifier only has one " +
             "'primary' slot and would flatten a multi-tone body to a single colour. " +
             "Colour variants ship as authored model variants instead.")]
    public bool HasAuthoredSurfaces;
    public float WheelRadius = 0.57f;
    public float WheelHalfTrack = 0.913f;

    public bool IsValid => Animator != null && Animator.runtimeAnimatorController != null &&
        Chassis != null && Head != null && LeftWheel != null && RightWheel != null;
}
