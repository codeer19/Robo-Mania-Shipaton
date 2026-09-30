using Fusion;
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct OnlineStructure : INetworkStruct
{
    public int Id, Health, MaximumHealth, Shots;
    public PlayerRef OwnerPlayerRef;
    public TeamSide OwnerSide;
    public BuildPlacementController.BuildableType Type;
    public Vector3 Position, AimPosition;
    public Quaternion Rotation, HeadRotation;
    public TickTimer NextAction;
}

/// <summary>
/// Build cards on the match authority. Placement requests are input only: the
/// authority checks them against the placing player's OWN loadout (published on
/// that player's avatar), the build phase, the point budget, the per-card cap and
/// the shared placement rules, then simulates every card from its prefab tuning.
/// Shots counts each card's visible events (pulse, heal tick, boost, jammer burst,
/// interception) so clients present them from replicated state.
/// </summary>
public sealed partial class NetworkedMatchState
{
    public const int StructureCapacity = 14;
    [Networked, Capacity(StructureCapacity)] public NetworkArray<OnlineStructure> Structures => default;
    [Networked] public int StructureSequence { get; set; }
    [Networked] public TickTimer MatchEnd { get; set; }
    /// <summary>
    /// The one deadline the opening build phase runs on.
    ///
    /// Networked rather than two local countdowns, because two coroutines started
    /// a round-trip apart drift, and the peer whose timer expired first would open
    /// fire on a player still in build mode.
    /// </summary>
    [Networked] public TickTimer BuildEnd { get; set; }
    [Networked] public NetworkBool IsDraw { get; set; }
    private BuildPlacementController builder;
    private readonly Queue<BuildInput> buildInputs = new();
    private readonly int[] buildSequence = new int[2];
    private int localBuildSequence;
    private readonly BuildPlacementController.BuildableType[][] loadoutCache = new BuildPlacementController.BuildableType[2][];
    private readonly int[] loadoutPacked = { -1, -1 };
    private struct BuildInput { public PlayerRef Owner; public int Sequence; public BuildPlacementController.BuildableType Type; public Vector3 Position; public Quaternion Rotation; public int Action; }
    public bool IsBuilding(TeamSide side) => Phase == OnlineCombatPhase.Build && Players[(int)side].Building;

    /// <summary>
    /// A side's three cards, from that player's own avatar. Each player writes only
    /// their own avatar, so one player's loadout can never overwrite the other's.
    /// Missing or malformed data falls back to the default loadout.
    /// </summary>
    public BuildPlacementController.BuildableType[] LoadoutOf(TeamSide side)
    {
        int packed = NetworkedPlayerAvatar.TryGet(Runner, Owner(side), out var avatar) ? avatar.BuildLoadout : 0;
        int index = (int)side;
        if (loadoutCache[index] == null || loadoutPacked[index] != packed)
        {
            loadoutCache[index] = BuildCards.Unpack(packed);
            loadoutPacked[index] = packed;
        }
        return loadoutCache[index];
    }

    /// <summary>Placements left for one card: loadout, its cap and the shared build-point budget.</summary>
    public int Remaining(TeamSide side, BuildPlacementController.BuildableType type)
    {
        if (builder == null || !BuildCards.Contains(LoadoutOf(side), type)) return 0;
        return Mathf.Max(0, Mathf.Min(builder.Limit(type) - Placed(side, type),
            BuildPointsRemaining(side) / BuildPlacementController.BuildItemCost));
    }
    public int BuildPointsRemaining(TeamSide side) =>
        Mathf.Max(0, BuildPlacementController.BuildPointsPerRound - Placed(side, null) * BuildPlacementController.BuildItemCost);

    // Counted from the replicated structures themselves, so the budget can never
    // drift from what is actually standing in the arena.
    private int Placed(TeamSide side, BuildPlacementController.BuildableType? type)
    {
        int count = 0;
        for (int i = 0; i < StructureCapacity; i++)
        {
            var structure = Structures[i];
            if (structure.Id == 0 || structure.OwnerSide != side || structure.Health <= 0) continue;
            if (type == null || structure.Type == type.Value) count++;
        }
        return count;
    }

