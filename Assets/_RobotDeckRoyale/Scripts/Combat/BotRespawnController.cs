using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Damageable))]
public class BotRespawnController : MonoBehaviour
{
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private GameObject visual;
    [SerializeField, Min(5)] private int respawnCountdownSeconds = 5;
    [SerializeField] private float spawnProtectionDuration = 2f;
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private FortressBotAI botAI;
    [SerializeField] private FortressTarget targetIdentity;
    [SerializeField] private FortressDuelManager duelManager;

    private Damageable damageable;
    private bool isRespawning;
    private WorldHealthBar[] healthBars;
    private RobotAnimationHooks animationHooks;
    private Coroutine hideVisualCoroutine;

    private void Awake()
    {
        if (!BotMatchPolicy.AllowActivation(this, "BotRespawnController.Awake")) return;
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
        if (!BotMatchPolicy.AllowActivation(this, "BotRespawnController.OnEnable")) return;
        if (damageable != null)
        {
            damageable.Died += HandleDeath;
        }
    }

    private void OnDisable()
    {
        if (damageable != null)
        {
            damageable.Died -= HandleDeath;
        }
    }

    private void HandleDeath(Damageable deadBot)
    {
        if (!isRespawning)
        {
            StartCoroutine(RespawnRoutine());
        }
    }

    private IEnumerator RespawnRoutine()
    {
        if (!BotMatchPolicy.RequireBotMatch("BotRespawnController.RespawnRoutine", this)) yield break;
        isRespawning = true;

        SetBotActiveState(false);
        BeginRespawnCountdown();

        int countdownStart = Mathf.Max(5, respawnCountdownSeconds);
        for (int remaining = countdownStart; remaining >= 0; remaining--)
        {
            SetRespawnCountdown(remaining);
            yield return new WaitForSecondsRealtime(remaining > 0 ? 1f : 0.35f);
        }

        if (!MatchSessionContext.CanInitializeAI || duelManager == null ||
            duelManager.CurrentPhase != FortressDuelPhase.Combat)
        {
            EndRespawnCountdown();
            isRespawning = false;
            yield break;
        }

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = false;
        }

        if (respawnPoint != null)
        {
            transform.position = respawnPoint.position;
            transform.rotation = respawnPoint.rotation;
        }

        damageable.ResetHealth();
        EndRespawnCountdown();

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = true;

            if (navMeshAgent.isOnNavMesh && respawnPoint != null)
            {
                navMeshAgent.Warp(respawnPoint.position);
            }
        }

        SetBotActiveState(true);

        StartCoroutine(SpawnProtectionRoutine());

        Debug.Log("Red bot respawned.");
        SpawnPadPresentation.NotifySpawn(TeamSide.SideB);
        isRespawning = false;
    }

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
    }

    private void EndRespawnCountdown()
    {
        foreach (WorldHealthBar bar in healthBars)
            if (bar != null)
                bar.EndRespawnCountdown();
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

    private void SetBotActiveState(bool isActive)
    {
        if (isActive && !BotMatchPolicy.RequireBotMatch("BotRespawnController.SetBotActiveState", this)) return;
        if (visual != null)
        {
            if (hideVisualCoroutine != null) { StopCoroutine(hideVisualCoroutine); hideVisualCoroutine = null; }
            if (!isActive && damageable.IsDead && animationHooks != null && animationHooks.DeathVisualSeconds > 0f)
                hideVisualCoroutine = StartCoroutine(HideAfterDeath());
            else visual.SetActive(isActive);
        }

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = isActive;
        }

        if (botAI != null)
        {
            botAI.enabled = isActive;
        }

        if (targetIdentity != null)
        {
            targetIdentity.enabled = isActive;
        }
    }

    private IEnumerator HideAfterDeath()
    {
        yield return new WaitForSeconds(animationHooks.DeathVisualSeconds);
        if (damageable.IsDead && visual != null) visual.SetActive(false);
        hideVisualCoroutine = null;
    }
}
