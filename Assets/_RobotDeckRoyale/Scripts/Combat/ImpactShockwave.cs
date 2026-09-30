using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ImpactShockwave : MonoBehaviour
{
    private static Material sharedMaterial;

    [SerializeField] private float duration = 0.32f;
    [SerializeField] private float maximumRadius = 1.55f;
    [SerializeField] private float lineWidth = 0.12f;
    [SerializeField] private Color color = new Color(1f, 0.58f, 0.08f, 1f);

    private LineRenderer line;
    private float startedAt;

    private void Awake()
    {
        startedAt = Time.time;
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 40;
        line.alignment = LineAlignment.TransformZ;
        line.numCornerVertices = 3;
        line.numCapVertices = 3;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = GetMaterial();
    }

    private void Update()
    {
        float progress = Mathf.Clamp01((Time.time - startedAt) / duration);
        float radius = maximumRadius * (1f - Mathf.Pow(1f - progress, 2f));

        for (int index = 0; index < line.positionCount; index++)
        {
            float angle = index / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(index, new Vector3(Mathf.Cos(angle) * radius, 0.04f, Mathf.Sin(angle) * radius));
        }

        Color fading = color;
        fading.a = 1f - progress;
        line.startColor = fading;
        line.endColor = fading;
        line.startWidth = lineWidth * (1f - progress * 0.65f);
        line.endWidth = line.startWidth;

        if (progress >= 1f)
            Destroy(this);
    }

    private static Material GetMaterial()
    {
        if (sharedMaterial != null)
            return sharedMaterial;

        sharedMaterial =
            VfxParticleMaterial.ResolveUnlitInstance(null, Color.white);

        return sharedMaterial;
    }
}
