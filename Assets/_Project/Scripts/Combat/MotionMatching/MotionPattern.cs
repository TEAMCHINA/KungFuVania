namespace KungFuVania.MotionMatching
{
    // Plain mirror of MotionInputTriggerSO.MotionStep (Assets/_Project/Scripts/Combat/
    // MotionInputTriggerSO.cs) — deliberately not a reference to it, since this assembly can never
    // see anything still sitting in Assembly-CSharp (see GAME_PLAN.md 3l "Testability"). The
    // adapter (MotionInputDetector) translates one into the other once, at setup.
    public readonly struct MotionPatternStep
    {
        // Any zone in this array satisfies the step — e.g. [1,4,7] means "any back direction".
        public readonly int[] Zones;

        // 0 = plain transition (pass-through); > 0 = charge step, must be held this long before
        // it can be satisfied. MotionMatcher's scan never compares this against elapsed time
        // directly — by the time a qualifying entry exists in the shared history at all, the hold
        // has already happened (see ChargeHoldTracker + MotionZoneEvent.IsChargeSatisfaction).
        public readonly float MinHoldDuration;

        public MotionPatternStep(int[] zones, float minHoldDuration = 0f)
        {
            Zones = zones;
            MinHoldDuration = minHoldDuration;
        }

        public bool IsChargeStep => MinHoldDuration > 0f;
    }

    // Plain mirror of MotionInputTriggerSO itself.
    public sealed class MotionPattern
    {
        public readonly MotionPatternStep[] Steps;
        public readonly string ConfirmButton;

        // Max seconds for the WHOLE sequence, first matched step to the most recent entry — not
        // per-step. See MotionMatcher.TryMatch.
        public readonly float SequenceWindow;

        // Intentionally opaque handle the caller attaches at build time and reads back off the
        // winning pattern after a successful TryFindBestMatch — this assembly has no idea what a
        // TriggerEffectSO is (and shouldn't need to); the adapter stashes whatever it needs to
        // fire a match with (the originating TriggerEffectSO, today).
        public readonly object Tag;

        public MotionPattern(MotionPatternStep[] steps, string confirmButton, float sequenceWindow, object tag = null)
        {
            Steps = steps;
            ConfirmButton = confirmButton;
            SequenceWindow = sequenceWindow;
            Tag = tag;
        }
    }
}
