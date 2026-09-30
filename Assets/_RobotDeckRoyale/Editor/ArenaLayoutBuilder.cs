using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Rebuilds the arena from an <see cref="ArenaLayoutDefinition"/>.
///
/// Everything it creates lives under a single generated root, so a rebuild is
/// always a clean replace rather than an incremental edit that can drift. All
/// mutations go through Undo, so any build can be reverted with Ctrl+Z.
/// </summary>
public static class ArenaLayoutBuilder
{
    public const string GeneratedRootName = "ArenaLayout_Generated";

    [MenuItem("Robo Mania/Arena/Build Layout From Asset %#b")]
    public static void BuildFromSelectionOrAsset()
    {
        ArenaLayoutDefinition layout = ResolveLayout();

        if (layout == null)
        {
            EditorUtility.DisplayDialog(
                "Arena Layout",
                "No ArenaLayoutDefinition found. Create one via " +
                "Assets > Create > Robo Mania > Arena Layout Definition, " +
                "or run Robo Mania/Arena/Capture Current Scene Into Layout.",
                "OK");
            return;
        }

        Build(layout);
    }

    public static Transform Build(ArenaLayoutDefinition layout)
    {
        if (layout == null)
            return null;

        Transform root = GetOrCreateRoot();

        // Clear previous output so a build is deterministic.
        for (int index = root.childCount - 1; index >= 0; index--)
            Undo.DestroyObjectImmediate(root.GetChild(index).gameObject);

        int created = 0;

        foreach (ArenaLayoutDefinition.Placement placement in layout.EnumeratePlacements())
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                placement.Source.prefab, root);

            if (instance == null)
                continue;

            Undo.RegisterCreatedObjectUndo(instance, "Build Arena Layout");

            instance.name = placement.Name;
            instance.transform.localScale = Vector3.Scale(
                placement.Source.prefab.transform.localScale,
                placement.Source.scale);
            instance.transform.SetPositionAndRotation(
                placement.Position,
                placement.Rotation);

            if (placement.Source.groundToFloor)
                GroundToFloor(instance, layout.floorTopHeight);

            if (placement.Source.role == ArenaPieceRole.FloorDecal ||
                placement.Source.role == ArenaPieceRole.Dressing)
            {
                // Decals and edge dressing must never affect movement or shots.
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                    Undo.DestroyObjectImmediate(collider);
            }

