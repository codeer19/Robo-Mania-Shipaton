using UnityEngine;

/// <summary>Explicit references: works even while PlayerRoot is inactive during loading.</summary>
public static class ArenaTeamPlacement
{
    public static bool PlaceLocalPlayer(RobotPlayerController player, FortressTeam team, Transform blueSpawn, Transform redSpawn)
    {
        Transform spawn = team == FortressTeam.Blue ? blueSpawn : team == FortressTeam.Red ? redSpawn : null;
        if (player == null || spawn == null) return false;
        var controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        player.ResetMovement();
        var aim = player.GetComponent<RobotAimController>();
        if (aim != null) aim.ClearAimTarget();
        var body = player.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        if (wasEnabled) controller.enabled = true;
        player.GetComponent<FortressTarget>().SetTeam(team);
        Debug.Log($"[SPAWN] Player=local Team={team} Spawn={spawn.name} position={spawn.position:F3}");
        return true;
    }
}

