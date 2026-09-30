using UnityEngine;

/// <summary>
/// Returns a one-shot visual effect to <see cref="CombatPool"/> after its
/// lifetime instead of destroying it. Added automatically by
/// <see cref="ProjectileLauncher"/> when it spawns an effect prefab, so existing
/// effect prefabs need no authoring change to become poolable.
/// </summary>
[DisallowMultipleComponent]
public class PooledEffect : MonoBehaviour, IPooledObject
{
    private float releaseAtTime;
    private bool active;
    private ParticleSystem[] particleSystems;
    private TrailRenderer[] trails;

    public void Play(float lifetime)
    {
        releaseAtTime = Time.time + Mathf.Max(0.05f, lifetime);
        active = true;
    }

    public void OnRetrievedFromPool()
    {
        active = false;

        if (particleSystems == null)
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem system in particleSystems)
        {
            if (system == null)
                continue;

            system.Clear(true);
            system.Play(true);
        }

        if (trails == null)
            trails = GetComponentsInChildren<TrailRenderer>(true);

        foreach (TrailRenderer trail in trails)
        {
            if (trail != null)
                trail.Clear();
        }
    }

    public void OnReturnedToPool()
    {
        active = false;

        if (particleSystems == null)
            return;

        foreach (ParticleSystem system in particleSystems)
        {
            if (system != null)
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void Update()
    {
        if (!active || Time.time < releaseAtTime)
            return;

        active = false;
        CombatPool.Release(gameObject);
    }
}
