namespace DroneAutomation
{
    /// <summary>
    /// A per-tick multiplier bundle layered on top of a core's quality-scaled knobs, built from the
    /// enhancement meta-modules (Overclock, Antenna) installed on the drone. Identity = no change, so
    /// a drone with no enhancement modules passes <see cref="None"/> and behaves exactly as before.
    /// </summary>
    public readonly struct DroneBoost
    {
        /// <summary>Multiplies per-action time. &lt;1 = faster (Overclock); 1 = no change.</summary>
        public readonly float SpeedMult;

        /// <summary>Multiplies reach, horizontal and vertical. &gt;1 = farther (Antenna); 1 = no change.</summary>
        public readonly float ReachMult;

        public static readonly DroneBoost None = new DroneBoost(1f, 1f);

        public DroneBoost(float _speedMult, float _reachMult)
        {
            SpeedMult = _speedMult;
            ReachMult = _reachMult;
        }
    }
}
