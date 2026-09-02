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
        [SerializeField] private float doubleTapWindow = 0.25f;
        [SerializeField] private float groundCheckRadius = 0.1f;

        private Rigidbody2D rb;
        private PlayerStateMachine stateMachine;

        private bool isGrounded;
        private float moveIntent;
        private bool runIntent;

        private int previousSign;
        private int pendingTapDirection;
        private float pendingTapTime;

        public Vector2 MoveInput { get; private set; }
        public bool IsRunning { get; private set; }
        public float JumpImpulseForce => jumpImpulseForce;
        public float JumpCutMultiplier => jumpCutMultiplier;

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

        public void ApplyImpulse(Vector2 force) => rb.AddForce(force, ForceMode2D.Impulse);
        public void ForceLocomotionState(string stateId) => stateMachine.ForceState(stateId);
        public Vector2 GetVelocity() => rb.linearVelocity;
        public bool IsGrounded() => isGrounded;
        public bool IsAirborne() => !IsGrounded();
    }
}
