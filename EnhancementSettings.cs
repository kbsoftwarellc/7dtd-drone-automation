using UnityEngine;

namespace DroneAutomation
{
    /// <summary>
    /// Overclock meta-module tunables (mod/droneautomation.xml). It cuts every installed core's
    /// per-action time; the installed Quality scales the cut from a Q1 to a Q6 amount. Values are
    /// action-time multipliers in (0,1]: lower = faster, so Q6 is the smaller (stronger) number.
    /// </summary>
    public sealed class OverclockSettings
    {
        public float Q1TimeMult = 0.85f;
        public float Q6TimeMult = 0.5f;

        public void Clamp()
        {
            ClampFrac(ref Q1TimeMult);
            ClampFrac(ref Q6TimeMult);
            // Q6 must be at least as strong (small) as Q1, so higher quality never means slower.
            if (Q6TimeMult > Q1TimeMult) Q6TimeMult = Q1TimeMult;
        }

        /// <summary>Quality-scaled action-time multiplier (Q1..Q6 -> Q1TimeMult..Q6TimeMult).</summary>
        public float SpeedMult(int _quality) => Mathf.Lerp(Q1TimeMult, Q6TimeMult, QualityScale.T(_quality));

        private static void ClampFrac(ref float _v)
        {
            if (_v > 1f) _v = 1f;
            if (_v < 0.05f) _v = 0.05f;
        }
    }

    /// <summary>
    /// Wide-Band Antenna meta-module tunables (mod/droneautomation.xml). It widens every installed
    /// core's reach; the installed Quality scales the gain. Values are reach multipliers &gt;=1:
    /// higher = farther, so Q6 is the larger (stronger) number.
    /// </summary>
    public sealed class AntennaSettings
    {
        public float Q1ReachMult = 1.15f;
        public float Q6ReachMult = 1.6f;

        public void Clamp()
        {
            if (Q1ReachMult < 1f) Q1ReachMult = 1f;
            if (Q6ReachMult < 1f) Q6ReachMult = 1f;
            // Q6 must be at least as strong (large) as Q1.
            if (Q6ReachMult < Q1ReachMult) Q6ReachMult = Q1ReachMult;
        }

        /// <summary>Quality-scaled reach multiplier (Q1..Q6 -> Q1ReachMult..Q6ReachMult).</summary>
        public float ReachMult(int _quality) => Mathf.Lerp(Q1ReachMult, Q6ReachMult, QualityScale.T(_quality));
    }
}
