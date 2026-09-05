using KungFuVania.Player;

namespace KungFuVania.Player.Locomotion
{
    public class IdleState : ILocomotionState
    {
        private readonly PlayerStateMachine machine;
        private PlayerController Controller => machine.Controller;

        public IdleState(PlayerStateMachine machine)
        {
            this.machine = machine;
        }

        public void Enter()
        {
            Controller.SetMoveIntent(0f, false);
        }

        public void Tick(float deltaTime)
        {
            if (!Controller.IsGrounded())
            {
                machine.ChangeState("FALL");
                return;
            }

            if (Controller.MoveInput.y < 0f)
            {
                machine.ChangeState("CROUCH");
                return;
            }

            if (Controller.MoveInput.x != 0f)
                machine.ChangeState("WALK");
        }

        public void Exit() { }
    }
}
