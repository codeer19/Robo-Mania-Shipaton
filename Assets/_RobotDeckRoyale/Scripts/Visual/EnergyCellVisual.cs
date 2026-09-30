using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnergyCellVisual : MonoBehaviour
{
    private sealed class MaterialBinding
    {
        public Renderer Renderer;
        public Material[] OriginalMaterials;
        public Material[] RuntimeMaterials;
        public Color[] OriginalEmissionColours;
    }

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Visual Root")]
    [Tooltip("Assign the imported ecell visual child when the pickup collider lives on this object.")]
    [SerializeField] private Transform visualRoot;

    [Header("Motion")]
    [SerializeField, Min(0f)] private float spinDegreesPerSecond = 70f;
    [SerializeField, Range(0f, 0.5f)] private float bobHeight = 0.09f;
    [SerializeField, Min(0f)] private float bobFrequency = 1.15f;
    [SerializeField, Range(0f, 12f)] private float tiltDegrees = 1.5f;

    [Header("Energy Glow")]
    [SerializeField, ColorUsage(true, true)]
    private Color glowColour = new Color(1.0f, 0.32f, 0.04f, 1f);

    [SerializeField, Min(0f)] private float glowIntensity = 0.3f;
    [SerializeField, Range(0f, 0.8f)] private float glowPulseAmount = 0.15f;
    [SerializeField, Min(0f)] private float glowPulseFrequency = 1.8f;

    private readonly List<MaterialBinding> materialBindings = new List<MaterialBinding>();
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private float phaseOffset;
    private float spinAngle;
    private float glowMultiplier = 1f;
    private bool isInitialised;

    private void Awake()
    {
        Initialise();
    }

    private void OnEnable()
    {
        Initialise();
    }

    private void Update()
    {
        if (!isInitialised || visualRoot == null)
        {
            return;
        }

        float time = Time.time + phaseOffset;
        spinAngle = Mathf.Repeat(spinAngle + spinDegreesPerSecond * Time.deltaTime, 360f);

        float bob = Mathf.Sin(time * bobFrequency * Mathf.PI * 2f) * bobHeight;
        float tilt = Mathf.Sin(time * bobFrequency * Mathf.PI) * tiltDegrees;
        visualRoot.localPosition = restLocalPosition + Vector3.up * bob;
        visualRoot.localRotation = restLocalRotation * Quaternion.Euler(tilt, spinAngle, 0f);

        float pulse = 1f +
            Mathf.Sin(time * glowPulseFrequency * Mathf.PI * 2f) * glowPulseAmount;
        ApplyGlow(Mathf.Max(0f, pulse * glowMultiplier));
    }

    private void OnDisable()
    {
        ApplyGlow(0f);
        RestoreTransform();
    }

    private void OnDestroy()
    {
        RestoreTransform();
        RestoreAndDestroyMaterials();
    }

    private void OnValidate()
    {
        spinDegreesPerSecond = Mathf.Max(0f, spinDegreesPerSecond);
        bobHeight = Mathf.Max(0f, bobHeight);
        bobFrequency = Mathf.Max(0f, bobFrequency);
        glowIntensity = Mathf.Max(0f, glowIntensity);
        glowPulseAmount = Mathf.Clamp(glowPulseAmount, 0f, 0.8f);
        glowPulseFrequency = Mathf.Max(0f, glowPulseFrequency);
    }

    public void SetGlowMultiplier(float multiplier)
    {
        glowMultiplier = Mathf.Max(0f, multiplier);
    }

    public void Configure(Transform importedVisualRoot = null)
    {
        if (importedVisualRoot != null && importedVisualRoot != visualRoot)
        {
            RestoreTransform();
            RestoreAndDestroyMaterials();
            isInitialised = false;
            visualRoot = importedVisualRoot;
        }

        Initialise();
    }

    public void RefreshVisual()
    {
        if (isInitialised)
        {
            RestoreTransform();
            RestoreAndDestroyMaterials();
            isInitialised = false;
        }

        Initialise();
    }

    private void Initialise()
    {
        if (isInitialised || !Application.isPlaying)
        {
            return;
        }

        ResolveVisualRoot();
        if (visualRoot == null)
        {
            return;
        }

        restLocalPosition = visualRoot.localPosition;
        restLocalRotation = visualRoot.localRotation;
        Vector3 worldPosition = transform.position;
        phaseOffset = Mathf.Repeat(
            Mathf.Abs(
                worldPosition.x * 1.37f +
                worldPosition.z * 2.11f +
                transform.GetSiblingIndex() * 0.71f),
            10f);
        CreateRuntimeMaterials();
        isInitialised = true;
    }

    private void ResolveVisualRoot()
    {
        if (visualRoot != null)
        {
            return;
        }

        // A gameplay pickup commonly has one imported-model child and keeps its
        // trigger collider on the parent. Prefer that child so the hitbox stays still.
        if (GetComponent<Renderer>() == null && transform.childCount == 1)
        {
            Transform onlyChild = transform.GetChild(0);
            if (onlyChild.GetComponentInChildren<Renderer>(true) != null)
            {
                visualRoot = onlyChild;
                return;
            }
        }

        visualRoot = transform;
    }

    private void CreateRuntimeMaterials()
    {
        foreach (Renderer targetRenderer in visualRoot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] originals = targetRenderer.sharedMaterials;
            Material[] runtimeMaterials = new Material[originals.Length];
            Color[] originalEmissionColours = new Color[originals.Length];

            for (int index = 0; index < originals.Length; index++)
            {
                Material original = originals[index];
                if (original == null)
                {
                    continue;
                }

                Material runtimeMaterial = new Material(original)
                {
                    name = original.name + " (Energy Cell Runtime)",
                    hideFlags = HideFlags.DontSave
                };

                if (runtimeMaterial.HasProperty(EmissionColorId))
                {
                    originalEmissionColours[index] = runtimeMaterial.GetColor(EmissionColorId);
                    runtimeMaterial.EnableKeyword("_EMISSION");
                    runtimeMaterial.globalIlluminationFlags =
                        MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                runtimeMaterials[index] = runtimeMaterial;
            }

            targetRenderer.sharedMaterials = runtimeMaterials;
            materialBindings.Add(new MaterialBinding
            {
                Renderer = targetRenderer,
                OriginalMaterials = originals,
                RuntimeMaterials = runtimeMaterials,
                OriginalEmissionColours = originalEmissionColours
            });
        }
    }

    private void ApplyGlow(float pulse)
    {
        foreach (MaterialBinding binding in materialBindings)
        {
            for (int index = 0; index < binding.RuntimeMaterials.Length; index++)
            {
                Material material = binding.RuntimeMaterials[index];
                if (material == null || !material.HasProperty(EmissionColorId))
                {
                    continue;
                }

                // Never replace the imported e-cell palette with a generic
                // cyan material. Keep the FBX orange/blue emissive accents and
                // only breathe their existing emission very subtly.
                Color original = binding.OriginalEmissionColours[index];
                material.SetColor(EmissionColorId, original *
                    (1f + Mathf.Max(0f, glowIntensity) * 0.16f * pulse));
            }
        }
    }

    private void RestoreTransform()
    {
        if (!isInitialised || visualRoot == null)
        {
            return;
        }

        visualRoot.localPosition = restLocalPosition;
        visualRoot.localRotation = restLocalRotation;
    }

    private void RestoreAndDestroyMaterials()
    {
        foreach (MaterialBinding binding in materialBindings)
        {
            if (binding.Renderer != null)
            {
                binding.Renderer.sharedMaterials = binding.OriginalMaterials;
            }

            foreach (Material runtimeMaterial in binding.RuntimeMaterials)
            {
                if (runtimeMaterial == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(runtimeMaterial);
                }
            }
        }

        materialBindings.Clear();
    }
}