            created++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Arena layout built: {created} pieces from '{layout.name}'.", layout);
        return root;
    }

    [MenuItem("Robo Mania/Arena/Clear Generated Layout")]
    public static void ClearGenerated()
    {
        GameObject root = GameObject.Find(GeneratedRootName);

        if (root != null)
            Undo.DestroyObjectImmediate(root);
    }

    /// <summary>
    /// Migration helper: reads the arena as it stands in the scene and writes it
    /// into a layout asset, so the data-driven pipeline starts from exactly what
    /// is already there instead of from a blank slate.
    /// </summary>
    [MenuItem("Robo Mania/Arena/Capture Current Scene Into Layout")]
    public static void CaptureSceneIntoLayout()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Arena Layout",
            "ArenaLayout",
            "asset",
            "Choose where to store the captured arena layout.");

        if (string.IsNullOrEmpty(path))
            return;

        ArenaLayoutDefinition layout =
            ScriptableObject.CreateInstance<ArenaLayoutDefinition>();

        CaptureGroup(layout, "ThemedArenaCover", ArenaPieceRole.FullCover);
        CaptureGroup(layout, "FlankSightlineWalls", ArenaPieceRole.LaneSeparator);
        CaptureGroup(layout, "ArenaLandmarks", ArenaPieceRole.FloorDecal);
        CaptureMachines(layout);

        AssetDatabase.CreateAsset(layout, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = layout;

        Debug.Log(
            $"Captured {layout.pieces.Count} authored pieces into '{path}'. " +
            "Mirrored counterparts were collapsed into single entries.",
            layout);
    }

    private static void CaptureGroup(
        ArenaLayoutDefinition layout,
        string parentName,
        ArenaPieceRole role)
    {
        GameObject parent = GameObject.Find(parentName);

        if (parent == null)
            return;

        List<Transform> children = new List<Transform>();
        foreach (Transform child in parent.transform)
            children.Add(child);

        HashSet<Transform> consumed = new HashSet<Transform>();

        foreach (Transform child in children)
        {
            if (consumed.Contains(child))
                continue;

            // Collapse a mirrored pair into one authored entry so symmetry is
            // enforced by the data from here on.
            Transform partner = FindMirrorPartner(child, children, consumed);

            if (partner != null)
                consumed.Add(partner);

            consumed.Add(child);

            Transform authored = child.position.z <= 0f ? child : (partner ?? child);

            GameObject prefabSource = ResolvePrefabSource(authored.gameObject);

            layout.pieces.Add(new ArenaPiece
            {
                id = authored.name,
                prefab = prefabSource,
                position = authored.position,
                rotation = authored.eulerAngles,
                scale = CaptureScaleRatio(authored, prefabSource),
                role = role,
                mirrorToOppositeHalf = partner != null,
                groundToFloor = role != ArenaPieceRole.FloorDecal
            });
        }
    }

    /// <summary>
    /// Records how much the placed instance deviates from its prefab's authored
    /// scale. Storing the ratio rather than the absolute scale means the layout
    /// keeps working if the prefab itself is later rescaled.
    /// </summary>
    private static Vector3 CaptureScaleRatio(Transform instance, GameObject prefabSource)
    {
        if (prefabSource == null)
            return Vector3.one;

        Vector3 prefabScale = prefabSource.transform.localScale;
        Vector3 instanceScale = instance.localScale;

        return new Vector3(
            Mathf.Approximately(prefabScale.x, 0f) ? 1f : instanceScale.x / prefabScale.x,
            Mathf.Approximately(prefabScale.y, 0f) ? 1f : instanceScale.y / prefabScale.y,
            Mathf.Approximately(prefabScale.z, 0f) ? 1f : instanceScale.z / prefabScale.z);
    }

    private static void CaptureMachines(ArenaLayoutDefinition layout)
    {
        GameObject cover = GameObject.Find("FD_BreakableCover");

        if (cover == null)
            return;

        List<Transform> machines = new List<Transform>();
        foreach (Transform child in cover.transform)
        {
            if (child.name.StartsWith("machine"))
                machines.Add(child);
        }

        HashSet<Transform> consumed = new HashSet<Transform>();

        foreach (Transform machine in machines)
        {
            if (consumed.Contains(machine))
                continue;

            Transform partner = FindMirrorPartner(machine, machines, consumed);

            if (partner != null)
                consumed.Add(partner);

            consumed.Add(machine);

            Transform authored = machine.position.z <= 0f ? machine : (partner ?? machine);

            GameObject prefabSource = ResolvePrefabSource(authored.gameObject);

            layout.pieces.Add(new ArenaPiece
            {
                id = authored.name,
                prefab = prefabSource,
                position = authored.position,
                rotation = authored.eulerAngles,
                scale = CaptureScaleRatio(authored, prefabSource),
                role = ArenaPieceRole.Landmark,
                mirrorToOppositeHalf = partner != null,
                groundToFloor = false
            });
        }
    }

    private static Transform FindMirrorPartner(
        Transform source,
        List<Transform> candidates,
        HashSet<Transform> consumed)
    {
        Vector3 mirrored = new Vector3(-source.position.x, source.position.y, -source.position.z);

        foreach (Transform candidate in candidates)
        {
            if (candidate == source || consumed.Contains(candidate))
                continue;

            Vector3 a = new Vector3(candidate.position.x, 0f, candidate.position.z);
            Vector3 b = new Vector3(mirrored.x, 0f, mirrored.z);

            if (Vector3.Distance(a, b) < 0.8f)
                return candidate;
        }

        return null;
    }

    private static GameObject ResolvePrefabSource(GameObject instance)
    {
        GameObject source =
            PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance);

        return source != null ? source : instance;
    }

    private static void GroundToFloor(GameObject instance, float floorTop)
    {
        if (!TryGetRendererBounds(instance, out Bounds bounds))
            return;

        instance.transform.position +=
            Vector3.up * (floorTop + 0.005f - bounds.min.y);
    }

    public static bool TryGetRendererBounds(GameObject target, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null ||
                renderer is LineRenderer ||
                renderer is TrailRenderer ||
                renderer.GetComponentInParent<Canvas>() != null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static Transform GetOrCreateRoot()
    {
        GameObject root = GameObject.Find(GeneratedRootName);

        if (root == null)
        {
            root = new GameObject(GeneratedRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Arena Layout Root");
        }

        // ArenaCombatSetup decides what counts as a gameplay obstacle by walking
        // up for a parent named FD_BreakableCover, and that is what gives each
        // piece its NavMeshObstacle and boundary registration. Nesting the
        // generated root there keeps built pieces carving the NavMesh.
        GameObject obstacleRoot = GameObject.Find("FD_BreakableCover");

        if (obstacleRoot != null && root.transform.parent != obstacleRoot.transform)
        {
            Undo.SetTransformParent(
                root.transform, obstacleRoot.transform, "Parent Arena Layout Root");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
        }

        return root.transform;
    }

    private static ArenaLayoutDefinition ResolveLayout()
    {
        if (Selection.activeObject is ArenaLayoutDefinition selected)
            return selected;

        string[] guids = AssetDatabase.FindAssets("t:ArenaLayoutDefinition");

        if (guids.Length == 0)
            return null;

        return AssetDatabase.LoadAssetAtPath<ArenaLayoutDefinition>(
            AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
