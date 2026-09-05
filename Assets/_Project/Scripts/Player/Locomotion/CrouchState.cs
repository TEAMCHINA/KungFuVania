using KungFuVania.Player;

namespace KungFuVania.Player.Locomotion
{
    // Holding down locks horizontal movement entirely — set once on Enter and never updated
    // again while crouching, since nothing else calls SetMoveIntent in the meantime. Releases
    // back to IDLE/WALK based on current horizontal input once down is released.
    public class CrouchState : ILocomotionState
    {
        private readonly PlayerStateMachine machine;
        private PlayerController Controller => machine.Controller;

        public CrouchState(PlayerStateMachine machine)
        {
            this.machine = machine;
        }

        public void Enter() => Controller.SetMoveIntent(0f, false);

        public void Tick(float deltaTime)
        {
            if (!Controller.IsGrounded())
            {
                machine.ChangeState("FALL");
                return;
            }

            if (Controller.MoveInput.y >= 0f)
                machine.ChangeState(Controller.MoveInput.x == 0f ? "IDLE" : "WALK");
        }

        public void Exit() { }
    }
}
