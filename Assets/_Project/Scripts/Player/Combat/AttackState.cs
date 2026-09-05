using KungFuVania.Combat;

namespace KungFuVania.Player.Combat
{
    // Reusable for both punch (ATTACK_1) and kick (ATTACK_2) — which hitbox data and animator
    // state to use is injected per instance rather than subclassed.
    public class AttackState : ICombatState
    {
        private readonly PlayerCombatStateMachine machine;
        private readonly HitboxDataSO hitboxData;
        private readonly string animStateId;
        private readonly bool exitOnLanding;

        // exitOnLanding: for aerial attacks only (e.g. JUMP_KICK) — landing mid-clip makes
        // PlayerAnimatorDriver force-play "LANDING" (see OnPlayerLanded), so the Animator will
        // never report this clip as finished. Without this, the combat state machine would stay
        // stuck here forever since its only other exit check is the clip reaching normalizedTime
        // 1. Grounded attacks never need this — they always start out already grounded, so the
        // check would fire on their very first Tick and cancel every ground attack immediately.
        public AttackState(PlayerCombatStateMachine machine, HitboxDataSO hitboxData, string animStateId, bool exitOnLanding = false)
        {
            this.machine = machine;
            this.hitboxData = hitboxData;
            this.animStateId = animStateId;
            this.exitOnLanding = exitOnLanding;
        }

        public void Enter()
        {
            machine.HitboxController.activeHitboxData = hitboxData;
        }

        public void Tick(float deltaTime)
        {
            if (exitOnLanding && machine.Controller.IsGrounded())
            {
                machine.ChangeState("NONE");
                return;
            }

            // Keyed off the Animator's own playback rather than a separate timer so the exit
            // point can never drift out of sync with the clip's Activate/Deactivate hitbox
            // events — both are driven by the same clip time.
            var info = machine.Animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(animStateId) && info.normalizedTime >= 1f)
                machine.ChangeState("NONE");
        }

        public void Exit() { }
    }
}
