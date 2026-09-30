using UnityEngine;

[RequireComponent(typeof(Damageable))]
public class SpawnProtectionVFX : MonoBehaviour
{
    [SerializeField] private float protectionDuration = 2f;
    [SerializeField] private float ringRadius = 0.78f;

    private static Material ringMaterial;

    private Damageable damageable;
    private LineRenderer ring;
    private bool diedSinceLastSpawn;
    private float visibleUntil;

    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        CreateRing();
    }

    private void OnEnable()
    {
        damageable.Died += OnDied;
        damageable.HealthChanged += OnHealthChanged;
    }

    private void OnDisable()
    {
        damageable.Died -= OnDied;
        damageable.HealthChanged -= OnHealthChanged;
    }

    private void Update()
    {
        if (!ring.enabled)
            return;

        if (Time.time >= visibleUntil)
        {
            ring.enabled = false;
            return;
        }

        float remaining =
            Mathf.Clamp01((visibleUntil - Time.time) / protectionDuration);

        float pulse = 0.9f + Mathf.Sin(Time.time * 9f) * 0.08f;

        ring.transform.localScale =
            new Vector3(pulse, 1f, pulse);

        Color color = new Color(
            0.18f,
            0.75f,
            1f,
            remaining * 0.75f
        );

        ring.startColor = color;
        ring.endColor = color;
    }

    private void OnDied(Damageable target)
    {
        diedSinceLastSpawn = true;
        ring.enabled = false;
    }

    private void OnHealthChanged(
        Damageable target,
        int currentHealth,
        int maxHealth)
    {
        if (!diedSinceLastSpawn || currentHealth < maxHealth)
            return;

        diedSinceLastSpawn = false;
        visibleUntil = Time.time + protectionDuration;
        ring.enabled = true;
    }

    private void CreateRing()
    {
        GameObject ringObject = new GameObject("Spawn Protection Ring");

        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 0.06f, 0f);

        ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 40;
        ring.widthMultiplier = 0.055f;
        ring.numCapVertices = 3;
        ring.numCornerVertices = 3;
        ring.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;

        if (ringMaterial == null)
        {
            ringMaterial =
                VfxParticleMaterial.ResolveUnlitInstance(null, Color.white);
        }

        ring.material = ringMaterial;

        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle =
                (float)i / ring.positionCount * Mathf.PI * 2f;

            ring.SetPosition(
                i,
                new Vector3(
                    Mathf.Cos(angle) * ringRadius,
                    0f,
                    Mathf.Sin(angle) * ringRadius
                )
            );
        }

        ring.enabled = false;
    }
}