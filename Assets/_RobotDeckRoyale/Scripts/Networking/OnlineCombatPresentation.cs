using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Renders replicated combat records using existing art. No collision, pickup or damage simulation.</summary>
public sealed class OnlineCombatPresentation : MonoBehaviour
{
    private NetworkedMatchState state;
    private FortressDuelManager arena;
    private RobotBlaster blaster;
    private SwarmChargeController economy;
    private readonly GameObject[] missiles = new GameObject[NetworkedMatchState.MissileCapacity];
    private readonly int[] missileIds = new int[NetworkedMatchState.MissileCapacity];
    private readonly bool[] impacted = new bool[NetworkedMatchState.MissileCapacity];
    private readonly float[] firstSeen = new float[NetworkedMatchState.MissileCapacity];
    private readonly bool[] missedFlight = new bool[NetworkedMatchState.MissileCapacity];
    private readonly GameObject[] spidys = new GameObject[NetworkedMatchState.SpidyCapacity];
    private int spidyMaximumHealth = 22;
    private readonly int[] swarmIds = new int[NetworkedMatchState.SpidyCapacity];
    private GameObject cell, spidyPrefab, cellPrefab, missilePrefab;
    private Renderer[] localRenderers;
    private bool[] localRendererEnabled;
    private int localRespawns, previousCellId;
    private bool previousAlive = true, resultShown;
    private float nextStateLog;
    private static readonly Unity.Profiling.ProfilerMarker PresentationMarker = new("Online Combat Presentation");
    private Damageable localHealth, remoteHealth;
    private readonly Damageable[] spidyHealth = new Damageable[NetworkedMatchState.SpidyCapacity];
    private readonly OnlineStructureView[] structures = new OnlineStructureView[NetworkedMatchState.StructureCapacity];
    private readonly int[] structureIds = new int[NetworkedMatchState.StructureCapacity];
    private BuildPlacementController builder;
    private RespawnCountdownUI respawnHud;
    private int lastRespawnSecond = -1;

