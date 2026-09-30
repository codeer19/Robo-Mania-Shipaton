using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The soft translucent ground circle that shows exactly where a structure acts.
///
/// The radius is the gameplay radius read from the structure's own tuning, so
/// the circle and the effect can never disagree. Each card has its own colour
/// (Pulse violet, Heal green, Jammer red-orange, Interceptor mint, Overdrive
/// amber), distinct from the blue/red team rings. One small mesh per radius and
/// one material per colour are shared by every structure; there is no texture
/// and no per-frame work: a faint fill, a readable rim and a feathered edge are
/// baked into the vertex colours.
/// </summary>
public static class AreaIndicator
{
    public enum Kind { Pulse, Heal, Jammer, Interceptor, Overdrive }

    private static readonly Color[] Colours =
    {
        new Color(0.66f, 0.42f, 1.00f),   // Pulse Tower: violet
        new Color(0.30f, 0.92f, 0.45f),   // Healing Pad: green
        new Color(1.00f, 0.34f, 0.16f),   // Recovery Jammer: red-orange
        new Color(0.30f, 0.95f, 0.80f),   // Missile Interceptor: mint
        new Color(1.00f, 0.76f, 0.16f),   // Overdrive Pad: amber
    };

    private const int Segments = 72;
    // Radius fractions and alphas: faint fill, a brighter band, a soft outer feather.
    private static readonly float[] RingRadius = { 0f, 0.84f, 0.95f, 1f, 1.035f };
    private static readonly float[] RingAlpha = { 0.10f, 0.12f, 0.38f, 0.55f, 0f };

    private static readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
    private static readonly Material[] materials = new Material[Colours.Length];

    public static Color ColourOf(Kind kind) => Colours[(int)kind];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCaches()
    {
        meshes.Clear();
        for (int i = 0; i < materials.Length; i++) materials[i] = null;
    }

    /// <summary>Adds the circle under a structure (flat, just above the floor, no shadows).</summary>
    public static Renderer Create(Transform parent, Kind kind, float radius, float height = 0.05f)
    {
        var go = new GameObject("Area " + kind);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * height;
        // Flat on the ground whatever the structure's facing.
        go.transform.localRotation = Quaternion.identity;
        go.AddComponent<MeshFilter>().sharedMesh = MeshFor(radius);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = MaterialFor(kind);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    private static Mesh MeshFor(float radius)
    {
        int key = Mathf.RoundToInt(radius * 100f);
        if (meshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

        int rings = RingRadius.Length;
        var vertices = new Vector3[1 + (rings - 1) * Segments];
        var colours = new Color32[vertices.Length];
        var triangles = new List<int>(Segments * 3 + (rings - 2) * Segments * 6);
        vertices[0] = Vector3.zero;
        colours[0] = new Color32(255, 255, 255, (byte)(RingAlpha[0] * 255f));
        for (int r = 1; r < rings; r++)
        {
            float distance = RingRadius[r] * radius;
            byte alpha = (byte)(RingAlpha[r] * 255f);
            for (int s = 0; s < Segments; s++)
            {
                float angle = s * Mathf.PI * 2f / Segments;
                int index = 1 + (r - 1) * Segments + s;
                vertices[index] = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                colours[index] = new Color32(255, 255, 255, alpha);
            }
        }
        for (int s = 0; s < Segments; s++)
        {
            int next = (s + 1) % Segments;
            triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + s);
        }
        for (int r = 1; r < rings - 1; r++)
        {
            int inner = 1 + (r - 1) * Segments, outer = 1 + r * Segments;
            for (int s = 0; s < Segments; s++)
            {
                int next = (s + 1) % Segments;
                triangles.Add(inner + s); triangles.Add(inner + next); triangles.Add(outer + s);
                triangles.Add(outer + s); triangles.Add(inner + next); triangles.Add(outer + next);
            }
        }
        var mesh = new Mesh { name = "Area r" + radius.ToString("0.0"), hideFlags = HideFlags.DontSave };
        mesh.vertices = vertices;
        mesh.colors32 = colours;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        meshes[key] = mesh;
        return mesh;
    }

    private static Material MaterialFor(Kind kind)
    {
        int index = (int)kind;
        if (materials[index] != null) return materials[index];
        Material material = VfxParticleMaterial.ResolveInstance(null, Colours[index]);
        if (material == null) return null;
        material.name = "Area " + kind;
        TeamGroundRing.ConfigureTransparentMaterial(material);
        // Under the team rings and every other ground marker.
        material.renderQueue = 3050;
        materials[index] = material;
        return material;
    }
}
