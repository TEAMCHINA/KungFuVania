using UnityEngine;
using KungFuVania.Player;

namespace KungFuVania.Player.Locomotion
{
    public class JumpState : ILocomotionState
    {
        private readonly PlayerStateMachine machine;
        private PlayerController Controller => machine.Controller;

        public JumpState(PlayerStateMachine machine)
        {
            this.machine = machine;
        }

        public void Enter()
        {
            Controller.ApplyImpulse(Vector2.up * Controller.JumpImpulseForce);
        }

        public void Tick(float deltaTime)
        {
            var horizontal = Controller.MoveInput.x;
            var direction = horizontal > 0f ? 1f : horizontal < 0f ? -1f : 0f;
            Controller.SetMoveIntent(direction, Controller.IsRunning);

            if (Controller.GetVelocity().y <= 0f)
                machine.ChangeState("FALL");
        }

        public void Exit() { }

        public void NotifyJumpReleased()
        {
            var velocity = Controller.GetVelocity();
            if (velocity.y > 0f)
                Controller.ApplyImpulse(Vector2.up * (velocity.y * (Controller.JumpCutMultiplier - 1f)));
        }
    }
}
