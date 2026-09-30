using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

public enum OnlineCombatPhase { Waiting, Build, Combat, Finished }
public struct OnlinePlayerState : INetworkStruct
{
    public int Health, Charge, Respawns;
    public NetworkBool Building;
    public TickTimer Respawn;
    // Pulse Tower slow and Recovery Jammer NO HEAL. Decided here; each client
    // applies the slow to its own robot and shows both.
    public TickTimer Slow;
    public TickTimer NoHeal;
    // Overdrive Pad speed boost, same pattern: decided here, applied by the owner.
    public TickTimer Boost;
}
public struct OnlineMissile : INetworkStruct
{
    public int Id;
    public PlayerRef OwnerPlayerRef;
    public TeamSide OwnerSide;
    public Vector3 Origin, Position, Direction;
    public NetworkBool Active;
    public int Damage;
    public float Speed, Range, Radius, Splash;
}
[System.Serializable]
public struct OnlineSpidy : INetworkStruct
{
    public int SwarmId, Health, Attacks;
    public PlayerRef OwnerPlayerRef;
    public TeamSide OwnerSide, TargetSide;
    public Vector3 Position;
    public Quaternion Rotation;
    public TickTimer NextAttack;
    public TickTimer Slow;
}

/// <summary>
/// Shared-mode creator owns ALL combat outcomes. Player-owned avatars publish pose only.
/// RPCs carry input and a sequence, never damage, health, ownership or a winning claim.
/// Bounded replicated records retain shot IDs even after impact, so snapshot loss cannot
/// turn two shots into one replay of the last shot. All views consume this same state.
/// </summary>
public sealed partial class NetworkedMatchState : NetworkBehaviour
{
    public const int MissileCapacity = 64, SpidyCapacity = 8;
    public static NetworkedMatchState Instance { get; private set; }
    [Networked] public PlayerRef PlayerA { get; set; }
    [Networked] public PlayerRef PlayerB { get; set; }
    [Networked] public OnlineCombatPhase Phase { get; set; }
    [Networked] public TeamSide WinningSide { get; set; }
    [Networked] public int PlayerMaximumHealth { get; set; }
    [Networked] public int HeistHealthA { get; set; }
    [Networked] public int HeistHealthB { get; set; }
    [Networked] public int MissileSequence { get; set; }
    [Networked] public int SwarmSequence { get; set; }
    [Networked] public int CellId { get; set; }
    [Networked] public NetworkBool CellAvailable { get; set; }
    [Networked] public Vector3 CellPosition { get; set; }
    [Networked] public TeamSide LastCellCollector { get; set; }
    [Networked] public TickTimer NextCell { get; set; }
    [Networked, Capacity(2)] public NetworkArray<OnlinePlayerState> Players => default;
    [Networked, Capacity(MissileCapacity)] public NetworkArray<OnlineMissile> Missiles => default;
    [Networked, Capacity(SpidyCapacity)] public NetworkArray<OnlineSpidy> Spidys => default;

    private FortressDuelManager arena;
    private RobotBlaster blaster;
    private SwarmChargeController economy;
    private OnlineCombatPresentation presentation;
    private readonly Queue<FireInput> fireInputs = new();
    private readonly Queue<PlayerRef> summonInputs = new();
    private readonly int[] acceptedFire = new int[2];
    private readonly WeaponCore[] weapons = { new WeaponCore(), new WeaponCore() };
    private int localFireSequence;
    private bool configured;
    private static readonly Unity.Profiling.ProfilerMarker SimulationMarker = new("Online Combat Simulation");
    private readonly bool[] developmentCharge = new bool[2];
    private struct FireInput { public PlayerRef Player; public int Sequence; public Vector3 Origin, Direction; }

