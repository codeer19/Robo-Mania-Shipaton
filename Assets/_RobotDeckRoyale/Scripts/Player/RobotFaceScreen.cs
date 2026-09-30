using UnityEngine;

/// <summary>
/// The expression that plays on the robot's chest screen.
///
/// The chassis reads as an object rather than a character: it blinks, but
/// nothing about it responds to being shot, winning a fight or nearly dying.
/// A screen face is the cheapest way to fix that on a model with no rig -- it
/// is one text mesh, it costs nothing, and it gives every combat beat a second
/// readable channel on top of the health bar.
///
/// State is derived, never pushed: this component listens to the robot's own
/// damage, weapon and health events, so it works identically for the player,
/// the AI opponent and anything else with a <see cref="Damageable"/>.
/// </summary>
[DefaultExecutionOrder(230)]
public class RobotFaceScreen : MonoBehaviour
{
    public enum FaceMood
    {
        Idle,
        Alert,
        Firing,
        Hurt,
        LowHealth,
        Dead,
        Cheer
    }

    [Header("Placement")]
    [Tooltip("The chest panel the face is drawn on. Left empty, the flattest " +
             "large forward-facing mesh on the body is used.")]
    [SerializeField] private Transform authoredScreenPanel;

    [Tooltip("How far the face floats off the panel, in metres. It has to clear " +
             "the chest bezel, because the face turns to meet the camera and " +
             "would otherwise cut into the surrounding geometry.")]
    [SerializeField] private float surfaceOffset = 0.16f;

    [Tooltip("Fraction of the panel's width the face fills.")]
    [SerializeField, Range(0.3f, 1f)] private float widthFill = 0.82f;

    [Tooltip("Turn the face toward the camera. The arena camera looks down from " +
             "behind, so a face locked flat to the chest is barely legible.")]
    [SerializeField] private bool faceCamera = true;

    [Tooltip("How far the face may turn from the panel's own facing, in degrees. " +
             "The arena camera looks down at ~52 degrees onto an upright chest " +
             "panel and the robot spins freely, so the turn needed to face the " +
             "camera regularly exceeds 90; clamping below 180 leaves the face " +
             "edge-on and unreadable in the only view that matters.")]
    [SerializeField, Range(0f, 180f)] private float maximumCameraTurn = 180f;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float hurtDuration = 0.55f;
    [SerializeField, Min(0.05f)] private float firingDuration = 0.35f;
    [SerializeField, Min(0.05f)] private float cheerDuration = 1.4f;
    [Tooltip("Health fraction below which the face stays worried.")]
    [SerializeField, Range(0f, 1f)] private float lowHealthFraction = 0.3f;

    [Header("Look")]
    [SerializeField] private float fontSize = 5.4f;
    [SerializeField] private Color idleColour = new Color(1f, 0.86f, 0.42f, 1f);
    [SerializeField] private Color alertColour = new Color(0.55f, 0.95f, 1f, 1f);
    [SerializeField] private Color hurtColour = new Color(1f, 0.36f, 0.30f, 1f);
    [SerializeField] private Color lowHealthColour = new Color(1f, 0.55f, 0.20f, 1f);
    [SerializeField] private Color cheerColour = new Color(0.5f, 1f, 0.6f, 1f);

    [Tooltip("Scale punch applied whenever the expression changes.")]
    [SerializeField, Range(1f, 2f)] private float changePunch = 1.35f;
    [SerializeField, Min(0.02f)] private float changePunchDuration = 0.18f;

    private Damageable damageable;
    private RobotBlaster blaster;

    private TMPro.TextMeshPro faceText;
    private Transform faceTransform;
    private Vector3 faceBaseScale = Vector3.one;

    private Transform screenPanel;

    /// <summary>Retry interval when the panel cannot be found, so a robot whose
    /// rig is mid-rebuild does not run the search every frame.</summary>
    private const float RebuildRetryInterval = 0.5f;

    private float nextRebuildAllowedAt;
    private FaceMood currentMood = FaceMood.Idle;
    private float moodHoldUntil;
    private float punchStartedAt = float.NegativeInfinity;
    private Camera gameplayCamera;
    private RobotRigBindings authoredRig;