    public void SetBuilding(bool active) => RPC_BuildMode(active);
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_BuildMode(bool active, RpcInfo info = default)
    {
        if (Phase == OnlineCombatPhase.Build && buildInputs.Count < 16 && TrySide(info.Source, out _))
            buildInputs.Enqueue(new BuildInput { Owner = info.Source, Action = active ? 1 : 2 });
    }
    public bool RequestPlacement(BuildPlacementController.BuildableType type, Vector3 position, Quaternion rotation)
    {
        if (!TrySide(Runner.LocalPlayer, out var side) || !IsBuilding(side) || Remaining(side,type) <= 0) return false;
        RPC_Place(++localBuildSequence,type,position,rotation);
        return true;
    }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Place(int sequence, BuildPlacementController.BuildableType type, Vector3 position, Quaternion rotation, RpcInfo info = default)
    {
        if (Phase != OnlineCombatPhase.Build || buildInputs.Count >= 16 || !TrySide(info.Source,out _) || !Finite(position) ||
            !float.IsFinite(rotation.x) || !float.IsFinite(rotation.y) || !float.IsFinite(rotation.z) || !float.IsFinite(rotation.w) ||
            !BuildCards.IsDefined(type)) return;
        buildInputs.Enqueue(new BuildInput { Owner=info.Source,Sequence=sequence,Type=type,Position=position,Rotation=rotation });
    }
    public void RequestUndo() => RPC_Undo();
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Undo(RpcInfo info = default)
    {
        if (Phase == OnlineCombatPhase.Build && buildInputs.Count < 16 && TrySide(info.Source,out _)) buildInputs.Enqueue(new BuildInput{Owner=info.Source,Action=3});
    }
    private void TickBuildInputs()
    {
        while (buildInputs.Count > 0)
        {
            var input = buildInputs.Dequeue();
            if (Phase != OnlineCombatPhase.Build ||
                !TrySide(input.Owner,out var side)) continue;
            var player = Players[(int)side];
            if (player.Health <= 0) continue;
            if (input.Action == 1 || input.Action == 2) continue;
            if (!player.Building) continue;
            if (input.Action == 3) { UndoStructure(side); continue; }
            if (input.Sequence <= buildSequence[(int)side]) continue;
            buildSequence[(int)side] = input.Sequence;
            Vector3 position = builder.SnapOnline(input.Position);
            Quaternion rotation = builder.RotationOnline(input.Type,input.Rotation,side);
            // Only cards in the placing player's own loadout; everything else is
            // refused here even if a client selected it locally.
            if (!BuildCards.Contains(LoadoutOf(side), input.Type)) { LogRejected(side, input.Type, position, "not in loadout"); continue; }
            if (Remaining(side,input.Type) == 0) { LogRejected(side, input.Type, position, "no build points / card cap"); continue; }
            if (!builder.ValidateOnline(side,input.Type,position,rotation,this)) { LogRejected(side, input.Type, position, "illegal position"); continue; }
            int slot = -1;
            for (int i=0;i<StructureCapacity;i++) if (Structures[i].Id == 0 || Structures[i].Health <= 0) { slot=i; break; }
            if (slot < 0) continue;
            int health = builder.Prefab(input.Type).GetComponent<Damageable>().MaxHealth;
            Structures.Set(slot,new OnlineStructure { Id=++StructureSequence,OwnerPlayerRef=input.Owner,OwnerSide=side,
                Type=input.Type,Position=position,Rotation=rotation,HeadRotation=rotation,Health=health,MaximumHealth=health });
            Debug.Log($"[BUILD] placed id={StructureSequence} owner={input.Owner} side={side} type={input.Type} position={position:F2}");
        }
    }
    private static void LogRejected(TeamSide side, BuildPlacementController.BuildableType type, Vector3 position, string reason) =>
        Debug.Log($"[BUILD] rejected side={side} type={type} position={position:F2} reason={reason}");

    private void SetBuildingBothSides(bool building)
    {
        for (int i = 0; i < 2; i++)
        {
            var player = Players[i];
            player.Building = building;
            Players.Set(i, player);
        }
    }

    /// <summary>Combat opens: every card starts its rhythm from here, not from when it was placed.</summary>
    private void PrimeStructuresForCombat()
    {
        for (int i = 0; i < StructureCapacity; i++)
        {
            var structure = Structures[i];
            if (structure.Id == 0 || structure.Health <= 0) continue;
            float delay = structure.Type == BuildPlacementController.BuildableType.PulseTower ? builder.Pulse.Interval * 0.5f : 0.2f;
            structure.NextAction = TickTimer.CreateFromSeconds(Runner, delay);
            Structures.Set(i, structure);
        }
    }

