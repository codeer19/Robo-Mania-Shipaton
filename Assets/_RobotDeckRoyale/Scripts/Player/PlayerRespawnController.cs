using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Damageable))]
public class PlayerRespawnController : MonoBehaviour
{
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private GameObject visual;
    [SerializeField, Min(5)] private int respawnCountdownSeconds = 5;
    [SerializeField] private float spawnProtectionDuration = 2f;

    [Header("Player Components")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private RobotPlayerController movement;
    [SerializeField] private RobotAimController aim;
    [SerializeField] private RobotBlaster blaster;
    [SerializeField] private FortressTarget targetIdentity;
    [SerializeField] private FortressDuelManager duelManager;

    private Damageable damageable;
    private Coroutine respawnCoroutine;
    private WorldHealthBar[] healthBars;
    private RobotAnimationHooks animationHooks;
    private Coroutine hideVisualCoroutine;

    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        healthBars = GetComponentsInChildren<WorldHealthBar>(true);
        animationHooks = GetComponentInChildren<RobotAnimationHooks>(true);

        if (duelManager == null)
        {
            duelManager = FindFirstObjectByType<FortressDuelManager>();
        }
    }

    private void OnEnable()
    {
        if (damageable != null)
        {
            damageable.Died += HandleDeath;
        }

        if (!Application.isPlaying || damageable == null || damageable.IsDead)
            return;

        // The death state disables the visual and the controller. If a scene is
        // ever saved mid-death those stay off, and the robot loads invisible and
        // immovable. A living robot always owns its alive presentation.
        SetPlayerActiveState(true);

        // The match itself is a spawn. Without this the robot enters combat with
        // no protection at all, and anything already in position can delete it
        // before the player has control.
        StartCoroutine(SpawnProtectionRoutine());
    }

    private void OnDisable()
    {
        if (damageable != null)
        {
            damageable.Died -= HandleDeath;
        }
    }

    private void HandleDeath(Damageable deadPlayer)
    {
        if (respawnCoroutine != null)
        {
            return;
        }

        respawnCoroutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        SetPlayerActiveState(false);
        BeginRespawnCountdown();

        int countdownStart = Mathf.Max(5, respawnCountdownSeconds);
        for (int remaining = countdownStart; remaining >= 0; remaining--)
        {
            if (MatchHasEnded())
            {
                EndRespawnCountdown();
                respawnCoroutine = null;
                yield break;
            }

            SetRespawnCountdown(remaining);

            // Polled per frame instead of one WaitForSecondsRealtime, because the
            // match can commit its result in the middle of a countdown second. The
            // result screen is terminal, so the countdown has to disappear on the
            // frame the match ends rather than keep ticking on top of VICTORY.
            float until = Time.realtimeSinceStartup + (remaining > 0 ? 1f : 0.35f);
            while (Time.realtimeSinceStartup < until)
            {
                if (MatchHasEnded())
                {
                    EndRespawnCountdown();
                    respawnCoroutine = null;
                    yield break;
                }

                yield return null;
            }
        }

        if (duelManager == null ||
            duelManager.CurrentPhase != FortressDuelPhase.Combat)
        {
            EndRespawnCountdown();
            respawnCoroutine = null;
            yield break;
        }

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        if (respawnPoint != null)
        {
            transform.position = respawnPoint.position;
            transform.rotation = respawnPoint.rotation;
        }

        damageable.ResetHealth();
        EndRespawnCountdown();

        if (blaster != null)
        {
            blaster.RestoreFullAmmo();
        }

        SetPlayerActiveState(true);

        StartCoroutine(SpawnProtectionRoutine());

        Debug.Log("Player respawned.");
        SpawnPadPresentation.NotifySpawn(TeamSide.SideA);
        respawnCoroutine = null;
    }

    /// <summary>
    /// The match has committed a Victory, Defeat or Draw. Nothing about respawn
    /// may survive this: not the overlay, not the countdown, not the respawn
    /// itself.
    /// </summary>
    private bool MatchHasEnded() =>
        duelManager != null && duelManager.CurrentPhase == FortressDuelPhase.Finished;

    private void BeginRespawnCountdown()
    {
        foreach (WorldHealthBar bar in healthBars)
            if (bar != null)
                bar.BeginRespawnCountdown();
    }

    private void SetRespawnCountdown(int secondsRemaining)
    {
        foreach (WorldHealthBar bar in healthBars)
            if (bar != null)
                bar.SetRespawnCountdown(secondsRemaining);

        // The countdown belongs to the local player and is shown once, on screen,
        // rather than floating over the corpse wherever it happened to fall.
        CombatFeedbackOverlay.GetOrCreate().ShowRespawnCountdown(secondsRemaining);
    }

    private void EndRespawnCountdown()
    {
        foreach (WorldHealthBar bar in healthBars)
            if (bar != null)
                bar.EndRespawnCountdown();

        CombatFeedbackOverlay.GetOrCreate().HideRespawnCountdown();
    }

    private IEnumerator SpawnProtectionRoutine()
    {
        damageable.SetInvulnerable(true);

        yield return new WaitForSeconds(spawnProtectionDuration);

        if (damageable != null && !damageable.IsDead)
        {
            damageable.SetInvulnerable(false);
        }
    }

    private void SetPlayerActiveState(bool isActive)
    {
        if (visual != null)
        {
            if (hideVisualCoroutine != null) { StopCoroutine(hideVisualCoroutine); hideVisualCoroutine = null; }
            if (!isActive && damageable.IsDead && animationHooks != null && animationHooks.DeathVisualSeconds > 0f)
                hideVisualCoroutine = StartCoroutine(HideAfterDeath());
            else visual.SetActive(isActive);
        }

        if (characterController != null)
        {
            characterController.enabled = isActive;
        }

        if (targetIdentity != null)
        {
            targetIdentity.enabled = isActive;
        }

        if (movement != null)
        {
            movement.enabled = isActive;
        }

        if (aim != null)
        {
            aim.enabled = isActive;
        }

        if (blaster != null)
        {
            blaster.enabled = isActive;
        }
    }

    private IEnumerator HideAfterDeath()
    {
        yield return new WaitForSeconds(animationHooks.DeathVisualSeconds);
        if (damageable.IsDead && visual != null) visual.SetActive(false);
        hideVisualCoroutine = null;
    }
}
