using Fusion;
using UnityEngine;
using UnityEngine.AI;

public sealed partial class NetworkedMatchState
{
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private readonly NavMeshPath[] paths = new NavMeshPath[SpidyCapacity];
    private readonly int[] pathCorners = new int[SpidyCapacity];
    private readonly float[] nextRepath = new float[SpidyCapacity];
    private readonly Vector3[][] cachedCorners = new Vector3[SpidyCapacity][];

    private void TickMissiles()
    {
        for (int i = 0; i < MissileCapacity && Phase == OnlineCombatPhase.Combat; i++)
        {
            var missile = Missiles[i];
            if (!missile.Active) continue;
            float remaining = missile.Range - Vector3.Distance(missile.Origin, missile.Position);
            float distance = Mathf.Min(missile.Speed * Runner.DeltaTime, Mathf.Max(0f, remaining));
            float nearest = distance;
            bool collided = false;
            int directTarget = -1; // 0=enemy player, 1=enemy heist, 2..9=Spidy slot.
            TeamSide enemy = TeamSides.Opponent(missile.OwnerSide);
            if (PlayerHealth(enemy) > 0 && NetworkedPlayerAvatar.TryGet(Runner, Owner(enemy), out var avatar) &&
                SweepSphere(missile.Position, missile.Direction, avatar.Position + Vector3.up, missile.Radius + 1.1f, ref nearest))
            { collided = true; directTarget = 0; }
            for (int b = 0; b < SpidyCapacity; b++)
            {
                var bot = Spidys[b];
                if (bot.Health > 0 && bot.OwnerSide == enemy &&
                    SweepSphere(missile.Position, missile.Direction, bot.Position + Vector3.up * 0.5f, missile.Radius + 0.7f, ref nearest))
                { collided = true; directTarget = b + 2; }
            }
            // Enemy structures are swept from replicated positions, exactly as
            // Spidys are. Their scene colliders are rejected by IgnoreCollider
            // because they carry a Damageable, so without this a missile passed
            // straight through an enemy structure without ever registering a hit.
            // Standing structures are swept as a sphere; floor items (Overdrive Pad,
            // Healing Pad) sit under the missile's flight height and only take
            // splash, as they do offline.
            for (int s = 0; s < StructureCapacity; s++)
            {
                var structure = Structures[s];
                if (structure.Id == 0 || structure.Health <= 0 || structure.OwnerSide != enemy || !IsStanding(structure.Type)) continue;
                if (SweepSphere(missile.Position, missile.Direction, structure.Position + Vector3.up * 0.7f, missile.Radius + 0.8f, ref nearest))
                { collided = true; directTarget = s + StructureTargetOffset; }
            }
            int count = Physics.SphereCastNonAlloc(missile.Position, missile.Radius, missile.Direction,
                hits, distance, blaster.ProjectileHitMask, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
            {
                var hit = hits[h];
                if (hit.collider == null || hit.distance > nearest || IgnoreCollider(hit.collider, missile.OwnerSide)) continue;
                nearest = hit.distance; collided = true;
                directTarget = hit.collider.GetComponentInParent<Damageable>() == arena.Heist(enemy) ? 1 : -1;
                var structure = hit.collider.GetComponentInParent<OnlineStructureView>();
                if (structure != null) directTarget = 10 + structure.Slot;
            }
            missile.Position += missile.Direction * nearest;
            if (collided || remaining <= distance + 0.001f)
            {
                missile.Active = false;
                Explode(missile, enemy, directTarget);
            }
            Missiles.Set(i, missile);
        }
    }
    private bool IgnoreCollider(Collider collider, TeamSide shooter)
    {
        var structure = collider.GetComponentInParent<OnlineStructureView>();
        if (structure != null) return structure.IsPreview || Structures[structure.Slot].Health <= 0;
        if (collider.transform.IsChildOf(arena.LocalPlayer.transform) ||
            collider.GetComponentInParent<NetworkedPlayerAvatar>() != null ||
            collider.GetComponentInParent<OnlineCombatPresentation>() != null) return true;
        var health = collider.GetComponentInParent<Damageable>();
        if (health == arena.Heist(shooter)) return true;
        // Dynamic combat bodies use the replicated simulation positions above, never a proxy's interpolated collider.
        return health != null && health != arena.Heist(TeamSides.Opponent(shooter));
    }
    private static bool IsStanding(BuildPlacementController.BuildableType type) =>
        type != BuildPlacementController.BuildableType.OverdrivePad && type != BuildPlacementController.BuildableType.HealingPad;

    private static bool SweepSphere(Vector3 origin, Vector3 direction, Vector3 centre, float radius, ref float nearest)
    {
        Vector3 offset = origin - centre;
        float b = Vector3.Dot(offset, direction), c = offset.sqrMagnitude - radius * radius;
        float discriminant = b * b - c;
        if (discriminant < 0f) return false;
        float distance = c <= 0f ? 0f : -b - Mathf.Sqrt(discriminant);
        if (distance < 0f || distance > nearest) return false;
        nearest = distance; return true;
    }
    private void Explode(OnlineMissile missile, TeamSide enemy, int direct)
    {
        for (int i = 0; i < StructureCapacity; i++)
        {
            var structure = Structures[i];
            if (structure.Id == 0 || structure.Health <= 0 || structure.OwnerSide != enemy) continue;
            float d = Mathf.Max(0, Vector3.Distance(missile.Position, structure.Position + Vector3.up * .5f) - .75f);
            DamageStructure(i, SplashDamage(missile, d, direct == i + 10), missile.Id);
        }
        // Each target is visited exactly once for this missile. No trigger/collision callbacks apply damage.
        if (NetworkedPlayerAvatar.TryGet(Runner, Owner(enemy), out var avatar))
        {
            float d = Mathf.Max(0f, Vector3.Distance(missile.Position, avatar.Position + Vector3.up) - 1.1f);
            int amount = SplashDamage(missile, d, direct == 0);
            if (amount > 0) DamagePlayer(enemy, amount, missile.Id);
        }
        float heistDistance = HeistDistance(enemy, missile.Position);
        int heistDamage = SplashDamage(missile, heistDistance, direct == 1);
        if (heistDamage > 0) DamageHeist(enemy, heistDamage, "missile:" + missile.Id);
        for (int b = 0; b < SpidyCapacity; b++)
        {
            var bot = Spidys[b];
            if (bot.Health <= 0 || bot.OwnerSide != enemy) continue;
            float d = Mathf.Max(0f, Vector3.Distance(missile.Position, bot.Position + Vector3.up * 0.5f) - 0.7f);
            int amount = SplashDamage(missile, d, direct == b + 2);
            if (amount <= 0) continue;
            bot.Health = Mathf.Max(0, bot.Health - amount); Spidys.Set(b, bot);
            if (CombatLog.Verbose) Debug.Log($"[SPIDY DAMAGE] swarm={bot.SwarmId} unit={b} shot={missile.Id} hp={bot.Health}");
        }
    }

    /// <summary>
    /// First direct-target code used for structures, matching the existing
    /// "direct == i + 10" convention in Explode.
    /// </summary>
    private const int StructureTargetOffset = SpidyCapacity + 2;
    private static int SplashDamage(OnlineMissile missile, float distance, bool direct)
    {
        if (direct) return missile.Damage;
        if (missile.Splash <= 0f || distance > missile.Splash) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(missile.Damage * Mathf.Lerp(1f, 0.4f, distance / missile.Splash)));
    }
    private float HeistDistance(TeamSide side, Vector3 point)
    {
        var colliders = arena.HeistColliders(side);
        float distance = float.PositiveInfinity;
        foreach (var collider in colliders)
            if (collider != null) distance = Mathf.Min(distance, Vector3.Distance(point, ColliderUtility.ClosestPointSafe(collider, point)));
        return distance;
    }
    private void Summon(TeamSide side, PlayerRef owner)
    {
        Vector3 spawn = arena.Spawn(side).position;
        if (!NavMesh.SamplePosition(spawn, out var sampled, 5f, NavMesh.AllAreas))
        { Debug.LogWarning("[SPIDY] Summon rejected: home spawn has no navigation surface."); return; }
        TeamSide enemy = TeamSides.Opponent(side);
        int swarm = ++SwarmSequence;
        int units = Mathf.Clamp(economy.OnlineBotsPerSwarm, 1, 4);
        for (int i = 0; i < units; i++)
        {
            Vector3 desired = sampled.position + Quaternion.Euler(0, i * 360f / units, 0) * Vector3.forward * 1.25f;
            Vector3 position = NavMesh.SamplePosition(desired, out var hit, 2f, NavMesh.AllAreas) ? hit.position : sampled.position;
            int slot = (int)side * 4 + i;
            Spidys.Set(slot, new OnlineSpidy { SwarmId = swarm, OwnerPlayerRef = owner, OwnerSide = side,
                TargetSide = enemy, Position = position,
                Rotation = Quaternion.LookRotation((arena.Heist(enemy).transform.position - position).normalized),
                Health = economy.OnlineSpidyPrefab.GetComponent<Damageable>().MaxHealth });
            paths[slot] = new NavMeshPath(); pathCorners[slot] = 0; nextRepath[slot] = 0;
        }
        var player = Players[(int)side]; player.Charge = 0; Players.Set((int)side, player);
        Debug.Log($"[SPIDY] Summon swarm={swarm} owner={owner} side={side} target={enemy} units={units}");
    }
    private void TickSpidys()
    {
        var config = economy.OnlineSpidyPrefab;
        float time = (float)Runner.SimulationTime;
        for (int i = 0; i < SpidyCapacity && Phase == OnlineCombatPhase.Combat; i++)
        {
            var bot = Spidys[i];
            if (bot.Health <= 0) continue;
            Vector3 target = arena.Heist(bot.TargetSide).transform.position;
            Vector3 towards = target - bot.Position; towards.y = 0;
            bool inRange = HeistDistance(bot.TargetSide, bot.Position + Vector3.up * 0.5f) <= config.OnlineAttackRange;
            bool clear = inRange && HasHeistSight(bot.Position + Vector3.up * 0.7f, target + Vector3.up * 0.7f, bot.TargetSide);
            if (clear)
            {
                if (towards.sqrMagnitude > 0.001f) bot.Rotation = Quaternion.LookRotation(towards);
                if (bot.NextAttack.ExpiredOrNotRunning(Runner))
                {
                    bot.NextAttack = TickTimer.CreateFromSeconds(Runner, config.OnlineAttackCooldown);
                    bot.Attacks++;
                    DamageHeist(bot.TargetSide, config.OnlineDamage, $"swarm:{bot.SwarmId}/unit:{i}/attack:{bot.Attacks}");
                }
            }
            else
            {
                if (paths[i] == null) paths[i] = new NavMeshPath();
                if (time >= nextRepath[i])
                {
                    // Approach the same enemy heist in shared world space. NavMesh avoids the arena obstacles.
                    Vector3 destination = target - towards.normalized * 3.8f;
                    destination.x += ((i % 4) - 1.5f) * 0.7f;
                    if (NavMesh.SamplePosition(destination, out var goal, 5f, NavMesh.AllAreas) &&
                        NavMesh.CalculatePath(bot.Position, goal.position, NavMesh.AllAreas, paths[i]))
                    { pathCorners[i] = 1; cachedCorners[i] = paths[i].corners; }
                    nextRepath[i] = time + 0.6f;
                }
                var corners = cachedCorners[i];
                int corner = pathCorners[i];
                if (corners != null && corner < corners.Length)
                {
                    Vector3 destination = corners[corner];
                    Vector3 heading = destination - bot.Position; heading.y = 0;
                    float speed = config.OnlineMovementSpeed * (IsSlowed(bot.Slow) ? SlowMultiplier : 1f);
                    bot.Position = Vector3.MoveTowards(bot.Position, destination, speed * Runner.DeltaTime);
                    if (heading.sqrMagnitude > 0.001f) bot.Rotation = Quaternion.LookRotation(heading);
                    if (Vector3.Distance(bot.Position, destination) < 0.15f) pathCorners[i]++;
                }
            }
            Spidys.Set(i, bot);
        }
    }
    private bool HasHeistSight(Vector3 start, Vector3 end, TeamSide target)
    {
        Vector3 direction = end - start;
        int count = Physics.RaycastNonAlloc(start, direction.normalized, hits, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
        float targetDistance = direction.magnitude;
        float obstacleDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            if (collider.GetComponentInParent<Damageable>() == arena.Heist(target)) targetDistance = Mathf.Min(targetDistance, hits[i].distance);
            else if (!IgnoreCollider(collider, TeamSides.Opponent(target))) obstacleDistance = Mathf.Min(obstacleDistance, hits[i].distance);
        }
        return obstacleDistance >= targetDistance - 0.05f;
    }
}
