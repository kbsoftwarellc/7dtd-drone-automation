namespace DroneAutomation
{
    /// <summary>
    /// Credit-based pacing shared by the block-touching modules. Banks elapsed real seconds off
    /// GameTimer and spends them against each action's cost, so a coarse or irregular tick rate
    /// still averages out, and a save/chunk reload cannot burst past MaxCatchupSeconds.
    /// </summary>
    public sealed class Pacer
    {
        private readonly float maxCatchup;
        private ulong lastTick;
        private float credit;

        public Pacer(float _maxCatchup) { maxCatchup = _maxCatchup; }

        public float Credit => credit;

        public void Accrue()
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

            if (credit > maxCatchup) credit = maxCatchup;
        }

        public bool TrySpend(float _seconds)
        {
            if (credit < _seconds) return false;
            credit -= _seconds;
            return true;
        }
    }
}
