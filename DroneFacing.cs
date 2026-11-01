using System.Runtime.CompilerServices;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// Turns a drone's body toward something other than its owner.
    ///
    /// Vanilla points the junk drone at its owner every tick (idleState and steerFollow both call
    /// rotateTo toward the owner's chest), from inside EntityDrone.OnUpdateEntity. DroneModulePatch is
    /// a postfix on that same method, so whatever is written to Entity.rotation here is the last word
    /// for the tick: it is what Entity.Update lerps the model toward each frame on the server, and
    /// what the entity tracker sends to every client (NetPackageEntityRelPosAndRot), so a vanilla
    /// client sees the turn with nothing installed. Only yaw shows - Entity.Update keeps the model
    /// level - so only yaw is written.
    ///
    /// The turn is rate-limited rather than snapped, and remembers where it got to: vanilla's own
    /// rotateTo has already dragged rotation back toward the owner by the time the postfix runs, so
    /// the previous tick's yaw has to be kept here, not read back off the entity.
    /// </summary>
    internal static class DroneFacing
    {
        /// <summary>Degrees per tick: a half-turn in under half a second at 20 ticks/s.</summary>
        public const float DegreesPerTick = 20f;

        private sealed class State
        {
            public float Yaw;
            public ulong Tick;
        }

        private static readonly ConditionalWeakTable<EntityDrone, State> states =
            new ConditionalWeakTable<EntityDrone, State>();

        /// <summary>
        /// Turn one step toward <paramref name="_point"/>. Returns how many degrees are still left,
        /// so a caller can wait until it is roughly facing the thing before acting on it.
        /// </summary>
        public static float Turn(EntityDrone _drone, Vector3 _point)
        {
            Vector3 dir = _point - _drone.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return 0f;

            float want = Quaternion.LookRotation(dir).eulerAngles.y;
            State s = states.GetValue(_drone, _ => new State());

            // Carry on from last tick's yaw if this drone was turned last tick (or already this tick);
            // otherwise start from where its model is actually pointing.
            ulong now = GameTimer.Instance.ticks;
            bool turning = s.Tick != 0UL && now >= s.Tick && now - s.Tick <= 1UL;
            float from = turning ? s.Yaw : _drone.transform.eulerAngles.y;

            float yaw = Mathf.MoveTowardsAngle(from, want, DegreesPerTick);
            s.Yaw = yaw;
            s.Tick = now;
            _drone.rotation = new Vector3(0f, yaw, 0f);
            return Mathf.Abs(Mathf.DeltaAngle(yaw, want));
        }
    }
}
