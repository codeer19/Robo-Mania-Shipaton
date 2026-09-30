using System;
using System.Collections.Generic;
using UnityEngine;

public enum ArenaPieceRole
{
    FullCover,
    LowCover,
    LaneSeparator,
    Landmark,
    Boundary,
    FloorDecal,
    Dressing
}

/// <summary>
/// One authored placement. When <see cref="mirrorToOppositeHalf"/> is set the
/// builder also emits the 180-degree rotational counterpart, which is what makes
/// arena symmetry a property of the data rather than something that has to be
/// re-checked by hand every time a piece moves.
/// </summary>
[Serializable]
public class ArenaPiece
{
    public string id = "Piece";
    public GameObject prefab;

    [Tooltip("Authored on the blue half (negative Z) when mirroring is enabled.")]
    public Vector3 position;

    [Tooltip("Full world rotation. Stored complete rather than as a yaw so prefabs " +
             "that carry a baked orientation (imported and generated meshes usually do) " +
             "survive a rebuild.")]
    public Vector3 rotation;
    public Vector3 scale = Vector3.one;
    public ArenaPieceRole role = ArenaPieceRole.FullCover;

    [Tooltip("Emit a 180-degree rotational counterpart so both teams get an identical arena.")]
    public bool mirrorToOppositeHalf = true;

    [Tooltip("Drop the piece so its lowest point rests on the arena floor height.")]
    public bool groundToFloor = true;
}

/// <summary>
/// The single source of truth for arena composition.
///
/// Placement used to live in three places that could disagree: a hardcoded
/// array inside ArenaCombatSetup, the scene transforms, and the runtime pass
/// that overwrote the scene from the array. This asset replaces all of that:
/// edit it in the Inspector or from tooling, press Build, and the scene is
/// regenerated deterministically.
/// </summary>
[CreateAssetMenu(
    fileName = "ArenaLayout",
    menuName = "Robo Mania/Arena Layout Definition")]
public class ArenaLayoutDefinition : ScriptableObject
{
    [Header("Arena")]
    public Vector2 playableHalfExtents = new Vector2(25f, 35f);

    [Tooltip("Top surface of the visible arena floor. Pieces ground to this, not to Y=0.")]
    public float floorTopHeight = 0.341f;

    [Header("Readability Budget")]
    [Tooltip("Props taller than this start hiding robots at the gameplay camera pitch.")]
    public float maximumPropHeight = 3.7f;

    [Tooltip("Minimum walkable gap the validator requires between blockers.")]
    public float minimumRouteWidth = 2.4f;

    [Header("Pieces")]
    public List<ArenaPiece> pieces = new List<ArenaPiece>();

    public struct Placement
    {
        public ArenaPiece Source;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsMirror;
        public string Name;
    }

    /// <summary>
    /// Expands the authored list into every placement the builder should create,
    /// including generated mirrors. A mirror is the original rotated 180 degrees
    /// about world up, which is exact for any starting orientation.
    /// </summary>
    public IEnumerable<Placement> EnumeratePlacements()
    {
        foreach (ArenaPiece piece in pieces)
        {
            if (piece == null || piece.prefab == null)
                continue;

            Quaternion rotation = Quaternion.Euler(piece.rotation);

            yield return new Placement
            {
                Source = piece,
                Position = piece.position,
                Rotation = rotation,
                IsMirror = false,
                Name = piece.id
            };

            if (!piece.mirrorToOppositeHalf)
                continue;

            yield return new Placement
            {
                Source = piece,
                Position = new Vector3(-piece.position.x, piece.position.y, -piece.position.z),
                Rotation = Quaternion.AngleAxis(180f, Vector3.up) * rotation,
                IsMirror = true,
                Name = piece.id + "_Mirror"
            };
        }
    }
}
