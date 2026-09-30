using UnityEngine;

[RequireComponent(typeof(Damageable))]
[DefaultExecutionOrder(200)]
public class RobotHitReaction : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private float hitDuration = 0.18f;
    [SerializeField] private float widthPunch = 1.14f;
    [SerializeField] private float heightSquash = 0.86f;
    [SerializeField] private Color flashColor = new Color(1f, 0.78f, 0.24f, 1f);

    [Tooltip("Punch multiplier for chassis driven by RobotMotionVisual. Keeps wheeled robots from deforming like rubber while still reacting to a hit.")]
    [SerializeField, Range(0f, 1f)] private float wheeledPunchScale = 0.4f;

    [Header("Impact Motion")]
    [Tooltip("How far the model is shoved away from the shot before springing back, in metres.")]
    [SerializeField, Range(0f, 0.8f)] private float knockbackDistance = 0.26f;

    [Tooltip("How far the model leans away from the shot, in degrees.")]
    [SerializeField, Range(0f, 30f)] private float knockbackLean = 11f;

    [Tooltip("Seconds the model takes to spring back to rest after the shove.")]
    [SerializeField, Range(0.05f, 0.6f)] private float recoverDuration = 0.26f;

    [Header("Damage Scaling")]
    [Tooltip("Reaction strength for a hit that removes none of this robot's health.")]
    [SerializeField, Range(0f, 1f)] private float minimumIntensity = 0.42f;

    [Tooltip("Fraction of max health a hit must remove to produce a full-strength reaction.")]
    [SerializeField, Range(0.05f, 1f)] private float fullIntensityFraction = 0.28f;

    private static readonly int EmissionColorId =
        Shader.PropertyToID("_EmissionColor");

    private Damageable damageable;
    private Vector3 originalScale;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private float hitUntil;
    private Renderer[] renderers;
    private int[] materialSlots;
    private Material[] baselineMaterials;
    private MaterialPropertyBlock propertyBlock;
    private float punchScale = 1f;
    private Color[] baselineEmission;
    private bool baselineCaptured;
    private bool flashApplied;

    // Per-hit reaction state, refreshed on every incoming DamageInfo.
    private float hitIntensity = 1f;
    private Vector3 knockbackDirection;
    private float knockbackEndsAt;
    private bool drivenByMotionVisual;
    private RobotRigBindings authoredRig;
    public Vector3 AuthoredWorldOffset { get; private set; }

    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        authoredRig = GetComponentInChildren<RobotRigBindings>(true);

        if (visualRoot == null)
            visualRoot = transform.Find("Visual");

        if (visualRoot == null)
            visualRoot = transform.Find("TurretVisual_FBX");

        if (visualRoot == null)
        {
            // Deployables and imported models do not always use the player
            // prefab's "Visual" name. Pick the first rendered child so the
            // hit punch never scales the gameplay collider or health canvas.
            foreach (Transform child in transform)
            {
                if (child != null && child.GetComponentInChildren<Renderer>(true) != null)
                {
                    visualRoot = child;
                    break;
                }
            }
        }

        if (visualRoot != null)
        {
            originalScale = visualRoot.localScale;
            originalLocalPosition = visualRoot.localPosition;
            originalLocalRotation = visualRoot.localRotation;
            renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            var surfaces = new System.Collections.Generic.List<Renderer>();
            var slots = new System.Collections.Generic.List<int>();
            var materials = new System.Collections.Generic.List<Material>();
            foreach (Renderer surface in renderers)
            {
                if (!(surface is MeshRenderer) && !(surface is SkinnedMeshRenderer)) continue;
                var shared = surface.sharedMaterials;
                for (int slot = 0; slot < shared.Length; slot++)
                { surfaces.Add(surface); slots.Add(slot); materials.Add(shared[slot]); }
            }
            renderers = surfaces.ToArray(); materialSlots = slots.ToArray(); baselineMaterials = materials.ToArray();
            propertyBlock = new MaterialPropertyBlock();

            // RobotMotionVisual drives position and rotation on a child rig and
            // leaves scale to this component, so a reduced punch layers cleanly
            // rather than having to be suppressed entirely.
            drivenByMotionVisual =
                visualRoot.GetComponent<RobotMotionVisual>() != null;

            punchScale = drivenByMotionVisual ? wheeledPunchScale : 1f;
        }
    }

    private void OnEnable()
    {
        damageable.DamageApplied += OnDamageApplied;
    }

    private void OnDisable()
    {
        AuthoredWorldOffset = Vector3.zero;
        damageable.DamageApplied -= OnDamageApplied;

        if (visualRoot != null)
        {
            visualRoot.localScale = originalScale;

            if (!drivenByMotionVisual)
            {
                visualRoot.localPosition = originalLocalPosition;
                visualRoot.localRotation = originalLocalRotation;
            }
        }

        ApplyFlash(0f);
    }

    private void LateUpdate()
    {
        if (visualRoot == null)
            return;

        if (authoredRig != null && authoredRig.IsValid)
        {
            float remaining = hitUntil - Time.time;
            float strength = remaining > 0f ? Mathf.Clamp01(remaining / Mathf.Max(0.01f, hitDuration)) * hitIntensity : 0f;
            ApplyFlash(strength);
            float age = Time.time - (hitUntil - hitDuration);
            AuthoredWorldOffset = !damageable.IsDead && age >= 0f && age < 0.12f
                ? knockbackDirection * (0.03f * hitIntensity * Mathf.Pow(1f - age / 0.12f, 2f)) : Vector3.zero;
            return;
        }
        UpdateSquash();
        UpdateKnockback();
    }

    /// <summary>
    /// Squash and emissive flash. The curve is asymmetric now -- it snaps to the
    /// deformed pose almost instantly and eases out of it -- because the old
    /// symmetric sine spent as long winding up as recovering, which read as a
    /// soft wobble rather than an impact.
    /// </summary>
    private void UpdateSquash()
    {
        float remaining = hitUntil - Time.time;

        if (remaining <= 0f)
        {
            visualRoot.localScale = Vector3.Lerp(
                visualRoot.localScale,
                originalScale,
                Time.deltaTime * 18f);

            ApplyFlash(0f);
            return;
        }

        float progress = 1f - remaining / hitDuration;

        // Fast attack, slow release: full deformation is reached in the first
        // 15% of the window and decays over the rest.
        float punch = progress < 0.15f
            ? progress / 0.15f
            : 1f - Mathf.SmoothStep(0f, 1f, (progress - 0.15f) / 0.85f);

        punch *= hitIntensity;

        Vector3 hitScale = new Vector3(
            originalScale.x * Mathf.LerpUnclamped(1f, widthPunch, punchScale),
            originalScale.y * Mathf.LerpUnclamped(1f, heightSquash, punchScale),
            originalScale.z * Mathf.LerpUnclamped(1f, widthPunch, punchScale));

        visualRoot.localScale = Vector3.Lerp(originalScale, hitScale, punch);

        ApplyFlash(punch);
    }

    /// <summary>
    /// Shoves the model along the shot direction and springs it back. Only the
    /// visual moves: the collider, the aim and the navigation position are
    /// untouched, so this cannot desync anything or push a robot through cover.
    /// Skipped when RobotMotionVisual owns the rig's local transform.
    /// </summary>
    private void UpdateKnockback()
    {
        if (drivenByMotionVisual || knockbackDistance <= 0.0001f)
            return;

        float remaining = knockbackEndsAt - Time.time;

        if (remaining <= 0f)
        {
            visualRoot.localPosition = Vector3.Lerp(
                visualRoot.localPosition, originalLocalPosition, Time.deltaTime * 16f);

            visualRoot.localRotation = Quaternion.Slerp(
                visualRoot.localRotation, originalLocalRotation, Time.deltaTime * 16f);

            return;
        }

        float progress = 1f - remaining / recoverDuration;

        // Out-and-back with a slight overshoot past rest, which is what sells
        // the recoil as elastic rather than as a slide.
        float offset =
            Mathf.Sin(progress * Mathf.PI) * Mathf.Lerp(1f, 0.35f, progress);

        Vector3 localDirection = visualRoot.parent != null
            ? visualRoot.parent.InverseTransformDirection(knockbackDirection)
            : knockbackDirection;

        localDirection.y = 0f;

        visualRoot.localPosition =
            originalLocalPosition +
            localDirection * (knockbackDistance * hitIntensity * offset);

        // Lean about the axis perpendicular to the shove.
        Vector3 leanAxis = Vector3.Cross(Vector3.up, localDirection);

        visualRoot.localRotation =
            originalLocalRotation *
            Quaternion.AngleAxis(knockbackLean * hitIntensity * offset, leanAxis);
    }

    private void OnDamageApplied(Damageable target, DamageInfo info)
    {
        hitIntensity = ResolveIntensity(info.Amount);
        hitUntil = Time.time + hitDuration;

        Vector3 direction = info.Direction;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f && info.Source != null)
        {
            // Splash and contact damage carry no travel direction, so fall back
            // to the line from the attacker.
            direction = transform.position - info.Source.transform.position;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude > 0.0001f)
        {
            knockbackDirection = direction.normalized;
            knockbackEndsAt = Time.time + recoverDuration;
        }
    }

    /// <summary>
    /// Scales the whole reaction by how much of this robot's health the hit took,
    /// so a graze and a near-lethal shot no longer look the same.
    /// </summary>
    private float ResolveIntensity(int damage)
    {
        if (damageable == null || damageable.MaxHealth <= 0)
            return 1f;

        float fraction = (float)damage / damageable.MaxHealth;

        return Mathf.Lerp(
            minimumIntensity, 1f,
            Mathf.Clamp01(fraction / fullIntensityFraction));
    }

    /// <summary>
    /// Writes the emissive flash only while a hit is actually playing, and adds
    /// it on top of whatever emission the robot already had. Overwriting
    /// _EmissionColor with black every frame was erasing the cosmetic accent
    /// lights applied by RobotCosmeticApplier.
    /// </summary>
    private void ApplyFlash(float intensity)
    {
        if (renderers == null || propertyBlock == null)
            return;

        if (intensity <= 0.001f)
        {
            if (flashApplied)
            {
                RestoreBaselineEmission();
                flashApplied = false;
            }

            return;
        }

        CaptureBaselineEmission();
        flashApplied = true;

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer targetRenderer = renderers[index];

            if (targetRenderer == null)
                continue;

            targetRenderer.GetPropertyBlock(propertyBlock, materialSlots[index]);
            propertyBlock.SetColor(
                EmissionColorId,
                baselineEmission[index] + flashColor * (intensity * 1.8f));
            targetRenderer.SetPropertyBlock(propertyBlock, materialSlots[index]);
        }
    }

    private void CaptureBaselineEmission()
    {
        if (baselineCaptured)
            return;

        // Captured on the first hit rather than in Awake, so cosmetics that are
        // applied after spawn are already part of the baseline.
        baselineCaptured = true;
        baselineEmission = new Color[renderers.Length];

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer targetRenderer = renderers[index];

            if (targetRenderer == null)
                continue;

            targetRenderer.GetPropertyBlock(propertyBlock, materialSlots[index]);

            Color existing = propertyBlock.GetColor(EmissionColorId);

            var currentMaterials = targetRenderer.sharedMaterials;
            Material current = materialSlots[index] < currentMaterials.Length ? currentMaterials[materialSlots[index]] : null;
            if (existing == default && current != null && current.HasProperty(EmissionColorId))
            {
                existing = current.GetColor(EmissionColorId);
            }

            baselineEmission[index] = existing;
        }
    }

    /// <summary>A skin swap invalidates the hit flash's cached emission colours.</summary>
    public void RefreshSkinEmission()
    {
        if (renderers == null || propertyBlock == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            var surface = renderers[i];
            if (surface == null) continue;
            var current = surface.sharedMaterials;
            int slot = materialSlots[i];
            if (slot >= current.Length || current[slot] == null || !current[slot].HasProperty(EmissionColorId)) continue;
            surface.GetPropertyBlock(propertyBlock, slot);
            propertyBlock.SetColor(EmissionColorId, current[slot].GetColor(EmissionColorId));
            surface.SetPropertyBlock(propertyBlock, slot);
        }
        baselineCaptured = false;
        flashApplied = false;
    }

    private void RestoreBaselineEmission()
    {
        if (!baselineCaptured || baselineEmission == null)
            return;

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer targetRenderer = renderers[index];

            if (targetRenderer == null)
                continue;

            targetRenderer.GetPropertyBlock(propertyBlock, materialSlots[index]);
            propertyBlock.SetColor(EmissionColorId, baselineEmission[index]);
            targetRenderer.SetPropertyBlock(propertyBlock, materialSlots[index]);
        }
    }
}
