using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives imported kit meshes a UV0 when the source file has none.
///
/// Much of the MapKit was exported without texture coordinates, and a mesh with
/// no UV0 cannot show a texture at all -- which is why those props stayed flat
/// colour while the floor took its panelling. Unity's model importer can only
/// generate UV2 for lightmapping, never UV0, so it has to be done here.
///
/// The projection is box mapping: each triangle is projected along whichever
/// world axis its normal points down. For hard-surface, axis-aligned props like
/// crates and containers that gives clean, square texel density on every face
/// with no visible stretching, and it needs no authoring in the source file.
///
/// Runs only on the kit folder, and only on meshes that are actually missing
/// UVs, so anything properly unwrapped is left exactly as the artist made it.
/// </summary>
public class MapKitUvPostprocessor : AssetPostprocessor
{
    private const string KitFolder = "Assets/Art/MapKit/";

    /// <summary>
    /// UV units per local unit. The kit's meshes are authored small and scaled
    /// up by roughly 100 in the scene, so this brings a UV unit to about a
    /// metre of finished surface and lets one tiling value suit every prop.
    /// </summary>
    private const float ProjectionScale = 100f;

    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(KitFolder))
            return;

        int generated = 0;

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;

            if (mesh == null)
                continue;

            if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount)
                continue;

            if (ApplyBoxProjection(mesh))
                generated++;
        }

        if (generated > 0)
        {
            Debug.Log(
                $"{System.IO.Path.GetFileName(assetPath)}: generated box-projected " +
                $"UVs for {generated} mesh(es) that shipped without any.");
        }
    }

    private static bool ApplyBoxProjection(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;

        if (vertices == null || vertices.Length == 0)
            return false;

        Vector3[] normals = mesh.normals;

        if (normals == null || normals.Length != vertices.Length)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        Vector2[] uv = new Vector2[vertices.Length];

        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 position = vertices[index] * ProjectionScale;
            Vector3 normal = normals[index];

            float absX = Mathf.Abs(normal.x);
            float absY = Mathf.Abs(normal.y);
            float absZ = Mathf.Abs(normal.z);

            // Project along the dominant axis so each face is mapped flat.
            if (absY >= absX && absY >= absZ)
                uv[index] = new Vector2(position.x, position.z);
            else if (absX >= absZ)
                uv[index] = new Vector2(position.z, position.y);
            else
                uv[index] = new Vector2(position.x, position.y);
        }

        mesh.uv = uv;
        return true;
    }
}
