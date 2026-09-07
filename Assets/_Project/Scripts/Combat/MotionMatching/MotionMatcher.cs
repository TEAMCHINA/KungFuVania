using System.Collections.Generic;

namespace KungFuVania.MotionMatching
{
    // The lookback matcher itself — see GAME_PLAN.md 3l "planned refactor: trie -> lookback ring
    // buffer" for the full design rationale and the two trie bugs this replaces. Pure function of
    // (history, pattern) in, match/no-match (+ how far back it had to look) out — no mutable state
    // of its own, so a caller can check any number of patterns against the same shared history
    // with zero risk of one interpretation stealing state from another (that shared-cursor
    // stealing was the old trie's bug 1).
    public static class MotionMatcher
    {
        // Scans 'history' backward against a single pattern. The MOST RECENT entry in history must
        // satisfy the pattern's LAST step — no skipping allowed there, unlike every earlier step —
        // which is what makes a completed-then-abandoned motion correctly stop matching the
        // instant the player's zone changes to anything else (old trie bug 2: "stay put on
        // mismatch" kept a finished motion armed indefinitely, until the whole-attempt timeout,
        // even after the player wandered away). Every step before the last is free to skip past
        // any number of non-matching "interior noise" entries while searching backward for it,
        // unbounded — today's forgiving tolerance, deliberately not capped here (see GAME_PLAN.md
        // 3l "explicitly out of scope" fuzziness axes for the future noise cap this leaves room
        // for without changing this signature).
        //
        // On success, 'lookbackSpan' is (most recent entry's timestamp) - (the earliest step's
        // matched-entry timestamp) — i.e. how much real time the whole matched sequence actually
        // spanned. Callers use this for two things: checking it against the pattern's own
        // sequenceWindow (below), and tie-breaking between multiple patterns that match the same
        // history (see TryFindBestMatch).
        public static bool TryMatch(MotionZoneRingBuffer history, MotionPattern pattern, out float lookbackSpan)
        {
            lookbackSpan = 0f;
            if (history == null || pattern?.Steps == null || pattern.Steps.Length == 0) return false;
            if (history.Count == 0) return false;

            var steps = pattern.Steps;
            var lastStepIndex = steps.Length - 1;
            var mostRecentTimestamp = history.FromEnd(0).Timestamp;
            var earliestMatchedTimestamp = mostRecentTimestamp;
            var searchCursor = 0;

            for (var stepIndex = lastStepIndex; stepIndex >= 0; stepIndex--)
            {
                var matched = false;
                while (searchCursor < history.Count)
                {
                    var candidate = history.FromEnd(searchCursor);
                    searchCursor++;

                    if (StepSatisfiedBy(steps[stepIndex], candidate))
                    {
                        matched = true;
                        earliestMatchedTimestamp = candidate.Timestamp;
                        break;
                    }

                    if (stepIndex == lastStepIndex)
                    {
                        // Tail rule: the very first (most recent) entry examined for the LAST step
                        // didn't satisfy it, and unlike every earlier step, the last step is never
                        // allowed to skip past it looking for an older one — this is bug 2's fix.
                        return false;
                    }
                }

                if (!matched) return false; // ran out of history before this step was satisfied
            }

            lookbackSpan = mostRecentTimestamp - earliestMatchedTimestamp;
            return lookbackSpan <= pattern.SequenceWindow;
        }

        // Checks every pattern in 'patterns' whose ConfirmButton matches 'pressedButton' against
        // the same shared history (never a per-pattern cursor — see TryMatch) and returns the
        // single best match, if any.
        //
        // Tie-break, for when more than one pattern matches the same press (e.g. a full HCF
        // naturally also satisfying a QCF registered in the same loadout, since QCF's zones sit
        // inside HCF's tail — see GAME_PLAN.md 3l): smallest 'lookbackSpan' wins, i.e. whichever
        // match needed to look back the least distance in time/interior-noise to complete. This
        // was an explicitly open decision in the plan ("leaning toward rewarding the shorter/more
        // specific pattern over the longer/more-committed one"); span was chosen over a raw
        // step-count comparison because step count alone doesn't actually disambiguate every real
        // case — notably the original bug-1 repro (a synthetic DP shaped [6]->[2,3]->[3,6] sharing
        // a loadout with QCF) has BOTH patterns at 3 steps, but QCF's own match only ever looks
        // back across its own 3 clean entries while DP's match has to reach past one older, skipped
        // entry to find its own first step — a strictly larger span. Rewarding the smaller span
        // picks QCF there, which is the behavior bug 1 was actually about: the fireball attempt
        // shouldn't lose to a DP shape that's only "there" by incidental subsequence embedding. On
        // an exact span tie, whichever pattern appears earlier in 'patterns' wins — deterministic,
        // but not a meaningful design decision, just a documented tiebreaker of last resort.
        public static bool TryFindBestMatch(MotionZoneRingBuffer history, IReadOnlyList<MotionPattern> patterns, string pressedButton, out MotionPattern winner)
        {
            winner = null;
            var haveWinner = false;
            var bestSpan = float.PositiveInfinity;

            for (var i = 0; i < patterns.Count; i++)
            {
                var pattern = patterns[i];
                if (pattern == null || pattern.ConfirmButton != pressedButton) continue;
                if (!TryMatch(history, pattern, out var span)) continue;

                if (!haveWinner || span < bestSpan)
                {
                    haveWinner = true;
                    bestSpan = span;
                    winner = pattern;
                }
            }

            return haveWinner;
        }

        // A step matches a candidate entry if the entry's zone is in the step's group — AND, for
        // a charge step specifically, only if the entry is itself flagged as a charge-satisfaction
        // marker (see MotionZoneEvent.IsChargeSatisfaction for why that flag has to gate this
        // rather than zone-membership alone).
        private static bool StepSatisfiedBy(MotionPatternStep step, MotionZoneEvent candidate)
        {
            if (step.IsChargeStep && !candidate.IsChargeSatisfaction) return false;
            return ZoneInGroup(candidate.Zone, step.Zones);
        }

        private static bool ZoneInGroup(int zone, int[] group)
        {
            for (var i = 0; i < group.Length; i++)
                if (group[i] == zone) return true;
            return false;
        }
    }
}
