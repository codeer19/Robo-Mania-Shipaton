using System;
using UnityEngine;

public class Damageable : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private bool destroyOnDeath = true;
    [Tooltip("Disable for deployables and swarm units that must never regenerate or receive healing.")]
    [SerializeField] private bool allowHealing = true;

    [Header("Auto Regeneration")]
    [SerializeField] private bool autoRegenerate;
    [SerializeField] private float regenerationDelay = 3f;
    [SerializeField] private float healthPerSecond = 16f;

    [Header("Attribution")]
    [Tooltip("A kill is still credited to the last attacker for this long after their final hit.")]
    [SerializeField, Min(0f)] private float attributionMemory = 8f;

    private int currentHealth;
    private bool isDead;
    private bool isInvulnerable;
    private float lastDamageTime;
    private float regenerationRemainder;
    private FortressTarget targetIdentity;
    private GameObject lastAttacker;
    private float lastAttackerTime = float.NegativeInfinity;
    private DamageInfo lastDamage;
    private float healingBlockedUntil = float.NegativeInfinity;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool IsInvulnerable => isInvulnerable;
    public bool AllowsHealing => allowHealing;

    /// <summary>
    /// NO HEAL (Recovery Jammer). While it runs every heal source is refused here,
    /// at the one place health goes up, so pads, regeneration and anything added
    /// later all respect it without their own checks.
    /// </summary>
    public bool HealingBlocked => Time.time < healingBlockedUntil;
    public float HealingBlockedRemaining => Mathf.Max(0f, healingBlockedUntil - Time.time);
    public void BlockHealing(float seconds)
    {
        if (seconds <= 0f) return;
        healingBlockedUntil = Mathf.Max(healingBlockedUntil, Time.time + seconds);
        NoHealIndicator.Show(transform, healingBlockedUntil - Time.time);
    }
    public float HealthPercent =>
        maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;

    /// <summary>
    /// Who most recently damaged this target, within the attribution window.
    /// Null when the death cannot be credited to anyone.
    /// </summary>
    public GameObject LastAttacker =>
        lastAttacker != null &&
        Time.time - lastAttackerTime <= attributionMemory
            ? lastAttacker
            : null;

    public DamageInfo LastDamage => lastDamage;

    public event Action<Damageable, int> Damaged;

    /// <summary>Attributed variant of <see cref="Damaged"/>.</summary>
    public event Action<Damageable, DamageInfo> DamageApplied;
    public event Action<Damageable, int> Healed;
    public event Action<Damageable, int, int> HealthChanged;
    public event Action<Damageable> Died;

    private void Awake()
    {
        targetIdentity = GetComponent<FortressTarget>();
        ResetHealth();
        lastDamageTime = Time.time;
    }

    private void Update()
    {
        TryRegenerateHealth();
    }

    public void TakeDamage(int damage)
    {
        ApplyDamage(new DamageInfo(damage));
    }

    public bool TakeDamage(
        int damage,
        FortressTeam attackingTeam,
        GameObject source = null)
    {
        return TakeDamage(new DamageInfo(
            damage,
            source,
            true,
            attackingTeam));
    }

    /// <summary>
    /// Full damage entry point. Carries the attacker and impact information
    /// through to the feedback layer so hits can be attributed and presented.
    /// </summary>
    public bool TakeDamage(in DamageInfo info)
    {
        if (info.HasTeam &&
            targetIdentity != null &&
            targetIdentity.Team == info.Team)
        {
            return false;
        }

        return ApplyDamage(info);
    }

    private bool ApplyDamage(in DamageInfo info)
    {
        if (MatchSessionContext.Type == MatchType.HumanOnline) return false;
        if (isDead || isInvulnerable || info.Amount <= 0)
        {
            return false;
        }

        lastDamageTime = Time.time;
        regenerationRemainder = 0f;

        if (info.Source != null)
        {
            lastAttacker = info.Source;
            lastAttackerTime = Time.time;
        }

        lastDamage = info;

        currentHealth = Mathf.Max(0, currentHealth - info.Amount);
        Damaged?.Invoke(this, info.Amount);
        DamageApplied?.Invoke(this, info);
        HealthChanged?.Invoke(this, currentHealth, maxHealth);
        CombatEvents.RaiseDamageApplied(this, info);

        if (currentHealth > 0)
        {
            return true;
        }

        isDead = true;
        Died?.Invoke(this);
        CombatEvents.RaiseKilled(this, lastAttacker);

        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }

        return true;
    }

    public void Heal(int amount)
    {
        if (MatchSessionContext.Type == MatchType.HumanOnline) return;
        if (!allowHealing || isDead || amount <= 0 || HealingBlocked)
        {
            return;
        }

        int previousHealth = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        int restoredHealth = currentHealth - previousHealth;

        if (restoredHealth <= 0)
        {
            return;
        }

        Healed?.Invoke(this, restoredHealth);
        HealthChanged?.Invoke(this, currentHealth, maxHealth);
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        isDead = false;
        isInvulnerable = false;
        regenerationRemainder = 0f;
        healingBlockedUntil = float.NegativeInfinity;
        lastAttacker = null;
        lastAttackerTime = float.NegativeInfinity;
        HealthChanged?.Invoke(this, currentHealth, maxHealth);
    }

    // Replicated health drives the existing bars. Offline death/regen callbacks must not
    // independently destroy a heist, respawn a player, or choose the match winner online.
    public void ApplyReplicatedHealth(int health, int maximum)
    {
        if (MatchSessionContext.Type != MatchType.HumanOnline || maximum <= 0) return;
        health = Mathf.Clamp(health, 0, maximum);
        if (currentHealth == health && maxHealth == maximum) return;
        maxHealth = maximum; currentHealth = health; isDead = health == 0;
        HealthChanged?.Invoke(this, currentHealth, maxHealth);
    }

    public void SetInvulnerable(bool shouldBeInvulnerable)
    {
        isInvulnerable = shouldBeInvulnerable;
    }

    private void TryRegenerateHealth()
    {
        if (!allowHealing ||
            !autoRegenerate ||
            isDead ||
            HealingBlocked ||
            currentHealth >= maxHealth ||
            Time.time < lastDamageTime + regenerationDelay)
        {
            return;
        }

        regenerationRemainder += healthPerSecond * Time.deltaTime;

        int healthToRestore =
            Mathf.FloorToInt(regenerationRemainder);

        if (healthToRestore <= 0)
        {
            return;
        }

        regenerationRemainder -= healthToRestore;

        Heal(healthToRestore);
    }
}
