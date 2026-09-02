using KungFuVania.Core;
using KungFuVania.Player;

namespace KungFuVania.Player.Locomotion
{
    public class FallState : ILocomotionState
    {
        private readonly PlayerStateMachine machine;
        private PlayerController Controller => machine.Controller;

        public FallState(PlayerStateMachine machine)
        {
            this.machine = machine;
        }

        public void Enter() { }

        public void Tick(float deltaTime)
        {
            var horizontal = Controller.MoveInput.x;
            var direction = horizontal > 0f ? 1f : horizontal < 0f ? -1f : 0f;
            Controller.SetMoveIntent(direction, Controller.IsRunning);

            if (!Controller.IsGrounded()) return;

            if (direction == 0f)
                machine.ChangeState("IDLE");
            else if (Controller.IsRunning)
                machine.ChangeState("RUN");
            else
                machine.ChangeState("WALK");
        }

        public void Exit()
        {
            EventBus.Publish(new OnPlayerLanded());
        }
    }
}
