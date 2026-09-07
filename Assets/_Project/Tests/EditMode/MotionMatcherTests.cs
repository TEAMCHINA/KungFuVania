using NUnit.Framework;
using KungFuVania.MotionMatching;

namespace KungFuVania.Tests.EditMode
{
    // Pure EditMode tests for the lookback ring-buffer motion matcher — no Play Mode, no
    // MonoBehaviour, no scene required (see GAME_PLAN.md 3l "Testability"). Covers the two real
    // trie bugs this replaced, the charge-hold synthetic-entry mechanism, the HCF/QCF "falls out
    // for free" claim plus its tie-break, sequenceWindow timeout, and interior-noise tolerance.
    public class MotionMatcherTests
    {
        // Mirrors Assets/_Project/Data/Combat/MotionInput_QCF_Test.asset exactly (zones, confirm
        // button, sequenceWindow) so these tests track the one real asset in the project.
        private static MotionPattern QcfPattern(string confirmButton = "LIGHT", float sequenceWindow = 0.5f, object tag = null)
        {
            return new MotionPattern(new[]
            {
                new MotionPatternStep(new[] { 2, 3 }),
                new MotionPatternStep(new[] { 3, 6 }),
                new MotionPatternStep(new[] { 6 }),
            }, confirmButton, sequenceWindow, tag);
        }

        // The plan's synthetic repro pattern for bug 1.
        private static MotionPattern DpPattern(string confirmButton = "LIGHT", float sequenceWindow = 0.5f, object tag = null)
        {
            return new MotionPattern(new[]
            {
                new MotionPatternStep(new[] { 6 }),
                new MotionPatternStep(new[] { 2, 3 }),
                new MotionPatternStep(new[] { 3, 6 }),
            }, confirmButton, sequenceWindow, tag);
        }

        // Mirrors GAME_PLAN.md 3l's HCB ([6,3]->[3,2]->[2,1]->[1,4]->[4]), reversed into HCF.
        // Its own tail three steps are byte-for-byte QCF's three steps.
        private static MotionPattern HcfPattern(string confirmButton = "LIGHT", float sequenceWindow = 1f, object tag = null)
        {
            return new MotionPattern(new[]
            {
                new MotionPatternStep(new[] { 4, 1 }),
                new MotionPatternStep(new[] { 1, 2 }),
                new MotionPatternStep(new[] { 2, 3 }),
                new MotionPatternStep(new[] { 3, 6 }),
                new MotionPatternStep(new[] { 6 }),
            }, confirmButton, sequenceWindow, tag);
        }

        // --- Ring buffer sanity -------------------------------------------------------------

        [Test]
        public void RingBuffer_FromEnd_OrdersNewestFirstAndWrapsCorrectly()
        {
            var buffer = new MotionZoneRingBuffer(capacity: 3);
            buffer.Push(1, 0f);
            buffer.Push(2, 1f);
            buffer.Push(3, 2f);
            buffer.Push(4, 3f); // overwrites the oldest entry (zone 1)

            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(4, buffer.FromEnd(0).Zone);
            Assert.AreEqual(3, buffer.FromEnd(1).Zone);
            Assert.AreEqual(2, buffer.FromEnd(2).Zone);
        }

        // --- Bug 1: setup-input hijacking -----------------------------------------------------

