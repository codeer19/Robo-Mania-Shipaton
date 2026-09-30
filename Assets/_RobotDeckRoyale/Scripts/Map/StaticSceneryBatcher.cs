using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws the arena's static scenery in a few dozen batches instead of ~1,700.
///
/// The sky arena is ~1,700 small static pieces (rails, bolts, planters, stairs)
/// sharing 14 materials. On WebGL every renderer costs its own draw call in the
/// shadow pass and again in the colour pass, and the profiler put the scenery at
/// two thirds of all draws in a match - the part of the frame that scales worst
/// on phones. At load the pieces are merged into one mesh per material, per
/// shadow setting and per grid cell (same vertices, same materials, same shadows,
/// culled per cell), and the originals stop rendering.
///
/// Presentation only: nothing here moves, collides, navigates or is networked,
/// and the pieces keep their GameObjects, so anything looking them up still finds
/// them. The source model must be Read/Write enabled; if it is not, the scenery is
/// left exactly as authored.
/// </summary>
[DisallowMultipleComponent]
public sealed class StaticSceneryBatcher : MonoBehaviour
{
    [SerializeField, Min(4f)] private float cellSize = 24f;

    // The merged meshes are built once per session and reused by every later match:
    // the scenery never changes, so rebuilding it would only repeat the work and
    // churn ~8 MB of mesh memory each time.
    private struct BatchRender
    {
        public Mesh Mesh;
        public Material Material;
        public ShadowCastingMode Cast;
        public bool Receive;
        public int Layer;
        public LightProbeUsage Probes;
        public ReflectionProbeUsage Reflections;
        public uint RenderingLayers;
    }

    private static readonly List<BatchRender> sessionBatches = new List<BatchRender>();
    private static string sessionKey;

    public int SourcePieces { get; private set; }
    public int BatchCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        sessionBatches.Clear();
        sessionKey = null;
    }

    private readonly struct BatchKey : IEquatable<BatchKey>
    {
        public readonly Material Material;
        public readonly ShadowCastingMode Cast;
        public readonly bool Receive;
        public readonly int Layer;
        public readonly int CellX, CellZ;

        public BatchKey(Material material, ShadowCastingMode cast, bool receive, int layer, int cellX, int cellZ)
        {
            Material = material; Cast = cast; Receive = receive; Layer = layer; CellX = cellX; CellZ = cellZ;
        }

        public bool Equals(BatchKey other) =>
            Material == other.Material && Cast == other.Cast && Receive == other.Receive &&
            Layer == other.Layer && CellX == other.CellX && CellZ == other.CellZ;

        public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Material != null ? Material.GetHashCode() : 0, (int)Cast, Receive, Layer, CellX, CellZ);
    }

    private sealed class Batch
    {
        public readonly List<CombineInstance> Parts = new List<CombineInstance>();
        public MeshRenderer Template;
        public int Vertices;
    }

    private void Awake() => Combine();

    private void Combine()
    {
        float startedAt = Time.realtimeSinceStartup;
        var groups = new Dictionary<BatchKey, Batch>();
        var merged = new List<MeshRenderer>();
        Matrix4x4 toRoot = transform.worldToLocalMatrix;

        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
            if (!mesh.isReadable)
            {
                // Nothing has been switched off yet: the scenery stays exactly as authored.
                Debug.LogWarning($"[SCENERY] {mesh.name} is not Read/Write enabled; scenery left unbatched.", this);
                return;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials.Length != mesh.subMeshCount || Array.IndexOf(materials, null) >= 0) continue;

            Vector3 centre = renderer.bounds.center;
            int cellX = Mathf.FloorToInt(centre.x / cellSize);
            int cellZ = Mathf.FloorToInt(centre.z / cellSize);
            Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var key = new BatchKey(materials[subMesh], renderer.shadowCastingMode, renderer.receiveShadows,
                    filter.gameObject.layer, cellX, cellZ);
                if (!groups.TryGetValue(key, out Batch batch)) groups[key] = batch = new Batch { Template = renderer };
                batch.Parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = subMesh, transform = matrix });
                batch.Vertices += mesh.vertexCount;
            }

            merged.Add(renderer);
        }

        if (merged.Count == 0) return;

        string layout = gameObject.scene.path + ":" + merged.Count + ":" + groups.Count;
        bool reuse = layout == sessionKey && sessionBatches.Count == groups.Count && sessionBatches.TrueForAll(b => b.Mesh != null);
        if (!reuse)
        {
            sessionBatches.Clear();
            foreach (KeyValuePair<BatchKey, Batch> group in groups)
            {
                var mesh = new Mesh
                {
                    name = $"Scenery {group.Key.Material.name} {group.Key.CellX},{group.Key.CellZ}",
                    indexFormat = group.Value.Vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };
                mesh.CombineMeshes(group.Value.Parts.ToArray(), true, true);
                // GPU copy only: the merged scenery is never read back.
                mesh.UploadMeshData(true);
                MeshRenderer template = group.Value.Template;
                sessionBatches.Add(new BatchRender
                {
                    Mesh = mesh, Material = group.Key.Material, Cast = group.Key.Cast, Receive = group.Key.Receive,
                    Layer = group.Key.Layer, Probes = template.lightProbeUsage, Reflections = template.reflectionProbeUsage,
                    RenderingLayers = template.renderingLayerMask
                });
            }
            sessionKey = layout;
        }

        var parent = new GameObject("Scenery Batches").transform;
        parent.SetParent(transform, false);
        foreach (BatchRender batch in sessionBatches)
        {
            var piece = new GameObject(batch.Mesh.name) { layer = batch.Layer };
            piece.transform.SetParent(parent, false);
            piece.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = batch.Material;
            renderer.shadowCastingMode = batch.Cast;
            renderer.receiveShadows = batch.Receive;
            renderer.lightProbeUsage = batch.Probes;
            renderer.reflectionProbeUsage = batch.Reflections;
            renderer.renderingLayerMask = batch.RenderingLayers;
        }

        foreach (MeshRenderer renderer in merged) renderer.enabled = false;
        SourcePieces = merged.Count;
        BatchCount = sessionBatches.Count;
        Debug.Log($"[SCENERY] {merged.Count} pieces drawn as {BatchCount} batches " +
                  $"({(reuse ? "reused" : "built")}) in {(Time.realtimeSinceStartup - startedAt) * 1000f:F0}ms");
    }
}
