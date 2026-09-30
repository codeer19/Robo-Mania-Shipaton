using UnityEngine;

public static class ColliderUtility
{
    /// <summary>
    /// Collider.ClosestPoint only supports primitives and convex meshes. Called
    /// on a concave MeshCollider - which the arena floor is - Unity logs a
    /// warning every call and returns the query point unchanged, so callers
    /// silently get a wrong answer as well as a flooded console.
    ///
    /// Falls back to the collider's bounding box for those, which is coarse but
    /// finite and does not spam.
    /// </summary>
    public static Vector3 ClosestPointSafe(Collider collider, Vector3 position)
    {
        if (collider == null)
        {
            return position;
        }

        MeshCollider mesh = collider as MeshCollider;
        if (mesh != null && !mesh.convex)
        {
            return collider.bounds.ClosestPoint(position);
        }

        return collider.ClosestPoint(position);
    }
}