        [Test]
        public void Bug1_SetupWalkDoesNotHijackSubsequentFireball()
        {
            // Old trie bug (GAME_PLAN.md 3l): walking forward (6) as pure movement/setup used to
            // commit the trie's one shared cursor to DP's own first step before the player had
            // even started a fireball attempt, so the fireball's own real inputs then got misread
            // as DP's remaining steps, and DP fired instead of the fireball.
            var dp = DpPattern(tag: "DP");
            var qcf = QcfPattern(tag: "QCF");

            var history = new MotionZoneRingBuffer();
            var t = 0f;
            history.Push(6, t);      // player walks forward - setup, not intended as any special
            t += 0.1f;
            history.Push(2, t);      // fireball attempt begins: down
            t += 0.1f;
            history.Push(3, t);      // down-forward
            t += 0.1f;
            history.Push(6, t);      // forward - QCF complete

            // Under independent-per-pattern scanning this history is a genuine, honest subsequence
            // match for BOTH patterns (DP's shape really is embedded in the raw input, same as it
            // was for the old trie) - this is actually a tie, resolved by span, not a case where
            // DP fails to match at all. Confirm that explicitly before checking who wins it.
            Assert.IsTrue(MotionMatcher.TryMatch(history, dp, out var dpSpan), "expected DP to also match - this scenario is a tie, not a non-match");
            Assert.IsTrue(MotionMatcher.TryMatch(history, qcf, out var qcfSpan));
            Assert.Less(qcfSpan, dpSpan, "QCF's match should need to look back less far than DP's");

            var found = MotionMatcher.TryFindBestMatch(history, new[] { dp, qcf }, "LIGHT", out var winner);

            Assert.IsTrue(found);
            Assert.AreEqual("QCF", winner.Tag, "QCF should win the tie - see MotionMatcher.TryFindBestMatch's tie-break doc comment");
        }

        // --- Bug 2: post-completion wandering --------------------------------------------------

        [Test]
        public void Bug2_WanderingAwayAfterCompletionPreventsFiring()
        {
            var qcf = QcfPattern();
            var history = new MotionZoneRingBuffer();
            var t = 0f;
            history.Push(2, t); t += 0.1f;
            history.Push(3, t); t += 0.1f;
            history.Push(6, t); t += 0.1f; // QCF complete here
            history.Push(4, t);            // player wanders away afterward - new tail, unrelated zone

            Assert.IsFalse(MotionMatcher.TryMatch(history, qcf, out _), "tail no longer satisfies QCF's last step after wandering away");
        }

        [Test]
        public void Bug2_ConfirmingImmediatelyAfterCompletionStillFires()
        {
            var qcf = QcfPattern();
            var history = new MotionZoneRingBuffer();
            var t = 0f;
            history.Push(2, t); t += 0.1f;
            history.Push(3, t); t += 0.1f;
            history.Push(6, t); // QCF complete, tail = 6, nothing after it

            Assert.IsTrue(MotionMatcher.TryMatch(history, qcf, out _));
        }

        // --- Charge steps -----------------------------------------------------------------------

        [Test]
        public void Charge_NotSatisfiedBeforeMinHoldDuration()
        {
            var tracker = new ChargeHoldTracker(new[] { 1, 4, 7 }, 1.0f);

            Assert.IsFalse(tracker.Tick(4, 0.5f));
            Assert.IsFalse(tracker.Satisfied);
            Assert.IsFalse(tracker.Tick(4, 0.4f)); // total 0.9s - still short of 1.0s
            Assert.IsFalse(tracker.Satisfied);
        }

        [Test]
        public void Charge_SatisfiedExactlyOnceThresholdIsCrossed()
        {
            var tracker = new ChargeHoldTracker(new[] { 1, 4, 7 }, 1.0f);

            tracker.Tick(4, 0.9f);
            var justSatisfied = tracker.Tick(4, 0.2f); // crosses 1.0s total on this tick

            Assert.IsTrue(justSatisfied);
            Assert.IsTrue(tracker.Satisfied);
        }

        [Test]
        public void Charge_BriefDriftWithinGroupDoesNotResetProgress()
        {
            var tracker = new ChargeHoldTracker(new[] { 1, 4, 7 }, 1.0f);

            tracker.Tick(4, 0.5f); // straight back
            tracker.Tick(1, 0.4f); // drifts to down-back - still in group, should keep accumulating
            var satisfied = tracker.Tick(7, 0.2f); // up-back - total 1.1s

            Assert.IsTrue(satisfied);
        }

        [Test]
        public void Charge_LeavingGroupEntirelyResetsProgress()
        {
            var tracker = new ChargeHoldTracker(new[] { 1, 4, 7 }, 1.0f);

            tracker.Tick(4, 0.9f);
            tracker.Tick(5, 0.1f); // neutral - leaves the group entirely, resets progress
            var satisfied = tracker.Tick(4, 0.2f); // only 0.2s accumulated since the reset

            Assert.IsFalse(satisfied);
            Assert.IsFalse(tracker.Satisfied);
        }

