using TMPro;
using UnityEngine;

/// <summary>Local accents only. Never changes Spark skin materials or a world-side assignment.</summary>
public static class OnlineTeamPresentation
{
    public static void BindPlayer(GameObject root, TeamSide side, TMP_Text nameplate = null)
    {
        AssignSide(root, side);
        EnsureHealthBar(root);
        var ring = root.GetComponent<TeamGroundRing>();
        bool showMarker = TeamGroundRing.AllowsMarker(root.GetComponent<FortressTarget>());
        if (showMarker && ring == null) ring = root.AddComponent<TeamGroundRing>();
        if (ring != null) ring.enabled = showMarker;
        if (nameplate != null) nameplate.color = TeamSides.Color(side);
    }
    private static void EnsureHealthBar(GameObject root)
    {
        if (root.GetComponent<Damageable>() == null) root.AddComponent<Damageable>();
        if (root.GetComponentInChildren<WorldHealthBar>(true) != null) return;
        var bar = new GameObject("Online Health", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        bar.transform.SetParent(root.transform, false);
        bar.transform.localPosition = Vector3.up * 2.7f;
        bar.transform.localScale = Vector3.one * .01f;
        bar.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        bar.AddComponent<WorldHealthBar>();
    }

    /// <summary>
    /// Sets which side owns an object, without attaching any ground decoration.
    ///
    /// Split out from BindPlayer because ownership and the ground ring are not the
    /// same thing: the heists need the team identity for targeting and scoring,
    /// but a ring sized to a heist covers a large part of the floor.
    /// </summary>
    public static void AssignSide(GameObject root, TeamSide side)
    {
        var identity = root.GetComponent<FortressTarget>();
        if (identity == null) identity = root.AddComponent<FortressTarget>();
        identity.SetTeam(TeamSides.SceneTeam(side));
    }
    public static void TintHeist(Damageable heist, TeamSide side)
    {
        // Only the authored colored panels are replaced. Black metal, glass and health screens retain their materials.
        foreach (var renderer in heist.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.GetComponentInParent<Canvas>() != null) continue;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
                string name = material.name.ToLowerInvariant();
                bool coloredPanel = name.Contains("blue") || name.Contains("red") ||
                    (color.b > color.r * 1.5f && color.b > 0.3f) || (color.r > color.b * 1.5f && color.r > 0.3f && color.g < color.r * 0.6f);
                if (!coloredPanel) continue;
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, i);
                block.SetColor("_BaseColor", TeamSides.Color(side)); block.SetColor("_Color", TeamSides.Color(side));
                renderer.SetPropertyBlock(block, i);
            }
        }
        // Identity only. The heist keeps its ownership, collider, health and
        // targeting; it just no longer gets a large coloured disc on the floor.
        AssignSide(heist.gameObject, side);

        // Remove one added by an earlier run or by the scene's own setup pass.
        var ring = heist.GetComponent<TeamGroundRing>();
        if (ring != null) Object.Destroy(ring);
    }
}
