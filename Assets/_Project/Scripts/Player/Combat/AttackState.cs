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

        public AttackState(PlayerCombatStateMachine machine, HitboxDataSO hitboxData, string animStateId)
        {
            this.machine = machine;
            this.hitboxData = hitboxData;
            this.animStateId = animStateId;
        }

        public void Enter()
        {
            machine.HitboxController.activeHitboxData = hitboxData;
        }

        public void Tick(float deltaTime)
        {
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