        [Test]
        public void Charge_StepOnlyMatchableViaSyntheticSatisfactionEntry()
        {
            // CB->F ("Sonic Boom" shape): hold back [1,4,7] >= 1.0s, then flick forward [6,3,9].
            // sequenceWindow is deliberately generous (5s) so this test isolates the
            // IsChargeSatisfaction gate itself, not an incidental window-size rejection.
            var pattern = new MotionPattern(new[]
            {
                new MotionPatternStep(new[] { 1, 4, 7 }, 1.0f),
                new MotionPatternStep(new[] { 6, 3, 9 }),
            }, "HEAVY", 5f);

            var history = new MotionZoneRingBuffer();
            history.Push(4, 0f);    // plain entry: player just pressed back, hold not yet satisfied
            history.Push(6, 0.05f); // flicks forward almost immediately - too soon for a real charge

            Assert.IsFalse(MotionMatcher.TryMatch(history, pattern, out _),
                "a plain (non-satisfaction) entry in the charge zone must not satisfy a charge step, however generous the window is");

            history.Push(4, 5f, isChargeSatisfaction: true); // the adapter's synthetic push once the hold actually completes
            history.Push(6, 5.1f);                            // NOW flicks forward

            Assert.IsTrue(MotionMatcher.TryMatch(history, pattern, out _));
        }

        // --- HCF also satisfies QCF, plus the tie-break -----------------------------------------

        [Test]
        public void FullHcf_AlsoSatisfiesQcf_AndQcfWinsTheTieBreak()
        {
            var hcf = HcfPattern(tag: "HCF");
            var qcf = QcfPattern(sequenceWindow: 1f, tag: "QCF");

            var history = new MotionZoneRingBuffer();
            history.Push(4, 0f);
            history.Push(1, 0.1f);
            history.Push(2, 0.2f);
            history.Push(3, 0.3f);
            history.Push(6, 0.4f);

            // Both independently match the same full-HCF history - QCF's zones sit inside HCF's
            // tail, so this "falls out" of the backward scan rather than needing a special case.
            Assert.IsTrue(MotionMatcher.TryMatch(history, hcf, out var hcfSpan));
            Assert.IsTrue(MotionMatcher.TryMatch(history, qcf, out var qcfSpan));
            Assert.Less(qcfSpan, hcfSpan);

            var found = MotionMatcher.TryFindBestMatch(history, new[] { hcf, qcf }, "LIGHT", out var winner);

            Assert.IsTrue(found);
            Assert.AreEqual("QCF", winner.Tag);
        }

        // --- sequenceWindow ----------------------------------------------------------------------

        [Test]
        public void SequenceWindow_TooSlowAttemptIsInvalidated()
        {
            var qcf = QcfPattern(sequenceWindow: 0.5f);
            var history = new MotionZoneRingBuffer();
            history.Push(2, 0f);
            history.Push(3, 0.1f);
            history.Push(6, 0.7f); // whole span 0.7s > 0.5s window

            Assert.IsFalse(MotionMatcher.TryMatch(history, qcf, out _));
        }

        [Test]
        public void SequenceWindow_WithinWindowStillMatches()
        {
            var qcf = QcfPattern(sequenceWindow: 0.5f);
            var history = new MotionZoneRingBuffer();
            history.Push(2, 0f);
            history.Push(3, 0.1f);
            history.Push(6, 0.4f); // whole span 0.4s <= 0.5s window

            Assert.IsTrue(MotionMatcher.TryMatch(history, qcf, out _));
        }

        // --- Interior noise tolerance --------------------------------------------------------

        [Test]
        public void InteriorNoise_BetweenRequiredStepsDoesNotBreakTheMatch()
        {
            var qcf = QcfPattern();
            var history = new MotionZoneRingBuffer();
            history.Push(2, 0f);    // step0
            history.Push(8, 0.1f);  // irrelevant wobble (up) - not part of QCF at all
            history.Push(3, 0.2f);  // step1
            history.Push(9, 0.25f); // more noise
            history.Push(6, 0.3f);  // step2 (tail)

            Assert.IsTrue(MotionMatcher.TryMatch(history, qcf, out _));
        }
    }
}
