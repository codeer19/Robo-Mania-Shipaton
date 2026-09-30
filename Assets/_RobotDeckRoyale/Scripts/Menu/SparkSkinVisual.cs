using System.Collections.Generic;
using UnityEngine;

/// <summary>Transfers authored surfaces and accessories onto the existing Spark skeleton.
/// Never replaces the gameplay rig, animator, sockets, meshes or combat components.</summary>
[DisallowMultipleComponent]
public sealed class SparkSkinVisual : MonoBehaviour
{
    private string appliedId;
    private readonly List<GameObject> accessories = new List<GameObject>();
    private readonly Dictionary<string, List<GameObject>> accessoryCache = new Dictionary<string, List<GameObject>>();
    private readonly Dictionary<string, Renderer> surfaces = new Dictionary<string, Renderer>();
    private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
    private bool initialized;
    public string AppliedSkinId => appliedId;

    public void Apply(string skinId)
    {
        if (skinId == appliedId) return;
        FrontendAssets assets;
        using (var loadTiming = new NetworkStartupDiagnostics.Step("Skin.LoadAssets"))
            assets = FrontendAssets.Load();
        SparkSkin skin = assets != null ? assets.FindSkin(skinId) : null;
        if (skin == null || skin.Source == null) return;
        if (!initialized)
        {
            using var cacheTiming = new NetworkStartupDiagnostics.Step("Skin.CacheHierarchy");
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) surfaces[r.name] = r;
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            initialized = true;
        }
        foreach (GameObject item in accessories)
        {
            if (item == null) continue;
            item.SetActive(false);
        }
        accessories.Clear();
        bool cached = accessoryCache.TryGetValue(skin.Id, out List<GameObject> cachedAccessories);
        if (cached)
        {
            accessories.AddRange(cachedAccessories);
            foreach (var item in accessories) if (item != null) item.SetActive(true);
        }
        var hidden = new HashSet<string>(skin.HiddenRenderers ?? new string[0]);
        float attachmentScale = 1;
        // Imported skin sources and the gameplay rig can use different model import scales.
        // Measure the immutable base mesh, not animated bone positions or renderer bounds.
        if (surfaces.TryGetValue("Spark_Body", out Renderer bodySurface) && bodySurface is SkinnedMeshRenderer liveBody)
            foreach (var sourceBody in skin.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sourceBody.name == "Spark_Body" && sourceBody.sharedMesh != null && liveBody.sharedMesh != null)
                    attachmentScale = liveBody.sharedMesh.bounds.size.magnitude / Mathf.Max(.0001f, sourceBody.sharedMesh.bounds.size.magnitude);
        using var transferTiming = new NetworkStartupDiagnostics.Step(cached ? "Skin.TransferCached" : "Skin.TransferFresh");
        foreach (Renderer source in skin.Source.GetComponentsInChildren<Renderer>(true))
        {
            if (surfaces.TryGetValue(source.name, out Renderer target))
            {
                var materials = new List<Material>(source.sharedMaterials);
                // QuickOutline adds presentation passes after the model's authored slots.
                // Equipping a skin must retain those passes on the live gameplay renderer.
                foreach (Material material in target.sharedMaterials)
                    if (material != null && material.shader != null && material.shader.name.Contains("Outline"))
                        materials.Add(material);
                target.sharedMaterials = materials.ToArray();
                target.enabled = !hidden.Contains(source.name);
                continue;
            }
            if (hidden.Contains(source.name)) continue;
            if (cached) continue;
            // FBX meshes are siblings of the armature. Retain authored mesh transforms and
            // bind new accessory renderers to the live bones, including animated eyes.
            Transform attachmentParent = transform;
            if (!(source is SkinnedMeshRenderer) && source.transform.parent != null &&
                bones.TryGetValue(source.transform.parent.name, out Transform authoredParent))
                attachmentParent = authoredParent;
            GameObject item = Instantiate(source.gameObject, attachmentParent, false);
            if (!(source is SkinnedMeshRenderer))
            {
                item.transform.localPosition = source.transform.localPosition * attachmentScale;
                item.transform.localScale = source.transform.localScale * attachmentScale;
            }
            item.name = source.name;
            item.layer = gameObject.layer;
            var skinned = item.GetComponent<SkinnedMeshRenderer>();
            var original = source as SkinnedMeshRenderer;
            if (skinned != null && original != null)
            {
                Transform[] mapped = new Transform[original.bones.Length];
                bool valid = true;
                for (int i = 0; i < mapped.Length; i++)
                    if (original.bones[i] == null || !bones.TryGetValue(original.bones[i].name, out mapped[i])) valid = false;
                if (!valid) { Release(item); continue; }
                skinned.bones = mapped;
                if (original.rootBone != null && bones.TryGetValue(original.rootBone.name, out Transform rootBone))
                    skinned.rootBone = rootBone;
                skinned.updateWhenOffscreen = true;
            }
            accessories.Add(item);
        }
        if (!cached) accessoryCache[skin.Id] = new List<GameObject>(accessories);
        appliedId = skinId;
        using var emissionTiming = new NetworkStartupDiagnostics.Step("Skin.HitReaction");
        GetComponentInParent<RobotHitReaction>()?.RefreshSkinEmission();
    }

    private void OnDestroy()
    {
        foreach (var set in accessoryCache.Values)
            foreach (GameObject item in set) if (item != null) Release(item);
        accessoryCache.Clear();
        accessories.Clear();
    }

    private static void Release(GameObject item)
    {
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
}
