using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared presentation for the placeable build items: team-coloured glow and a
/// world health bar. Friendly glow is cyan, enemy glow is red, decided from the
/// viewer's side so both players read their own defences the same way.
/// </summary>
public static class BuildItemPresentation
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public static bool IsFriendly(FortressTarget identity)
    {
        if (identity == null) return true;
        return MatchSessionContext.Type == MatchType.HumanOnline
            ? TeamSides.IsFriendly(TeamSides.FromSceneTeam(identity.Team))
            : identity.Team == FortressTeam.Blue;
    }

    public static Color GlowColour(bool friendly) => friendly ? BuildVfx.Friendly : BuildVfx.Enemy;

    /// <summary>Tints one material slot of a renderer without instancing its material.</summary>
    public static void Tint(Renderer renderer, int materialIndex, Color colour, MaterialPropertyBlock block)
    {
        if (renderer == null || block == null) return;
        renderer.GetPropertyBlock(block, materialIndex);
        block.SetColor(BaseColorId, colour);
        renderer.SetPropertyBlock(block, materialIndex);
    }

    /// <summary>Same world-space bar the turret builds for itself (AutoTurret).</summary>
    public static void EnsureHealthBar(Transform root, float height)
    {
        if (root.GetComponentInChildren<WorldHealthBar>(true) != null) return;
        var barObject = new GameObject("Build Item Health Bar", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        barObject.transform.SetParent(root, false);
        barObject.transform.localPosition = Vector3.up * height;
        barObject.transform.localScale = Vector3.one * 0.01f;
        var canvas = barObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        barObject.AddComponent<WorldHealthBar>();
    }
}
