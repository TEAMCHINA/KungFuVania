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
        // spanned. 'skippedEntryCount' is how many interior entries had to be passed over
        // (across every step) to complete the match. Callers use lookbackSpan to check against
        // the pattern's own sequenceWindow (below); both feed tie-breaking between multiple
        // patterns that match the same history (see TryFindBestMatch) — skip count first, span
        // only as the tiebreaker, not the other way around (see that method's doc comment for why).
        public static bool TryMatch(MotionZoneRingBuffer history, MotionPattern pattern, out float lookbackSpan, out int skippedEntryCount)
        {
            lookbackSpan = 0f;
            skippedEntryCount = 0;
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

                    skippedEntryCount++;
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
        // inside HCF's tail — see GAME_PLAN.md 3l): fewest skipped interior entries wins first;
        // only on a skip-count tie does the largest 'lookbackSpan' win. Two illustrative cases,
        // not one, and the order of these two criteria isn't interchangeable:
        //   - A full, clean HCF and the QCF sitting inside its tail both match with ZERO skips —
        //     a genuine tie on skip count — so it falls through to span, and HCF's is larger
        //     simply because it's a longer, fully deliberate motion. HCF wins: a player who
        //     performs the more committed motion shouldn't lose it to the simpler one embedded
        //     inside it.
        //   - The original bug-1 repro (a synthetic DP shaped [6]->[2,3]->[3,6] sharing a loadout
        //     with QCF, triggered by walking forward as setup and then throwing a real QCF) is
        //     NOT that case: QCF's match needs zero skips, but DP's can only reach its own first
        //     step by skipping past QCF's own "down" entry to find an older, incidental "forward"
        //     left over from walking — one skip where QCF needs none. If span alone decided ties
        //     (largest wins, no skip check first), DP would win here too, since a skip-stretched
        //     match also happens to span more real time — silently reintroducing bug 1. Skip
        //     count has to be checked FIRST specifically because it's what tells "genuinely
        //     longer deliberate motion" apart from "coincidental subsequence collision stretched
        //     by a skip" — span alone cannot make that distinction.
        // On an exact tie in both, whichever pattern appears earlier in 'patterns' wins —
        // deterministic, but not a meaningful design decision, just a documented tiebreaker of
        // last resort.
        public static bool TryFindBestMatch(MotionZoneRingBuffer history, IReadOnlyList<MotionPattern> patterns, string pressedButton, out MotionPattern winner)
        {
            winner = null;
            var haveWinner = false;
            var bestSkips = int.MaxValue;
            var bestSpan = 0f;

            for (var i = 0; i < patterns.Count; i++)
            {
                var pattern = patterns[i];
                if (pattern == null || pattern.ConfirmButton != pressedButton) continue;
                if (!TryMatch(history, pattern, out var span, out var skips)) continue;

                var better = !haveWinner
                    || skips < bestSkips
                    || (skips == bestSkips && span > bestSpan);

                if (better)
                {
                    haveWinner = true;
                    bestSkips = skips;
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
