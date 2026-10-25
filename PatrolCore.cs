using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for patrol, read from mod/droneautomation.xml.</summary>
    public sealed class PatrolSettings
    {
        /// Farthest a patrol spot may be from you, in metres. Capped at 6 because vanilla's own leash
        /// switches an idle drone back to following once it is FollowDistance + 2 (7 m) from your
        /// chest, and a spot past that would be abandoned on arrival.
        public float Radius = 5f;

        /// Nearest a patrol spot may be to you, so it does not just bob at your shoulder.
        public float MinRadius = 2f;

        /// How long it hovers at each spot before flying to the next.
        public float PauseSeconds = 4f;

        /// Flying speed while patrolling, as a fraction of the drone's own (3 m/s on vanilla).
        public float SpeedMult = 0.5f;

        public void Clamp()
        {
            Radius = Mathf.Clamp(Radius, 1f, 6f);
            MinRadius = Mathf.Clamp(MinRadius, 0f, Radius);
            if (PauseSeconds < 0.5f) PauseSeconds = 0.5f;
            SpeedMult = Mathf.Clamp(SpeedMult, 0.1f, 1f);
        }
    }

    /// <summary>
    /// Patrol: while it follows you and nothing hostile is near, the drone drifts between spots a few
    /// metres around you instead of hanging at your shoulder, and turns to face where it is going.
    /// Switched on per player from the drone's talk menu (the daPatrol CVar).
    ///
    /// It never touches the drone's orders. Vanilla's Follow order is what keeps the drone with you -
    /// the follow leash, the teleport back when it falls 32 m behind, the teleport along with you -
    /// and the Stay order, the only way to give a drone a spot of its own (SentryPos), switches all of
    /// that off and makes the block modules treat it as parked. So this only nudges a drone vanilla
    /// already has in its Idle state, using the drone's own move(): every tick idleState holds it at
    /// your chest height and turns it to face you, and this adds a push toward the spot and a turn
    /// toward it on top (DroneFacing). The moment you walk off, an enemy comes into range or you
    /// give it an order, vanilla leaves Idle and this lets go.
    ///
    /// No pathfinding: a spot is only picked when there is a clear line to it from your chest and
    /// from the drone, and move() itself refuses a step into a block.
    /// </summary>
    public sealed class PatrolCore
    {
        private readonly PatrolSettings settings;

        private Vector3 spot;
        private bool hasSpot;
        private ulong giveUpTick;
        private ulong pauseUntilTick;

        /// <summary>Same mask vanilla's drone uses for its own "is that way blocked" checks.</summary>
        private const int BlockMask = 1073807360;

        /// <summary>A spot not reached in this long is dropped (snagged on something).</summary>
        private const float GiveUpSeconds = 8f;

        public PatrolCore(PatrolSettings _settings)
        {
            settings = _settings;
        }

        private static ulong TicksFor(float _seconds)
        {
            float perSecond = GameTimer.Instance.ticksPerSecond;
            if (perSecond <= 0f) perSecond = 20f;
            return (ulong)Mathf.CeilToInt(_seconds * perSecond);
        }

        /// <summary>Returns true while it is flying to a spot.</summary>
        public bool Tick(EntityDrone _drone, EntityPlayer _owner)
        {
            ulong now = GameTimer.Instance.ticks;
            if (_drone.OrderState != EntityDrone.Orders.Follow || _drone.state != EntityDrone.State.Idle
                || _owner.AttachedToEntity != null || _owner.IsDead()
                || (_drone.sensors != null && _drone.sensors.IsEnemyInRange()))
            {
                Rest(now);
                return false;
            }

            Vector3 chest = _owner.getChestPosition();
            if (!hasSpot)
            {
                if (now < pauseUntilTick) return false;
                if (!Pick(_drone, chest, out spot))
                {
                    // Boxed in (a corridor, a small room): try again in a second, not every tick.
                    pauseUntilTick = now + TicksFor(1f);
                    return false;
                }
                hasSpot = true;
                giveUpTick = now + TicksFor(GiveUpSeconds);
            }

            // You walked away from it (vanilla follow takes over past 7 m anyway), it is stuck, or it
            // has arrived: hover a while, then pick the next one.
            float far = settings.Radius + 1f;
            Vector3 to = spot - _drone.position;
            if ((spot - chest).sqrMagnitude > far * far || now > giveUpTick || to.sqrMagnitude <= 0.36f)
            {
                Rest(now);
                return false;
            }

            // Full speed until the last metre, then ease in.
            Vector3 dir = to.sqrMagnitude > 1f ? to.normalized : to;
            _drone.move(dir, _drone.SpeedFlying * settings.SpeedMult);
            DroneFacing.Turn(_drone, spot);
            return true;
        }

        private void Rest(ulong _now)
        {
            if (hasSpot) pauseUntilTick = _now + TicksFor(settings.PauseSeconds);
            hasSpot = false;
        }

        private bool Pick(EntityDrone _drone, Vector3 _chest, out Vector3 _spot)
        {
            for (int i = 0; i < 6; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(settings.MinRadius, settings.Radius);
                Vector3 p = _chest + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

                // Not a "move" of under a metre, and not through a wall from you or from the drone.
                if ((p - _drone.position).sqrMagnitude < 1f) continue;
                if (EntityDrone.IsPositionBlocked(_chest, p, BlockMask)) continue;
                if (EntityDrone.IsPositionBlocked(_drone.position, p, BlockMask)) continue;

                _spot = p;
                return true;
            }
            _spot = Vector3.zero;
            return false;
        }
    }
}
