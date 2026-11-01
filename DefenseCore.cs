using System;
using System.Collections.Generic;
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
        /// nothing but ammo; turn it off to let it fire the instant an enemy is in range.) A zombie
        /// behind cover is passed over for the nearest one it CAN hit.
        public bool RequireLineOfSight = true;

        /// On by default from 0.11.0: every shot spends one round, loaded from the drone's bag. Off
        /// restores the free gun of earlier versions.
        public bool RequireAmmo = true;

        /// What the gun loads, spent in this order. Vanilla's own (commented-out) ranged action on
        /// gunBotT3JunkDrone names exactly these three as the drone's Magazine_items, so this is the
        /// ammo The Fun Pimps meant the gun to take. Every kind fires the same round.
        public string AmmoItems = "ammoJunkTurretRegular,ammoJunkTurretShell,ammoJunkTurretAP";

        /// Rounds per magazine. 0 = the drone's own MagazineSize (62 at Q1, up to 92 at Q6).
        public int MagazineSize = 0;

        /// How long a reload takes: the gun falls silent this long when its magazine runs dry
        /// mid-fight, and between fights it tops up once nothing has been in its sights for this
        /// long. 3.9 is the Reload_time on that same vanilla action.
        public float ReloadSeconds = 3.9f;

        /// Heat (screamer) per shot as a multiple of what a vanilla auto turret's shot makes.
        /// 0 = silent, the way versions before 0.11.0 were.
        public float HeatMultiplier = 1f;

        public void Clamp()
        {
            if (Range < 0f) Range = 0f;
            if (SecondsPerShot < 0.05f) SecondsPerShot = 0.05f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            if (MagazineSize < 0) MagazineSize = 0;
            if (ReloadSeconds < 0f) ReloadSeconds = 0f;
            if (HeatMultiplier < 0f) HeatMultiplier = 0f;
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
    /// Target selection follows the drone's own GetNearestEnemyInRange - its revenge target first,
    /// then hostile EntityEnemy only, through its own canAttackTarget (alive, not a sleeping
    /// sleeper, not shocked, not behind blocks) - so players, allies and traders are never in the
    /// running. Unlike that method it takes the nearest one the drone can actually SEE, not just the
    /// nearest. Anchored on the drone itself: a following drone guards you, a parked one stands
    /// sentry over the spot you left it.
    ///
    /// FACING (0.13.0). Vanilla turns the drone to face its owner every tick, and until 0.13.0 the
    /// line-of-sight check was EntityAlive.CanSee, which only counts what is inside a 180-degree view
    /// cone around the way the entity faces (CanEntityBeSeen with checkViewCone). Together: the drone
    /// only ever saw the half of the world facing you, minus whatever your body blocked, so it shot
    /// zombies between you and it and ignored the ones coming up behind it. Now the check is a plain
    /// raycast with no cone, and the drone turns to face what it is shooting (DroneFacing), holding
    /// fire until it roughly does - the gun aims at the target either way, but the shot leaves from
    /// its right wrist, and facing away it would be firing through its own body. It goes back to
    /// facing you a second after its last shot, and only turns at all when it has rounds to fire.
    ///
    /// AMMO. The gun fires from a magazine held in EntityDrone.AmmoCount - vanilla's own field for
    /// it, stored in the drone item's Meta, so the magazine stays with the drone when you pick it up.
    /// The gun's own _fireWeapon already takes one round off it per shot (whenever the drone item
    /// has a MagazineSize, which vanilla gives it); nothing ever refilled it, so before 0.11.0 it
    /// simply ran negative. The magazine is refilled from the drone's bag, and ONLY while the bag is
    /// closed: the bag is client-authoritative while its owner has it open (the client sends the
    /// whole bag back on close, and that wins), so taking rounds out of it then would be undone or
    /// duplicated. The magazine is what lets the drone keep firing while you rummage in its storage.
    /// </summary>
    public sealed class DefenseCore
    {
        private readonly DefenseSettings settings;
        private readonly Pacer pacer;

        // Built lazily and cached per drone (DefenseCore itself is per-drone). The weapon binds to the
        // drone's "WristRight" model joint for its muzzle, so it can't be built until that transform
        // exists - hence the retry rather than a one-shot attempt.
        private DroneWeapons.MachineGunWeapon weapon;

        /// <summary>Tick at which a mid-fight reload finishes; 0 = not reloading.</summary>
        private ulong reloadDoneTick;

        /// <summary>Set once the owner has been told the gun is dry; cleared when it fires again.</summary>
        private bool toldDry;

        /// <summary>Last tick a target was in its sights; 0 = never.</summary>
        private ulong lastTargetTick;

        /// <summary>What it is turned toward, and until when; null = facing its owner, as vanilla.</summary>
        private EntityAlive facing;
        private ulong faceUntilTick;

        /// <summary>How long it keeps facing a target after the last time it lined up a shot.</summary>
        private const float FaceHoldSeconds = 1f;

        /// <summary>It fires once it is turned to within this many degrees of the target.</summary>
        private const float FireConeDegrees = 30f;

        // Reused every search, so FindTarget allocates nothing.
        private readonly List<Entity> nearby = new List<Entity>();
        private readonly List<KeyValuePair<float, EntityAlive>> candidates = new List<KeyValuePair<float, EntityAlive>>();
        private static readonly Comparison<KeyValuePair<float, EntityAlive>> nearestFirst = (a, b) => a.Key.CompareTo(b.Key);

        private static ulong TicksFor(float _seconds)
        {
            float perSecond = GameTimer.Instance.ticksPerSecond;
            if (perSecond <= 0f) perSecond = 20f;
            return (ulong)Mathf.CeilToInt(_seconds * perSecond);
        }

        private bool IdleFor(float _seconds)
        {
            if (lastTargetTick == 0UL) return true;
            ulong now = GameTimer.Instance.ticks;
            float perSecond = GameTimer.Instance.ticksPerSecond;
            if (perSecond <= 0f) perSecond = 20f;
            return now < lastTargetTick || (now - lastTargetTick) / perSecond >= _seconds;
        }

        public DefenseCore(DefenseSettings _settings)
        {
            settings = _settings;
            pacer = new Pacer(_settings.MaxCatchupSeconds);
        }

        public bool Tick(EntityDrone _drone, EntityPlayer _owner, int _quality, DroneBoost _boost, bool _bagLocked)
        {
            pacer.Accrue();
            if (settings.Range <= 0f) return false;

            // Every tick, not only on the ticks it may fire: vanilla turns it back to its owner each
            // tick, so a turn made only when a shot is due would flick back and forth.
            float aimError = HoldFacing(_drone, _quality, _boost);

            List<ItemValue> ammo = NeedsAmmo(settings) ? Ammo(settings) : null;
            if (ammo != null)
            {
                Remember(_drone, _owner);

                // Drones fielded before 0.11.0 carry a negative count: the gun has always decremented
                // it and nothing ever put rounds back.
                if (_drone.AmmoCount < 0) _drone.AmmoCount = 0;

                if (reloadDoneTick != 0UL)
                {
                    // Reloading mid-fight from rounds in its bag: keep facing the fight meanwhile.
                    if (facing != null) faceUntilTick = GameTimer.Instance.ticks + TicksFor(FaceHoldSeconds);
                    if (GameTimer.Instance.ticks < reloadDoneTick || _bagLocked) return false;
                    reloadDoneTick = 0UL;
                    Load(_drone, _owner, ammo);
                }
            }

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

            EntityAlive target = FindTarget(_drone, _quality, _boost);
            if (target == null)
            {
                // Between fights: once nothing has been in its sights for a reload's length, top the
                // magazine up, so a fight starts on a full one. Not sooner - a target drops out of sight
                // for a tick all the time mid-fight, and refilling then would mean it never runs dry.
                if (ammo != null && !_bagLocked && _drone.AmmoCount < Magazine(_drone, settings) && IdleFor(settings.ReloadSeconds))
                    Load(_drone, _owner, ammo);
                return false;
            }
            lastTargetTick = GameTimer.Instance.ticks;

            if (ammo != null && _drone.AmmoCount <= 0)
            {
                if (!_bagLocked && BagRounds(_drone.bag, ammo) > 0)
                {
                    if (settings.ReloadSeconds <= 0f)
                    {
                        Load(_drone, _owner, ammo);
                    }
                    else
                    {
                        float perSecond = GameTimer.Instance.ticksPerSecond;
                        if (perSecond <= 0f) perSecond = 20f;
                        reloadDoneTick = GameTimer.Instance.ticks + (ulong)Mathf.CeilToInt(settings.ReloadSeconds * perSecond);
                        return false;
                    }
                }
                if (_drone.AmmoCount <= 0)
                {
                    TellDry(_owner, _bagLocked && BagRounds(_drone.bag, ammo) > 0);
                    return false;
                }
            }

            // It has a target and a round for it: face it, and hold the shot until it roughly does.
            // The credit stays banked meanwhile, so the first shot goes the moment it has turned.
            if (target != facing)
            {
                facing = target;
                aimError = DroneFacing.Turn(_drone, target.getChestPosition());
            }
            faceUntilTick = GameTimer.Instance.ticks + TicksFor(FaceHoldSeconds);
            if (aimError > FireConeDegrees) return false;

            if (!pacer.TrySpend(secondsPerShot)) return false;

            // Fire is server-gated internally and self-contained (raycast -> hit -> muzzle FX). We
            // drive cadence with the Pacer above rather than the weapon's own burst clock, which
            // keeps quality scaling in one place and needs none of the weapon's tick state.
            int before = _drone.AmmoCount;
            weapon.Fire(target);

            if (ammo != null)
            {
                // The gun only counts the round itself when the drone item has a MagazineSize; an
                // overhaul that strips it must not get a free gun.
                if (_drone.AmmoCount >= before) _drone.AmmoCount = before - 1;
                toldDry = false;
            }
            else
            {
                // Ammo switched off: leave the magazine as it was rather than let it run negative.
                _drone.AmmoCount = before;
            }

            Heat.GunFired(GameManager.Instance.World, _drone.position, settings.HeatMultiplier);
            return true;
        }

        /// <summary>
        /// Keep turning toward the target it is fighting, until a second after it last lined up a
        /// shot, or until that target dies or leaves its reach. Returns the degrees still to turn
        /// (180 when it is not facing anything).
        /// </summary>
        private float HoldFacing(EntityDrone _drone, int _quality, DroneBoost _boost)
        {
            if (facing == null) return 180f;

            float range = Reach(_quality, _boost);
            if (facing.IsDead() || GameTimer.Instance.ticks > faceUntilTick
                || (facing.position - _drone.position).sqrMagnitude > range * range)
            {
                facing = null;
                return 180f;
            }
            return DroneFacing.Turn(_drone, facing.getChestPosition());
        }

        private float Reach(int _quality, DroneBoost _boost) =>
            QualityScale.Reach(settings.Range, settings.LowQualityReach, _quality) * _boost.ReachMult;

        /// <summary>
        /// The nearest hostile in reach that the drone can hit, its revenge target first. The same
        /// rules as the drone's own GetNearestEnemyInRange, except that a zombie it cannot see no
        /// longer hides every other one: until 0.13.0 only the single nearest was ever considered, so
        /// one behind a wall left the drone silent with others in the open.
        /// </summary>
        private EntityAlive FindTarget(EntityDrone _drone, int _quality, DroneBoost _boost)
        {
            float range = Reach(_quality, _boost);
            if (range <= 0f || _drone.sensors == null) return null;
            float rangeSq = range * range;

            EntityAlive revenge = _drone.GetRevengeTarget();
            if (revenge != null && !revenge.IsDead() && !revenge.Buffs.HasBuff("buffShocked")
                && (revenge.position - _drone.position).sqrMagnitude <= rangeSq && Visible(_drone, revenge))
                return revenge;

            nearby.Clear();
            candidates.Clear();
            GameManager.Instance.World.GetEntitiesAround(EntityFlags.AIHearing | EntityFlags.Player, _drone.position, range, nearby);
            for (int i = 0; i < nearby.Count; i++)
            {
                EntityEnemy enemy = nearby[i] as EntityEnemy;
                if (enemy == null || enemy.EntityClass == null || !enemy.EntityClass.bIsEnemyEntity) continue;

                float distSq = (enemy.position - _drone.position).sqrMagnitude;
                if (distSq > rangeSq || !_drone.sensors.canAttackTarget(enemy)) continue;
                candidates.Add(new KeyValuePair<float, EntityAlive>(distSq, enemy));
            }
            nearby.Clear();

            candidates.Sort(nearestFirst);
            EntityAlive found = null;
            for (int i = 0; i < candidates.Count && found == null; i++)
            {
                if (Visible(_drone, candidates[i].Value)) found = candidates[i].Value;
            }
            candidates.Clear();
            return found;
        }

        /// <summary>
        /// A clear line from the drone's head to the target's, with NO view cone: CanEntityBeSeen with
        /// checkViewCone false. EntityAlive.CanSee, used until 0.13.0, goes through the same method
        /// with the cone on, and the cone points wherever vanilla last turned the drone - at you.
        /// It still fails when anything else is in the way first, you included.
        /// </summary>
        private bool Visible(EntityDrone _drone, EntityAlive _target) =>
            !settings.RequireLineOfSight || _drone.CanEntityBeSeen(_target, false);

        /// <summary>
        /// Fill the magazine from the bag, spending the ammo kinds in AmmoItems order. Says so once
        /// when this load is the last of what the bag held, which is the moment worth restocking.
        /// </summary>
        private void Load(EntityDrone _drone, EntityPlayer _owner, List<ItemValue> _ammo)
        {
            int room = Magazine(_drone, settings) - _drone.AmmoCount;
            if (room <= 0) return;

            int loaded = 0;
            for (int i = 0; i < _ammo.Count && loaded < room; i++)
            {
                int have = _drone.bag.GetItemCount(_ammo[i]);
                if (have <= 0) continue;

                int take = Mathf.Min(have, room - loaded);
                _drone.bag.DecItem(_ammo[i], take);
                loaded += take;
            }
            if (loaded <= 0) return;

            _drone.AmmoCount += loaded;
            toldDry = false;

            if (_owner != null && BagRounds(_drone.bag, _ammo) == 0)
                Msg.Tell(_owner.entityId, $"Drone: last magazine loaded ({_drone.AmmoCount} rounds). Put Robotic Turret Ammo in my bag to keep Auto-Defense firing.");
        }

        private void TellDry(EntityPlayer _owner, bool _waitingOnBag)
        {
            if (toldDry || _owner == null) return;
            toldDry = true;
            Msg.Tell(_owner.entityId, _waitingOnBag
                ? "Drone: out of ammo. I'll reload when you close my bag."
                : "Drone: out of ammo. Auto-Defense needs Robotic Turret Ammo in my bag.");
        }

        // ---------------------------------------------------------------------------------------
        // Shared ammo bookkeeping: used by the core, by the talk menu (DronePatch) and by /das ammo.

        /// <summary>What the talk menu's ammo row shows. 0 = no row (ammo not required).</summary>
        public const int AmmoNoRow = 0, AmmoPlenty = 1, AmmoLow = 2, AmmoEmpty = 3;

        public static bool NeedsAmmo(DefenseSettings _settings) => _settings.RequireAmmo && Ammo(_settings).Count > 0;

        /// <summary>
        /// Plenty, low (the bag holds less than one more magazine) or empty (nothing in the magazine
        /// and nothing in the bag) - for the drone's talk menu. Polarity follows the other menu CVars:
        /// 0, the value an unwritten CVar reads as, hides the row.
        /// </summary>
        public static int AmmoState(EntityDrone _drone, DefenseSettings _settings)
        {
            if (!NeedsAmmo(_settings) || _drone?.bag == null) return AmmoNoRow;

            List<ItemValue> ammo = Ammo(_settings);
            int mag = Mathf.Max(0, _drone.AmmoCount);
            int bag = BagRounds(_drone.bag, ammo);
            if (mag + bag <= 0) return AmmoEmpty;
            return bag < Magazine(_drone, _settings) ? AmmoLow : AmmoPlenty;
        }

        /// <summary>Rounds per magazine: the configured size, else the drone item's own.</summary>
        public static int Magazine(EntityDrone _drone, DefenseSettings _settings)
        {
            if (_settings.MagazineSize > 0) return _settings.MagazineSize;

            // The same call _fireWeapon makes to decide whether a magazine exists at all.
            int size = (int)EffectManager.GetValue(PassiveEffects.MagazineSize, _drone.OriginalItemValue);
            return size > 0 ? size : 62;
        }

        public static int BagRounds(Bag _bag, List<ItemValue> _ammo)
        {
            if (_bag == null) return 0;
            int n = 0;
            for (int i = 0; i < _ammo.Count; i++) n += _bag.GetItemCount(_ammo[i]);
            return n;
        }

        private static List<ItemValue> ammoItems;
        private static string ammoSource;

        /// <summary>
        /// AmmoItems resolved to item values. Lazily, because the settings are read in InitMod, before
        /// the item list exists; the first caller is a drone tick, long after it does. A name this game does not know is logged once and dropped; if none
        /// resolve, the gun fires free rather than not at all - a guard that never shoots reads as a
        /// broken mod, and the log says why.
        /// </summary>
        public static List<ItemValue> Ammo(DefenseSettings _settings)
        {
            if (ammoItems != null && ammoSource == _settings.AmmoItems) return ammoItems;

            var list = new List<ItemValue>();
            var unknown = new List<string>();
            foreach (string raw in (_settings.AmmoItems ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                ItemValue iv = ItemClass.GetItem(name);
                if (iv == null || iv.IsEmpty()) { unknown.Add(name); continue; }
                list.Add(iv);
            }

            if (unknown.Count > 0)
                Log.Warning("[DroneAutomation] Auto-Defense: unknown AmmoItems ignored: " + string.Join(", ", unknown)
                            + (list.Count == 0 ? ". None left, so the drone gun fires without ammo." : ""));

            ammoItems = list;
            ammoSource = _settings.AmmoItems;
            return ammoItems;
        }

        // ---------------------------------------------------------------------------------------
        // Which drones each player has fielded, for /das ammo. Weak, so a despawned drone is not kept.

        private struct Fielded
        {
            public WeakReference<EntityDrone> Drone;
            public int OwnerId;
            public ulong Tick;
        }

        private static readonly Dictionary<int, Fielded> fielded = new Dictionary<int, Fielded>();

        private static void Remember(EntityDrone _drone, EntityPlayer _owner)
        {
            if (_owner == null) return;

            // Runs every tick, so reuse the entry rather than allocate a fresh WeakReference each time.
            fielded.TryGetValue(_drone.entityId, out Fielded f);
            if (f.Drone == null || !f.Drone.TryGetTarget(out EntityDrone known) || known != _drone)
                f.Drone = new WeakReference<EntityDrone>(_drone);
            f.OwnerId = _owner.entityId;
            f.Tick = GameTimer.Instance.ticks;
            fielded[_drone.entityId] = f;
        }

        /// <summary>One chat line per Auto-Defense drone the player has out, ticked in the last ~10s.</summary>
        public static List<string> Report(int _ownerId, DefenseSettings _settings)
        {
            var lines = new List<string>();
            ulong now = GameTimer.Instance.ticks;
            var stale = new List<int>();

            foreach (KeyValuePair<int, Fielded> kv in fielded)
            {
                Fielded f = kv.Value;
                if (!f.Drone.TryGetTarget(out EntityDrone d) || d == null || d.IsDead()
                    || now < f.Tick || now - f.Tick > 200UL)
                {
                    stale.Add(kv.Key);
                    continue;
                }
                if (f.OwnerId != _ownerId) continue;

                List<ItemValue> ammo = Ammo(_settings);
                lines.Add($"  Drone {kv.Key}: {Mathf.Max(0, d.AmmoCount)}/{Magazine(d, _settings)} in the magazine, {BagRounds(d.bag, ammo)} more in its bag.");
            }
            for (int i = 0; i < stale.Count; i++) fielded.Remove(stale[i]);
            return lines;
        }
    }
}
