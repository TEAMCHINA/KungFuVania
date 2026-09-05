using KungFuVania.Core;
using KungFuVania.Player;

namespace KungFuVania.Player.Locomotion
{
    public class WallSlideState : ILocomotionState
    {
        private readonly PlayerStateMachine machine;
        private PlayerController Controller => machine.Controller;

        public WallSlideState(PlayerStateMachine machine)
        {
            this.machine = machine;
        }

        public void Enter()
        {
            Controller.SetMoveIntent(0f, false);
        }

        public void Tick(float deltaTime)
        {
            var horizontal = Controller.MoveInput.x;
            var direction = horizontal > 0f ? 1f : horizontal < 0f ? -1f : 0f;

            if (Controller.IsGrounded())
            {
                EventBus.Publish(new OnPlayerLanded());
                machine.ChangeState(direction == 0f ? "IDLE" : Controller.IsRunning ? "RUN" : "WALK");
                return;
            }

            if (direction == 0f || !Controller.IsTouchingWall(direction))
            {
                machine.ChangeState("FALL");
                return;
            }

            Controller.ClampFallSpeed(Controller.WallSlideSpeed);
        }

        public void Exit() { }
    }
}
