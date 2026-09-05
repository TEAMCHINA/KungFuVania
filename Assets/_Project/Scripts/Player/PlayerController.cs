using UnityEngine;
using KungFuVania.Input;
using KungFuVania.Actors;

namespace KungFuVania.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private InputReader inputReader;
        [SerializeField] private Transform groundCheck;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private LayerMask npcBodyLayer;
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
        private CapsuleCollider2D capsule;
        private PlayerStateMachine stateMachine;
        private PlayerCombatStateMachine combatStateMachine;

        private bool isGrounded;
        private bool physicsSuspended;
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
        public bool FacingRight { get; private set; } = true;
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
            capsule = GetComponent<CapsuleCollider2D>();
            stateMachine = GetComponent<PlayerStateMachine>();
            combatStateMachine = GetComponent<PlayerCombatStateMachine>();
        }

        private void OnEnable()
        {
            inputReader.OnMove += HandleMove;
            inputReader.OnJump += HandleJump;
            inputReader.OnJumpCancelled += HandleJumpCancelled;
            inputReader.OnAttackLight += HandleAttackLight;
            inputReader.OnAttackHeavy += HandleAttackHeavy;
            inputReader.OnDodge += HandleDodge;
        }

        private void OnDisable()
        {
            inputReader.OnMove -= HandleMove;
            inputReader.OnJump -= HandleJump;
            inputReader.OnJumpCancelled -= HandleJumpCancelled;
            inputReader.OnAttackLight -= HandleAttackLight;
            inputReader.OnAttackHeavy -= HandleAttackHeavy;
            inputReader.OnDodge -= HandleDodge;
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

            if (physicsSuspended) return;
            if (Time.time < wallJumpLockUntil) return;

            var speed = runIntent ? walkSpeed * runSpeedMultiplier : walkSpeed;

            if (TryResolveNpcPush(speed, out var pushVelocityX))
            {
                rb.linearVelocity = new Vector2(pushVelocityX, rb.linearVelocity.y);
                return;
            }

            rb.linearVelocity = new Vector2(moveIntent * speed, rb.linearVelocity.y);
        }

        // Mass contest against a blocking NpcBlocker: heavier side "wins" and keeps moving at its
        // own intended speed, lighter side is dragged along — both end up at winnerSpeed *
        // (loserMass / winnerMass). A stationary heavier NPC (winnerSpeed = 0) naturally stops the
        // player rather than needing a separate "can't push through" case. Only engages while the
        // player is actively moving toward the NPC, so backing away is never blocked.
        private bool TryResolveNpcPush(float speed, out float pushVelocityX)
        {
            pushVelocityX = 0f;
            if (moveIntent == 0f) return false;

            var hit = Physics2D.OverlapBox(GetBodyCenter(), capsule.size, 0f, npcBodyLayer);
            // The NpcBody trigger lives on a child collider (its own layer, separate from the
            // NPC's other colliders); attachedRigidbody resolves to the actual body that owns it.
            var blocker = hit != null && hit.attachedRigidbody != null
                ? hit.attachedRigidbody.GetComponent<NpcBlocker>()
                : null;
            if (blocker == null || !blocker.BlocksPlayer) return false;

            var towardNpc = Mathf.Sign(blocker.GetPosition().x - rb.position.x);
            if (Mathf.Sign(moveIntent) != towardNpc) return false;

            var playerMass = Mass;
            var npcMass = blocker.Mass;
            if (playerMass == npcMass) return true; // tie: stand-off, both stay put

            var playerWins = playerMass > npcMass;
            var winnerVelocityX = playerWins ? moveIntent * speed : 0f;
            var ratio = Mathf.Min(playerMass, npcMass) / Mathf.Max(playerMass, npcMass);
            var intendedVelocityX = winnerVelocityX * ratio;

            // Move the NPC first and use what it actually achieved (it may be jammed against a
            // wall) so the player is kept in sync rather than sliding on through a stopped NPC —
            // its own colliders are triggers, so nothing else would stop the player from doing so.
            var actualDelta = blocker.MoveBy(intendedVelocityX * Time.fixedDeltaTime, groundLayer);
            pushVelocityX = actualDelta / Time.fixedDeltaTime;
            return true;
        }

        private void HandleMove(Vector2 value)
        {
            MoveInput = value;

            if (value.x != 0f) FacingRight = value.x > 0f;

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
        private void HandleAttackLight() => combatStateMachine.TryEnterState(stateMachine.CurrentStateId == "CROUCH" ? "CROUCH_ATTACK_1" : "ATTACK_1");
        private void HandleAttackHeavy()
        {
            var locomotionId = stateMachine.CurrentStateId;
            string targetState;
            if (locomotionId == "CROUCH") targetState = "CROUCH_ATTACK_2";
            else if (locomotionId == "JUMP" || locomotionId == "FALL") targetState = "JUMP_KICK";
            else targetState = "ATTACK_2";
            combatStateMachine.TryEnterState(targetState);
        }
        private void HandleDodge() => combatStateMachine.TryEnterState("DODGE");

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

        // Suspends normal walk/run velocity application in FixedUpdate — used by DodgeState,
        // which drives position itself via MoveTo and must not have moveIntent-based velocity
        // fighting its per-tick delta on a Kinematic body.
        public void SetKinematic(bool kinematic)
        {
            physicsSuspended = kinematic;
            rb.bodyType = kinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
        }

        public void MoveTo(Vector2 position) => rb.MovePosition(position);
        public Vector2 GetPosition() => rb.position;
        public Vector2 GetBodyCenter() => rb.position + capsule.offset;
        public float BodyHalfWidth => capsule.size.x * 0.5f;
    }
}
