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

        // Sweeps the player's own full body box (not a single-height raycast from its leading
        // edge, the original approach — see below) over this tick's delta distance and clamps to
        // whatever it hits, so a dodge into a wall/platform edge stops there instead of tunneling
        // through it (Kinematic MovePosition performs no collision response on its own).
        //
        // A single raycast at the body's own vertical center used to do this instead, and missed
        // short obstacles entirely: LeftWall (tall, roof-to-floor) was always caught, but
        // LeftPlatform (top edge well below the player's chest-height center) sat completely
        // outside that one ray's line, so a dodge toward it went through with zero clamping —
        // confirmed live (a -1.5 delta aimed squarely at LeftPlatform came back uncapped). Same
        // bug, same fix as NpcBlocker.MoveBy (see its comment for the original repro) — a BoxCast
        // built from BodyHalfWidth/BodyHalfHeight checks the whole vertical extent at once, so a
        // short obstacle only has to overlap any part of that height, not one exact line. The
        // capsule collider's true shape is a little more generous at the rounded top/bottom
        // corners than this bounding box, which only matters at the very top/bottom few
        // hundredths of a unit — not a correctness concern for wall-obstruction detection, so
        // BoxCast (matching NpcBlocker) rather than a shape-perfect CapsuleCast.
        // Vertical inset applied to the swept box, below. Not tunable per-instance — it only
        // exists to clear the player's own resting contact with the ground, not to express any
        // real gameplay footprint, so one project-wide constant is correct, not a compromise.
        private const float GroundContactSkin = 0.1f;

        private void MoveWithoutTunneling(float delta)
        {
            if (delta == 0f) return;

            var controller = machine.Controller;
            var castDirection = delta > 0f ? Vector2.right : Vector2.left;
            var center = controller.GetBodyCenter();
            var distance = Mathf.Abs(delta);

            // Insetting vertically keeps the swept box from touching the ground the player is
            // already standing on (Physics2D.queriesStartInColliders is true project-wide) --
            // BoxCast reports an already-touching collider as a distance-0 hit on every call,
            // which clamped ALL movement to zero regardless of direction or any actual wall,
            // caught live: even a push with nothing whatsoever in its path returned a fully
            // clamped distance before this inset. Horizontal size is untouched -- the ground
            // only ever touches vertically.
            var castSize = new Vector2(controller.BodyHalfWidth * 2f,
                Mathf.Max(0.01f, controller.BodyHalfHeight * 2f - GroundContactSkin));

            var hit = Physics2D.BoxCast(center, castSize, 0f, castDirection, distance, obstructionMask);
            if (hit.collider != null)
                distance = Mathf.Max(0f, hit.distance - 0.01f);

            controller.MoveTo(controller.GetPosition() + castDirection * distance);
        }
    }
}
