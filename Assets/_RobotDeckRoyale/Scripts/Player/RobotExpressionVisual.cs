using UnityEngine;

/// <summary>
/// Character motion that belongs to individual parts rather than the chassis:
/// the eyes blinking, and the gun arm kicking up when the weapon fires.
///
/// Deliberately separate from <see cref="RobotMotionVisual"/>, which owns the
/// chassis transform, and from RobotHitReaction, which owns the flash. Each of
/// the three drives different properties, so they layer instead of fighting over
/// the same values. Everything here is procedural because the robot is a flat
/// collection of rigid meshes with no rig to key against.
/// </summary>
[DefaultExecutionOrder(220)]
public class RobotExpressionVisual : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("Left and right eye meshes. Left empty, the two small paired meshes " +
             "highest on the body are used.")]
    [SerializeField] private Transform leftEye;
    [SerializeField] private Transform rightEye;

    [Tooltip("The weapon arm that kicks when firing. Left empty, the widest mesh " +
             "on the body is used.")]
    [SerializeField] private Transform gunArm;

    [Header("Blink")]
    [SerializeField] private Vector2 blinkInterval = new Vector2(2.4f, 5.6f);
    [SerializeField, Min(0.02f)] private float blinkDuration = 0.11f;
    [Tooltip("How far the eye closes. 0 is shut, 1 is open.")]
    [SerializeField, Range(0.02f, 1f)] private float blinkClose = 0.12f;
    [Tooltip("Chance a blink is a quick double rather than a single.")]
    [SerializeField, Range(0f, 1f)] private float doubleBlinkChance = 0.25f;

    [Header("Weapon Arm")]
    [Tooltip("How far the arm swings when firing. The sign picks the direction, " +
             "so negate it if the arm swings the wrong way for a given robot.")]
    [SerializeField] private float armLiftAngle = -80f;
    [SerializeField, Min(0.01f)] private float armLiftDuration = 0.10f;
    [SerializeField, Min(0.01f)] private float armSettleDuration = 0.30f;

    [Tooltip("The imported body bakes both arms into a single mesh, so hinging it " +
             "raises both hands at once. When on, that mesh is split into its two " +
             "disconnected halves at load and only the weapon side is animated. " +
             "No-op on a body whose arms are already separate meshes.")]
    [SerializeField] private bool splitArmBar = true;

    [Tooltip("Which side carries the weapon. The other arm stays at rest.")]
    [SerializeField] private bool weaponOnRight = true;

    [Tooltip("How far the separated weapon arm kicks back on a shot, in metres. " +
             "Used instead of a swing, because the pods have no modelled shoulder.")]
    [SerializeField, Range(0f, 0.5f)] private float armRecoilDistance = 0.16f;

    private RobotBlaster blaster;
    private RobotRigBindings authoredRig;
    private RobotAnimationHooks animationHooks;

    private Vector3 leftEyeRestScale = Vector3.one;
    private Vector3 rightEyeRestScale = Vector3.one;
    private int leftEyeSquashAxis;
    private int rightEyeSquashAxis;

    private Quaternion armRestRotation;
    private Vector3 armRestLocalPosition;
    private Vector3 armPivotLocal;
    private Vector3 armLiftAxisParent = Vector3.right;
    private Transform armParent;
    private float armFiredAt = float.NegativeInfinity;

    // True once the two-arm bar has been separated, which changes where the arm
    // hinges: a lone arm swings from its shoulder, not from the bar's centre.
    private bool armIsSingleSide;

    // Where the separated arm meets the torso, in the arm mesh's own space.
    private Vector3 armShoulderMeshLocal;

    private float nextBlinkTime;
    private float blinkStartedAt = float.NegativeInfinity;
    private float blinkEndTime = float.NegativeInfinity;
    public float EyeOpenness { get; private set; } = 1f;

    private void Awake()
    {
        blaster = GetComponentInParent<RobotBlaster>();
        authoredRig = GetComponentInChildren<RobotRigBindings>(true);
        animationHooks = GetComponent<RobotAnimationHooks>();

        if (authoredRig != null && authoredRig.IsValid)
        {
            leftEye = authoredRig.LeftEye;
            rightEye = authoredRig.RightEye;
            gunArm = null; // Authored Fire owns arm position/rotation.
        }
        else { authoredRig = null; ResolveParts(); }
        ScheduleNextBlink();
    }

    private void Start()
    {
        // Deliberately not in Awake. RobotMotionVisual reparents the body meshes
        // into a runtime rig during its own Awake, and the arm's hinge is stored
        // in its parent's space -- captured any earlier it would describe a
        // parent the arm no longer has, and the hinge would drift on every shot.
        CaptureRestPose();
    }

    private void OnEnable()
    {
        if (blaster != null)
            blaster.Fired += HandleFired;
    }

    private void OnDisable()
    {
        if (blaster != null)
            blaster.Fired -= HandleFired;

        RestoreRestPose();
    }

    private void HandleFired(RobotBlaster weapon)
    {
        armFiredAt = Time.time;
    }

    /// <summary>Lets the bot drive the same kick without a RobotBlaster.</summary>
    public void NotifyFired()
    {
        armFiredAt = Time.time;
    }

    private void LateUpdate()
    {
        if (Time.deltaTime <= 0f) return;
        if (authoredRig != null && animationHooks != null && animationHooks.SuppressProcedural)
        {
            EyeOpenness = 1f;
            ApplyEyeOpenness(leftEye, leftEyeRestScale, leftEyeSquashAxis, 1f);
            ApplyEyeOpenness(rightEye, rightEyeRestScale, rightEyeSquashAxis, 1f);
            return;
        }
        UpdateBlink();
        if (authoredRig == null) UpdateArm();
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Drives the blink from a single end time rather than a counter.
    ///
    /// The counter version could wedge: if anything reset the remaining count
    /// while the start time kept refreshing, the eye never reached the branch
    /// that reopens it and stayed shut. Here the next blink is scheduled the
    /// moment one begins, so the start condition cannot retrigger, and openness
    /// is unconditionally 1 outside the window -- an eye left closed is not a
    /// state this can reach.
    /// </summary>
    private void UpdateBlink()
    {
        if (Time.time >= nextBlinkTime && Time.time >= blinkEndTime)
        {
            int count = Random.value < doubleBlinkChance ? 2 : 1;
            blinkStartedAt = Time.time;
            blinkEndTime = Time.time + blinkDuration * count;
            ScheduleNextBlink();
        }

        float openness = 1f;

        if (Time.time < blinkEndTime)
        {
            // Down and back up within each duration, so a blink reads as a snap
            // rather than a slow close, and a double blink is two of them.
            float age = Time.time - blinkStartedAt;
            float half = Mathf.PingPong(age / blinkDuration * 2f, 1f);
            openness = Mathf.Lerp(1f, blinkClose, half);
        }

        ApplyEyeOpenness(leftEye, leftEyeRestScale, leftEyeSquashAxis, openness);
        ApplyEyeOpenness(rightEye, rightEyeRestScale, rightEyeSquashAxis, openness);
        EyeOpenness = openness;
    }

    private static void ApplyEyeOpenness(
        Transform eye, Vector3 restScale, int axis, float openness)
    {
        if (eye == null)
            return;

        Vector3 scale = restScale;
        scale[axis] = restScale[axis] * openness;
        eye.localScale = scale;
    }

    private void ScheduleNextBlink()
    {
        nextBlinkTime = Time.time + Random.Range(
            Mathf.Min(blinkInterval.x, blinkInterval.y),
            Mathf.Max(blinkInterval.x, blinkInterval.y));
    }

    // ------------------------------------------------------------------

    private void UpdateArm()
    {
        if (gunArm == null)
            return;

        // Anything that reparents the arm invalidates the cached hinge.
        if (gunArm.parent != armParent)
            CaptureArmPivot();

        float age = Time.time - armFiredAt;
        float angle = 0f;

        if (age >= 0f && age < armLiftDuration + armSettleDuration)
        {
            if (age < armLiftDuration)
            {
                // Snap up fast: the kick should read on the frame the shot lands.
                float k = age / armLiftDuration;
                angle = armLiftAngle * (1f - (1f - k) * (1f - k));
            }
            else
            {
                // Then ease back down, slower than it went up.
                float k = (age - armLiftDuration) / armSettleDuration;
                angle = armLiftAngle * (1f - k) * (1f - k);
            }
        }

        if (armIsSingleSide)
        {
            ApplySingleArmRecoil(angle / Mathf.Max(1f, Mathf.Abs(armLiftAngle)));
            return;
        }

        // Swung about the shoulder rather than the barrel's own origin. Rotating
        // a centre-pivoted barrel see-saws it: the muzzle drops and the back end
        // swings up through the body, which reads as the top of the robot
        // turning rather than an arm being raised.
        Quaternion swing = Quaternion.AngleAxis(angle, armLiftAxisParent);
        gunArm.localRotation = swing * armRestRotation;
        gunArm.localPosition =
            armPivotLocal + swing * (armRestLocalPosition - armPivotLocal);
    }

    /// <summary>
    /// Recoil for a single separated arm, as a shove rather than a swing.
    ///
    /// The arms on this body are short pods stuck to the sides of the torso with
    /// no modelled shoulder, so there is no pivot that lets a lone pod rotate far
    /// enough to read without part of it passing through the chest. Driving the
    /// pod back and up along the robot's own axes instead is unconditionally
    /// safe -- it cannot intersect the body at any strength -- and at this scale
    /// a kick back from the muzzle reads as recoil more clearly than a rotation
    /// does anyway.
    ///
    /// Replace this with the authored Fire clip once the rigged body is in use;
    /// that rig has real Shoulder_R and Arm_R bones to swing from.
    /// </summary>
    private void ApplySingleArmRecoil(float strength)
    {
        Transform parent = gunArm.parent != null ? gunArm.parent : transform;

        Vector3 back = parent.InverseTransformDirection(-transform.forward);
        Vector3 up = parent.InverseTransformDirection(transform.up);

        // Rotation is deliberately untouched: the imported part carries a baked
        // orientation, and writing a separately captured rest rotation over it
        // was enough to lose that and fling the pod through the body.
        gunArm.localPosition = armRestLocalPosition +
            (back * armRecoilDistance + up * (armRecoilDistance * 0.45f)) * strength;
    }

    // ------------------------------------------------------------------

    private void ResolveParts()
    {
        if (gunArm == null)
            gunArm = FindWidestPart();

        if (splitArmBar && gunArm != null)
            TrySplitArmBar();

        if (leftEye == null || rightEye == null)
            FindEyePair();
    }

    /// <summary>
    /// Separates a mesh that contains both arms into one mesh per arm.
    ///
    /// The imported body bakes the arm pair into a single part, so the previous
    /// behaviour -- hinging that part about its own length -- necessarily raised
    /// both hands on every shot. The two arms are disconnected geometry with a
    /// wide empty band between them, so they can be split cleanly by which side
    /// of that band each triangle sits on.
    ///
    /// The weapon arm keeps the original GameObject, so every reference already
    /// cached against it (hit flash renderers, cosmetic material slots) stays
    /// valid. Only the idle arm becomes a new object.
    /// </summary>
    private void TrySplitArmBar()
    {
        MeshFilter filter = gunArm.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = gunArm.GetComponent<MeshRenderer>();

        if (filter == null || meshRenderer == null)
            return;

        Mesh source = filter.sharedMesh;

        // A non-readable mesh cannot be split in a player build. Leaving the bar
        // intact is the correct fallback: both arms lift, which is the old
        // behaviour, rather than the arm disappearing.
        if (source == null || !source.isReadable || source.vertexCount == 0)
            return;

        Vector3 size = source.bounds.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);

        if (!TryFindSeparationGap(source, axis, out float splitAt))
            return;

        Vector3[] vertices = source.vertices;

        Mesh positiveSide = BuildSide(source, vertices, axis, splitAt, true);
        Mesh negativeSide = BuildSide(source, vertices, axis, splitAt, false);

        if (positiveSide == null || negativeSide == null)
            return;

        // Work out which half sits on the robot's right in world space; the local
        // axis sign means nothing on an imported mesh with a baked orientation.
        Vector3 positiveCentre = gunArm.TransformPoint(positiveSide.bounds.center);
        bool positiveIsRight =
            Vector3.Dot(positiveCentre - transform.position, transform.right) > 0f;

        bool weaponIsPositive = positiveIsRight == weaponOnRight;

        Mesh weaponMesh = weaponIsPositive ? positiveSide : negativeSide;
        Mesh idleMesh = weaponIsPositive ? negativeSide : positiveSide;

        GameObject idleArm = new GameObject(gunArm.name + "_IdleArm");
        idleArm.layer = gunArm.gameObject.layer;
        Transform idleTransform = idleArm.transform;
        idleTransform.SetParent(gunArm.parent, false);
        idleTransform.localPosition = gunArm.localPosition;
        idleTransform.localRotation = gunArm.localRotation;
        idleTransform.localScale = gunArm.localScale;

        idleArm.AddComponent<MeshFilter>().sharedMesh = idleMesh;

        MeshRenderer idleRenderer = idleArm.AddComponent<MeshRenderer>();
        idleRenderer.sharedMaterials = meshRenderer.sharedMaterials;
        idleRenderer.shadowCastingMode = meshRenderer.shadowCastingMode;
        idleRenderer.receiveShadows = meshRenderer.receiveShadows;
        idleRenderer.lightProbeUsage = meshRenderer.lightProbeUsage;
        idleRenderer.reflectionProbeUsage = meshRenderer.reflectionProbeUsage;

        filter.sharedMesh = weaponMesh;

        // The arm turns about its own centre. This body has no modelled
        // shoulder -- the arms are short pods stuck on the sides of the torso --
        // so every attempt to derive a hinge point from the geometry put the
        // pivot somewhere the arm is not, and swung it through the chest. A
        // centre pivot cannot leave the pod, so the kick always reads as recoil
        // rather than as a limb detaching.
        armShoulderMeshLocal = weaponMesh.bounds.center;

        // Captured here, where the arm is definitely at rest and definitely
        // under its final parent. Relying on the shared CaptureRestPose pass for
        // this recorded a zero position, and the recoil then teleported the arm
        // to the body's origin -- which is what put arm geometry inside the chest.
        armRestLocalPosition = gunArm.localPosition;
        armRestRotation = gunArm.localRotation;
        armParent = gunArm.parent;
        armIsSingleSide = true;
    }

    /// <summary>
    /// Finds the empty band between two disconnected clusters of geometry along
    /// <paramref name="axis"/>, and returns the coordinate at its centre.
    /// Returns false when the mesh is one solid piece, which is the signal that
    /// the arms are already separate parts and no split is needed.
    /// </summary>
    private static bool TryFindSeparationGap(Mesh source, int axis, out float splitAt)
    {
        const int BinCount = 32;
        splitAt = 0f;

        float minimum = source.bounds.min[axis];
        float extent = source.bounds.size[axis];

        if (extent <= 0.0001f)
            return false;

        int[] histogram = new int[BinCount];
        Vector3[] vertices = source.vertices;

        for (int index = 0; index < vertices.Length; index++)
        {
            int bin = Mathf.Clamp(
                (int)((vertices[index][axis] - minimum) / extent * BinCount), 0, BinCount - 1);

            histogram[bin]++;
        }

        int bestStart = -1;
        int bestLength = 0;
        int runStart = -1;

        for (int bin = 0; bin < BinCount; bin++)
        {
            if (histogram[bin] == 0)
            {
                if (runStart < 0)
                    runStart = bin;

                int runLength = bin - runStart + 1;

                if (runLength > bestLength)
                {
                    bestLength = runLength;
                    bestStart = runStart;
                }
            }
            else
            {
                runStart = -1;
            }
        }

        // The gap must be substantial and have geometry on both sides, otherwise
        // this is a single part with a hollow rather than two arms.
        if (bestStart <= 0 ||
            bestStart + bestLength >= BinCount ||
            bestLength < BinCount / 8)
        {
            return false;
        }

        float centreBin = bestStart + bestLength * 0.5f;
        splitAt = minimum + centreBin / BinCount * extent;
        return true;
    }

    /// <summary>
    /// Copies the mesh and keeps only the triangles whose centroid falls on one
    /// side of the split. The vertex buffer is left whole so normals, UVs and
    /// tangents need no remapping; the unreferenced vertices cost nothing to
    /// draw and this model is small.
    ///
    /// The bounds are computed here from the vertices this half actually uses.
    /// RecalculateBounds cannot be used for that: it measures the whole vertex
    /// buffer, so both halves would report the full bar's extents. Everything
    /// downstream reads those bounds -- the shoulder pivot and the test for
    /// which half is on the robot's right -- so getting them wrong hinged the
    /// arm from the far end of the bar and could pick the wrong arm entirely.
    /// </summary>
    private static Mesh BuildSide(
        Mesh source, Vector3[] vertices, int axis, float splitAt, bool positive)
    {
        Mesh side = Instantiate(source);
        side.name = source.name + (positive ? "_SideA" : "_SideB");

        var kept = new System.Collections.Generic.List<int>(256);
        int total = 0;

        Vector3 minimum = Vector3.positiveInfinity;
        Vector3 maximum = Vector3.negativeInfinity;

        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            int[] triangles = source.GetTriangles(subMesh);
            kept.Clear();

            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                float centroid = (
                    vertices[triangles[index]][axis] +
                    vertices[triangles[index + 1]][axis] +
                    vertices[triangles[index + 2]][axis]) / 3f;

                if (centroid >= splitAt != positive)
                    continue;

                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[index + corner];
                    kept.Add(vertex);
                    minimum = Vector3.Min(minimum, vertices[vertex]);
                    maximum = Vector3.Max(maximum, vertices[vertex]);
                }
            }

            total += kept.Count;
            side.SetTriangles(kept, subMesh, false);
        }

        if (total == 0)
        {
            DestroyImmediate(side);
            return null;
        }

        Bounds bounds = new Bounds();
        bounds.SetMinMax(minimum, maximum);
        side.bounds = bounds;

        return side;
    }

    /// <summary>
    /// The weapon arm is the part that reaches furthest across the body, which on
    /// this robot is the barrel assembly.
    /// </summary>
    private Transform FindWidestPart()
    {
        Transform widest = null;
        float widestExtent = 0f;

        foreach (Renderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            float extent = renderer.bounds.size.x;

            if (extent > widestExtent)
            {
                widestExtent = extent;
                widest = renderer.transform;
            }
        }

        return widest;
    }

    /// <summary>
    /// Eyes are the small pair sitting highest on the body, mirrored across the
    /// robot's centre line. Matching on shape and placement rather than on mesh
    /// names means a re-exported or replaced robot still blinks.
    /// </summary>
    private void FindEyePair()
    {
        Transform best = null;
        Transform bestPartner = null;
        float bestHeight = float.NegativeInfinity;

        MeshRenderer[] parts = GetComponentsInChildren<MeshRenderer>(true);

        foreach (MeshRenderer candidate in parts)
        {
            Bounds bounds = candidate.bounds;

            if (bounds.size.x > 0.6f || bounds.size.y > 0.6f)
                continue;

            Vector3 local = transform.InverseTransformPoint(bounds.center);

            if (Mathf.Abs(local.x) < 0.03f)
                continue;

            foreach (MeshRenderer other in parts)
            {
                if (other == candidate)
                    continue;

                Vector3 otherLocal =
                    transform.InverseTransformPoint(other.bounds.center);

                bool mirrored =
                    Mathf.Abs(otherLocal.x + local.x) < 0.08f &&
                    Mathf.Abs(otherLocal.y - local.y) < 0.06f &&
                    Mathf.Abs(otherLocal.z - local.z) < 0.08f;

                if (!mirrored || local.y <= bestHeight)
                    continue;

                bestHeight = local.y;
                best = local.x < 0f ? candidate.transform : other.transform;
                bestPartner = local.x < 0f ? other.transform : candidate.transform;
            }
        }

        if (best == null)
            return;

        leftEye = leftEye != null ? leftEye : best;
        rightEye = rightEye != null ? rightEye : bestPartner;
    }

    private void CaptureRestPose()
    {
        if (leftEye != null)
        {
            leftEyeRestScale = leftEye.localScale;
            leftEyeSquashAxis = ResolveVerticalAxis(leftEye);
        }

        if (rightEye != null)
        {
            rightEyeRestScale = rightEye.localScale;
            rightEyeSquashAxis = ResolveVerticalAxis(rightEye);
        }

        if (gunArm != null)
            CaptureArmPivot();
    }

    /// <summary>
    /// Works out where the arm should hinge from and which way it swings.
    ///
    /// The hinge is the rear end of the barrel along the robot's facing, which is
    /// where a shoulder would be. Everything is kept in the arm's parent space so
    /// the swing is unaffected by whatever baked orientation the imported mesh
    /// carries -- resolving the axis in the mesh's own local space meant a
    /// negative baked axis silently inverted the whole motion.
    /// </summary>
    private void CaptureArmPivot()
    {
        // The split path records its own rest pose at the moment it separates
        // the arms, which is the only point the arm is guaranteed to be at rest
        // under its final parent. Re-recording it here would overwrite that with
        // whatever pose the arm happens to be in.
        if (!armIsSingleSide)
        {
            armRestRotation = gunArm.localRotation;
            armRestLocalPosition = gunArm.localPosition;
        }

        armParent = gunArm.parent;

        Transform parent = gunArm.parent != null ? gunArm.parent : transform;

        // Rotate about the robot's right, so the arm swings in the vertical plane.
        armLiftAxisParent = parent.InverseTransformDirection(transform.right);
        armLiftAxisParent = armLiftAxisParent.sqrMagnitude > 0.0001f
            ? armLiftAxisParent.normalized
            : Vector3.right;

        armPivotLocal = armRestLocalPosition;
        armLiftAxisParent = parent.InverseTransformDirection(transform.right);

        MeshFilter filter = gunArm.GetComponent<MeshFilter>();

        if (filter == null || filter.sharedMesh == null)
            return;

        // Taken from the mesh's own extremes rather than the renderer's world
        // bounding box. For a part sitting at an angle the box corner is not the
        // end of the part, and hinging about the wrong point makes the arm slide
        // as it swings instead of pivoting.
        Bounds local = filter.sharedMesh.bounds;

        int longest = 0;
        if (local.size.y > local.size[longest]) longest = 1;
        if (local.size.z > local.size[longest]) longest = 2;

        Vector3 nearEnd = local.center;
        Vector3 farEnd = local.center;
        nearEnd[longest] = local.min[longest];
        farEnd[longest] = local.max[longest];

        if (armIsSingleSide)
        {
            CaptureSingleArmPivot(parent);
            return;
        }

        Vector3 nearWorld = gunArm.TransformPoint(nearEnd);
        Vector3 farWorld = gunArm.TransformPoint(farEnd);

        // The hands swing forward about the shoulder line the arms hang from,
        // which is this bar's own length. Rotating about that axis carries the
        // hands from resting down to thrown out in front; rotating across it
        // instead just tips the bar over sideways.
        //
        // The pivot sits on that axis, at the bar's centre, so the arms rotate
        // in place rather than the whole assembly swinging away from the body.
        Vector3 barDirection = (farWorld - nearWorld).normalized;

        armPivotLocal = parent.InverseTransformPoint(
            (nearWorld + farWorld) * 0.5f);

        if (barDirection.sqrMagnitude > 0.0001f)
            armLiftAxisParent = parent.InverseTransformDirection(barDirection);
    }

    /// <summary>
    /// Hinge for a single separated arm. It swings forward and up about the
    /// robot's right, from the shoulder -- the top inner corner of the arm,
    /// where it meets the body. Hinging a lone arm about the old bar centre
    /// would swing it out sideways away from the torso instead.
    /// </summary>
    private void CaptureSingleArmPivot(Transform parent)
    {
        // Built from the arm's *rest* transform rather than its current one, so
        // a re-capture triggered mid-swing cannot bake the swing into the pivot.
        Matrix4x4 rest = Matrix4x4.TRS(
            armRestLocalPosition, armRestRotation, gunArm.localScale);

        armPivotLocal = rest.MultiplyPoint3x4(armShoulderMeshLocal);
        armLiftAxisParent = parent.InverseTransformDirection(transform.right);

        if (armLiftAxisParent.sqrMagnitude > 0.0001f)
            armLiftAxisParent.Normalize();
        else
            armLiftAxisParent = Vector3.right;
    }

    /// <summary>
    /// Which of the part's own axes points along the robot's up. Imported meshes
    /// carry arbitrary baked orientations, so squashing local Y blind would close
    /// some eyes sideways.
    /// </summary>
    private int ResolveVerticalAxis(Transform part)
    {
        Vector3 localUp = part.InverseTransformDirection(transform.up);
        Vector3 absolute = new Vector3(
            Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));

        if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            return 0;

        return absolute.y >= absolute.z ? 1 : 2;
    }

    private Vector3 ResolveAxisAlignedTo(Transform part, Vector3 worldAxis)
    {
        Vector3 local = part.InverseTransformDirection(worldAxis);
        Vector3 absolute = new Vector3(
            Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));

        if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            return Vector3.right * Mathf.Sign(local.x);

        if (absolute.y >= absolute.z)
            return Vector3.up * Mathf.Sign(local.y);

        return Vector3.forward * Mathf.Sign(local.z);
    }

    private void RestoreRestPose()
    {
        if (leftEye != null)
            leftEye.localScale = leftEyeRestScale;

        if (rightEye != null)
            rightEye.localScale = rightEyeRestScale;

        if (gunArm != null)
        {
            gunArm.localRotation = armRestRotation;
            gunArm.localPosition = armRestLocalPosition;
        }

        blinkEndTime = float.NegativeInfinity;
        armFiredAt = float.NegativeInfinity;
    }
}
