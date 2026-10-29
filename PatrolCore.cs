using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for patrol, read from mod/droneautomation.xml.</summary>
    public sealed class PatrolSettings
    {
        /// Farthest a patrol spot may be from you, in metres.
        public float Radius = 20f;

        /// Nearest a patrol spot may be to you, so it does not just bob at your shoulder.
        public float MinRadius = 5f;

        /// How long it hovers at each spot before flying to the next.
        public float PauseSeconds = 2f;

        /// Flying speed while roaming, as a fraction of the drone's own.
        public float SpeedMult = 0.5f;

        /// Height above the ground it roams at, in metres: over a zombie's head.
        public float HoverHeight = 2.5f;

        /// Go after hostiles it spots (only with Auto-Defense fitted, switched on and loaded).
        public bool Hunt = true;

        /// Farthest from you it will chase something. Capped at 30, because vanilla teleports a
        /// following drone back to you the moment it is 32 m away.
        public float ChaseRadius = 28f;

        /// How far from its target it stops to shoot. It backs off to this distance if the target
        /// closes in. Never more than 80% of Auto-Defense's reach, or it would stop out of range.
        public float StandOff = 6f;

        /// Flying speed while chasing, as a fraction of the drone's own.
        public float ChaseSpeedMult = 1f;

        public void Clamp()
        {
            Radius = Mathf.Clamp(Radius, 2f, 28f);
            MinRadius = Mathf.Clamp(MinRadius, 0f, Radius);
            if (PauseSeconds < 0f) PauseSeconds = 0f;
            SpeedMult = Mathf.Clamp(SpeedMult, 0.1f, 1.5f);
            HoverHeight = Mathf.Clamp(HoverHeight, 1f, 5f);
            ChaseRadius = Mathf.Clamp(ChaseRadius, Radius, 30f);
            StandOff = Mathf.Clamp(StandOff, 2f, 15f);
            ChaseSpeedMult = Mathf.Clamp(ChaseSpeedMult, 0.1f, 1.5f);
        }
    }

    /// <summary>
    /// Patrol: the drone roams up to Radius metres around you, and with Auto-Defense fitted it goes
    /// after any hostile it spots within ChaseRadius of you, closes to StandOff and lets Auto-Defense
    /// shoot. Switched on per player from the drone's talk menu (the daPatrol CVar).
    ///
    /// It never touches the drone's orders: the drone stays on Follow, so the teleport back to you
    /// past 32 m and the teleport along with you work as always. What it does change is vanilla's
    /// leash. In its Idle state vanilla calls the drone back to following you once it is 7 m out
    /// (FollowDistance + 2), which no patrol could live inside, so IdleLeashPatch skips idleState
    /// while this is holding the drone - and holding only lasts while the drone is inside the leash
    /// patrol sets (Radius + 3 roaming, ChaseRadius + 2 hunting). Past that, or the moment patrol has
    /// nothing to say (you are in a vehicle, in water, have its menu or bag open, gave it Stay),
    /// vanilla's own idleState runs again and brings it home.
    ///
    /// No pathfinding: a roam spot is only picked with a clear line to it from your chest and from
    /// the drone, it only chases what it can see, and move() itself refuses a step into a block. A
    /// spot or a target it makes no headway on is dropped.
    ///
    /// While you look straight at it from within 8 m (and it is not fighting) it waits where it is, so
    /// you can walk up and talk to it: vanilla's talk reach is 2 m, and every roam spot is at least
    /// MinRadius from you.
    /// </summary>
    public sealed class PatrolCore
    {
        private readonly PatrolSettings settings;

        // Roaming.
        private Vector3 spot;
        private bool hasSpot;
        private ulong pauseUntilTick;

        // Hunting.
        private EntityAlive prey;
        private ulong lastSeenTick;
        private ulong nextSearchTick;
        private readonly Dictionary<int, ulong> ignored = new Dictionary<int, ulong>();
        private readonly List<Entity> nearby = new List<Entity>();

        // Headway, for whichever it is flying to.
        private ulong progressTick;
        private float progressDist;

        /// <summary>Tick until which IdleLeashPatch leaves vanilla's idleState out. 0 = not holding.</summary>
        private ulong holdUntilTick;

        /// <summary>What it is doing, for the Debug log.</summary>
        public string Doing { get; private set; } = "off";

        /// <summary>Same mask vanilla's drone uses for its own "is that way blocked" checks.</summary>
        private const int BlockMask = 1073807360;

        /// <summary>Seconds it may fly toward something without closing in before it gives up.</summary>
        private const float NoHeadwaySeconds = 3f;

        /// <summary>A target out of sight this long is let go.</summary>
        private const float LostSightSeconds = 3f;

        /// <summary>A target it gave up on is left alone this long.</summary>
        private const float IgnoreSeconds = 10f;

        /// <summary>It waits while you look at it from this close, so you can walk up and talk to it.</summary>
        private const float LookRange = 8f;

        private static readonly ConditionalWeakTable<EntityDrone, PatrolCore> cores =
            new ConditionalWeakTable<EntityDrone, PatrolCore>();

        private PatrolCore(PatrolSettings _settings)
        {
            settings = _settings;
        }

        public static PatrolCore For(EntityDrone _drone) =>
            cores.GetValue(_drone, _ => new PatrolCore(DroneAutomationMod.PatrolSettings));

        /// <summary>True while patrol, not vanilla's idleState, decides where this drone goes.</summary>
        public static bool Holds(EntityDrone _drone) =>
            cores.TryGetValue(_drone, out PatrolCore core) && core.holdUntilTick != 0UL
            && GameTimer.Instance.ticks <= core.holdUntilTick;

        private static ulong TicksFor(float _seconds)
        {
            float perSecond = GameTimer.Instance.ticksPerSecond;
            if (perSecond <= 0f) perSecond = 20f;
            return (ulong)Mathf.CeilToInt(_seconds * perSecond);
        }

        /// <summary>
        /// One tick. <paramref name="_fightReach"/> is how far Auto-Defense can shoot right now, 0 when
        /// it cannot (not fitted, switched off, out of rounds): then it only roams, and when a hostile
        /// turns up it comes back to you instead of hanging about out there unarmed.
        /// </summary>
        public void Tick(EntityDrone _drone, EntityPlayer _owner, bool _on, bool _locked, float _fightReach)
        {
            ulong now = GameTimer.Instance.ticks;
            Vector3 chest = _owner.getChestPosition();

            if (!_on || _locked || _drone.OrderState != EntityDrone.Orders.Follow || _drone.state != EntityDrone.State.Idle
                || _owner.AttachedToEntity != null || _owner.IsDead() || _drone.isTargetUnderWater(chest))
            {
                Release(now, _on ? (_locked ? "paused: its menu or bag is open" : "waiting: not idle beside you") : "off");
                prey = null;
                return;
            }

            bool canFight = settings.Hunt && _fightReach > 0f;
            if (canFight) Hunt(_drone, _owner, chest, now);
            else prey = null;

            if (prey != null)
            {
                if (!Within(_drone.position, chest, settings.ChaseRadius + 2f))
                {
                    Release(now, "past the chase leash, coming back");
                    prey = null;
                    ComeBack(_drone, chest);
                    return;
                }
                Hold(now);
                Chase(_drone, chest, now, _fightReach);
                return;
            }

            // A hostile it cannot fight: come back to you, and let vanilla keep it at your shoulder.
            if (!canFight && _drone.sensors != null && _drone.sensors.IsEnemyInRange())
            {
                Release(now, "hostile near and nothing to fight with: staying by you");
                ComeBack(_drone, chest);
                return;
            }

            if (!Within(_drone.position, chest, settings.Radius + 3f))
            {
                Release(now, "past the roam leash, coming back");
                ComeBack(_drone, chest);
                return;
            }
            Hold(now);

            // You are looking at it: wait, so you can walk up and talk to it (vanilla's talk reach is
            // 2 m, and every roam spot is at least MinRadius from you).
            if (LookedAt(_drone, _owner))
            {
                if (hasSpot) pauseUntilTick = now + TicksFor(settings.PauseSeconds);
                hasSpot = false;
                Doing = "waiting: you are looking at it";
                return;
            }
            Roam(_drone, chest, now);
        }

        /// <summary>Within LookRange of your eyes and within a metre of where you are looking.</summary>
        private static bool LookedAt(EntityDrone _drone, EntityPlayer _owner)
        {
            Vector3 to = _drone.position - _owner.getHeadPosition();
            if (to.sqrMagnitude > LookRange * LookRange) return false;
            Vector3 look = _owner.GetLookVector();
            float along = Vector3.Dot(to, look);
            if (along <= 0f) return false;
            return (to - look * along).sqrMagnitude <= 1f;
        }

        /// <summary>
        /// Fly back toward you. Vanilla does this itself past 7 m - except while a hostile is within
        /// its 22 m detection radius, when its idleState leaves the drone wherever it is.
        /// </summary>
        private static void ComeBack(EntityDrone _drone, Vector3 _chest)
        {
            if (Within(_drone.position, _chest, _drone.FollowDistance)) return;
            _drone.move((_chest - _drone.position).normalized, _drone.SpeedFlying);
            DroneFacing.Turn(_drone, _chest);
        }

        private void Hold(ulong _now) => holdUntilTick = _now + 2UL;

        private void Release(ulong _now, string _why)
        {
            holdUntilTick = 0UL;
            if (hasSpot) pauseUntilTick = _now + TicksFor(settings.PauseSeconds);
            hasSpot = false;
            Doing = _why;
        }

        private static bool Within(Vector3 _a, Vector3 _b, float _dist) => (_a - _b).sqrMagnitude <= _dist * _dist;

        // ---------------------------------------------------------------------------------------
        // Roaming.

        private void Roam(EntityDrone _drone, Vector3 _chest, ulong _now)
        {
            if (!hasSpot)
            {
                if (_now < pauseUntilTick) { Doing = "roaming: looking around"; return; }
                if (!Pick(_drone, _chest, out spot))
                {
                    // Boxed in (a corridor, a small room): try again in a second, not every tick.
                    pauseUntilTick = _now + TicksFor(1f);
                    Doing = "roaming: no clear spot around you";
                    return;
                }
                hasSpot = true;
                StartHeadway(_drone, spot, _now);
            }

            // You walked off and the spot is now outside the ring around you, it has stopped making
            // headway, or it has arrived: hover a moment, then pick the next one.
            Vector3 to = spot - _drone.position;
            if (!Within(spot, _chest, settings.Radius + 3f) || NoHeadway(_drone, spot, _now) || to.sqrMagnitude <= 0.36f)
            {
                pauseUntilTick = _now + TicksFor(settings.PauseSeconds);
                hasSpot = false;
                Doing = "roaming: looking around";
                return;
            }

            // Full speed until the last metre, then ease in.
            Vector3 dir = to.sqrMagnitude > 1f ? to.normalized : to;
            _drone.move(dir, _drone.SpeedFlying * settings.SpeedMult);
            DroneFacing.Turn(_drone, spot);
            Doing = "roaming: flying to a spot " + Mathf.RoundToInt(Vector3.Distance(spot, _chest)) + " m from you";
        }

        /// <summary>
        /// A spot somewhere in the ring around you, HoverHeight over the ground, that both you and the
        /// drone have a clear line to - so it stays on your side of walls, and indoors stays indoors.
        /// </summary>
        private bool Pick(EntityDrone _drone, Vector3 _chest, out Vector3 _spot)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(settings.MinRadius, settings.Radius);
                Vector3 p = _chest + new Vector3(Mathf.Cos(angle) * dist, 0.5f, Mathf.Sin(angle) * dist);
                if (EntityDrone.IsPositionBlocked(_chest, p, BlockMask)) continue;

                // Down to the ground; nothing within 12 m below is a drop it should not hang over.
                if (!EntityDrone.IsPositionBlocked(p, p + Vector3.down * 12f, out RaycastHit hit, BlockMask)) continue;
                float ground = hit.point.y + Origin.position.y;
                p.y = Mathf.Max(ground + 1.2f, Mathf.Min(ground + settings.HoverHeight, p.y + 1f));

                if ((p - _drone.position).sqrMagnitude < 4f) continue;
                if (EntityDrone.IsPositionBlocked(_chest, p, BlockMask)) continue;
                if (EntityDrone.IsPositionBlocked(_drone.position, p, BlockMask)) continue;

                _spot = p;
                return true;
            }
            _spot = Vector3.zero;
            return false;
        }

        // ---------------------------------------------------------------------------------------
        // Hunting.

        /// <summary>Keep the current target while it is worth chasing, else look for one twice a second.</summary>
        private void Hunt(EntityDrone _drone, EntityPlayer _owner, Vector3 _chest, ulong _now)
        {
            if (prey != null)
            {
                bool gone = prey.IsDead() || !Within(prey.position, _chest, settings.ChaseRadius)
                            || prey.Buffs.HasBuff("buffShocked") || (prey.IsSleeper && prey.IsSleeping);
                if (!gone && Visible(_drone, prey)) lastSeenTick = _now;
                if (gone || _now - lastSeenTick > TicksFor(LostSightSeconds))
                {
                    if (!prey.IsDead()) ignored[prey.entityId] = _now + TicksFor(IgnoreSeconds);
                    prey = null;
                    hasSpot = false;
                }
                return;
            }

            // Sneaking: it guards you (Auto-Defense still fires at what comes into its reach), but it
            // does not go looking for a fight that gives you away.
            if (_owner.IsCrouching || _now < nextSearchTick) return;
            nextSearchTick = _now + TicksFor(0.5f);

            prey = Find(_drone, _chest, _now);
            if (prey == null) return;
            lastSeenTick = _now;
            hasSpot = false;
            StartHeadway(_drone, prey.getChestPosition(), _now);
        }

        /// <summary>
        /// The nearest hostile the drone can see, within its own detection radius (22 m on vanilla)
        /// and within ChaseRadius of you: its revenge target first, then the same rules as Auto-Defense
        /// (hostile EntityEnemy only, not a sleeping sleeper, not shocked, clear line, no view cone).
        /// </summary>
        private EntityAlive Find(EntityDrone _drone, Vector3 _chest, ulong _now)
        {
            if (_drone.sensors == null) return null;

            EntityAlive revenge = _drone.GetRevengeTarget();
            if (revenge != null && !revenge.IsDead() && Within(revenge.position, _chest, settings.ChaseRadius)
                && _drone.sensors.canAttackTarget(revenge) && Visible(_drone, revenge))
                return revenge;

            EntityAlive best = null;
            float bestSq = float.MaxValue;
            float radius = _drone.sensors.EnemyDetectionRadius;
            nearby.Clear();
            GameManager.Instance.World.GetEntitiesAround(EntityFlags.AIHearing | EntityFlags.Player, _drone.position, radius, nearby);
            for (int i = 0; i < nearby.Count; i++)
            {
                EntityEnemy enemy = nearby[i] as EntityEnemy;
                if (enemy == null || enemy.EntityClass == null || !enemy.EntityClass.bIsEnemyEntity) continue;
                if (ignored.TryGetValue(enemy.entityId, out ulong until) && _now < until) continue;

                float distSq = (enemy.position - _drone.position).sqrMagnitude;
                if (distSq >= bestSq || distSq > radius * radius || !Within(enemy.position, _chest, settings.ChaseRadius)) continue;
                if (!_drone.sensors.canAttackTarget(enemy) || !Visible(_drone, enemy)) continue;
                best = enemy;
                bestSq = distSq;
            }
            nearby.Clear();

            if (ignored.Count > 32) ignored.Clear();
            return best;
        }

        private static bool Visible(EntityDrone _drone, EntityAlive _target) => _drone.CanEntityBeSeen(_target, false);

        /// <summary>
        /// Close to StandOff from the target on the side the drone is already on, a little over its
        /// head, never past ChaseRadius from you. Too close, and the same rule backs it off.
        /// </summary>
        private void Chase(EntityDrone _drone, Vector3 _chest, ulong _now, float _reach)
        {
            Vector3 target = prey.getChestPosition();
            Vector3 away = _drone.position - target;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = _chest - target;
            away.y = 0f;
            float standOff = Mathf.Min(settings.StandOff, _reach * 0.8f);
            Vector3 goal = target + away.normalized * standOff + Vector3.up * 1f;

            // Keep the goal on the leash: slide it back toward you until it is inside ChaseRadius.
            Vector3 fromYou = goal - _chest;
            if (fromYou.sqrMagnitude > settings.ChaseRadius * settings.ChaseRadius)
                goal = _chest + fromYou.normalized * settings.ChaseRadius;

            DroneFacing.Turn(_drone, target);
            Vector3 to = goal - _drone.position;
            if (to.sqrMagnitude <= 0.36f)
            {
                StartHeadway(_drone, goal, _now);
                Doing = "hunting: in position on " + prey.EntityName;
                return;
            }

            if (NoHeadway(_drone, goal, _now))
            {
                // Snagged on something it cannot fly round. Let it go for a while, and if it is in
                // reach anyway Auto-Defense keeps shooting it from here.
                ignored[prey.entityId] = _now + TicksFor(IgnoreSeconds);
                prey = null;
                Doing = "hunting: could not get at it";
                return;
            }

            Vector3 dir = to.sqrMagnitude > 1f ? to.normalized : to;
            _drone.move(dir, _drone.SpeedFlying * settings.ChaseSpeedMult);
            Doing = "hunting: chasing " + prey.EntityName + " " + Mathf.RoundToInt(Vector3.Distance(_drone.position, target)) + " m away";
        }

        // ---------------------------------------------------------------------------------------
        // Headway: give up on a goal it has not closed on by 0.3 m in NoHeadwaySeconds.

        private void StartHeadway(EntityDrone _drone, Vector3 _goal, ulong _now)
        {
            progressTick = _now;
            progressDist = Vector3.Distance(_drone.position, _goal);
        }

        private bool NoHeadway(EntityDrone _drone, Vector3 _goal, ulong _now)
        {
            float dist = Vector3.Distance(_drone.position, _goal);
            if (dist < progressDist - 0.3f || dist <= 0.6f)
            {
                progressTick = _now;
                progressDist = dist;
                return false;
            }
            return _now - progressTick > TicksFor(NoHeadwaySeconds);
        }
    }

    /// <summary>
    /// Vanilla's idleState pulls the drone back to following once it is 7 m from you, turns it to face
    /// you and matches your height, every tick. While patrol holds the drone (PatrolCore.Holds), all of
    /// that is patrol's job, so idleState is skipped; the moment patrol lets go it runs as normal.
    /// </summary>
    [HarmonyPatch(typeof(EntityDrone), nameof(EntityDrone.idleState))]
    public static class IdleLeashPatch
    {
        public static bool Prefix(EntityDrone __instance)
        {
            try
            {
                return !PatrolCore.Holds(__instance);
            }
            catch
            {
                return true;
            }
        }
    }
}