    public int PlayerHealth(TeamSide side) => Players[(int)side].Health;
    public int Charge(TeamSide side) => Players[(int)side].Charge;
    public int HeistHealth(TeamSide side) => side == TeamSide.SideA ? HeistHealthA : HeistHealthB;
    public PlayerRef Owner(TeamSide side) => side == TeamSide.SideA ? PlayerA : PlayerB;
    public bool TrySide(PlayerRef player, out TeamSide side)
    {
        side = player == PlayerA ? TeamSide.SideA : TeamSide.SideB;
        return player.IsRealPlayer && (player == PlayerA || player == PlayerB);
    }
    public int ActiveSpidys(TeamSide side)
    {
        int count = 0;
        for (int i = 0; i < SpidyCapacity; i++) if (Spidys[i].Health > 0 && Spidys[i].OwnerSide == side) count++;
        return count;
    }
    public bool CanSummon(TeamSide side) => Phase == OnlineCombatPhase.Combat &&
        PlayerHealth(side) > 0 && Charge(side) >= economy.MaximumCharge && ActiveSpidys(side) == 0;

    public override void Spawned()
    {
        using var startupTiming = new NetworkStartupDiagnostics.Step("MatchState.Spawned");
        Instance = this;
        arena = FortressDuelManager.ActiveArena;
        // Release WebGL has no null checks on plain C# dereferences, so a missing
        // arena here is a hard crash rather than a logged exception.
        if (arena == null || arena.LocalPlayer == null)
        { Debug.LogError("[COMBAT] No arena bound when the match state spawned; refusing startup."); return; }
        blaster = arena.LocalPlayer.GetComponent<RobotBlaster>();
        economy = arena.OnlineEconomy;
        builder = arena.GetComponent<BuildPlacementController>();
        configured = blaster != null && blaster.ProjectilePrefab != null && economy != null &&
            economy.OnlineCellPrefab != null && economy.OnlineSpidyPrefab != null &&
            arena.Heist(TeamSide.SideA) != null && arena.Heist(TeamSide.SideB) != null;
        if (!configured) { Debug.LogError("[COMBAT] Missing authored combat references; refusing startup."); return; }
        foreach (var weapon in weapons) weapon.Configure(blaster.MaxAmmo, blaster.ReloadSecondsPerAmmo, blaster.FireInterval);
        if (HasStateAuthority)
        {
            PlayerMaximumHealth = arena.LocalPlayer.GetComponent<Damageable>().MaxHealth;
            HeistHealthA = arena.Heist(TeamSide.SideA).MaxHealth;
            HeistHealthB = arena.Heist(TeamSide.SideB).MaxHealth;
            for (int i = 0; i < 2; i++) Players.Set(i, new OnlinePlayerState { Health = PlayerMaximumHealth });
        }
        presentation = gameObject.AddComponent<OnlineCombatPresentation>();
        presentation.Initialize(this, arena, blaster, economy);
        Debug.Log($"[COMBAT] State authority={Object.StateAuthority} id={Object.Id} local={Runner.LocalPlayer}");
    }
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }
    public bool RequestFire(Vector3 origin, Vector3 direction)
    {
        if (!configured || Phase != OnlineCombatPhase.Combat || !TrySide(Runner.LocalPlayer, out var side) || PlayerHealth(side) <= 0) return false;
        RPC_Fire(++localFireSequence, origin, direction);
        return true;
    }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Fire(int sequence, Vector3 origin, Vector3 direction, RpcInfo info = default)
    {
        if (fireInputs.Count >= 32 || !TrySide(info.Source, out _) || !Finite(origin) || !Finite(direction)) return;
        fireInputs.Enqueue(new FireInput { Player = info.Source, Sequence = sequence, Origin = origin, Direction = direction });
    }
    public void RequestSummon()
    {
        if (TrySide(Runner.LocalPlayer, out var side) && CanSummon(side)) RPC_Summon();
    }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_DevelopmentCharge(RpcInfo info = default)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (NetworkTestHarness.IsActive && TrySide(info.Source, out var side)) developmentCharge[(int)side] = true;
