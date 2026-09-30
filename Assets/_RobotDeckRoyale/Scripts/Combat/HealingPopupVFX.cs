using TMPro;
using UnityEngine;

[RequireComponent(typeof(Damageable))]
public class HealingPopupVFX : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TMP_FontAsset popupFont;
    [SerializeField] private Vector3 popupOffset = new Vector3(0f, 2.25f, 0f);
    [SerializeField] private float textScale = 0.04f;

    [Header("Healing Ring")]
    [SerializeField] private float ringRadius = 0.72f;
    [SerializeField] private float ringDuration = 0.7f;

    private static Material ringMaterial;

    private Damageable damageable;
    private LineRenderer healingRing;
    private ParticleSystem healSparks;

    private int pendingHealing;
    private float popupAtTime;
    private float ringEndTime;
    private float ringStartTime;

    private void Awake()
    {
        damageable = GetComponent<Damageable>();

        if (popupFont == null)
            popupFont = TMP_Settings.defaultFontAsset;

        CreateHealingRing();
        CreateHealSparks();
    }

    private void OnEnable()
    {
        damageable.Healed += OnHealed;
    }

    private void OnDisable()
    {
        damageable.Healed -= OnHealed;
    }

    private void Update()
    {
        UpdateRing();

        if (pendingHealing > 0 && Time.time >= popupAtTime)
        {
            CreateHealingNumber(pendingHealing);
            pendingHealing = 0;
        }
    }

    private void OnHealed(Damageable target, int amount)
    {
        pendingHealing += amount;

        if (popupAtTime <= Time.time)
            popupAtTime = Time.time + 0.3f;

        if (Time.time >= ringEndTime)
        {
            ringStartTime = Time.time;
            healSparks.Play(true);
        }

        ringEndTime = Time.time + ringDuration;
        healingRing.enabled = true;
    }

    private void CreateHealingRing()
    {
        GameObject ringObject = new GameObject("Healing Ring");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 0.06f, 0f);

        healingRing = ringObject.AddComponent<LineRenderer>();
        healingRing.useWorldSpace = false;
        healingRing.loop = true;
        healingRing.positionCount = 40;
        healingRing.widthMultiplier = 0.065f;
        healingRing.numCapVertices = 3;
        healingRing.numCornerVertices = 3;
        healingRing.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        healingRing.receiveShadows = false;

        if (ringMaterial == null)
        {
            ringMaterial =
                VfxParticleMaterial.ResolveUnlitInstance(null, Color.white);
        }

        healingRing.material = ringMaterial;

        for (int i = 0; i < healingRing.positionCount; i++)
        {
            float angle =
                (float)i / healingRing.positionCount * Mathf.PI * 2f;

            healingRing.SetPosition(
                i,
                new Vector3(
                    Mathf.Cos(angle) * ringRadius,
                    0f,
                    Mathf.Sin(angle) * ringRadius
                )
            );
        }

        healingRing.enabled = false;
    }

    private void CreateHealSparks()
    {
        GameObject sparksObject = new GameObject("Healing Sparks");
        sparksObject.transform.SetParent(transform, false);
        sparksObject.transform.localPosition = new Vector3(0f, 0.3f, 0f);

        healSparks = sparksObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = healSparks.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime =
            new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        main.startSpeed =
            new ParticleSystem.MinMaxCurve(0.8f, 1.7f);
        main.startSize =
            new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startColor = new Color(0.35f, 1f, 0.3f, 1f);
        main.maxParticles = 18;

        ParticleSystem.EmissionModule emission = healSparks.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)12)
        });

        ParticleSystem.ShapeModule shape = healSparks.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = ringRadius * 0.65f;

        // Built in code, so the renderer would otherwise keep the non-URP
        // default material and the heal sparks would draw magenta.
        ParticleSystemRenderer renderer =
            healSparks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = VfxParticleMaterial.Resolve(null);
    }

    private void UpdateRing()
    {
        if (healingRing == null || !healingRing.enabled)
            return;

        if (Time.time >= ringEndTime)
        {
            healingRing.enabled = false;
            return;
        }

        float progress = Mathf.InverseLerp(
            ringStartTime,
            ringEndTime,
            Time.time
        );

        float alpha = 1f - progress;
        Color ringColor = new Color(0.3f, 1f, 0.28f, alpha);

        healingRing.startColor = ringColor;
        healingRing.endColor = ringColor;
        healingRing.widthMultiplier = Mathf.Lerp(0.075f, 0.018f, progress);

        float scale = Mathf.Lerp(0.85f, 1.15f, progress);
        healingRing.transform.localScale = new Vector3(scale, 1f, scale);
    }

    private void CreateHealingNumber(int amount)
    {
        if (popupFont == null)
        {
            Debug.LogWarning(
                "HealingPopupVFX needs a TMP Font Asset assigned."
            );
            return;
        }

        GameObject popupObject = new GameObject("Healing Number");

        popupObject.transform.position =
            transform.position + popupOffset;

        popupObject.transform.localScale =
            Vector3.one * textScale;

        TextMeshPro text = popupObject.AddComponent<TextMeshPro>();

        text.font = popupFont;
        text.text = "+" + amount;
        text.fontSize = 36f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.35f, 1f, 0.3f, 1f);
        text.outlineColor = new Color(0.03f, 0.12f, 0.04f, 1f);
        text.outlineWidth = 0.2f;

        HealingNumberMotion motion =
            popupObject.AddComponent<HealingNumberMotion>();

        motion.Setup(text, textScale);
    }
}

public class HealingNumberMotion : MonoBehaviour
{
    private TextMeshPro text;
    private Camera mainCamera;
    private Vector3 startPosition;
    private float baseScale;
    private float age;

    public void Setup(TextMeshPro popupText, float scale)
    {
        text = popupText;
        baseScale = scale;
        startPosition = transform.position;
        mainCamera = Camera.main;
    }

    private void Update()
    {
        age += Time.deltaTime;

        float progress = Mathf.Clamp01(age / 0.8f);

        transform.position = startPosition +
            Vector3.up * (progress * 0.75f);

        float punch = 1f + Mathf.Sin(progress * Mathf.PI) * 0.3f;
        transform.localScale = Vector3.one * baseScale * punch;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
        {
            transform.LookAt(
                transform.position +
                mainCamera.transform.rotation * Vector3.forward,

                mainCamera.transform.rotation * Vector3.up
            );
        }

        Color color = text.color;
        color.a = 1f - progress;
        text.color = color;

        if (progress >= 1f)
            Destroy(gameObject);
    }
}