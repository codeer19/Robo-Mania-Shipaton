using UnityEngine;

[RequireComponent(typeof(Damageable))]
[RequireComponent(typeof(FortressTarget))]
public class FortressVault : MonoBehaviour
{
    private Damageable damageable;
    private FortressTarget fortressTarget;
    private FortressDuelManager duelManager;

    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        fortressTarget = GetComponent<FortressTarget>();
        duelManager = FindFirstObjectByType<FortressDuelManager>();
    }

    private void OnEnable()
    {
        if (damageable != null)
        {
            damageable.Died += HandleVaultDestroyed;
        }
    }

    private void OnDisable()
    {
        if (damageable != null)
        {
            damageable.Died -= HandleVaultDestroyed;
        }
    }

    private void HandleVaultDestroyed(Damageable destroyedVault)
    {
        if (duelManager == null || fortressTarget == null)
        {
            return;
        }

        FortressTeam winningTeam =
            fortressTarget.Team == FortressTeam.Blue
            ? FortressTeam.Red
            : FortressTeam.Blue;

        duelManager.EndMatch(winningTeam);
    }
}