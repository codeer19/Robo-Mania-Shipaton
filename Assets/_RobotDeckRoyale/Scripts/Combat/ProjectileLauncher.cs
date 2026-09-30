using UnityEngine;

/// <summary>
/// The single place a shot is produced. Every shooter previously had its own
/// copy of "instantiate the prefab, look down the aim vector, initialise the
/// owner and damage, play a muzzle effect", which is how their behaviour drifted
/// apart. Routing all of them through here also means projectile pooling and any
/// future Fusion spawn path only has to be implemented once.
/// </summary>
public static class ProjectileLauncher
{
    public static BasicProjectile Fire(
        BasicProjectile prefab,
        Vector3 origin,
        Vector3 direction,
        GameObject owner,
        int damageOverride = -1,
        float aimErrorDegrees = 0f,
        GameObject muzzleEffectPrefab = null,
        float muzzleEffectLifetime = 1.5f)
    {
        if (prefab == null || direction.sqrMagnitude <= 0.000001f)
            return null;

        Vector3 launchDirection = ApplyAimError(direction.normalized, aimErrorDegrees);
        Quaternion rotation = Quaternion.LookRotation(launchDirection, Vector3.up);

        BasicProjectile projectile = CombatPool.Get(prefab, origin, rotation);

        if (projectile == null)
            return null;

        projectile.Initialize(owner, damageOverride);

        SpawnEffect(muzzleEffectPrefab, origin, rotation, muzzleEffectLifetime);

        return projectile;
    }

    /// <summary>
    /// Pooled replacement for the Instantiate/Destroy pairs that used to spawn
    /// muzzle flashes and impact effects on every shot and every hit.
    /// </summary>
    public static void SpawnEffect(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifetime)
    {
        if (prefab == null)
            return;

        GameObject effect = CombatPool.Get(prefab, position, rotation);

        if (effect == null)
            return;

        PooledEffect pooled = effect.GetComponent<PooledEffect>();

        if (pooled == null)
        {
            pooled = effect.AddComponent<PooledEffect>();
            CombatPool.RefreshParts(effect);
        }

        pooled.Play(lifetime);
    }

    private static Vector3 ApplyAimError(Vector3 direction, float aimErrorDegrees)
    {
        if (aimErrorDegrees <= 0f)
            return direction;

        float yaw = Random.Range(-aimErrorDegrees, aimErrorDegrees);
        return Quaternion.Euler(0f, yaw, 0f) * direction;
    }
}
