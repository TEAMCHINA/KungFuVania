using UnityEngine;

namespace KungFuVania.Player.Combat
{
    // Shared by the dash-forward/dash-back/dodge-roll trio — same mechanic throughout (constant
    // speed for totalDuration, i-frame window, wall-aware movement), only distance and direction
    // differ per instance. See PlayerController's forwardDashDistance/backDashDistance/
    // dodgeDistance for the tunable distances.
    public class DodgeState : ICombatState
    {
        private readonly PlayerCombatStateMachine machine;
        private readonly float totalDuration;
        private readonly System.Func<float> getDistance;
        private readonly bool reverseDirection;
        private readonly float iFrameStart;
        private readonly float iFrameEnd;
        private readonly LayerMask obstructionMask;

        private float elapsed;
        private float direction;
        private bool iFramesActive;

        // getDistance is read fresh every FixedTick rather than captured once, so tweaking
        // PlayerController's dash/dodge distance fields in the Inspector takes effect on the next
        // dodge immediately — no need to stop and restart Play Mode to test a tuning change.
        public DodgeState(PlayerCombatStateMachine machine, float totalDuration, System.Func<float> getDistance,
            float iFrameStart, float iFrameEnd, LayerMask obstructionMask, bool reverseDirection = false)
        {
            this.machine = machine;
            this.totalDuration = totalDuration;
            this.getDistance = getDistance;
            this.iFrameStart = iFrameStart;
            this.iFrameEnd = iFrameEnd;
            this.obstructionMask = obstructionMask;
            this.reverseDirection = reverseDirection;
        }

        public void Enter()
        {
            elapsed = 0f;
            iFramesActive = false;
            var facing = machine.Controller.FacingRight ? 1f : -1f;
            direction = reverseDirection ? -facing : facing;
            machine.Controller.SetKinematic(true);
        }

        public void Tick(float deltaTime) { }

        // All movement lives here rather than in Tick: this calls Rigidbody2D.MovePosition
        // (via MoveWithoutTunneling) on a Kinematic body, which only takes effect on the next
        // physics step. Update can fire more than once per physics step at high framerates, and
        // each call computes its target from the not-yet-moved current position — so calling this
        // from Update would let later calls overwrite earlier ones instead of accumulating,
        // silently dropping most of the intended distance (confirmed live: a configured 4-unit
        // roll travelling as little as 0.5 units at high framerate). FixedUpdate guarantees exactly
        // one call per physics step.
        public void FixedTick(float fixedDeltaTime)
        {
            elapsed += fixedDeltaTime;

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

            var speed = getDistance() / totalDuration;
            MoveWithoutTunneling(speed * fixedDeltaTime * direction);

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
