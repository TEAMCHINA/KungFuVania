using System.Collections.Generic;
using UnityEngine;

namespace KungFuVania.Combat
{
    // Two tiers per GAME_PLAN.md 3l: an unbounded owned pool (everything ever unlocked) and a
    // small, player-curated active loadout (the only thing MotionInputDetector ever reads). No
    // persistence yet — no save system exists in this codebase — so both lists are in-memory/
    // Inspector-configured for now, same precedent as PlayerController's jumpCharges/maxWallJumps.
    public class SkillLoadout : MonoBehaviour
    {
        [SerializeField] private List<TriggerEffectSO> ownedPool = new();
        [SerializeField] private int maxActiveSlots = 4;
        [SerializeField] private List<TriggerEffectSO> activeLoadout = new();

        public IReadOnlyList<TriggerEffectSO> OwnedPool => ownedPool;
        public IReadOnlyList<TriggerEffectSO> ActiveLoadout => activeLoadout;

        // The only thing MotionInputDetector ever looks at — filtered to entries whose trigger
        // is a MotionInputTriggerSO, per GAME_PLAN.md 3l (other trigger types, if any exist, are
        // irrelevant to it).
        public IEnumerable<TriggerEffectSO> ActiveMotionInputs()
        {
            foreach (var entry in activeLoadout)
                if (entry != null && entry.trigger is MotionInputTriggerSO)
                    yield return entry;
        }

        public bool TryActivate(TriggerEffectSO skill)
        {
            if (skill == null || activeLoadout.Contains(skill)) return false;
            if (activeLoadout.Count >= maxActiveSlots) return false;

            activeLoadout.Add(skill);
            return true;
        }

        public void Deactivate(TriggerEffectSO skill) => activeLoadout.Remove(skill);
    }
}