    private void Awake()
    {
        damageable = GetComponentInParent<Damageable>();
        blaster = GetComponentInParent<RobotBlaster>();
        authoredRig = GetComponentInChildren<RobotRigBindings>(true);
        if (authoredRig != null && authoredRig.IsValid) authoredScreenPanel = authoredRig.ScreenPanel;
    }

    private void Start()
    {
        // Deliberately not in Awake. RobotMotionVisual rebuilds the body into a
        // runtime rig during its own Awake, destroying the original meshes --
        // a face parented to one of them in Awake is destroyed with it.
        TryRebuildFace();
    }

    /// <summary>
    /// Finds the panel to track. Only the *panel* is re-resolved here; the face
    /// object itself is created once and parented to this component, never to a
    /// body mesh, because the rig is destroyed and rebuilt on every respawn and
    /// on every cosmetic change -- a face parented into it is destroyed with it.
    /// </summary>
    private void TryRebuildFace()
    {
        if (Time.time < nextRebuildAllowedAt)
            return;

        nextRebuildAllowedAt = Time.time + RebuildRetryInterval;

        screenPanel = authoredScreenPanel != null
            ? authoredScreenPanel
            : ResolveScreenPanel();

        if (screenPanel == null)
            return;

        if (faceText == null)
            BuildFace();
    }

    private void OnEnable()
    {
        if (damageable != null)
        {
            damageable.DamageApplied += HandleDamage;
            damageable.Died += HandleDied;
        }

        if (blaster != null)
            blaster.Fired += HandleFired;

        CombatEvents.Killed += HandleAnyKill;
    }

    private void OnDisable()
    {
        if (damageable != null)
        {
            damageable.DamageApplied -= HandleDamage;
            damageable.Died -= HandleDied;
        }

        if (blaster != null)
            blaster.Fired -= HandleFired;

        CombatEvents.Killed -= HandleAnyKill;
    }

    private void LateUpdate()
    {
        // The body rig is rebuilt on respawn and by the cosmetic system, which
        // destroys the panel and the face parented to it. Rebuilding here means
        // a robot that respawns does not come back permanently expressionless.
        if (faceText == null || faceTransform == null || screenPanel == null)
        {
            TryRebuildFace();
            return;
        }

        ApplyMood(ResolveMood());
        UpdatePlacement();
        UpdatePunch();
    }

    // ------------------------------------------------------------------
    // Mood
    // ------------------------------------------------------------------

    private void HandleDamage(Damageable target, DamageInfo info)
    {
        SetMood(FaceMood.Hurt, hurtDuration);
    }

    private void HandleFired(RobotBlaster weapon)
    {
        // A hurt reaction outranks the attack face: being shot is the more
        // urgent thing to communicate.
        if (currentMood == FaceMood.Hurt && Time.time < moodHoldUntil)
            return;

        SetMood(FaceMood.Firing, firingDuration);
    }

    private void HandleDied(Damageable victim)
    {
        SetMood(FaceMood.Dead, 3f);
    }

    private void HandleAnyKill(Damageable victim, GameObject attacker)
    {
        if (attacker == null || damageable == null)
            return;

        // Cheer only for our own kills, and never for killing ourselves.
        if (victim == damageable)
            return;

        if (attacker != damageable.gameObject &&
            attacker.transform.root != damageable.transform.root)
        {
            return;
        }

        SetMood(FaceMood.Cheer, cheerDuration);
    }

    private void SetMood(FaceMood mood, float holdSeconds)
    {
        moodHoldUntil = Time.time + holdSeconds;

        if (currentMood == mood)
            return;

        currentMood = mood;
        punchStartedAt = Time.time;
    }

    /// <summary>
    /// Held moods win until they expire; otherwise the face falls back to a
    /// state derived from health and whether the weapon has a target.
    /// </summary>
    private FaceMood ResolveMood()
    {
        if (damageable != null && damageable.IsDead)
            return FaceMood.Dead;

        if (Time.time < moodHoldUntil)
            return currentMood;

        if (damageable != null &&
            damageable.MaxHealth > 0 &&
            (float)damageable.CurrentHealth / damageable.MaxHealth <= lowHealthFraction)
        {
            return FaceMood.LowHealth;
        }

        return FaceMood.Idle;
    }

    private void ApplyMood(FaceMood mood)
    {
        if (mood != currentMood)
        {
            currentMood = mood;
            punchStartedAt = Time.time;
        }

        faceText.text = GlyphFor(mood);
        faceText.color = ColourFor(mood);
    }

