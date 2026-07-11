using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// Maps an installed module's Quality (1-6) onto its effective reach and per-action time.
    /// The configured settings values are the Q6 (top-tier) ceiling; Q1 is the craftable floor.
    /// Mods sit outside the vanilla CraftingTier system, so crafting always yields Q1 - the strong,
    /// far-reaching, fast modules only ever come from loot or the trader.
    /// </summary>
    public static class QualityScale
    {
        public const int MinQuality = 1;
        public const int MaxQuality = 6;

        /// <summary>0 at Q1, 1 at Q6. Off-range quality (e.g. an unset 0) clamps to the ends.</summary>
        public static float T(int _quality)
        {
            int q = Mathf.Clamp(_quality, MinQuality, MaxQuality);
            return (q - MinQuality) / (float)(MaxQuality - MinQuality);
        }

        /// <summary>Reach grows from _lowFrac*_max at Q1 to _max at Q6.</summary>
        public static float Reach(float _max, float _lowFrac, int _quality)
        {
            return _max * Mathf.Lerp(_lowFrac, 1f, T(_quality));
        }

        /// <summary>Action time shrinks from _timeMult*_max at Q1 to _max at Q6 (higher quality = faster).</summary>
        public static float Time(float _max, float _timeMult, int _quality)
        {
            return _max * Mathf.Lerp(_timeMult, 1f, T(_quality));
        }

        /// <summary>Clamps the two per-module knobs: reach fraction to [0,1], time multiplier to >=1.</summary>
        public static void ClampKnobs(ref float _lowQualityReach, ref float _lowQualityTimeMult)
        {
            if (_lowQualityReach < 0f) _lowQualityReach = 0f;
            if (_lowQualityReach > 1f) _lowQualityReach = 1f;
            if (_lowQualityTimeMult < 1f) _lowQualityTimeMult = 1f;
        }
    }
}
