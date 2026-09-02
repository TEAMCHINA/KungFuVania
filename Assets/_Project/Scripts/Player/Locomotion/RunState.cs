using UnityEngine;
using KungFuVania.Player;

namespace KungFuVania.Player.Locomotion
{
    public class RunState : ILocomotionState
    {
        private readonly PlayerStateMachine machine;
        private PlayerController Controller => machine.Controller;
        private float runDirection;

        public RunState(PlayerStateMachine machine)
        {
            this.machine = machine;
        }

        public void Enter()
        {
            runDirection = Mathf.Sign(Controller.MoveInput.x);
        }

        public void Tick(float deltaTime)
        {
            if (!Controller.IsGrounded())
            {
                machine.ChangeState("FALL");
                return;
            }

            var horizontal = Controller.MoveInput.x;
            var sameDirection = horizontal != 0f && Mathf.Sign(horizontal) == runDirection;
            if (!sameDirection)
            {
                machine.ChangeState("WALK");
                return;
            }

            Controller.SetMoveIntent(runDirection, true);
        }

        public void Exit() { }
    }
}
