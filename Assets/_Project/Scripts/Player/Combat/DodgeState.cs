using UnityEngine;

namespace KungFuVania.Player.Combat
{
    public class DodgeState : ICombatState
    {
        private readonly PlayerCombatStateMachine machine;
        private readonly float totalDuration;
        private readonly AnimationCurve speedCurve;
        private readonly float iFrameStart;
        private readonly float iFrameEnd;
        private readonly LayerMask obstructionMask;

        private float elapsed;
        private float direction;
        private bool iFramesActive;

        public DodgeState(PlayerCombatStateMachine machine, float totalDuration, AnimationCurve speedCurve,
            float iFrameStart, float iFrameEnd, LayerMask obstructionMask)
        {
            this.machine = machine;
            this.totalDuration = totalDuration;
            this.speedCurve = speedCurve;
            this.iFrameStart = iFrameStart;
            this.iFrameEnd = iFrameEnd;
            this.obstructionMask = obstructionMask;
        }

        public void Enter()
        {
            elapsed = 0f;
            iFramesActive = false;
            direction = machine.Controller.FacingRight ? 1f : -1f;
            machine.Controller.SetKinematic(true);
        }

        public void Tick(float deltaTime)
        {
            elapsed += deltaTime;

            if (!iFramesActive && elapsed >= iFrameStart)
            {
                iFramesActive = true;
                machine.HurtboxController.SetInvulnerable(true);
            }
            if (iFramesActive && elapsed >= iFrameEnd)
            {
                iFramesActive = false;
                machine.HurtboxController.SetInvulnerable(false);
            }

            var speed = speedCurve.Evaluate(Mathf.Clamp01(elapsed / totalDuration));
            MoveWithoutTunneling(speed * deltaTime * direction);

            if (elapsed >= totalDuration)
            {
                machine.Controller.SetKinematic(false);
                machine.ChangeState("DODGE_RECOVERY");
            }
        }

        public void Exit() { }

        // Casts from the leading edge of the body collider over this tick's delta distance and
        // clamps to whatever it hits, so a dodge into a wall/platform edge stops there instead of
        // tunneling through it (Kinematic MovePosition performs no collision response on its own).
        private void MoveWithoutTunneling(float delta)
        {
            if (delta == 0f) return;

            var controller = machine.Controller;
            var castDirection = delta > 0f ? Vector2.right : Vector2.left;
            var origin = controller.GetBodyCenter() + castDirection * controller.BodyHalfWidth;
            var distance = Mathf.Abs(delta);

            var hit = Physics2D.Raycast(origin, castDirection, distance, obstructionMask);
            if (hit.collider != null)
                distance = Mathf.Max(0f, hit.distance - 0.01f);

            controller.MoveTo(controller.GetPosition() + castDirection * distance);
        }
    }
}