    private static string GlyphFor(FaceMood mood)
    {
        switch (mood)
        {
            case FaceMood.Alert: return "O_O";
            case FaceMood.Firing: return ">:D";
            case FaceMood.Hurt: return ">_<";
            case FaceMood.LowHealth: return "T_T";
            case FaceMood.Dead: return "x_x";
            case FaceMood.Cheer: return "^o^";
            default: return "^_^";
        }
    }

    private Color ColourFor(FaceMood mood)
    {
        switch (mood)
        {
            case FaceMood.Alert: return alertColour;
            case FaceMood.Firing: return alertColour;
            case FaceMood.Hurt: return hurtColour;
            case FaceMood.LowHealth: return lowHealthColour;
            case FaceMood.Dead: return hurtColour;
            case FaceMood.Cheer: return cheerColour;
            default: return idleColour;
        }
    }

    // ------------------------------------------------------------------
    // Presentation
    // ------------------------------------------------------------------

    /// <summary>
    /// Pins the face to the panel every frame. The face is not a child of the
    /// panel, so it has to follow it -- which is exactly what makes it survive
    /// the panel being destroyed and rebuilt underneath it.
    /// </summary>
    private void UpdatePlacement()
    {
        if (authoredRig != null && authoredRig.FaceSocket != null)
        {
            faceTransform.SetPositionAndRotation(authoredRig.FaceSocket.position, authoredRig.FaceSocket.rotation);
            return;
        }
        Renderer panelRenderer = screenPanel.GetComponent<Renderer>();

        Vector3 anchor = panelRenderer != null
            ? panelRenderer.bounds.center
            : screenPanel.position;

        Vector3 outward = screenPanel.forward;

        if (panelRenderer != null)
            TryMeasurePanel(panelRenderer, out outward, out _, out _);

        if (Vector3.Dot(outward, transform.forward) < 0f)
            outward = -outward;

        faceTransform.position = anchor + outward * surfaceOffset;

        Quaternion rest = Quaternion.LookRotation(outward, Vector3.up);

        if (!faceCamera)
        {
            faceTransform.rotation = rest;
            return;
        }

        if (gameplayCamera == null)
        {
            gameplayCamera = Camera.main;

            if (gameplayCamera == null)
            {
                faceTransform.rotation = rest;
                return;
            }
        }

        // TextMeshPro is readable from its +Z side, so +Z has to point *at* the
        // camera. Pointing it along the camera's view direction instead showed
        // the back of the text.
        Vector3 toCamera = gameplayCamera.transform.position - faceTransform.position;

        if (toCamera.sqrMagnitude < 0.0001f)
        {
            faceTransform.rotation = rest;
            return;
        }

        Quaternion look = Quaternion.LookRotation(toCamera.normalized, Vector3.up);

        faceTransform.rotation =
            Quaternion.RotateTowards(rest, look, maximumCameraTurn);
    }

    private void UpdatePunch()
    {
        float elapsed = Time.time - punchStartedAt;

        if (elapsed < 0f || elapsed > changePunchDuration)
        {
            faceTransform.localScale = faceBaseScale;
            return;
        }

        float progress = elapsed / changePunchDuration;
        float punch = Mathf.Sin(progress * Mathf.PI);

        faceTransform.localScale =
            faceBaseScale * Mathf.LerpUnclamped(1f, changePunch, punch);
    }

    /// <summary>
    /// Picks the chest screen out of the body meshes. The body is imported with
    /// generic mesh names ("Cube.006"), so nothing can be matched by name.
    ///
    /// A screen is an upright panel: thin along a horizontal axis, tall and wide
    /// on the other two. Requiring the thin axis to be horizontal is what rules
    /// out flat horizontal slabs like the chassis base, which a pure flatness
    /// score picks first.
    /// </summary>
    private Transform ResolveScreenPanel()
    {
        Transform best = null;
        float bestScore = 0f;

        foreach (MeshRenderer candidate in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (candidate == null || candidate.transform == transform)
                continue;

            if (!TryMeasurePanel(candidate, out _, out float faceArea, out float flatness))
                continue;

            // Flatness carries the decision: a screen is defined by being thin
            // for its size, not by being the biggest thing on the robot.
            if (faceArea * flatness > bestScore)
            {
                bestScore = faceArea * flatness;
                best = candidate.transform;
            }
        }

        return best;
    }

