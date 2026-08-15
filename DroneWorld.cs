using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// Server-side helpers shared by the block-touching modules (Salvage, Harvest, Repair).
    ///
    /// The vanilla melee harvest/salvage drop path is client-only (GameUtils.HarvestOnAttack bails
    /// unless the holder is an EntityPlayerLocal, and deposits to the local player's inventory), so
    /// a drone cannot reuse it. These helpers reproduce the server-safe pieces: read a block's drop
    /// list, compute counts with the owner's perks, deposit into the drone bag, and mutate the block
    /// via Block.DamageBlock / World.SetBlockRPC, both of which sync to clients on their own.
    /// </summary>
    public static class DroneWorld
    {
        private static readonly HashSet<Vector3i> scanSet = new HashSet<Vector3i>();

        /// <summary>
        /// Collects distinct non-air block positions in a cylinder around <paramref name="_center"/>,
        /// each resolved to its multiblock parent. Only ever called when a module can afford at least
        /// one action, so the scan cost is bounded to roughly once per action, not once per frame.
        /// </summary>
        public static void CollectParents(World _world, Vector3 _center, float _radius, float _vertical, List<Vector3i> _out)
        {
            _out.Clear();
            scanSet.Clear();

            int cx = Utils.Fastfloor(_center.x);
            int cy = Utils.Fastfloor(_center.y);
            int cz = Utils.Fastfloor(_center.z);
            int r = Mathf.CeilToInt(_radius);
            int vr = Mathf.CeilToInt(_vertical);
            float r2 = _radius * _radius;

            for (int dy = -vr; dy <= vr; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (dx * dx + dz * dz > r2) continue;

                        Vector3i pos = new Vector3i(cx + dx, cy + dy, cz + dz);
                        BlockValue bv = _world.GetBlock(pos);
                        if (bv.isair) continue;

                        Vector3i parent = ResolveParent(pos, bv);
                        if (scanSet.Add(parent)) _out.Add(parent);
                    }
                }
            }
        }

        /// <summary>Multiblocks (cars, crops) span cells; act on the parent only or you double-process.</summary>
        public static Vector3i ResolveParent(Vector3i _pos, BlockValue _bv)
        {
            Block b = _bv.Block;
            if (b != null && b.isMultiBlock && _bv.ischild && b.multiBlockPos != null)
                return b.multiBlockPos.GetParentPos(_pos, _bv);
            return _pos;
        }

        public static EnumLandClaimOwner Claim(World _world, PersistentPlayerData _ownerData, Vector3i _pos)
        {
            return _world.GetLandClaimOwner(_pos, _ownerData);
        }

        /// <summary>
        /// The vanilla per-drop count: a random pull between min and max, scaled by the owner's
        /// tag-scoped HarvestCount perk. Owner offline (null) → base count, no perk scaling.
        /// Returns 0 when the drop's probability roll fails or the entry is empty.
        /// </summary>
        public static int DropCount(Block.SItemDropProb _drop, EntityPlayer _owner, System.Random _rand)
        {
            if (_drop.prob < 1f && _rand.NextDouble() > _drop.prob) return 0;

            int lo = _drop.minCount;
            int hi = _drop.maxCount;
            int baseN = hi <= lo ? lo : _rand.Next(lo, hi + 1);
            if (baseN <= 0) return 0;

            FastTags<TagGroup.Global> tag = string.IsNullOrEmpty(_drop.tag)
                ? default
                : FastTags<TagGroup.Global>.Parse(_drop.tag);

            float hc = EffectManager.GetValue(PassiveEffects.HarvestCount, null, 1f, _owner, null, tag);
            int n = Mathf.RoundToInt(baseN * (hc <= 0f ? 1f : hc));
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// Emits a block's drops for one event into the drone bag. name="*" means the block's own
        /// item. Anything that does not fit spills to the ground, exactly like an over-full loot.
        ///
        /// <paramref name="_payOne"/> withholds a single unit of one item from the payout, for a
        /// caller that is about to spend it on the player's behalf. Auto-Harvest uses it so the
        /// seed it replants with is the seed the crop just dropped, rather than a conjured extra:
        /// a crop yields its seed AND gets replanted, so banking both would mint one free seed per
        /// crop per cycle. Only ONE unit is withheld, so perk-boosted seed drops still pay out the
        /// surplus.
        /// </summary>
        public static void EmitDrops(Block _b, EnumDropEvent _event, BlockValue _bv,
                                     EntityPlayer _owner, EntityDrone _drone, System.Random _rand,
                                     ItemValue _payOne = null)
        {
            if (_b.itemsToDrop == null) return;
            if (!_b.itemsToDrop.TryGetValue(_event, out List<Block.SItemDropProb> list) || list == null) return;

            bool paid = false;

            for (int i = 0; i < list.Count; i++)
            {
                Block.SItemDropProb drop = list[i];
                int count = DropCount(drop, _owner, _rand);
                if (count <= 0) continue;

                ItemValue iv = drop.name == "*" ? _bv.ToItemValue() : ItemClass.GetItem(drop.name);
                if (iv == null || iv.IsEmpty()) continue;

                if (!paid && _payOne != null && iv.type == _payOne.type)
                {
                    paid = true;
                    count--;
                    if (count <= 0) continue;
                }

                Deposit(_drone, iv.Clone(), count);
            }
        }

        /// <summary>
        /// Deposits into the drone bag, spilling to the ground if it will not fit. Bag.AddItem is
        /// all-or-nothing (it places the whole stack or returns false untouched), so dropping the
        /// remainder on a false return cannot duplicate.
        /// </summary>
        public static void Deposit(EntityDrone _drone, ItemValue _iv, int _count)
        {
            if (_iv == null || _count <= 0) return;

            ItemStack stack = new ItemStack(_iv, _count);
            if (!_drone.bag.AddItem(stack))
                GameManager.Instance.ItemDropServer(stack, _drone.position, Vector3.zero, _drone.entityId);
        }
    }
}
