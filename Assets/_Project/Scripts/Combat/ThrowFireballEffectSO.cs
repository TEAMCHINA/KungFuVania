using UnityEngine;
using KungFuVania.Player;
using KungFuVania.Player.Combat;

namespace KungFuVania.Combat
{
    // Visual + projectile spawn, all self-contained — every piece of fireball-specific
    // configuration (which clip, which frame it spawns on, which prefab/hitbox/offset) lives here
    // on the effect itself, not on PlayerCombatStateMachine. That's the point: a second projectile
    // ability later is a second AbilityEffectSO instance with its own data, not new fields and a
    // new dedicated state class bolted onto the combat state machine.
    //
    // Fired directly by MotionInputDetector the instant a motion+button completes, same as every
    // other matched special (never touches InputBuffer). TryEnterAbilityState failing (player
    // mid-something-else) is left to fail silently, same as a mistimed normal attack press
    // elsewhere in this codebase — not worth a special case.
    [CreateAssetMenu(fileName = "ThrowFireballEffect", menuName = "KungFuVania/Throw Fireball Effect")]
    public class ThrowFireballEffectSO : AbilityEffectSO
    {
        private const string AnimStateId = "THROW_FIREBALL";

        [SerializeField] private AnimationClip clip;
        // 0-indexed -- 2 is the 3rd frame. Read against the clip's own live frameRate/length in
        // AbilityAnimationState, not a baked Animation Event timestamp, so retiming the clip later
        // just works on the next play with no re-authoring step.
        [SerializeField] private int spawnFrameIndex = 2;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private HitboxDataSO projectileHitboxData;
        // Roughly chest height, forward of the pivot -- not pixel-verified against the throw pose,
        // just a reasonable starting offset. Checked against TrainingDummy's actual hurtbox bounds
        // (root at (3, -0.12), body hurtbox spanning y=[-0.12, 1.88]): 0.5 lands comfortably inside
        // that range rather than grazing its very top edge.
        [SerializeField] private Vector2 spawnOffset = new Vector2(0.5f, 0.5f);

        // Not serialized -- ProjectileManager is a scene instance, and a project asset (this SO)
        // can't hold a persistent reference to one. Looked up once per cast instead; that's a rare
        // event (a player manually inputting a motion+button), not a per-frame cost worth caching
        // a scene reference for.
        public override void Execute(GameObject caster)
        {
            var machine = caster.GetComponent<PlayerCombatStateMachine>();
            if (machine == null) return;

            var state = new AbilityAnimationState(machine, AnimStateId, clip, spawnFrameIndex, () => SpawnProjectile(caster));
            machine.TryEnterAbilityState(AnimStateId, state);
        }

        private void SpawnProjectile(GameObject caster)
        {
            if (projectilePrefab == null) return;

            var projectileManager = Object.FindAnyObjectByType<ProjectileManager>();
            if (projectileManager == null) return;

            var controller = caster.GetComponent<PlayerController>();
            if (controller == null) return;

            var facingSign = controller.FacingRight ? 1f : -1f;
            var origin = controller.GetPosition() + new Vector2(spawnOffset.x * facingSign, spawnOffset.y);
            var direction = new Vector2(facingSign, 0f);

            projectileManager.Spawn(projectilePrefab, origin, direction, projectileHitboxData, caster.transform);
        }
    }
}