    private void UndoStructure(TeamSide side)
    {
        int slot=-1, newest=0;
        for(int i=0;i<StructureCapacity;i++)
        {
            var structure=Structures[i];
            if(structure.Id>0 && structure.OwnerSide==side && structure.Id>newest && structure.Health==structure.MaximumHealth && structure.Health>0 && structure.Shots==0)
            {slot=i;newest=structure.Id;}
        }
        if(slot<0) return;
        Structures.Set(slot,default);
    }

    private void TickStructures()
    {
        for(int i=0;i<StructureCapacity && Phase==OnlineCombatPhase.Combat;i++)
        {
            var structure=Structures[i];
            if(structure.Id==0 || structure.Health<=0) continue;
            switch (structure.Type)
            {
                case BuildPlacementController.BuildableType.Turret: TickTurret(ref structure, i); break;
                case BuildPlacementController.BuildableType.PulseTower: TickPulseTower(ref structure); break;
                case BuildPlacementController.BuildableType.HealingPad: TickHealingPad(ref structure); break;
                case BuildPlacementController.BuildableType.OverdrivePad: TickOverdrive(ref structure); break;
                case BuildPlacementController.BuildableType.RecoveryJammer: TickJammer(ref structure); break;
                case BuildPlacementController.BuildableType.MissileInterceptor: TickInterceptor(ref structure); break;
            }
            Structures.Set(i,structure);
        }
    }

    /// <summary>TURRET: unchanged targeting and firing.</summary>
    private void TickTurret(ref OnlineStructure structure, int slot)
    {
        var enemy=TeamSides.Opponent(structure.OwnerSide);
        float range=builder.Turret.FiringRange;
        float nearest=range*range;
        int target=-1;
        Vector3 targetPosition=Vector3.zero;
        if(PlayerHealth(enemy)>0 && NetworkedPlayerAvatar.TryGet(Runner,Owner(enemy),out var avatar))
        {
            Vector3 delta=avatar.Position-structure.Position;delta.y=0;
            if(delta.sqrMagnitude<=nearest){nearest=delta.sqrMagnitude;target=0;targetPosition=avatar.Position+Vector3.up;}
        }
        for(int b=0;b<SpidyCapacity;b++)
        {
            var bot=Spidys[b]; if(bot.Health<=0 || bot.OwnerSide!=enemy) continue;
            Vector3 delta=bot.Position-structure.Position;delta.y=0;
            if(delta.sqrMagnitude<nearest){nearest=delta.sqrMagnitude;target=b+1;targetPosition=bot.Position+Vector3.up*.5f;}
        }
        if(target<0) return;
        Vector3 origin=structure.Position+Vector3.up*builder.Turret.MuzzleHeight;
        Vector3 direction=targetPosition-origin;direction.y=0;
        if(direction.sqrMagnitude<.001f) return;
        structure.HeadRotation=Quaternion.RotateTowards(structure.HeadRotation,Quaternion.LookRotation(direction),builder.Turret.TurnSpeed*Runner.DeltaTime);
        structure.AimPosition=targetPosition;
        if(structure.NextAction.ExpiredOrNotRunning(Runner) && Quaternion.Angle(structure.HeadRotation,Quaternion.LookRotation(direction))<12f &&
            HasTurretSight(origin,targetPosition,structure.OwnerSide,slot))
        {
            origin+=structure.HeadRotation*Vector3.forward*builder.Turret.MuzzleForward;
            var prefab=builder.Turret.OnlineProjectile;
            int id=++MissileSequence;
            Missiles.Set((id-1)%MissileCapacity,new OnlineMissile {Id=id,OwnerPlayerRef=structure.OwnerPlayerRef,OwnerSide=structure.OwnerSide,
                Origin=origin,Position=origin,Direction=direction.normalized,Active=true,Damage=builder.Turret.DamagePerShot,
                Speed=prefab.Speed,Range=builder.Turret.FiringRange,Radius=prefab.HitRadius,Splash=prefab.ExplosionRadius});
            structure.Shots++;structure.NextAction=TickTimer.CreateFromSeconds(Runner,builder.Turret.FireInterval);
        }
    }

