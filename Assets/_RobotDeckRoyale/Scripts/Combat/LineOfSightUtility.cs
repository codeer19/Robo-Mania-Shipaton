using UnityEngine;

/// <summary>
/// Shared, allocation-free line-of-sight test for the combat actors.
///
/// Every shooter previously ran its own copy of Physics.RaycastAll followed by
/// Array.Sort with a lambda comparer. That allocated a hit array and a delegate
/// on every call, and the bot AI calls it several times per frame, so it was a
/// steady source of garbage during a fight.
/// </summary>
public static class LineOfSightUtility
{
    private const int MaximumHits = 16;

    private static readonly RaycastHit[] HitBuffer = new RaycastHit[MaximumHits];

    /// <summary>
    /// Returns true when nothing except <paramref name="shooter"/> stands
    /// between the origin and the collider belonging to <paramref name="target"/>.
    /// </summary>
    public static bool IsClear(
        Vector3 origin,
        Vector3 direction,
        float distance,
        LayerMask mask,
        Transform shooter,
        Damageable target)
    {
        int count = Physics.RaycastNonAlloc(
            origin,
            direction,
            HitBuffer,
            distance,
            mask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        Collider nearestCollider = null;

        for (int index = 0; index < count; index++)
        {
            Collider candidate = HitBuffer[index].collider;

            if (candidate == null)
                continue;

            if (shooter != null &&
                (candidate.transform == shooter ||
                 candidate.transform.IsChildOf(shooter)))
            {
                continue;
            }

            if (HitBuffer[index].distance >= nearestDistance)
                continue;

            nearestDistance = HitBuffer[index].distance;
            nearestCollider = candidate;
        }

        if (nearestCollider == null)
            return true;

        return nearestCollider.GetComponentInParent<Damageable>() == target;
    }
}