    public void Initialize(NetworkedMatchState match, FortressDuelManager manager, RobotBlaster weapon, SwarmChargeController charge)
    {
        state = match; arena = manager; blaster = weapon; economy = charge;
        builder = arena.GetComponent<BuildPlacementController>();
        localHealth = arena.LocalPlayer.GetComponent<Damageable>();
        respawnHud = RespawnCountdownUI.Create(transform);
        spidyPrefab = Resources.Load<GameObject>("Networking/PF_Spidy_Visual");
        if (spidyPrefab != null)
        {
            var template = spidyPrefab.GetComponent<Damageable>();
            if (template != null) spidyMaximumHealth = template.MaxHealth;
        }
        cellPrefab = Resources.Load<GameObject>("Networking/PF_Cell_Visual");
        missilePrefab = Resources.Load<GameObject>("Networking/PF_Missile_Visual");
        if (spidyPrefab == null || cellPrefab == null || missilePrefab == null)
            Debug.LogError("[COMBAT VIEW] Registered presentation assets missing.");
        localRenderers = arena.LocalPlayer.GetComponentsInChildren<Renderer>(true);
        localRendererEnabled = new bool[localRenderers.Length];
        for (int i=0;i<localRenderers.Length;i++) localRendererEnabled[i]=localRenderers[i].enabled;
        OnlineTeamPresentation.BindPlayer(arena.LocalPlayer.gameObject, MatchSessionContext.LocalSide);
        OnlineTeamPresentation.TintHeist(arena.Heist(TeamSide.SideA), TeamSide.SideA);
        OnlineTeamPresentation.TintHeist(arena.Heist(TeamSide.SideB), TeamSide.SideB);
    }
    private void LateUpdate()
    {
        using var startupTiming = new NetworkStartupDiagnostics.Step("CombatPresentation.LateUpdate");
        using var profile = PresentationMarker.Auto();
        if (state == null || state.Object == null || !state.Object.IsValid) return;
        var localSide = MatchSessionContext.LocalSide;
        var local = state.Players[(int)localSide];
        localHealth.ApplyReplicatedHealth(local.Health, state.PlayerMaximumHealth);
        for (int i = 0; i < 2; i++)
        {
            var side = (TeamSide)i;
            var heist = arena.Heist(side);
            heist.ApplyReplicatedHealth(state.HeistHealth(side), heist.MaxHealth);
        }
        var remote = OnlineMatchDirector.Instance?.RemoteAvatar;
        if (remote != null)
        {
            if (remoteHealth == null) remoteHealth = remote.RemoteVisualRoot.GetComponentInChildren<Damageable>(true);
            if (remoteHealth != null) remoteHealth.ApplyReplicatedHealth(state.PlayerHealth(remote.Team), state.PlayerMaximumHealth);
        }
        if (state.Phase != OnlineCombatPhase.Waiting)
        {
            bool alive = local.Health > 0;
            if (alive != previousAlive)
            {
                for (int i=0;i<localRenderers.Length;i++) if(localRenderers[i]!=null) localRenderers[i].enabled=alive && localRendererEnabled[i];
                previousAlive = alive;
            }
            arena.SetOnlinePlayerAlive(alive && !local.Building && state.Phase == OnlineCombatPhase.Combat);
            if (local.Respawns != localRespawns)
            {
                localRespawns = local.Respawns;
                arena.RespawnOnlinePlayer();
            }
        }
        if (state.Phase == OnlineCombatPhase.Combat) arena.ShowOnlineTime(state.MatchEnd.RemainingTime(state.Runner) ?? 0);
        // Pulse Tower slow, Overdrive boost and Recovery Jammer NO HEAL: the
        // authority owns the timers; this client applies slow and boost to its own
        // robot and shows every effect on either robot.
        MovementSlow.SetReplicated(arena.LocalPlayer.gameObject, local.Health > 0 && state.IsSlowed(local.Slow), state.SlowMultiplier);
        MovementBoost.SetReplicated(arena.LocalPlayer.gameObject, local.Health > 0 && state.IsBoosted(local.Boost), state.BoostMultiplier);
        NoHealIndicator.SetReplicated(arena.LocalPlayer.transform, local.Health > 0 && state.IsHealBlocked(local.NoHeal));
        if (remote != null)
        {
            var remoteState = state.Players[(int)remote.Team];
            MovementSlow.SetReplicated(remote.RemoteVisualRoot, remoteState.Health > 0 && state.IsSlowed(remoteState.Slow), state.SlowMultiplier);
            MovementBoost.SetReplicated(remote.RemoteVisualRoot, remoteState.Health > 0 && state.IsBoosted(remoteState.Boost), state.BoostMultiplier);
            NoHealIndicator.SetReplicated(remote.RemoteVisualRoot.transform, remoteState.Health > 0 && state.IsHealBlocked(remoteState.NoHeal));
        }
        bool respawning = local.Health <= 0 && state.Phase == OnlineCombatPhase.Combat;
        respawnHud.Show(respawning, Mathf.Max(1, Mathf.CeilToInt(local.Respawn.RemainingTime(state.Runner) ?? 0)));
        int respawnSecond = Mathf.Max(1, Mathf.CeilToInt(local.Respawn.RemainingTime(state.Runner) ?? 0));
        if (respawning && lastRespawnSecond != respawnSecond)
        { lastRespawnSecond = respawnSecond;  }
        if (!respawning) lastRespawnSecond = -1;
        RenderMissiles(); RenderSpidys(); RenderStructures();
        if (state.CellAvailable)
        {
            if (cell == null && cellPrefab != null) cell = Instantiate(cellPrefab, transform);
            if (cell != null)
            {
                cell.SetActive(true); cell.transform.position = state.CellPosition + Vector3.up * (Mathf.Sin(Time.time * 4f) * 0.1f);
                cell.transform.Rotate(Vector3.up, 80f * Time.deltaTime);
            }
        }
        else if (cell != null) cell.SetActive(false);
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) state.RequestSummon();
        if (state.Phase == OnlineCombatPhase.Finished && !resultShown)
        {
            resultShown = true; arena.EndOnlineMatch(state.WinningSide, state.IsDraw, state.MatchEnd.Expired(state.Runner));
        }
        if (NetworkTestHarness.VerboseDiagnostics && Time.unscaledTime >= nextStateLog)
        {
            nextStateLog = Time.unscaledTime + 2f;
            Debug.Log($"[MATCH STATE] phase={state.Phase} A={state.PlayerA}/hp:{state.PlayerHealth(TeamSide.SideA)}/charge:{state.Charge(TeamSide.SideA)} B={state.PlayerB}/hp:{state.PlayerHealth(TeamSide.SideB)}/charge:{state.Charge(TeamSide.SideB)} heists={state.HeistHealthA},{state.HeistHealthB} cell={state.CellId}/{state.CellAvailable} shots={state.MissileSequence} swarms={state.SwarmSequence} winner={state.WinningSide}");
        }
    }
    private void RenderMissiles()
    {
        for (int i = 0; i < missiles.Length; i++)
        {
            var shot = state.Missiles[i];
            if (shot.Id == 0) continue;
            if (missileIds[i] != shot.Id)
            {
                missileIds[i] = shot.Id; impacted[i] = false;
                firstSeen[i] = Time.unscaledTime; missedFlight[i] = !shot.Active;
                if (missiles[i] == null && missilePrefab != null) missiles[i] = Instantiate(missilePrefab, transform);
                if (missiles[i] != null)
                {
                    missiles[i].transform.SetPositionAndRotation(shot.Origin, Quaternion.LookRotation(shot.Direction));
                    foreach (var trail in missiles[i].GetComponentsInChildren<TrailRenderer>(true)) trail.Clear();
                }
                ProjectileLauncher.SpawnEffect(blaster.MuzzleEffectPrefab, shot.Origin, Quaternion.LookRotation(shot.Direction), 1.5f);
                // Runs once per missile id per client, for local and remote shots
                // alike, so each listener hears exactly one launch per missile.
                ReleaseAudio.PlayAt(ReleaseAudioCue.MissileFire, shot.Origin, UnityEngine.Random.Range(0.97f, 1.03f));
                if (CombatLog.Verbose) Debug.Log($"[MISSILE VIEW] id={shot.Id} owner={shot.OwnerPlayerRef} side={shot.OwnerSide} local={state.Runner.LocalPlayer}");
            }
            if (missiles[i] != null)
            {
                bool briefFlight = missedFlight[i] && Time.unscaledTime - firstSeen[i] < 0.1f;
                missiles[i].SetActive((shot.Active || briefFlight) && state.Phase == OnlineCombatPhase.Combat);
                Vector3 position = briefFlight ? Vector3.Lerp(shot.Origin, shot.Position, (Time.unscaledTime - firstSeen[i]) / 0.1f) : shot.Position;
                missiles[i].transform.SetPositionAndRotation(position, Quaternion.LookRotation(shot.Direction));
            }
            if (!shot.Active && !impacted[i] && (!missedFlight[i] || Time.unscaledTime - firstSeen[i] >= 0.1f))
            {
                impacted[i] = true;
                blaster.ProjectilePrefab.ShowOnlineImpact(shot.Position, -shot.Direction);
            }
        }
    }
    private void RenderSpidys()
    {
        for (int i = 0; i < spidys.Length; i++)
        {
            var bot = state.Spidys[i];
            if (bot.SwarmId == 0) continue;
            if (spidys[i] == null && spidyPrefab != null)
            {
                spidys[i] = Instantiate(spidyPrefab, bot.Position, bot.Rotation, transform);
                SpidyHealthBar.Attach(spidys[i]);
                OnlineTeamPresentation.BindPlayer(spidys[i], bot.OwnerSide);
                spidyHealth[i] = spidys[i].GetComponent<Damageable>();
                var animator = spidys[i].GetComponentInChildren<Animator>();
                if (animator != null) animator.applyRootMotion = false;
            }
            if (swarmIds[i] != bot.SwarmId)
            {
                swarmIds[i] = bot.SwarmId;
                Debug.Log($"[SPIDY VIEW] swarm={bot.SwarmId} unit={i} owner={bot.OwnerPlayerRef} side={bot.OwnerSide} target={bot.TargetSide} friendly={TeamSides.IsFriendly(bot.OwnerSide)}");
            }
            if (spidys[i] == null) continue;

            // Feed the replicated health into the visual's Damageable so its bar
            // reads the authoritative value. Damage was already being applied by
            // the simulation, but nothing on screen showed it: the visual carried
            // no health bar at all, so a Spidy soaked hits silently and then
            // simply vanished, which read as "missiles do not hit Spidy".
            var botHealth = spidyHealth[i];
            if (botHealth != null) botHealth.ApplyReplicatedHealth(bot.Health, spidyMaximumHealth);

            spidys[i].SetActive(bot.Health > 0);
            MovementSlow.SetReplicated(spidys[i], bot.Health > 0 && state.IsSlowed(bot.Slow), state.SlowMultiplier);
            spidys[i].transform.SetPositionAndRotation(Vector3.Lerp(spidys[i].transform.position, bot.Position,
                1f - Mathf.Exp(-18f * Time.deltaTime)), bot.Rotation);
        }
    }
    private void RenderStructures()
    {
        for (int i = 0; i < structures.Length; i++)
        {
            var data = state.Structures[i];
            if (data.Id != structureIds[i])
            {
                if (structures[i] != null) Destroy(structures[i].gameObject);
                structureIds[i] = data.Id;
                if (data.Id == 0) continue;
                structures[i] = OnlineStructureView.Create(builder.Prefab(data.Type), transform, data.Position, data.Rotation, i);
                OnlineTeamPresentation.BindPlayer(structures[i].gameObject, data.OwnerSide);
            }
            if (structures[i] != null) structures[i].Apply(data);
        }
    }
}