    /// <summary>PULSE TOWER: on its interval, small damage and a short mild slow to every enemy in the ring.</summary>
    private void TickPulseTower(ref OnlineStructure structure)
    {
        if(!structure.NextAction.ExpiredOrNotRunning(Runner)) return;
        var pulse=builder.Pulse;
        var enemy=TeamSides.Opponent(structure.OwnerSide);
        float reach=pulse.Radius*pulse.Radius;
        if(PlayerHealth(enemy)>0 && NetworkedPlayerAvatar.TryGet(Runner,Owner(enemy),out var avatar) && FlatDistanceSquared(avatar.Position,structure.Position)<=reach)
        {
            DamagePlayer(enemy,pulse.Damage,-structure.Id);
            var victim=Players[(int)enemy];
            if(victim.Health>0 && pulse.SlowSeconds>0f){victim.Slow=TickTimer.CreateFromSeconds(Runner,pulse.SlowSeconds);Players.Set((int)enemy,victim);}
        }
        for(int b=0;b<SpidyCapacity;b++)
        {
            var bot=Spidys[b];
            if(bot.Health<=0 || bot.OwnerSide!=enemy || FlatDistanceSquared(bot.Position,structure.Position)>reach) continue;
            bot.Health=Mathf.Max(0,bot.Health-pulse.Damage);
            if(pulse.SlowSeconds>0f) bot.Slow=TickTimer.CreateFromSeconds(Runner,pulse.SlowSeconds);
            Spidys.Set(b,bot);
        }
        structure.Shots++;
        structure.NextAction=TickTimer.CreateFromSeconds(Runner,pulse.Interval);
        if (CombatLog.Verbose) Debug.Log($"[PULSE] id={structure.Id} owner={structure.OwnerSide} pulse={structure.Shots}");
    }

    /// <summary>HEALING PAD: heals only its owner's robot, standing on it, from a finite pool; NO HEAL stops it.</summary>
    private void TickHealingPad(ref OnlineStructure structure)
    {
        var pad=builder.Pad;
        if(structure.Shots>=pad.HealPool || !structure.NextAction.ExpiredOrNotRunning(Runner)) return;
        var side=structure.OwnerSide;
        var player=Players[(int)side];
        if(player.Health<=0 || player.Health>=PlayerMaximumHealth || IsRunning(player.NoHeal)) return;
        if(!NetworkedPlayerAvatar.TryGet(Runner,Owner(side),out var avatar) ||
            FlatDistanceSquared(avatar.Position,structure.Position)>pad.Radius*pad.Radius) return;
        player.Health=Mathf.Min(PlayerMaximumHealth,player.Health+1);
        Players.Set((int)side,player);
        structure.Shots++;
        structure.NextAction=TickTimer.CreateFromSeconds(Runner,1f/pad.HealPerSecond);
    }

    /// <summary>
    /// OVERDRIVE PAD: its owner's robot driving across the pad gets a short speed
    /// boost. The timer is refreshed (at most four times a second), never stacked,
    /// and always runs out on its own; enemies and Spidys are never boosted.
    /// Shots counts fresh boosts so every client can play the burst once.
    /// </summary>
    private void TickOverdrive(ref OnlineStructure structure)
    {
        var pad=builder.Overdrive;
        var side=structure.OwnerSide;
        var player=Players[(int)side];
        if(player.Health<=0 || !NetworkedPlayerAvatar.TryGet(Runner,Owner(side),out var avatar) ||
            FlatDistanceSquared(avatar.Position,structure.Position)>pad.Radius*pad.Radius) return;
        bool fresh=!IsRunning(player.Boost);
        float left=player.Boost.RemainingTime(Runner) ?? 0f;
        if(!fresh && left>=pad.BoostSeconds-0.25f) return;
        player.Boost=TickTimer.CreateFromSeconds(Runner,pad.BoostSeconds);
        Players.Set((int)side,player);
        if(fresh)
        {
            structure.Shots++;
            if (CombatLog.Verbose) Debug.Log($"[OVERDRIVE] id={structure.Id} owner={side} boost={pad.SpeedMultiplier:F2}x {pad.BoostSeconds:F1}s");
        }
    }

