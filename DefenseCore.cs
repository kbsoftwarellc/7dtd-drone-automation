using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for Auto-Defense, read from mod/droneautomation.xml.</summary>
    public sealed class DefenseSettings
    {
        /// Engagement range in metres. The drone gun's own MaxDistance is 15, so there's no point
        /// reaching past it; this is the Q6 ceiling, scaled toward the Q1 floor by LowQualityReach.
        public float Range = 15f;

        /// Seconds banked per shot - the Q6 (fastest) cadence. Q1 fires LowQualityTimeMult times
        /// slower. Kept low so the "gun" actually rattles rather than plinking once a second.
        public float SecondsPerShot = 0.4f;

        /// Small on purpose: this bounds how much of a firing burst can be banked while no target is
        /// in range, so the drone can't unload a stored magazine the instant one wanders up.
        public float MaxCatchupSeconds = 2f;

        /// Q1 reach as a fraction of the configured (Q6) reach; Q6 = full.
        public float LowQualityReach = 0.6f;
        /// Q1 time between shots as a multiple of the configured (Q6) time; Q6 = full speed.
        public float LowQualityTimeMult = 2f;

        /// On by default: only fire when the drone actually has a clear line to the target, so it
        /// doesn't waste shots into cover. (The drone gun does no block damage, so this protects
        /// nothing but ammo-feel; turn it off to let it fire the instant an enemy is in range.)
        public bool RequireLineOfSight = true;

        public void Clamp()
        {
            if (Range < 0f) Range = 0f;
            if (SecondsPerShot < 0.05f) SecondsPerShot = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            QualityScale.ClampKnobs(ref LowQualityReach, ref LowQualityTimeMult);
        }
    }

    /// <summary>
    /// Auto-Defense: turns the junk drone into a bodyguard. Each pass it picks the nearest hostile
    /// and fires the drone's OWN machine gun at it - the one The Fun Pimps built into EntityDrone but
    /// never wired up (attackState() is empty and the gun is never instantiated in vanilla).
    ///
    /// We reuse DroneWeapons.MachineGunWeapon directly rather than dealing damage by hand: it is a
    /// server-authoritative hitscan that lands via ItemActionAttack.Hit, credits the kill to the
    /// drone's owner (XP and quests), and spawns the base-game muzzle-flash particle - so an EAC-on
    /// vanilla client sees the drone shoot with nothing installed. The gun reads its base stats
    /// (5 damage, 3-round burst, 15m, 5-degree spread) straight off the junk drone's entity class, so
    /// this module supplies none of them; module Quality scales fire rate here (Pacer + QualityScale)
    /// and per-shot damage via the EntityDamage passive on the item_modifier.
    ///
    /// Target selection is the drone's own public GetNearestEnemyInRange, which already excludes the
    /// owner, their allies, party members and traders - so there is no friendly fire and nothing to
    /// re-implement. Anchored on the drone itself: a following drone guards you, a parked one stands
    /// sentry over the spot you left it. It never touches the drone's bag, so DronePatch runs it even
    /// while the owner has the storage open, and independently of the parked / near-owner gates that
    /// bound the block modules.
    /// </summary>
    public sealed class DefenseCore
    {
        private readonly DefenseSettings settings;
        private readonly Pacer pacer;

        // Built lazily and cached per drone (DefenseCore itself is per-drone). The weapon binds to the
        // drone's "WristRight" model joint for its muzzle, so it can't be built until that transform
        // exists - hence the retry rather than a one-shot attempt.
        private DroneWeapons.MachineGunWeapon weapon;

        public DefenseCore(DefenseSettings _settings)
        {
            settings = _settings;
            pacer = new Pacer(_settings.MaxCatchupSeconds);
        }

        public bool Tick(EntityDrone _drone, int _quality, DroneBoost _boost)
        {
            pacer.Accrue();
            if (settings.Range <= 0f) return false;

            float secondsPerShot = QualityScale.Time(settings.SecondsPerShot, settings.LowQualityTimeMult, _quality) * _boost.SpeedMult;
            if (pacer.Credit < secondsPerShot) return false;

            if (weapon == null)
            {
                DroneWeapons.MachineGunWeapon w = new DroneWeapons.MachineGunWeapon(_drone);
                w.Init();
                // No muzzle joint yet (drone model still spawning). Try again next tick.
                if (w.WeaponJoint == null) return false;
                weapon = w;
            }

            EntityAlive target = _drone.GetNearestEnemyInRange(_drone.position);
            if (target == null || target.IsDead()) return false;

            float range = QualityScale.Reach(settings.Range, settings.LowQualityReach, _quality) * _boost.ReachMult;
            if ((target.position - _drone.position).sqrMagnitude > range * range) return false;

            if (settings.RequireLineOfSight && !_drone.CanSee(target)) return false;

            if (!pacer.TrySpend(secondsPerShot)) return false;

            // Fire is server-gated internally and self-contained (raycast -> hit -> muzzle FX). We
            // drive cadence with the Pacer above rather than the weapon's own burst clock, which
            // keeps quality scaling in one place and needs none of the weapon's tick state.
            weapon.Fire(target);
            return true;
        }
    }
}
