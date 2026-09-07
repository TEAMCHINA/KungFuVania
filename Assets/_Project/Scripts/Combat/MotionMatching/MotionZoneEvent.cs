namespace KungFuVania.MotionMatching
{
    // One (zone, timestamp) sample in a motion attempt's shared history. Pushed on every real
    // zone change by whatever owns the buffer (see MotionZoneRingBuffer), and also pushed as a
    // synthetic marker by charge-hold tracking (see ChargeHoldTracker) once a charge requirement's
    // hold duration is first satisfied.
    //
    // Deliberately plain data with zero UnityEngine dependency anywhere in this assembly (see
    // GAME_PLAN.md 3l "Testability") — "timestamp" is whatever clock the caller uses (real
    // Time.time in the game, arbitrary floats in a test); nothing in this assembly ever reads a
    // clock itself.
    public readonly struct MotionZoneEvent
    {
        public readonly int Zone;
        public readonly float Timestamp;

        // True only for a synthetic marker pushed the instant a charge requirement's hold
        // duration was first satisfied — never true for a plain "the stick moved to a new zone"
        // push. MotionMatcher requires this flag on whichever entry satisfies a charge step
        // specifically (see MotionPatternStep.IsChargeStep); a plain (non-charge) step never
        // looks at it, so a synthetic entry is just as valid a plain zone marker as a real one
        // for every other pattern's non-charge steps.
        //
        // Why this can't just be "does the zone match the group": without this flag, the very
        // first plain entry created the instant the player pressed INTO the charge zone (long
        // before the hold requirement was actually met) would already satisfy the group-membership
        // test on its own — silently bypassing the hold-duration requirement whenever a pattern's
        // sequenceWindow happens to be configured longer than its own charge duration. Requiring
        // this flag closes that gap regardless of how the two are tuned relative to each other.
        public readonly bool IsChargeSatisfaction;

        public MotionZoneEvent(int zone, float timestamp, bool isChargeSatisfaction = false)
        {
            Zone = zone;
            Timestamp = timestamp;
            IsChargeSatisfaction = isChargeSatisfaction;
        }
    }
}
