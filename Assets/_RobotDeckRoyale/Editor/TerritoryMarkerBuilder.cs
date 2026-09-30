using UnityEditor;
using UnityEngine;

/// <summary>
/// Lays the dashed red and blue lines that show whose half of the arena is
/// whose, the way the reference map marks its two territories.
///
/// Built as flat quads sitting just above the deck rather than as decals or a
/// projector: the arena floor is a single flat plane, so a quad is the cheapest
/// thing that reads correctly and costs one draw call per dash with no
/// projection maths on a phone.
///
/// Everything lands under one parent so a rebuild is a clean replace, and the
/// dashes carry no colliders -- they are paint on the floor, not geometry.
/// </summary>
public static class TerritoryMarkerBuilder
{
    private const string RootName = "TerritoryMarkers";
    private const string MaterialFolder = "Assets/_RobotDeckRoyale/Art/Materials/Arena";

    // Sits just clear of the deck so it never z-fights with the floor plate.
    private const float Height = 0.36f;

    [MenuItem("Robo Mania/Arena/Build Territory Markers")]
    public static void Build()
    {
        GameObject existing = GameObject.Find(RootName);

        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Territory Markers");

        Material blue = ResolveMaterial("M_Territory_Blue", new Color(0.16f, 0.52f, 1f));
        Material red = ResolveMaterial("M_Territory_Red", new Color(1f, 0.20f, 0.24f));

        int dashes = 0;
        dashes += BuildLine(root.transform, blue, -1f, "Blue");
        dashes += BuildLine(root.transform, red, 1f, "Red");

        EditorSceneManagerMarkDirty();
        Debug.Log($"Territory markers rebuilt: {dashes} dashes.", root);
        Selection.activeGameObject = root;
    }

    /// <summary>
    /// One dashed line across a half. It bows toward the centre so it reads as a
    /// front line rather than a fence, and the dash count is fixed so both
    /// halves get an identical line.
    /// </summary>
    private static int BuildLine(
        Transform parent, Material material, float side, string team)
    {
        // The same curve the placement rule uses (BuildPlacementController.TerritoryEdge).
        const int DashCount = 13;
        const float HalfWidth = BuildPlacementController.TerritoryHalfSpan;
        const float BaseDepth = BuildPlacementController.TerritoryEdgeDepth;
        const float Bow = BuildPlacementController.TerritoryEdgeBow;
        const float DashLength = 1.9f;
        const float DashWidth = 0.42f;

        GameObject group = new GameObject(team + "Territory");
        group.transform.SetParent(parent, false);

        for (int index = 0; index < DashCount; index++)
        {
            float t = index / (float)(DashCount - 1);
            float x = Mathf.Lerp(-HalfWidth, HalfWidth, t);
            float z = side * (BaseDepth - Bow * Mathf.Sin(t * Mathf.PI));

            // Angle each dash along the curve so the line flows instead of
            // reading as a row of loose ticks.
            float slope = -side * Bow * Mathf.PI *
                          Mathf.Cos(t * Mathf.PI) / (HalfWidth * 2f);
            float yaw = Mathf.Atan2(slope, 1f) * Mathf.Rad2Deg;

            GameObject dash = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dash.name = $"{team}Dash_{index:00}";
            Object.DestroyImmediate(dash.GetComponent<Collider>());
            dash.transform.SetParent(group.transform, false);
            dash.transform.SetPositionAndRotation(
                new Vector3(x, Height, z),
                Quaternion.Euler(90f, yaw, 0f));
            dash.transform.localScale = new Vector3(DashLength, DashWidth, 1f);
            dash.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(
                dash, StaticEditorFlags.BatchingStatic);
        }

        return DashCount;
    }

    private static Material ResolveMaterial(string materialName, Color colour)
    {
        string path = $"{MaterialFolder}/{materialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader) { name = materialName };
            AssetDatabase.CreateAsset(material, path);
        }

        // Unlit so the line keeps its team colour regardless of how the arena is
        // lit; a shaded marker would go muddy in the darker half of the map.
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", colour);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", colour);

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void EditorSceneManagerMarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
}
