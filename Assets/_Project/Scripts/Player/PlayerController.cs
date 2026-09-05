using UnityEngine;
using KungFuVania.Input;

namespace KungFuVania.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private InputReader inputReader;
        [SerializeField] private Transform groundCheck;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float runSpeedMultiplier = 1.6f;
        [SerializeField] private float jumpImpulseForce = 12f;
        [SerializeField] private float jumpCutMultiplier = 0.5f;
        // Extra mid-air jumps beyond the initial ground jump. 0 = single jump (default, unchanged
        // feel). Bump this in the Inspector to test double/triple jump; will later be driven by
        // gear/skills (see GAME_PLAN.md 3k DoubleJumpAbility) instead of a flat manual value.
        [SerializeField] private int jumpCharges = 0;
        [SerializeField] private float doubleTapWindow = 0.25f;
        [SerializeField] private float groundCheckRadius = 0.1f;
        [SerializeField] private Transform wallCheck;
        [SerializeField] private float wallCheckDistance = 0.4f;
        [SerializeField] private float wallSlideSpeed = 2f;
        // Consecutive wall jumps off the SAME wall before landing or touching a different wall
        // is required to recharge. 0 = ability not yet acquired (default, unchanged feel).
        // Bump this in the Inspector to test wall jump; will later be driven by gear/skills,
        // same manual-testing pattern as jumpCharges.
        [SerializeField] private int maxWallJumps = 0;
        [SerializeField] private Vector2 wallJumpVelocity = new Vector2(8f, 12f);
        // Brief lockout after a wall jump during which held input doesn't override the outward
        // launch velocity — without it, still holding "into" the wall (as wall-sliding requires)
        // would cancel the horizontal kick on the very next physics step.
        [SerializeField] private float wallStickTime = 0.2f;

        private Rigidbody2D rb;
        private PlayerStateMachine stateMachine;

        private bool isGrounded;
        private float moveIntent;
        private bool runIntent;
        private int remainingJumpCharges;
        private int remainingWallJumps;
        private Collider2D lastWallTouched;
        private float wallJumpLockUntil;

        private int previousSign;
        private int pendingTapDirection;
        private float pendingTapTime;

        public Vector2 MoveInput { get; private set; }
        public bool IsRunning { get; private set; }
        public float JumpImpulseForce => jumpImpulseForce;
        public float JumpCutMultiplier => jumpCutMultiplier;
        public float Mass => rb.mass;
        public int JumpCharges
        {
            get => jumpCharges;
            set => jumpCharges = value;
        }
        public float WallSlideSpeed => wallSlideSpeed;
        public int MaxWallJumps
        {
            get => maxWallJumps;
            set => maxWallJumps = value;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            stateMachine = GetComponent<PlayerStateMachine>();
        }

        private void OnEnable()
        {
            inputReader.OnMove += HandleMove;
            inputReader.OnJump += HandleJump;
            inputReader.OnJumpCancelled += HandleJumpCancelled;
        }

        private void OnDisable()
        {
            inputReader.OnMove -= HandleMove;
            inputReader.OnJump -= HandleJump;
            inputReader.OnJumpCancelled -= HandleJumpCancelled;
        }

        private void FixedUpdate()
        {
            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            if (isGrounded)
            {
                remainingJumpCharges = jumpCharges;
                remainingWallJumps = maxWallJumps;
                lastWallTouched = null;
            }

            if (Time.time < wallJumpLockUntil) return;

            var speed = runIntent ? walkSpeed * runSpeedMultiplier : walkSpeed;
            rb.linearVelocity = new Vector2(moveIntent * speed, rb.linearVelocity.y);
        }

        private void HandleMove(Vector2 value)
        {
            MoveInput = value;

            var newSign = System.Math.Sign(value.x);
            if (newSign == 0)
            {
                IsRunning = false;
            }
            else if (newSign != previousSign)
            {
                IsRunning = newSign == pendingTapDirection && Time.time - pendingTapTime <= doubleTapWindow;
                if (!IsRunning)
                {
                    pendingTapDirection = newSign;
                    pendingTapTime = Time.time;
                }
            }

            previousSign = newSign;
        }

        private void HandleJump() => stateMachine.NotifyJumpPressed();
        private void HandleJumpCancelled() => stateMachine.NotifyJumpReleased();

        public void SetMoveIntent(float horizontalDirection, bool running)
        {
            moveIntent = horizontalDirection;
            runIntent = running;
        }

        public bool ConsumeJumpCharge()
        {
            if (remainingJumpCharges <= 0) return false;
            remainingJumpCharges--;
            return true;
        }

        public void ApplyImpulse(Vector2 force) => rb.AddForce(force, ForceMode2D.Impulse);
        public void SetVerticalVelocity(float verticalVelocity) => rb.linearVelocity = new Vector2(rb.linearVelocity.x, verticalVelocity);

        public void ClampFallSpeed(float maxFallSpeed)
        {
            if (rb.linearVelocity.y < -maxFallSpeed)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, -maxFallSpeed);
        }

        public Collider2D GetTouchedWall(float direction)
        {
            if (direction == 0f) return null;
            var dir = direction > 0f ? Vector2.right : Vector2.left;
            return Physics2D.Raycast(wallCheck.position, dir, wallCheckDistance, groundLayer).collider;
        }

        public bool IsTouchingWall(float direction) => GetTouchedWall(direction) != null;

        // Restores the wall-jump charge only when the wall itself is new (a different collider,
        // or none touched since the last landing) — repeatedly re-clinging to the SAME wall
        // without landing or touching another wall does not refill it. See maxWallJumps.
        public void NotifyWallContact(Collider2D wall)
        {
            if (wall == null || wall == lastWallTouched) return;

            remainingWallJumps = maxWallJumps;
            lastWallTouched = wall;
        }

        public bool TryWallJump()
        {
            if (remainingWallJumps <= 0) return false;

            var horizontal = MoveInput.x;
            var wallDirection = horizontal > 0f ? 1f : horizontal < 0f ? -1f : 0f;
            if (wallDirection == 0f) return false;

            remainingWallJumps--;
            rb.linearVelocity = new Vector2(-wallDirection * wallJumpVelocity.x / Mass, wallJumpVelocity.y / Mass);
            wallJumpLockUntil = Time.time + wallStickTime;
            return true;
        }

        public void ForceLocomotionState(string stateId) => stateMachine.ForceState(stateId);
        public Vector2 GetVelocity() => rb.linearVelocity;
        public bool IsGrounded() => isGrounded;
        public bool IsAirborne() => !IsGrounded();
    }
}
