using UnityEngine;

namespace KungFuVania.Combat
{
    // TEMPORARY VERIFICATION STAND-IN ONLY — proves the motion-input matcher fires an effect
    // end-to-end. Not a real ability: a real melee special needs its own authored animation/
    // hitbox pass (see GAME_PLAN.md 3l), and a ranged one needs the Projectile System, which
    // was deliberately not built this pass (see build order row 6).
    [CreateAssetMenu(fileName = "DebugLogAbilityEffect", menuName = "KungFuVania/Debug Log Ability Effect (Test Only)")]
    public class DebugLogAbilityEffectSO : AbilityEffectSO
    {
        public override void Execute(GameObject caster)
        {
            Debug.Log($"[MotionInput] Special fired by {caster.name}");
        }
    }
}
