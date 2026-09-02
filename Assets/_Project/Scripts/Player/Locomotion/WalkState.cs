using UnityEngine;
using KungFuVania.Player;

namespace KungFuVania.Player.Locomotion
{
    public class WalkState : ILocomotionState
    {
        private readonly PlayerStateMachine machine;
        private PlayerController Controller => machine.Controller;

        public WalkState(PlayerStateMachine machine)
        {
            this.machine = machine;
        }

        public void Enter() { }

        public void Tick(float deltaTime)
        {
            if (!Controller.IsGrounded())
            {
                machine.ChangeState("FALL");
                return;
            }

            var horizontal = Controller.MoveInput.x;
            if (horizontal == 0f)
            {
                machine.ChangeState("IDLE");
                return;
            }

            if (Controller.IsRunning)
            {
                machine.ChangeState("RUN");
                return;
            }

            Controller.SetMoveIntent(Mathf.Sign(horizontal), false);
        }

        public void Exit() { }
    }
}
