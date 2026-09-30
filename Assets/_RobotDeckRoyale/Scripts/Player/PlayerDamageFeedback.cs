using UnityEngine;

[RequireComponent(typeof(Damageable))]
public class PlayerDamageFeedback : MonoBehaviour
{
    private Damageable damageable;
    private FortressTarget targetIdentity;

    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        targetIdentity = GetComponent<FortressTarget>();
    }

    private void OnEnable()
    {
        damageable.DamageApplied += OnDamageApplied;
    }

    private void OnDisable()
    {
        damageable.DamageApplied -= OnDamageApplied;
    }

    private void OnDamageApplied(Damageable target, DamageInfo info)
    {
        // Scaled against this robot's own health rather than a flat 28 damage,
        // so the same shot reads as a graze on a vault and as a heavy hit on a
        // crawler instead of every impact landing at roughly the same strength.
        float healthFraction = target != null && target.MaxHealth > 0
            ? (float)info.Amount / target.MaxHealth
            : Mathf.Clamp01(info.Amount / 28f);

        float normalizedImpact = Mathf.Clamp01(healthFraction / 0.25f);
        bool localRobotWasHit = targetIdentity == null || targetIdentity.Team == FortressTeam.Blue;

        if (localRobotWasHit)
        {
            // Keep the arena completely clear: feedback is a compact amber
            // reticle pulse plus the robot-local hit flash and camera impulse.
            CombatFeedbackOverlay localDamageOverlay = CombatFeedbackOverlay.GetOrCreate();
            localDamageOverlay.PulseDamage(
                Mathf.Lerp(0.45f, 0.9f, normalizedImpact),
                ResolveIncomingDirection(info));

            if (CameraShake.Instance != null)
                CameraShake.Instance.Shake(Mathf.Lerp(0.08f, 0.18f, normalizedImpact));

            return;
        }

        CombatFeedbackOverlay overlay = CombatFeedbackOverlay.GetOrCreate();
        overlay.PulseHitConfirm(Mathf.Lerp(0.55f, 1f, normalizedImpact));

        // The old pause was 12-26ms, which is under one frame at 60fps, so the
        // hit stop that should make a shot land was never actually visible.
        // Two to five frames is the range that reads as impact without turning
        // sustained fire into a stutter.
        overlay.RequestImpactPause(
            Mathf.Lerp(0.035f, 0.085f, normalizedImpact), 0.12f);

        if (CameraShake.Instance != null)
            CameraShake.Instance.Shake(Mathf.Lerp(0.1f, 0.23f, normalizedImpact));
    }

    /// <summary>
    /// World-space direction the damage arrived from, so the HUD can point at
    /// the threat instead of pulsing symmetrically and telling the player nothing.
    /// </summary>
    private Vector3 ResolveIncomingDirection(in DamageInfo info)
    {
        Vector3 from = Vector3.zero;

        if (info.Source != null)
            from = info.Source.transform.position - transform.position;
        else if (info.HasHitPoint)
            from = -info.Direction;

        from.y = 0f;

        return from.sqrMagnitude > 0.0001f ? from.normalized : Vector3.zero;
    }
}
