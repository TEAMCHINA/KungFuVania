using System.Collections.Generic;
using UnityEngine;
using KungFuVania.Combat;
using KungFuVania.Input;
using KungFuVania.MotionMatching;

namespace KungFuVania.Player
{
    // Real-time adapter over the pure lookback matcher in KungFuVania.MotionMatching — see
    // GAME_PLAN.md 3l "MotionInputDetector" and "planned refactor: trie -> lookback ring buffer"
    // for the full design/history. This class owns everything Unity-flavored (real Time.time,
    // the active skill loadout, firing the matched TriggerEffectSO's effect); the actual "does
    // this history satisfy this pattern" logic has zero dependency on this class and lives in the
    // separate KungFuVania.MotionMatching assembly instead, so it can be unit-tested without Play
    // Mode (Assets/_Project/Tests/EditMode/MotionMatcherTests.cs).
    //
    // Evaluates unconditionally, every frame, regardless of PlayerController.LockFacingActive —
    // forward-biased motions (QCF, DP, ...) never press away from facing so they can never
    // trigger a flip that would corrupt them, and don't need Lock Facing held at all; only
    // back-crossing motions (HCB, HCF, 360s) are actually at risk of that, and for those it's on
    // the player to hold Lock Facing themselves — the detector doesn't know or enforce which
    // patterns "need" it, it's a natural consequence of whether a given attempt's own zones would
    // trigger a facing flip, not something tracked here.
    [RequireComponent(typeof(PlayerController))]
    public class MotionInputDetector : MonoBehaviour
    {
        [SerializeField] private StickInputConfigSO stickConfig;
        [SerializeField] private SkillLoadout skillLoadout;

        private PlayerController controller;

        // The shared history every registered pattern is independently checked against — no
        // per-pattern cursor, which is what fixes the old trie's bug 1 (a shared cursor letting
        // one pattern's walk "steal" another's inputs). See MotionMatcher for the actual scan.
        private MotionZoneRingBuffer history;
        private readonly List<MotionPattern> patterns = new();

        // One tracker per charge step across the whole active loadout (no dedup across patterns
        // that happen to share an identical charge requirement — not worth the bookkeeping for a
        // feature no real asset exercises yet, see GAME_PLAN.md 3l).
        private readonly List<ChargeHoldTracker> chargeTrackers = new();

        private int lastZone = -1;

        private void Awake() => controller = GetComponent<PlayerController>();

        private void Start() => BuildPatterns();

        // Rebuilt on the active loadout changing would normally hook in here too — no menu/
        // loadout-editing UI exists yet, so build-once on Start covers the current scope.
        private void BuildPatterns()
        {
            history = new MotionZoneRingBuffer();
            patterns.Clear();
            chargeTrackers.Clear();
            lastZone = -1;

            if (skillLoadout == null) return;

            foreach (var entry in skillLoadout.ActiveMotionInputs())
            {
                var motionTrigger = (MotionInputTriggerSO)entry.trigger;
                var steps = new MotionPatternStep[motionTrigger.sequence.Length];

                for (var i = 0; i < motionTrigger.sequence.Length; i++)
                {
                    var step = motionTrigger.sequence[i];
                    steps[i] = new MotionPatternStep(step.zones, step.minHoldDuration);

                    if (step.minHoldDuration > 0f)
                        chargeTrackers.Add(new ChargeHoldTracker(step.zones, step.minHoldDuration));
                }

                // Tag = the originating TriggerEffectSO, so a winning match can fire it back —
                // MotionMatching has no idea what a TriggerEffectSO is (see GAME_PLAN.md 3l
                // "Testability"), it just round-trips this opaque handle.
                patterns.Add(new MotionPattern(steps, motionTrigger.confirmButton, motionTrigger.sequenceWindow, entry));
            }
        }

        private void Update()
        {
            var zone = stickConfig != null ? stickConfig.ComputeZone(controller.MoveInput, controller.FacingRight) : 5;

            TickChargeTrackers(zone);

            if (zone != lastZone)
            {
                history.Push(zone, Time.time);
                lastZone = zone;
            }
        }

        // Ticks every active charge requirement every frame (not just on zone change) — a held,
        // unchanging direction never produces its own zone-change event, so without this a charge
        // step's hold could never be observed at all. The instant any one of them first becomes
        // satisfied, push a synthetic entry into the SAME shared history at the real current
        // time, flagged so only a charge step can be satisfied by it (see
        // MotionZoneEvent.IsChargeSatisfaction for why that flag is load-bearing).
        private void TickChargeTrackers(int zone)
        {
            for (var i = 0; i < chargeTrackers.Count; i++)
            {
                if (chargeTrackers[i].Tick(zone, Time.deltaTime))
                    history.Push(zone, Time.time, isChargeSatisfaction: true);
            }
        }

        // Called from PlayerController's attack-button handling, using history as of the press.
        // Checks every registered pattern whose confirmButton matches this press independently
        // against the same shared history (see MotionMatcher.TryFindBestMatch for the tie-break
        // used when more than one matches at once) and fires the winner's effect, if any. False
        // falls through to a normal buffered attack.
        public bool TryFireCompletedMotion(string attackAction)
        {
            if (history == null) return false;
            if (!MotionMatcher.TryFindBestMatch(history, patterns, attackAction, out var winner)) return false;

            var entry = (TriggerEffectSO)winner.Tag;
            entry.effect.Execute(gameObject);
            return true;
        }
    }
}
