using UnityEngine;
using KungFuVania.Player;

namespace KungFuVania.Combat
{
    // Visual-only for now — plays the THROW_FIREBALL animation via the normal combat state
    // machine, no projectile/hitbox/damage. Fired directly by MotionInputDetector the instant a
    // motion+button completes, same as every other matched special (never touches InputBuffer).
    // TryEnterState failing (player mid-something-else) is left to fail silently, same as a
    // mistimed normal attack press elsewhere in this codebase — not worth a special case.
    [CreateAssetMenu(fileName = "ThrowFireballEffect", menuName = "KungFuVania/Throw Fireball Effect")]
    public class ThrowFireballEffectSO : AbilityEffectSO
    {
        public override void Execute(GameObject caster)
        {
            caster.GetComponent<PlayerCombatStateMachine>()?.TryEnterState("THROW_FIREBALL");
        }
    }
}
