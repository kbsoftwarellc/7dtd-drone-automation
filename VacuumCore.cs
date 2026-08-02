using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>Tunables for the Auto-Loot behaviour, read from mod/droneautomation.xml.</summary>
    public sealed class VacuumSettings
    {
        /// Block containers. Zero disables container draining entirely.
        public float ContainerRadius = 5f;

        /// Loot bags and dropped items.
        public float EntityRadius = 15f;

        public float VerticalRadius = 8f;
        public float SpeedMultiplier = 1f;
        public float MinSecondsPerTarget = 0.25f;
        public float ItemPickupSeconds = 0.25f;
        public float MaxCatchupSeconds = 5f;
        public float SkipIfPlayerWithin;

        /// How long a thrown item is left alone after it is thrown. Everything a player throws -
        /// a rock, a molotov, a grenade - is a live EntityItem, so without this the drone catches
        /// it. Zero disables the grace period, but never the in-flight and armed checks.
        public float ThrownGraceSeconds = 5f;

        /// Q1 reach as a fraction of the configured (Q6) reach; Q6 = full.
        public float LowQualityReach = 0.55f;
        /// Q1 action time as a multiple of the configured (Q6) time; Q6 = full speed.
        public float LowQualityTimeMult = 2f;

        public void Clamp()
        {
            if (ContainerRadius < 0f) ContainerRadius = 0f;
            if (EntityRadius < 0f) EntityRadius = 0f;
            if (VerticalRadius < 0f) VerticalRadius = 0f;
            if (SpeedMultiplier <= 0f) SpeedMultiplier = 1f;
            if (MinSecondsPerTarget < VacuumCore.MinOpenSeconds) MinSecondsPerTarget = VacuumCore.MinOpenSeconds;
            if (ItemPickupSeconds < 0f) ItemPickupSeconds = 0f;
            if (MaxCatchupSeconds < 0f) MaxCatchupSeconds = 0f;
            if (ThrownGraceSeconds < 0f) ThrownGraceSeconds = 0f;
            QualityScale.ClampKnobs(ref LowQualityReach, ref LowQualityTimeMult);
        }
    }

    /// <summary>
    /// Where absorbed loot goes. On the drone that is its Bag. Kept as an interface so the
    /// draining logic stays identical to the block vacuum's shared original.
    /// </summary>
    public interface IVacuumSink
    {
        ItemStack[] Items { get; }
        (bool anyMoved, bool allMoved) TryStackItem(int _startIndex, ItemStack _stack);
        void SetSlot(int _index, ItemStack _stack);
        void MarkChanged();
    }

    public sealed class BagSink : IVacuumSink
    {
        private readonly Bag bag;
        public BagSink(Bag _bag) { bag = _bag; }

        public ItemStack[] Items => bag.items;
        public (bool anyMoved, bool allMoved) TryStackItem(int _startIndex, ItemStack _stack) => bag.TryStackItem(_startIndex, _stack);
        public void SetSlot(int _index, ItemStack _stack) => bag.SetSlot(_index, _stack.Clone());
        public void MarkChanged() => bag.onBackpackChanged();
    }

    /// <summary>
    /// Auto-Loot: finds loot bags, containers and dropped items, opens them as the owner, drains
    /// them into the drone's bag.
    ///
    /// Server-side only. Callers must guarantee that, and must never invoke this while a client
    /// holds a lock on the sink - loot containers and bags are client-authoritative, so a client's
    /// next snapshot would silently overwrite anything written here.
    ///
    /// One instance per drone: it carries that drone's pacing state.
    /// </summary>
    public sealed class VacuumCore
    {
        /// Vanilla clamps a zero open time to this rather than opening instantly.
        public const float MinOpenSeconds = 0.01f;

        /// Backstop against a pathological sweep if many targets end up costing ~nothing.
        private const int MaxTargetsPerTick = 32;

        private readonly VacuumSettings settings;

        private ulong lastTick;
        private float credit;
        private int processedThisPass;
        private bool stalled;

        // Effective, quality-scaled values recomputed at the top of each Tick. The configured
        // settings are the Q6 (top-tier) ceiling; a lower-quality module reaches less far and
        // works more slowly.
        private float effEntityRadius;
        private float effContainerRadius;
        private float effVerticalRadius;
        private float effMinSecondsPerTarget;
        private float effItemPickupSeconds;
        private float effSpeedMultiplier;

        private static readonly List<Entity> entityBuffer = new List<Entity>();
        private static readonly List<TileEntity> tileEntityBuffer = new List<TileEntity>();

        public VacuumCore(VacuumSettings _settings) { settings = _settings; }

        /// <summary>Owner must be online: LootContainerOpened marks a container looted before it
        /// checks that it can resolve the player, so an absent owner permanently empties it.</summary>
        public static EntityPlayer ResolveOwner(World _world, PlatformUserIdentifierAbs _ownerId, out PersistentPlayerData _ownerData)
        {
            _ownerData = null;
            if (_ownerId == null) return null;

            PersistentPlayerList players = GameManager.Instance.GetPersistentPlayerList();
            if (players == null) return null;

            _ownerData = players.GetPlayerData(_ownerId);
            if (_ownerData == null || _ownerData.EntityId == -1) return null;

            return _world.GetEntity(_ownerData.EntityId) as EntityPlayer;
        }

        /// <summary>
        /// Runs one pass. Returns false when nothing could be afforded yet, so callers can cheaply bail.
        /// </summary>
        public bool Tick(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData,
                         PlatformUserIdentifierAbs _ownerId, IVacuumSink _sink, Vector3 _center, int _quality, DroneBoost _boost)
        {
            AccrueCredit();

            // The configured radii/times are the Q6 ceiling; scale down toward the Q1 floor.
            effEntityRadius        = QualityScale.Reach(settings.EntityRadius, settings.LowQualityReach, _quality);
            effContainerRadius     = QualityScale.Reach(settings.ContainerRadius, settings.LowQualityReach, _quality);
            effVerticalRadius      = QualityScale.Reach(settings.VerticalRadius, settings.LowQualityReach, _quality);
            effMinSecondsPerTarget = QualityScale.Time(settings.MinSecondsPerTarget, settings.LowQualityTimeMult, _quality);
            effItemPickupSeconds   = QualityScale.Time(settings.ItemPickupSeconds, settings.LowQualityTimeMult, _quality);
            effSpeedMultiplier     = QualityScale.Time(settings.SpeedMultiplier, settings.LowQualityTimeMult, _quality);

            // Enhancement meta-modules (Overclock/Antenna) layer on top: farther reach, faster arms.
            effEntityRadius        *= _boost.ReachMult;
            effContainerRadius     *= _boost.ReachMult;
            effVerticalRadius      *= _boost.ReachMult;
            effMinSecondsPerTarget *= _boost.SpeedMult;
            effItemPickupSeconds   *= _boost.SpeedMult;
            effSpeedMultiplier     *= _boost.SpeedMult;

            // Cheapest possible target costs MinSecondsPerTarget, so this also throttles scanning.
            if (credit < effMinSecondsPerTarget) return false;
            if (!HasFreeSlot(_sink)) return false;

            processedThisPass = 0;
            stalled = false;

            DrainBags(_world, _owner, _ownerData, _sink, _center);
            if (!stalled && effContainerRadius > 0f) DrainContainers(_world, _owner, _ownerData, _ownerId, _sink, _center);
            if (!stalled) DrainLooseItems(_world, _ownerData, _sink, _center);

            return processedThisPass > 0;
        }

        /// <summary>
        /// Banks elapsed seconds and spends them against each target's real open time, so the rate
        /// survives coarse or irregular ticks. The cap keeps a reload or chunk reload from bursting.
        /// </summary>
        private void AccrueCredit()
        {
            ulong now = GameTimer.Instance.ticks;
            if (now < lastTick) lastTick = now;

            if (lastTick == 0UL)
            {
                lastTick = now;
                return;
            }

            float perSecond = GameTimer.Instance.ticksPerSecond;
            if (perSecond <= 0f) perSecond = 20f;

            credit += (now - lastTick) / perSecond;
            lastTick = now;

            if (credit > settings.MaxCatchupSeconds) credit = settings.MaxCatchupSeconds;
        }

        /// <summary>
        /// Spends a target's open time. Refuses rather than skipping ahead, so an expensive bag is
        /// not leapfrogged by a cheap container - one pair of hands, one target at a time.
        /// </summary>
        private bool TrySpend(float _seconds)
        {
            if (processedThisPass >= MaxTargetsPerTick) { stalled = true; return false; }
            if (credit < _seconds) { stalled = true; return false; }

            credit -= _seconds;
            processedThisPass++;
            return true;
        }

        /// <summary>The vanilla loot-timer formula, minus the crouch bonus (a drone isn't crouching).</summary>
        private float OpenSeconds(EntityPlayer _owner, string _lootListName)
        {
            LootContainer container = LootContainer.GetLootContainer(_lootListName, false);
            float baseTime = container?.openTime ?? 0f;

            float seconds = EffectManager.GetValue(PassiveEffects.ScavengingTime, null, baseTime, _owner)
                            * LootContainer.LootTimerModifier
                            * effSpeedMultiplier;

            return seconds < MinOpenSeconds ? MinOpenSeconds : seconds;
        }

        /// <summary>
        /// The drone's own arm speed. Nearly half of vanilla's loot containers declare no
        /// open_time, so a player opens them instantly - without a floor those would all be
        /// swallowed in one tick instead of draining as a visible process.
        /// </summary>
        private float Cost(float _seconds)
        {
            return _seconds < effMinSecondsPerTarget ? effMinSecondsPerTarget : _seconds;
        }

        private void DrainBags(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData,
                               IVacuumSink _sink, Vector3 _center)
        {
            entityBuffer.Clear();
            _world.GetEntitiesInBounds(typeof(EntityLootContainer), BoundsAround(_center, effEntityRadius), entityBuffer);

            for (int i = 0; i < entityBuffer.Count; i++)
            {
                if (!(entityBuffer[i] is EntityLootContainer bagEntity)) continue;
                if (bagEntity.bag == null) continue;
                if (!InRange(_center, bagEntity.position, effEntityRadius)) continue;
                if (LockManager.Instance.IsLockedServer(bagEntity)) continue;
                if (IsBlockedPosition(_world, _ownerData, World.worldToBlockPos(bagEntity.position))) continue;
                if (!HasFreeSlot(_sink)) break;

                Bag bag = bagEntity.bag;

                // An already-opened bag costs nothing to rifle through, same as vanilla.
                float cost = Cost(bag.Touched ? MinOpenSeconds : OpenSeconds(_owner, bagEntity.GetLootList()));
                if (!TrySpend(cost)) break;

                if (!bag.Touched && bag.IsEmpty())
                {
                    GameManager.Instance.lootManager.LootBagOpened(bag, bagEntity, _owner.entityId);
                }

                MoveSlots(_sink, bag.GetSlots());

                // EntityLootContainer.OnUpdateEntity removes itself once the bag is touched, empty
                // and unlocked - the same path a player emptying it takes, so clients stay in sync.
                bag.Touched = true;
            }

            entityBuffer.Clear();
        }

        private void DrainContainers(World _world, EntityPlayer _owner, PersistentPlayerData _ownerData,
                                     PlatformUserIdentifierAbs _ownerId, IVacuumSink _sink, Vector3 _center)
        {
            CollectTileEntitiesInRange(_world, _center);

            for (int i = 0; i < tileEntityBuffer.Count; i++)
            {
                TileEntity te = tileEntityBuffer[i];
                if (!te.TryGetSelfOrFeature(out ITileEntityLootable loot)) continue;

                Vector3i pos = te.ToWorldPos();
                if (!InRange(_center, te.ToWorldCenterPos(), effContainerRadius)) continue;

                // bPlayerStorage is true for anything with an Owner, which covers every
                // player-placed chest - and the loot vacuum block's own storage.
                if (loot.bPlayerStorage) continue;
                if (LockManager.Instance.IsLockedServer(loot)) continue;

                if (te.TryGetSelfOrFeature(out TEFeatureLockable lockable)
                    && lockable.IsLocked() && (_ownerId == null || !lockable.IsUserAllowed(_ownerId))) continue;

                if (IsBlockedPosition(_world, _ownerData, pos)) continue;
                if (!HasFreeSlot(_sink)) break;

                BlockValue block = _world.GetBlock(pos);

                // Honour whatever stops the player looting this by hand (e.g. Twitch effects).
                if (EffectManager.GetValue(PassiveEffects.DisableLoot, null, 0f, _owner, null, block.Block.Tags) > 0f) continue;

                bool empty = loot.IsEmpty();
                bool touched = loot.bTouched;

                // Untouched but already holding items means quest/POI-staged loot. Opening it
                // would flag it touched, generate nothing, and let us steal the staged items.
                if (!touched && !empty) continue;
                if (touched && empty) continue;

                float cost = Cost(touched ? MinOpenSeconds : OpenSeconds(_owner, loot.lootListName));
                if (!TrySpend(cost)) break;

                if (!touched)
                {
                    GameManager.Instance.lootManager.LootContainerOpened(loot, _owner.entityId, block.Block.Tags);
                    loot.bTouched = true;
                }

                if (!MoveFromLootable(_sink, loot)) continue;

                loot.SetModified();
                if (loot.IsEmpty()) GameManager.Instance.CheckDestroyTileEntity(loot, pos);
            }

            tileEntityBuffer.Clear();
        }

        private void DrainLooseItems(World _world, PersistentPlayerData _ownerData, IVacuumSink _sink, Vector3 _center)
        {
            entityBuffer.Clear();
            _world.GetEntitiesInBounds(typeof(EntityItem), BoundsAround(_center, effEntityRadius), entityBuffer);

            for (int i = 0; i < entityBuffer.Count; i++)
            {
                Entity entity = entityBuffer[i];

                // EntityBackpack (player death bags) and EntityLootContainer both subclass
                // EntityItem, so the type scan hands them back here too.
                if (entity is EntityBackpack || entity is EntityLootContainer) continue;
                if (!(entity is EntityItem item)) continue;
                if (item.itemStack == null || item.itemStack.IsEmpty()) continue;
                if (!IsSettledLoot(item)) continue;
                if (!InRange(_center, item.position, effEntityRadius)) continue;
                if (IsBlockedPosition(_world, _ownerData, World.worldToBlockPos(item.position))) continue;

                // With a free slot the move is all-or-nothing, so we never have to write a
                // reduced count back to a live entity.
                if (!HasFreeSlot(_sink)) break;
                if (!TrySpend(Cost(effItemPickupSeconds))) break;

                ItemStack moving = item.itemStack.Clone();
                if (!Absorb(_sink, moving, moving.count) || moving.count > 0) continue;

                _world.RemoveEntity(item.entityId, EnumRemoveEntityReason.Killed);
            }

            entityBuffer.Clear();
        }

        /// <summary>
        /// Vanilla's own pickup rule, plus a grace period for anything a player threw.
        ///
        /// A thrown rock, molotov, grenade or pipe bomb is not a special projectile: ItemActionThrowAway
        /// spawns a plain EntityItem and hands it the throw as its initial motion, so an unfiltered
        /// EntityItem scan catches live ones out of the air. EntityItem.AllowActivationCommand gates a
        /// player's own "take" on CanCollect() and onGround, which covers both the in-flight case and an
        /// armed explosive - ItemClassTimeBomb.CanCollect turns false as soon as its fuse is running.
        ///
        /// onGround alone is not enough. It is a vertical-motion heuristic that banks a tick every time
        /// the item barely moves up or down and never gives those ticks back, so a lobbed item collects
        /// enough of them near the top of its arc to flip itself mid-flight. The grace period covers that,
        /// and it also leaves a thrown decoy on the ground long enough to actually pull anything.
        ///
        /// Ordinary loot is unaffected: bags, harvest yields and dropped stacks are spawned with no
        /// motion, so AddVelocity never runs for them and bWasThrown stays false.
        /// </summary>
        private bool IsSettledLoot(EntityItem _item)
        {
            if (!_item.onGround) return false;
            if (!_item.CanCollect()) return false;
            if (!_item.bWasThrown) return true;

            return _item.ticksExisted >= ThrownGraceTicks();
        }

        private int ThrownGraceTicks()
        {
            if (settings.ThrownGraceSeconds <= 0f) return 0;

            float perSecond = GameTimer.Instance.ticksPerSecond;
            if (perSecond <= 0f) perSecond = 20f;

            return Mathf.CeilToInt(settings.ThrownGraceSeconds * perSecond);
        }

        private void CollectTileEntitiesInRange(World _world, Vector3 _center)
        {
            tileEntityBuffer.Clear();

            float r = effContainerRadius;
            int minX = World.toChunkXZ(Utils.Fastfloor(_center.x - r));
            int maxX = World.toChunkXZ(Utils.Fastfloor(_center.x + r));
            int minZ = World.toChunkXZ(Utils.Fastfloor(_center.z - r));
            int maxZ = World.toChunkXZ(Utils.Fastfloor(_center.z + r));

            for (int cz = minZ; cz <= maxZ; cz++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (!(_world.GetChunkSync(cx, cz) is Chunk chunk)) continue;
                    // Snapshot: CheckDestroyTileEntity can remove entries mid-iteration.
                    tileEntityBuffer.AddRange(chunk.GetTileEntities().list);
                }
            }
        }

        private bool IsBlockedPosition(World _world, PersistentPlayerData _ownerData, Vector3i _pos)
        {
            if (_world.GetLandClaimOwner(_pos, _ownerData) == EnumLandClaimOwner.Other) return true;
            if (settings.SkipIfPlayerWithin <= 0f) return false;

            float sqr = settings.SkipIfPlayerWithin * settings.SkipIfPlayerWithin;
            Vector3 target = new Vector3(_pos.x, _pos.y, _pos.z);
            List<EntityPlayer> players = _world.Players.list;
            for (int i = 0; i < players.Count; i++)
            {
                if ((players[i].position - target).sqrMagnitude <= sqr) return true;
            }
            return false;
        }

        /// <summary>
        /// A cylinder, not a sphere: horizontal and vertical reach are independent, because bags
        /// fall to the ground while the drone may hover above them.
        /// </summary>
        private bool InRange(Vector3 _center, Vector3 _pos, float _horizontalRadius)
        {
            if (Mathf.Abs(_pos.y - _center.y) > effVerticalRadius) return false;

            float dx = _pos.x - _center.x;
            float dz = _pos.z - _center.z;
            return dx * dx + dz * dz <= _horizontalRadius * _horizontalRadius;
        }

        private Bounds BoundsAround(Vector3 _center, float _horizontalRadius)
        {
            return new Bounds(_center, new Vector3(_horizontalRadius * 2f, effVerticalRadius * 2f, _horizontalRadius * 2f));
        }

        private static bool HasFreeSlot(IVacuumSink _sink)
        {
            ItemStack[] items = _sink.Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsEmpty()) return true;
            }
            return false;
        }

        private static int FirstFreeSlot(IVacuumSink _sink)
        {
            ItemStack[] items = _sink.Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsEmpty()) return i;
            }
            return -1;
        }

        /// <summary>Moves what fits, leaving any remainder in the source slot.</summary>
        private static bool MoveSlots(IVacuumSink _sink, ItemStack[] _slots)
        {
            bool moved = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                ItemStack source = _slots[i];
                if (source == null || source.IsEmpty()) continue;

                ItemStack moving = source.Clone();
                if (!Absorb(_sink, moving, source.count)) continue;

                _slots[i] = moving.count == 0 ? ItemStack.Empty : moving.Clone();
                moved = true;
            }
            return moved;
        }

        private static bool MoveFromLootable(IVacuumSink _sink, ITileEntityLootable _source)
        {
            bool moved = false;
            ItemStack[] items = _source.items;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack source = items[i];
                if (source == null || source.IsEmpty()) continue;

                ItemStack moving = source.Clone();
                if (!Absorb(_sink, moving, source.count)) continue;

                _source.UpdateSlot(i, moving.count == 0 ? ItemStack.Empty : moving);
                moved = true;
            }
            return moved;
        }

        /// <summary>Stacks then slots <paramref name="_moving"/> into the sink, mutating its count.</summary>
        private static bool Absorb(IVacuumSink _sink, ItemStack _moving, int _originalCount)
        {
            _sink.TryStackItem(0, _moving);

            if (_moving.count > 0)
            {
                int free = FirstFreeSlot(_sink);
                if (free >= 0)
                {
                    _sink.SetSlot(free, _moving);
                    _moving.count = 0;
                }
            }

            bool moved = _moving.count != _originalCount;
            if (moved) _sink.MarkChanged();
            return moved;
        }
    }
}
