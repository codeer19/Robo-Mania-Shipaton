using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a raw Meshy/AI-generated model into something that drops straight into
/// the arena.
///
/// Generated meshes arrive with an arbitrary export scale, a pivot at the mesh
/// centre or at the origin of whatever coordinate space the generator used, no
/// collider, and the wrong layer. Fixing that by hand for every asset is where
/// mismatches and floating/sunk props come from, so it is done here instead:
/// measure the bounds, rescale to a real gameplay dimension, re-pivot to the
/// base centre, add a primitive collider, and save a prefab.
/// </summary>
public class MeshyAssetNormalizer : EditorWindow
{
    private enum PivotMode
    {
        BaseCentre,
        TrueCentre,
        Unchanged
    }

    private enum ColliderMode
    {
        Box,
        Capsule,
        None
    }

    private enum FitAxis
    {
        LongestHorizontal,
        Width,
        Height,
        Depth
    }

    private float targetSize = 4.1f;
    private FitAxis fitAxis = FitAxis.LongestHorizontal;
    private PivotMode pivotMode = PivotMode.BaseCentre;
    private ColliderMode colliderMode = ColliderMode.Box;
    private string targetLayer = "Default";
    private bool markStatic = true;
    private string outputFolder = "Assets/_RobotDeckRoyale/Prefabs/Arena";

    [MenuItem("Robo Mania/Arena/Meshy Asset Normalizer")]
    public static void Open()
    {
        GetWindow<MeshyAssetNormalizer>("Meshy Normalizer").minSize =
            new Vector2(380f, 320f);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Select one or more imported model assets in the Project window, " +
            "set the real in-game size, then press Normalize. " +
            "A corrected prefab is written to the output folder.",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sizing", EditorStyles.boldLabel);
        fitAxis = (FitAxis)EditorGUILayout.EnumPopup("Fit Axis", fitAxis);
        targetSize = EditorGUILayout.FloatField("Target Size (m)", targetSize);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
        pivotMode = (PivotMode)EditorGUILayout.EnumPopup("Pivot", pivotMode);
        colliderMode = (ColliderMode)EditorGUILayout.EnumPopup("Collider", colliderMode);
        targetLayer = EditorGUILayout.TextField("Layer", targetLayer);
        markStatic = EditorGUILayout.Toggle("Mark Static", markStatic);

        EditorGUILayout.Space();
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Arena presets", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Floor 4m")) ApplyPreset(4f, FitAxis.Width, ColliderMode.None, PivotMode.TrueCentre);
            if (GUILayout.Button("Low cover 4m")) ApplyPreset(4f, FitAxis.LongestHorizontal, ColliderMode.Box, PivotMode.BaseCentre);
            if (GUILayout.Button("Cover 4.1m")) ApplyPreset(4.1f, FitAxis.LongestHorizontal, ColliderMode.Box, PivotMode.BaseCentre);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Wall 7m")) ApplyPreset(7f, FitAxis.LongestHorizontal, ColliderMode.Box, PivotMode.BaseCentre);
            if (GUILayout.Button("Machine 4.2m")) ApplyPreset(4.2f, FitAxis.LongestHorizontal, ColliderMode.Box, PivotMode.BaseCentre);
            if (GUILayout.Button("Decal 5.7m")) ApplyPreset(5.7f, FitAxis.LongestHorizontal, ColliderMode.None, PivotMode.TrueCentre);
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
        {
            if (GUILayout.Button("Normalize Selected", GUILayout.Height(32f)))
                NormalizeSelection();
        }

        EditorGUILayout.LabelField(
            $"Selected: {Selection.gameObjects.Length}",
            EditorStyles.miniLabel);
    }

    private void ApplyPreset(float size, FitAxis axis, ColliderMode collider, PivotMode pivot)
    {
        targetSize = size;
        fitAxis = axis;
        colliderMode = collider;
        pivotMode = pivot;
    }

    private void NormalizeSelection()
    {
        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            string parent = Path.GetDirectoryName(outputFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(outputFolder);

            if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder(parent, leaf);
            else
            {
                Debug.LogError($"Output folder '{outputFolder}' does not exist.");
                return;
            }
        }

        int layer = LayerMask.NameToLayer(targetLayer);

        if (layer < 0)
        {
            Debug.LogError($"Layer '{targetLayer}' does not exist.");
            return;
        }

        foreach (GameObject selected in Selection.gameObjects)
        {
            string sourcePath = AssetDatabase.GetAssetPath(selected);

            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"'{selected.name}' is not an asset; skipped.");
                continue;
            }

            Normalize(selected, layer);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void Normalize(GameObject sourceAsset, int layer)
    {
        GameObject working = (GameObject)PrefabUtility.InstantiatePrefab(sourceAsset);

        if (working == null)
        {
            Debug.LogWarning($"Could not instantiate '{sourceAsset.name}'.");
            return;
        }

        PrefabUtility.UnpackPrefabInstance(
            working, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        working.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        working.transform.localScale = Vector3.one;

        if (!ArenaLayoutBuilder.TryGetRendererBounds(working, out Bounds raw))
        {
            Debug.LogWarning($"'{sourceAsset.name}' has no renderers; skipped.");
            DestroyImmediate(working);
            return;
        }

        // 1. Rescale so the asset is the size the arena actually expects.
        float measured = fitAxis switch
        {
            FitAxis.Width => raw.size.x,
            FitAxis.Height => raw.size.y,
            FitAxis.Depth => raw.size.z,
            _ => Mathf.Max(raw.size.x, raw.size.z)
        };

        if (measured < 0.0001f)
        {
            Debug.LogWarning($"'{sourceAsset.name}' has a degenerate bounds axis; skipped.");
            DestroyImmediate(working);
            return;
        }

        float scaleFactor = targetSize / measured;

        // 2. Re-pivot by nesting the mesh under a clean root, rather than
        //    moving vertices. Keeps the source mesh untouched and reversible.
        GameObject root = new GameObject(sourceAsset.name + "_Normalized");
        working.transform.SetParent(root.transform, true);
        working.transform.localScale = Vector3.one * scaleFactor;

        ArenaLayoutBuilder.TryGetRendererBounds(root, out Bounds scaled);

        Vector3 offset = pivotMode switch
        {
            PivotMode.BaseCentre => new Vector3(-scaled.center.x, -scaled.min.y, -scaled.center.z),
            PivotMode.TrueCentre => -scaled.center,
            _ => Vector3.zero
        };

        working.transform.position += offset;

        // 3. Gameplay setup.
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;

        foreach (Collider existing in root.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(existing);

        ArenaLayoutBuilder.TryGetRendererBounds(root, out Bounds final);

        if (colliderMode == ColliderMode.Box)
        {
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = final.center - root.transform.position;
            box.size = final.size;
        }
        else if (colliderMode == ColliderMode.Capsule)
        {
            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = final.center - root.transform.position;
            capsule.height = final.size.y;
            capsule.radius = Mathf.Max(final.size.x, final.size.z) * 0.5f;
            capsule.direction = 1;
        }

        if (markStatic)
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic);

        string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{outputFolder}/PF_{sourceAsset.name}.prefab");

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        DestroyImmediate(root);

        Debug.Log(
            $"Normalized '{sourceAsset.name}': raw {raw.size:F2} -> " +
            $"{final.size:F2} (x{scaleFactor:F3}), pivot {pivotMode}, " +
            $"collider {colliderMode}. Saved to {prefabPath}",
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
    }
}
