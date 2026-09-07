namespace KungFuVania.MotionMatching
{
    // Pure equivalent of the old MotionInputDetector.TickChargeHold — tracks whether ONE charge
    // zone-group has been held continuously for its required duration. Tick-driven (the caller
    // supplies deltaTime) rather than reading any clock itself, so it behaves identically whether
    // driven from a real per-frame Update() or a synchronous unit test.
    //
    // The ring-buffer redesign has no notion of "current attempt" to hang charge state off of —
    // there's no cursor any more (see MotionMatcher) — so this tracker owns its own continuous
    // arm/fire/re-arm lifecycle instead: accumulate while inside the group, fire (return true)
    // exactly once when the threshold is first crossed, then stay silently satisfied until the
    // zone actually leaves the group entirely, at which point it re-arms for a future hold. Brief
    // drift between the group's OWN member zones (e.g. [1,4,7] wobbling between straight-back and
    // down-back) never resets progress — only leaving the group does — matching TickChargeHold's
    // original tolerance.
    public sealed class ChargeHoldTracker
    {
        private readonly int[] zones;
        private readonly float minHoldDuration;
        private float heldDuration;
        private bool satisfied;

        public ChargeHoldTracker(int[] zones, float minHoldDuration)
        {
            this.zones = zones;
            this.minHoldDuration = minHoldDuration;
        }

        public bool Satisfied => satisfied;

        // Call once per tick with the current zone and elapsed time since the last call. Returns
        // true only on the single tick where the hold first becomes satisfied — that's the
        // caller's cue to push a synthetic MotionZoneEvent (isChargeSatisfaction: true) into the
        // shared history at "now", since a held-and-unchanging zone otherwise never produces a
        // discrete entry of its own for the backward scan to find.
        public bool Tick(int zone, float deltaTime)
        {
            if (System.Array.IndexOf(zones, zone) < 0)
            {
                heldDuration = 0f;
                satisfied = false;
                return false;
            }

            if (satisfied) return false;

            heldDuration += deltaTime;
            if (heldDuration < minHoldDuration) return false;

            satisfied = true;
            return true;
        }
    }
}
