using System;
using UnityEngine;

namespace KungFuVania.Combat
{
    // Fighting-game-style directional sequence — see GAME_PLAN.md 3l. Authored facing-relative:
    // zone 6 always means "toward the opponent", mirrored at the stick-to-zone mapping stage
    // (StickInputConfigSO), never here.
    [CreateAssetMenu(fileName = "MotionInputTrigger", menuName = "KungFuVania/Motion Input Trigger")]
    public class MotionInputTriggerSO : TriggerSO
    {
        [Serializable]
        public struct MotionStep
        {
            // Any zone in this array satisfies the step — e.g. [1,4,7] means "any back direction".
            public int[] zones;
            // 0 = plain transition (pass-through); > 0 = charge step, must be held this long.
            public float minHoldDuration;
        }

        public MotionStep[] sequence;

        // Deviation from the doc's InputAction sketch: a plain string ("LIGHT"/"HEAVY"), matching
        // the attack-identity convention InputBuffer/PlayerController.ResolveAttackState already
        // use everywhere else — a live InputAction reference doesn't serialize cleanly onto an SO
        // asset, and this keeps confirm-button comparisons a plain string equality check.
        public string confirmButton;

        // Max seconds for the WHOLE sequence, first step to last — not per-step. See
        // MotionInputDetector for why this stays a single short window regardless of step count.
        public float sequenceWindow = 1f;
    }
}
