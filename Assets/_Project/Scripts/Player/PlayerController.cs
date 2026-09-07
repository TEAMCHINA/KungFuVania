using UnityEngine;
using KungFuVania.Input;
using KungFuVania.Actors;
using KungFuVania.Combat;

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
        [SerializeField] private float forwardDashDistance = 1f;
        [SerializeField] private float backDashDistance = 1f;
        [SerializeField] private float dodgeDistance = 2f;
        // How long to wait after a single dodge-button tap to see whether a second tap arrives
        // (making it a roll) before committing to a dash. This is the standard tap-vs-double-tap
        // tradeoff — it puts a small, fixed delay on every single-tap dash, since there's no way
        // to know a tap won't become a double-tap until this window has passed.
        [SerializeField] private float dodgeDoubleTapWindow = 0.25f;
        // Manual stand-in for the eventual ability-gate check, same precedent as jumpCharges/
        // maxWallJumps above — no WorldStateManager/unlock-persistence system exists yet. Becomes
        // a WorldStateManager.unlockedAbilities check on "LOCK_FACING" once that exists.
        [SerializeField] private bool lockFacingUnlocked = true;

        private Rigidbody2D rb;
        private CapsuleCollider2D capsule;
        private PlayerStateMachine stateMachine;
        private PlayerCombatStateMachine combatStateMachine;
        private InputBuffer inputBuffer;
        private MotionInputDetector motionInputDetector;

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

        private float lastDodgeTapTime = -999f;
        private string pendingDashStateId;
        private float pendingDashDeadline;

        private bool lockFacingHeld;

        public Vector2 MoveInput { get; private set; }
        public bool IsRunning { get; private set; }
        public bool FacingRight { get; private set; } = true;
        // Lock Facing (formerly "Chi Mode" — narrowed down to just this one job, see GAME_PLAN.md
        // 3l). Holding the assigned button (Shift/left bumper) suppresses HandleMove's facing
        // flip below, nothing else — motion-input detection no longer depends on this being held
        // at all (see MotionInputDetector), since only back-crossing motions (HCB/HCF/360s) are
        // actually at risk of a mid-motion facing flip corrupting their zone mirroring; forward-
        // biased motions (QCF, DP) never press away from facing and so can never trigger one,
        // held or not.
        public bool LockFacingActive => lockFacingUnlocked && lockFacingHeld;
        public float JumpImpulseForce => jumpImpulseForce;
        public float JumpCutMultiplier => jumpCutMultiplier;
        public float Mass => rb.mass;
        public int JumpCharges
        {
            get => jumpCharges;
            set => jumpCharges = value;
        }
        public float WallSlideSpeed => wallSlideSpeed;
        public float ForwardDashDistance => forwardDashDistance;
        public float BackDashDistance => backDashDistance;
        public float DodgeDistance => dodgeDistance;
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
            inputBuffer = GetComponent<InputBuffer>();
            motionInputDetector = GetComponent<MotionInputDetector>();
        }

        private void OnEnable()
        {
            inputReader.OnMove += HandleMove;
            inputReader.OnJump += HandleJump;
            inputReader.OnJumpCancelled += HandleJumpCancelled;
            inputReader.OnAttackLight += HandleAttackLight;
            inputReader.OnAttackHeavy += HandleAttackHeavy;
            inputReader.OnDodge += HandleDodge;
            inputReader.OnLockFacingPressed += HandleLockFacingPressed;
            inputReader.OnLockFacingReleased += HandleLockFacingReleased;
        }

        private void OnDisable()
        {
            inputReader.OnMove -= HandleMove;
            inputReader.OnJump -= HandleJump;
            inputReader.OnJumpCancelled -= HandleJumpCancelled;
            inputReader.OnAttackLight -= HandleAttackLight;
            inputReader.OnAttackHeavy -= HandleAttackHeavy;
            inputReader.OnDodge -= HandleDodge;
            inputReader.OnLockFacingPressed -= HandleLockFacingPressed;
            inputReader.OnLockFacingReleased -= HandleLockFacingReleased;
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

            // Only ever diverges from facing while Lock Facing is held — outside that, HandleMove
            // flips FacingRight to match moveIntent's sign immediately, so this is always false.
            // Never eligible for the run multiplier: backing away is a deliberate slow retreat,
            // not something you sprint.
            var movingBackward = moveIntent != 0f && Mathf.Sign(moveIntent) != (FacingRight ? 1f : -1f);
            var speed = movingBackward ? walkSpeed * 0.5f : runIntent ? walkSpeed * runSpeedMultiplier : walkSpeed;

            if (TryResolveNpcPush(speed, out var pushVelocityX))
            {
                rb.linearVelocity = new Vector2(pushVelocityX, rb.linearVelocity.y);
                return;
            }

            rb.linearVelocity = new Vector2(moveIntent * speed, rb.linearVelocity.y);
        }

        // Resolves a single-tap dash once its double-tap window has passed without a follow-up
        // tap upgrading it to a roll — see HandleDodge.
        private void Update()
        {
            if (pendingDashStateId != null && Time.time >= pendingDashDeadline)
            {
                combatStateMachine.TryEnterState(pendingDashStateId);
                pendingDashStateId = null;
            }
        }

        // Not in Update(): PlayerCombatStateMachine.Update() (which fires ChangeState("NONE") when
        // an attack's clip ends) has no guaranteed order relative to this script's Update() — if it
        // happens to run first in the same frame, retrying here would call Animator.Play() a second
        // time before Unity ever evaluates the animator in between, and confirmed live, that second
        // Play() call is just silently dropped (the animator stays on whatever the first call this
        // frame requested). Unity runs its own animator evaluation between Update() and LateUpdate()
        // for every script, so waiting for LateUpdate() guarantees a real evaluation already happened
        // in between — same frame, no perceptible delay, but no longer racing the animator.
        private void LateUpdate()
        {
            TryFireBufferedAttack();
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

            // Lock Facing suppresses the flip below, nothing else — movement is never locked, the
            // stick always drives real walking (including backward) at the same time it's read as
            // a motion-input direction by MotionInputDetector, exactly like real fighting games
            // (the same stick both walks you and inputs specials, always, no separate mode).
            // Facing only needs to stay fixed for motions that cross the back side, where a flip
            // mid-sequence would invert the matcher's "toward/away" zone mirroring underneath an
            // in-progress attempt — see LockFacingActive.
            if (!LockFacingActive && value.x != 0f) FacingRight = value.x > 0f;

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

        // Motion Input System integration point (GAME_PLAN.md 3l): every attack press is first
        // offered to MotionInputDetector, unconditionally — a completed, button-matching motion
        // fires its special directly and never touches the buffer. Anything else (no match) falls
        // through to the buffer exactly as before — every such press lands there, full stop.
        // TryFireBufferedAttack (LateUpdate) remains the one and only place that ever calls
        // TryEnterState for a normal attack, whether that ends up happening the same frame it was
        // pressed (nothing in the way) or several attacks later. A wrong-locomotion press (e.g.
        // attacking mid wall-slide) still just fails and clears itself there one frame later —
        // not worth a second code path to special-case.
        private void HandleAttackLight() => HandleAttackButton("LIGHT");
        private void HandleAttackHeavy() => HandleAttackButton("HEAVY");

        private void HandleAttackButton(string attackAction)
        {
            var specialFired = motionInputDetector != null && motionInputDetector.TryFireCompletedMotion(attackAction);
            if (!specialFired)
                inputBuffer.Record(attackAction);
        }

        // Re-run at consume time too (see TryFireBufferedAttack) rather than trusting whatever
        // locomotion said back when the press was buffered — a crouch attack buffered while
        // crouched should not fire if the player has since stood up.
        private string ResolveAttackState(string attackAction)
        {
            var locomotionId = stateMachine.CurrentStateId;
            var isCrouching = locomotionId == "CROUCH";
            var isAirborne = locomotionId == "JUMP" || locomotionId == "FALL";

            if (attackAction == "LIGHT")
            {
                if (isCrouching) return "CROUCH_ATTACK_1";
                if (isAirborne) return "JUMP_ATTACK_1";
                return "ATTACK_1";
            }

            if (isCrouching) return "CROUCH_ATTACK_2";
            if (isAirborne) return "JUMP_ATTACK_2";
            return "ATTACK_2";
        }

        // The single entry point into combat for attacks (see HandleAttackLight/HandleAttackHeavy)
        // — fires the freshest still-valid buffered press once the combat state machine is free.
        // Deliberately not reacting to CombatStateChanged directly — that event is published from
        // inside PlayerCombatStateMachine's own Tick, and firing TryEnterState from an event handler
        // nested a second ChangeState/Publish inside the same call stack, same frame, as the one
        // that just fired for the attack ending. Called from LateUpdate (see that method's comment)
        // rather than Update, for the same reason: it needs a frame where Animator.Play("ATTACK_1")
        // isn't racing another Play() call already made this frame. Re-resolves against current
        // locomotion (ResolveAttackState) rather than trusting whatever locomotion was true when the
        // press was buffered, and relies on TryEnterState's own eligibility gate to fail closed if
        // that resolution is no longer valid (e.g. player left CROUCH).
        private void TryFireBufferedAttack()
        {
            if (combatStateMachine.CurrentStateId != "NONE") return;
            if (inputBuffer.TryConsumeFreshest(out var attackAction))
                combatStateMachine.TryEnterState(ResolveAttackState(attackAction));
        }

        // Single tap = a short dash: forward if currently pressing the direction the player is
        // facing, backward otherwise (so a neutral tap dashes back). Double tap = a longer roll,
        // always in the facing direction. A tap can't be classified as single-vs-double until the
        // window passes without a follow-up, so a single tap is buffered for dodgeDoubleTapWindow
        // before it actually fires (see Update) — a double tap fires DODGE_ROLL immediately and
        // cancels whatever single tap it superseded.
        private void HandleDodge()
        {
            var isDoubleTap = Time.time - lastDodgeTapTime <= dodgeDoubleTapWindow;
            lastDodgeTapTime = Time.time;

            if (isDoubleTap)
            {
                pendingDashStateId = null;
                combatStateMachine.TryEnterState("DODGE_ROLL");
                return;
            }

            var facingSign = FacingRight ? 1f : -1f;
            var pressingForward = MoveInput.x != 0f && Mathf.Sign(MoveInput.x) == facingSign;
            pendingDashStateId = pressingForward ? "DASH_FORWARD" : "DASH_BACK";
            pendingDashDeadline = Time.time + dodgeDoubleTapWindow;
        }

        // Plain hold-button reflection — no latch, no cancel-on-attack. Attacking while holding
        // this has zero effect on it; LockFacingActive just tracks whatever the physical button
        // is doing for as long as it's held, full stop.
        private void HandleLockFacingPressed() => lockFacingHeld = true;
        private void HandleLockFacingReleased() => lockFacingHeld = false;

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
