using System;
using UnityEngine;

/// <summary>
/// Ammunition, reload and rate-of-fire rules shared by every shooter in the
/// arena. The player blaster, the duel bot, the swarm crawlers and the auto
/// turret each used to carry their own copy of this logic, which is how the bot
/// ended up on a different fire cadence to the player.
///
/// This is a plain serialisable class rather than a MonoBehaviour so an owner
/// can keep its existing inspector layout, and every timing decision takes the
/// current time as a parameter. When the project moves to Photon Fusion the
/// same instance can be driven from tick time instead of Time.time without
/// touching the rules themselves.
/// </summary>
[Serializable]
public class WeaponCore
{
    [SerializeField, Min(1)] private int maxAmmo = 3;
    [SerializeField, Min(0.05f)] private float reloadSecondsPerAmmo = 1.15f;
    [SerializeField, Min(0.02f)] private float fireInterval = 0.25f;

    private int currentAmmo = -1;
    private float nextFireTime;
    private float nextReloadTime;

    public event Action<int, int> AmmoChanged;

    public int CurrentAmmo => currentAmmo < 0 ? maxAmmo : currentAmmo;
    public int MaxAmmo => maxAmmo;
    public float FireInterval => fireInterval;
    public float ReloadSecondsPerAmmo => reloadSecondsPerAmmo;

    /// <summary>
    /// Lets an owner that already exposes its own tuning fields keep them as the
    /// authored source of truth while still sharing the behaviour.
    /// </summary>
    public void Configure(int ammoCapacity, float secondsPerAmmo, float secondsBetweenShots)
    {
        maxAmmo = Mathf.Max(1, ammoCapacity);
        reloadSecondsPerAmmo = Mathf.Max(0.05f, secondsPerAmmo);
        fireInterval = Mathf.Max(0.02f, secondsBetweenShots);
        RestoreFull();
    }

    public void Initialize()
    {
        currentAmmo = maxAmmo;
        nextFireTime = 0f;
        nextReloadTime = 0f;
    }

    public bool HasAmmo => CurrentAmmo > 0;

    public bool IsReady(float time)
    {
        return CurrentAmmo > 0 && time >= nextFireTime;
    }

    public float GetReloadProgress(float time)
    {
        if (CurrentAmmo >= maxAmmo)
            return 1f;

        return Mathf.Clamp01(
            1f - (nextReloadTime - time) / reloadSecondsPerAmmo);
    }

    /// <summary>
    /// Advances passive reload. Safe to call every frame from any owner.
    /// </summary>
    public void Tick(float time)
    {
        if (currentAmmo < 0)
            Initialize();

        if (currentAmmo >= maxAmmo || time < nextReloadTime)
            return;

        currentAmmo++;
        AmmoChanged?.Invoke(currentAmmo, maxAmmo);

        if (currentAmmo < maxAmmo)
            nextReloadTime = time + reloadSecondsPerAmmo;
    }

    /// <summary>
    /// Consumes a round and starts both cooldowns. Call only after the shot has
    /// actually been produced.
    /// </summary>
    public void CommitShot(float time)
    {
        if (currentAmmo < 0)
            Initialize();

        currentAmmo = Mathf.Max(0, currentAmmo - 1);
        nextFireTime = time + fireInterval;
        nextReloadTime = time + reloadSecondsPerAmmo;

        AmmoChanged?.Invoke(currentAmmo, maxAmmo);
    }

    public void RestoreFull()
    {
        currentAmmo = maxAmmo;
        nextFireTime = 0f;
        nextReloadTime = 0f;

        AmmoChanged?.Invoke(currentAmmo, maxAmmo);
    }
}
