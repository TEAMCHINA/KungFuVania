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

        // Sets vertical velocity to an absolute value rather than adding an impulse, so every
        // jump — ground or airborne charge-based — launches the player the same amount
        // regardless of whatever vertical velocity it already had (grounded is ~0 anyway).
        // Divided by mass (impulse / mass = velocity) so gear/skills that later change the
        // player's Rigidbody2D mass raise or lower jump height without touching this code.
        public void Enter()
        {
            Controller.SetVerticalVelocity(Controller.JumpImpulseForce / Controller.Mass);
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
