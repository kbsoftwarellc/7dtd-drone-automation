using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// Heat map (screamer) parity for the modules that break blocks.
    ///
    /// A drone breaking a block generates NO heat in vanilla, and not because the mod skips a call:
    /// the game's own destroy sound IS emitted server-side for our Block.DamageBlock (Block ->
    /// SpawnDowngradeFX -> SpawnDestroyParticleEffect -> ParticleEffect.PlaySoundInServer ->
    /// AIDirector.OnSoundPlayedAtPosition), and then AIDirector.NotifyNoise throws it away, because
    /// the instigator we pass is the drone and EntityDrone.IsIgnoredByAI() returns true. Every drone
    /// noise is invisible to the AI director by design.
    ///
    /// So the drone gets a free pass a player does not: wrenching a whole apartment block by hand
    /// calls a screamer in a couple of minutes, and the drone doing the same work called nothing.
    /// This re-adds exactly the heat the vanilla path would have added, by looking up the same
    /// noise entry the block's own destroy sound carries in sounds.xml and notifying the director
    /// directly - the one call that does not care whether the instigator is ignored by AI.
    /// </summary>
    public static class Heat
    {
        /// <summary>
        /// Vanilla's own duration for a sound-sourced heat event (AIDirector.NotifyNoise passes this
        /// constant rather than the sound's heat_map_time).
        /// </summary>
        private const float EventDuration = 240f;

        /// <summary>
        /// Fallback strength when the destroy sound has no noise entry to read - the value vanilla's
        /// "metaldestroy" carries, since salvage targets are metal almost without exception. Only
        /// used if the sound data is missing entirely (a dedicated server that never parsed
        /// sounds.xml), so heat still lands rather than silently disappearing.
        /// </summary>
        private const float FallbackStrength = 1.42f;

        /// <summary>
        /// Adds the heat one hand-broken stage of this block would have made. <paramref name="_mult"/>
        /// is the module's HeatMultiplier: 1 = vanilla parity, 0 = no heat at all.
        /// </summary>
        public static void BlockBroken(World _world, Block _block, Vector3i _pos, float _mult)
        {
            if (_mult <= 0f || _world?.aiDirector == null || _block == null) return;

            string surface = _block.blockMaterial?.SurfaceCategory;
            if (string.IsNullOrEmpty(surface)) return;

            float strength = FallbackStrength;
            if (AIDirectorData.FindNoise(surface + "destroy", out AIDirectorData.Noise noise))
            {
                // A material whose destroy sound carries no heat (cloth, plant) makes none by hand
                // either - honour that rather than falling back to the metal value.
                strength = noise.heatMapStrength;
            }
            if (strength <= 0f) return;

            _world.aiDirector.NotifyActivity(EnumAIDirectorChunkEvent.Sound, _pos, strength * _mult, EventDuration);
        }
    }
}