    /// <summary>
    /// Measures a mesh as a candidate screen and reports the world direction it
    /// faces.
    ///
    /// The extents have to be read on the renderer's own axes, because the
    /// chassis turns at runtime and a world-aligned box around a rotated panel
    /// measures as a cube. The *up test*, though, has to be done in world space:
    /// these meshes come out of Blender with a baked axis swap, so a panel's
    /// local Y is not the direction it stands up in.
    /// </summary>
    private static bool TryMeasurePanel(
        Renderer candidate,
        out Vector3 outward,
        out float faceArea,
        out float flatness)
    {
        outward = Vector3.forward;
        faceArea = 0f;
        flatness = 0f;

        Vector3 size = candidate.localBounds.size;

        if (size.x <= 0.001f || size.y <= 0.001f || size.z <= 0.001f)
            return false;

        Transform panel = candidate.transform;

        Vector3 thinAxis;
        float thin;

        if (size.x <= size.y && size.x <= size.z)
        {
            thin = size.x;
            thinAxis = panel.right;
        }
        else if (size.y <= size.z)
        {
            thin = size.y;
            thinAxis = panel.up;
        }
        else
        {
            thin = size.z;
            thinAxis = panel.forward;
        }

        // A slab lying flat faces up or down and is not a screen.
        if (Mathf.Abs(Vector3.Dot(thinAxis.normalized, Vector3.up)) > 0.7f)
            return false;

        faceArea = size.x * size.y * size.z / thin;
        flatness = faceArea / (thin * thin);

        // Require a genuinely flat panel, not a block seen edge-on.
        if (flatness < 8f)
            return false;

        outward = thinAxis.normalized;
        return true;
    }

    private void BuildFace()
    {
        GameObject faceObject = new GameObject("Face Screen");

        // TextMeshPro needs a RectTransform, and adding it replaces the
        // GameObject's plain Transform -- so the transform reference has to be
        // taken *after* the component exists. Caching faceObject.transform first
        // left this component holding a destroyed Transform, which silently
        // disabled every face update.
        faceText = faceObject.AddComponent<TMPro.TextMeshPro>();
        faceTransform = faceText.rectTransform;

        // Parented to this component, not to the panel. The panel belongs to a
        // rig that is torn down and rebuilt on respawn and on cosmetic changes;
        // anything parented into it is destroyed with it. UpdatePlacement keeps
        // the face glued to the panel instead.
        faceTransform.SetParent(transform, false);

        // The imported meshes carry a ~100x transform scale. Normalising here
        // keeps the text in metres whatever the parent scale turns out to be.
        Vector3 parentScale = transform.lossyScale;
        faceTransform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? 1f : 1f / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? 1f : 1f / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? 1f : 1f / parentScale.z);

        Renderer panelRenderer = screenPanel.GetComponent<Renderer>();

        faceText.text = GlyphFor(FaceMood.Idle);
        faceText.alignment = TMPro.TextAlignmentOptions.Center;
        faceText.color = idleColour;
        faceText.enableWordWrapping = false;
        faceText.raycastTarget = false;

        RectTransform rect = faceText.rectTransform;
        Vector3 worldSize = panelRenderer != null
            ? panelRenderer.bounds.size
            : Vector3.one * 0.5f;

        // The panel is only axis-aligned by accident, so take its two largest
        // world extents as the face area rather than assuming which is which.
        float largest = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));
        float smallest = Mathf.Min(worldSize.x, Mathf.Min(worldSize.y, worldSize.z));
        float middle = worldSize.x + worldSize.y + worldSize.z - largest - smallest;

        rect.sizeDelta = new Vector2(largest * widthFill, middle * widthFill);

        // Auto-sized so the glyph always fits whatever panel it landed on,
        // instead of one hand-tuned point size having to suit every robot.
        faceText.enableAutoSizing = true;
        faceText.fontSizeMin = 0.5f;
        faceText.fontSizeMax = fontSize;

        MeshRenderer faceRenderer = faceObject.GetComponent<MeshRenderer>();

        if (faceRenderer != null)
        {
            faceRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            faceRenderer.receiveShadows = false;
        }

        faceBaseScale = faceTransform.localScale;
    }
}
