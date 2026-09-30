using UnityEngine;

public enum TeamSide { SideA, SideB }
public enum PrivateRoomAction { Create, Join }

/// <summary>World sides never change with the viewer. Color is derived locally.</summary>
public static class TeamSides
{
    public static TeamSide Opponent(TeamSide side) => side == TeamSide.SideA ? TeamSide.SideB : TeamSide.SideA;
    public static bool IsFriendly(TeamSide side) => side == MatchSessionContext.LocalSide;
    public static Color Color(TeamSide side) => IsFriendly(side)
        ? new Color(0.05f, 0.55f, 1f) : new Color(1f, 0.12f, 0.06f);
    // Adapter for existing serialized scene anchors and offline-only components.
    public static FortressTeam SceneTeam(TeamSide side) => side == TeamSide.SideA ? FortressTeam.Blue : FortressTeam.Red;
    public static TeamSide FromSceneTeam(FortressTeam team) => team == FortressTeam.Blue ? TeamSide.SideA : TeamSide.SideB;
    public static FortressTeam PresentationTeam(TeamSide side) => IsFriendly(side) ? FortressTeam.Blue : FortressTeam.Red;
    public static Vector3 CameraDirection(Camera camera, Vector2 input)
    {
        if (camera == null) return Vector3.zero;
        Vector3 forward = camera.transform.forward, right = camera.transform.right;
        forward.y = right.y = 0f;
        return forward.normalized * input.y + right.normalized * input.x;
    }
}