    /// <summary>
    /// RECOVERY JAMMER: an enemy robot inside the ring is under NO HEAL, refreshed
    /// while it stays (every half second, not every tick) and running out
    /// NoHealSeconds after it leaves; on a recharge it also takes a small burst.
    /// </summary>
    private void TickJammer(ref OnlineStructure structure)
    {
        var jammer=builder.Jammer;
        var enemy=TeamSides.Opponent(structure.OwnerSide);
        if(PlayerHealth(enemy)<=0 || !NetworkedPlayerAvatar.TryGet(Runner,Owner(enemy),out var avatar) ||
            FlatDistanceSquared(avatar.Position,structure.Position)>jammer.Radius*jammer.Radius) return;
        var victim=Players[(int)enemy];
        float left=victim.NoHeal.RemainingTime(Runner) ?? 0f;
        if(left < jammer.NoHealSeconds - 0.5f)
        {
            victim.NoHeal=TickTimer.CreateFromSeconds(Runner,jammer.NoHealSeconds);
            Players.Set((int)enemy,victim);
        }
        if(!structure.NextAction.ExpiredOrNotRunning(Runner)) return;
        DamagePlayer(enemy,jammer.InterferenceDamage,-structure.Id);
        structure.Shots++;
        structure.AimPosition=avatar.Position;
        structure.NextAction=TickTimer.CreateFromSeconds(Runner,jammer.Cooldown);
        Debug.Log($"[JAMMER] id={structure.Id} owner={structure.OwnerSide} burst; noheal={jammer.NoHealSeconds:F1}s after leaving");
    }

    /// <summary>
    /// MISSILE INTERCEPTOR: shoots down the nearest ENEMY missile in range, one per
    /// recharge. The replicated missile is removed here, so every client agrees.
    /// </summary>
    private void TickInterceptor(ref OnlineStructure structure)
    {
        if(!structure.NextAction.ExpiredOrNotRunning(Runner)) return;
        var interceptor=builder.Interceptor;
        var enemy=TeamSides.Opponent(structure.OwnerSide);
        float nearest=interceptor.Range*interceptor.Range;
        int best=-1;
        for(int m=0;m<MissileCapacity;m++)
        {
            var missile=Missiles[m];
            if(!missile.Active || missile.OwnerSide!=enemy) continue;
            float d=FlatDistanceSquared(missile.Position,structure.Position);
            if(d>nearest) continue;
            nearest=d; best=m;
        }
        if(best<0) return;
        var target=Missiles[best];
        target.Active=false;
        Missiles.Set(best,target);
        Vector3 flat=target.Position-structure.Position; flat.y=0f;
        if(flat.sqrMagnitude>0.01f) structure.HeadRotation=Quaternion.LookRotation(flat);
        structure.AimPosition=target.Position;
        structure.Shots++;
        structure.NextAction=TickTimer.CreateFromSeconds(Runner,interceptor.Cooldown);
        Debug.Log($"[INTERCEPT] id={structure.Id} owner={structure.OwnerSide} missile={target.Id}");
    }

    private static float FlatDistanceSquared(Vector3 a,Vector3 b){ a.y=0f; b.y=0f; return (a-b).sqrMagnitude; }
    private bool IsRunning(TickTimer timer) => timer.IsRunning && !timer.Expired(Runner);

    /// <summary>Whether a replicated slow timer is still running.</summary>
    public bool IsSlowed(TickTimer slow) => IsRunning(slow);
    /// <summary>Whether a replicated NO HEAL timer is still running.</summary>
    public bool IsHealBlocked(TickTimer noHeal) => IsRunning(noHeal);
    public float SlowMultiplier => builder != null && builder.Pulse != null ? builder.Pulse.SlowMultiplier : 1f;
    /// <summary>Whether a replicated Overdrive boost timer is still running.</summary>
    public bool IsBoosted(TickTimer boost) => IsRunning(boost);
    public float BoostMultiplier => builder != null && builder.Overdrive != null ? builder.Overdrive.SpeedMultiplier : 1f;

    private bool HasTurretSight(Vector3 start,Vector3 end,TeamSide owner,int slot)
    {
        Vector3 direction=end-start;
        int count=Physics.RaycastNonAlloc(start,direction.normalized,hits,direction.magnitude,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
        {
            var view=hits[i].collider.GetComponentInParent<OnlineStructureView>();
            if(view!=null && view.Slot==slot) continue;
            if(!IgnoreCollider(hits[i].collider,owner)) return false;
        }
        return true;
    }
    private void DamageStructure(int slot,int amount,int shot)
    {
        var structure=Structures[slot];if(structure.Id==0 || structure.Health<=0 || amount<=0) return;
        structure.Health=Mathf.Max(0,structure.Health-amount);Structures.Set(slot,structure);
        if (CombatLog.Verbose) Debug.Log($"[STRUCTURE DAMAGE] id={structure.Id} shot={shot} hp={structure.Health}");
    }
}
