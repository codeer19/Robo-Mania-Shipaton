using UnityEngine;

/// <summary>
/// Marks a static arena prop (the flower pots) as solid for every mover.
///
/// The prop carries a plain collider sized to its visual footprint, so robots
/// stop against it; <see cref="ArenaCombatSetup"/> sees this marker and gives
/// the collider a carving NavMesh obstacle, so the bot and the Spidys path
/// around it instead of pushing into it. No per-frame work.
/// </summary>
[DisallowMultipleComponent]
public sealed class ArenaObstacle : MonoBehaviour
{
}