#endif
    }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Summon(RpcInfo info = default)
    {
        if (summonInputs.Count < 4 && TrySide(info.Source, out _)) summonInputs.Enqueue(info.Source);
    }
    public override void FixedUpdateNetwork()
    {
        using var profile = SimulationMarker.Auto();
        if (!HasStateAuthority || !configured) return;
        if (Phase == OnlineCombatPhase.Waiting)
        {
            var director = OnlineMatchDirector.Instance;
            if (director == null || !director.BothPlayersReady) return;
            var a = director.SideAAvatar;
            var local = director.LocalAvatar;
            var b = local != null && local.Team == TeamSide.SideB ? local : director.RemoteAvatar;
            if (a == null || b == null) return;
            PlayerA = a.OwnerPlayerRef; PlayerB = b.OwnerPlayerRef;
            if (Object.StateAuthority != PlayerA) { Debug.LogError("[COMBAT] Authority must be creator/SideA."); return; }
            if (!a.IntroStartedConfirmed || !a.IntroEnd.Expired(Runner)) return;
            Phase = OnlineCombatPhase.Build;
            BuildEnd = TickTimer.CreateFromSeconds(Runner, arena.OnlineBuildDuration);
            SetBuildingBothSides(true);
            Debug.Log($"[BUILD PHASE] Started PlayerA={PlayerA} PlayerB={PlayerB} seconds={arena.OnlineBuildDuration:F0}");
        }
        if (Phase == OnlineCombatPhase.Build)
        {
            // Placement is the only input this phase accepts. Fire and summon
            // requests are dropped rather than queued, so nothing discharges the
            // instant combat opens.
            fireInputs.Clear();
            summonInputs.Clear();
            if (!BuildEnd.ExpiredOrNotRunning(Runner))
            {
                TickBuildInputs();
                return;
            }
            buildInputs.Clear();
            SetBuildingBothSides(false);
            Phase = OnlineCombatPhase.Combat;
            PrimeStructuresForCombat();
            MatchEnd = TickTimer.CreateFromSeconds(Runner, NetworkTestHarness.IsActive ? 900 : arena.OnlineCombatDuration);
            NextCell = TickTimer.CreateFromSeconds(Runner, 0.8f);
            Debug.Log($"[COMBAT] Started PlayerA={PlayerA} PlayerB={PlayerB}");
        }
        if (Phase != OnlineCombatPhase.Combat) { fireInputs.Clear(); summonInputs.Clear(); return; }
        float time = (float)Runner.SimulationTime;
        if (MatchEnd.Expired(Runner))
        {
            float a = HeistHealthA / (float)arena.Heist(TeamSide.SideA).MaxHealth;
            float b = HeistHealthB / (float)arena.Heist(TeamSide.SideB).MaxHealth;
            IsDraw = Mathf.Abs(a - b) <= arena.OnlineDrawTolerance;
            WinningSide = a >= b ? TeamSide.SideA : TeamSide.SideB;
            Phase = OnlineCombatPhase.Finished;
            return;
        }
        TickBuildInputs();
        for (int i = 0; i < 2; i++) if (developmentCharge[i])
        {
            developmentCharge[i] = false;
            var player = Players[i]; player.Charge = economy.MaximumCharge; Players.Set(i, player);
            Debug.Log($"[ACCEPTANCE] Forced charge side={(TeamSide)i} value={player.Charge}");
        }
        foreach (var weapon in weapons) weapon.Tick(time);
        while (fireInputs.Count > 0) AcceptFire(fireInputs.Dequeue(), time);
        while (summonInputs.Count > 0)
        {
            var owner = summonInputs.Dequeue();
            if (TrySide(owner, out var side) && CanSummon(side)) Summon(side, owner);
        }
        TickMissiles();
        TickStructures();
        TickSpidys();
        TickCells();
        for (int i = 0; i < 2; i++)
        {
            var player = Players[i];
            if (player.Health == 0 && player.Respawn.Expired(Runner))
            {
                player.Health = PlayerMaximumHealth; player.Respawns++; player.Respawn = default;
                player.Slow = default; player.NoHeal = default; player.Boost = default;
                Players.Set(i, player);
                Debug.Log($"[RESPAWN] Side={(TeamSide)i} count={player.Respawns}");
            }
        }
    }
    private void AcceptFire(FireInput input, float time)
    {
        if (!TrySide(input.Player, out var side) || PlayerHealth(side) <= 0 || IsBuilding(side) ||
            !NetworkedPlayerAvatar.TryGet(Runner, input.Player, out var avatar)) return;
        int index = (int)side;
        if (input.Sequence <= acceptedFire[index] || !weapons[index].IsReady(time) ||
            Vector3.Distance(input.Origin, avatar.Position) > 5f || input.Direction.sqrMagnitude < 0.5f) return;
        // The request contains the actual local muzzle. Restrict it to the robot's body envelope;
        // the shooter cannot submit an impact position or the resulting damage.
        acceptedFire[index] = input.Sequence;
        weapons[index].CommitShot(time);
        var prefab = blaster.ProjectilePrefab;
        int id = ++MissileSequence;
        Missiles.Set((id - 1) % MissileCapacity, new OnlineMissile {
            Id = id, OwnerPlayerRef = input.Player, OwnerSide = side, Origin = input.Origin,
            Position = input.Origin, Direction = input.Direction.normalized, Active = true,
            Damage = prefab.ConfiguredDamage, Speed = prefab.Speed, Range = prefab.MaxTravelDistance,
            Radius = prefab.HitRadius, Splash = prefab.ExplosionRadius });
        if (CombatLog.Verbose) Debug.Log($"[MISSILE] id={id} owner={input.Player} side={side} origin={input.Origin:F3} direction={input.Direction:F3}");
    }
    private void DamagePlayer(TeamSide side, int amount, int shot)
    {
        var player = Players[(int)side];
        if (Phase != OnlineCombatPhase.Combat || player.Health <= 0 || amount <= 0) return;
        player.Health = Mathf.Max(0, player.Health - amount);
        if (player.Health == 0) { player.Respawn = TickTimer.CreateFromSeconds(Runner, 3f); player.Building = false; }
        Players.Set((int)side, player);
        if (CombatLog.Verbose) Debug.Log($"[DAMAGE] shot={shot} targetPlayer={Owner(side)} side={side} amount={amount} hp={player.Health}");
    }
    private void DamageHeist(TeamSide side, int amount, string source)
    {
        if (Phase != OnlineCombatPhase.Combat || amount <= 0) return;
        int health = Mathf.Max(0, HeistHealth(side) - amount);
        if (side == TeamSide.SideA) HeistHealthA = health; else HeistHealthB = health;
        if (CombatLog.Verbose) Debug.Log($"[HEIST] side={side} source={source} damage={amount} hp={health}");
        if (health == 0)
        {
            WinningSide = TeamSides.Opponent(side);
            Phase = OnlineCombatPhase.Finished;
            Debug.Log($"[RESULT] WinningSide={WinningSide}");
        }
    }
    private void TickCells()
    {
        if (Phase != OnlineCombatPhase.Combat) return;
        if (!CellAvailable && NextCell.Expired(Runner) && economy.TryGetOnlineCellPosition(out var position))
        {
            CellPosition = position; CellId++; CellAvailable = true; NextCell = default;
            if (CombatLog.Verbose) Debug.Log($"[ECELL] Spawn id={CellId} position={position:F3}");
        }
        if (!CellAvailable) return;
        // One authority arbitrates contention, nearest eligible player wins, then SideA for an exact tie.
        TeamSide collector = TeamSide.SideA;
        float closest = 2.1f * 2.1f;
        bool found = false;
        for (int i = 0; i < 2; i++)
        {
            var side = (TeamSide)i;
            if (PlayerHealth(side) <= 0 || Charge(side) >= economy.MaximumCharge ||
                !NetworkedPlayerAvatar.TryGet(Runner, Owner(side), out var avatar)) continue;
            Vector3 delta = avatar.Position - CellPosition; delta.y = 0f;
            if (delta.sqrMagnitude >= closest) continue;
            closest = delta.sqrMagnitude; collector = side; found = true;
        }
        if (!found) return;
        var player = Players[(int)collector];
        player.Charge = Mathf.Min(economy.MaximumCharge, player.Charge + economy.OnlineChargePerCell);
        Players.Set((int)collector, player);
        CellAvailable = false; LastCellCollector = collector;
        NextCell = TickTimer.CreateFromSeconds(Runner, 3f);
        if (CombatLog.Verbose) Debug.Log($"[ECELL] Consumed id={CellId} collector={Owner(collector)} side={collector} charge={player.Charge}");
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}
