using System.Collections.Generic;
using UnityEngine;

using KungFuVania.Core;
using KungFuVania.Combat;
using KungFuVania.Player.Combat;

namespace KungFuVania.Player
{
    public class PlayerCombatStateMachine : MonoBehaviour
    {
        [SerializeField] private HitboxController hitboxController;
        [SerializeField] private HurtboxController hurtboxController;
        [SerializeField] private HitboxDataSO punchData;
        [SerializeField] private HitboxDataSO kickData;
        [SerializeField] private HitboxDataSO crouchPunchData;
        [SerializeField] private HitboxDataSO crouchKickData;
        [SerializeField] private HitboxDataSO jumpKickData;
        [SerializeField] private HitboxDataSO jumpPunchData;

        [SerializeField] private LayerMask dodgeObstructionMask;
        [SerializeField] private float dodgeTotalDuration = 0.2f;
        [SerializeField] private float dodgeIFrameStart = 0.05f;
        [SerializeField] private float dodgeIFrameEnd = 0.15f;
        [SerializeField] private float dodgeRecoveryDuration = 0.15f;
        // Roll covers ~2x the distance of a dash but shouldn't cover it in the same time — that
        // read as a near-teleport. Stretching its duration (i-frame timing stays absolute/
        // unscaled) brings its effective speed down closer to the dash's instead of doubling it.
        [SerializeField] private float dodgeRollDurationMultiplier = 1.5f;

        private readonly Dictionary<string, ICombatState> states = new();
        private ICombatState currentState;
        private string currentStateId;

        private PlayerStateMachine locomotion;

        public PlayerController Controller { get; private set; }
        public Animator Animator { get; private set; }
        public HitboxController HitboxController => hitboxController;
        public HurtboxController HurtboxController => hurtboxController;
        public string CurrentStateId => currentStateId;

        private void Awake()
        {
            Controller = GetComponent<PlayerController>();
            Animator = GetComponent<Animator>();
            locomotion = GetComponent<PlayerStateMachine>();

            states["NONE"] = new NoneState();
            states["ATTACK_1"] = new AttackState(this, punchData, "ATTACK_1");
            states["ATTACK_2"] = new AttackState(this, kickData, "ATTACK_2");
            states["CROUCH_ATTACK_1"] = new AttackState(this, crouchPunchData, "CROUCH_ATTACK_1");
            states["CROUCH_ATTACK_2"] = new AttackState(this, crouchKickData, "CROUCH_ATTACK_2");
            states["JUMP_KICK"] = new AttackState(this, jumpKickData, "JUMP_KICK", exitOnLanding: true);
            states["JUMP_PUNCH"] = new AttackState(this, jumpPunchData, "JUMP_PUNCH", exitOnLanding: true);
            states["DASH_FORWARD"] = new DodgeState(this, dodgeTotalDuration, () => Controller.ForwardDashDistance, dodgeIFrameStart, dodgeIFrameEnd, dodgeObstructionMask);
            states["DASH_BACK"] = new DodgeState(this, dodgeTotalDuration, () => Controller.BackDashDistance, dodgeIFrameStart, dodgeIFrameEnd, dodgeObstructionMask, reverseDirection: true);
            states["DODGE_ROLL"] = new DodgeState(this, dodgeTotalDuration * dodgeRollDurationMultiplier, () => Controller.DodgeDistance, dodgeIFrameStart, dodgeIFrameEnd * dodgeRollDurationMultiplier, dodgeObstructionMask);
            states["DODGE_RECOVERY"] = new DodgeRecoveryState(this, dodgeRecoveryDuration);
        }

        private void Start()
        {
            ChangeState("NONE");
        }

        private void Update()
        {
            currentState?.Tick(Time.deltaTime);
        }

        // Physics-moving states (DodgeState) tick here instead — see ICombatState.FixedTick.
        private void FixedUpdate()
        {
            currentState?.FixedTick(Time.fixedDeltaTime);
        }

        // Only succeeds from NONE — no combos this session. Crouch attacks require the player to
        // actually be in CROUCH; JUMP_KICK requires actually being airborne (JUMP/FALL); every
        // other state (standing attacks, dodge) requires standing (IDLE/WALK/RUN) — no dodge
        // in the air, no attacking mid-crouch-transition either.
        public bool TryEnterState(string stateId)
        {
            if (currentStateId != "NONE") return false;

            var locomotionId = locomotion.CurrentStateId;
            bool allowed;
            if (IsCrouchAttack(stateId)) allowed = locomotionId == "CROUCH";
            else if (IsAerialAttack(stateId)) allowed = IsAirborneLocomotion(locomotionId);
            else allowed = IsGroundedLocomotion(locomotionId);
            if (!allowed) return false;

            ChangeState(stateId);
            return true;
        }

        public void ChangeState(string stateId)
        {
            if (stateId == currentStateId) return;

            currentState?.Exit();
            currentStateId = stateId;
            currentState = states[stateId];
            currentState.Enter();

            EventBus.Publish(new CombatStateChanged { StateId = stateId });
        }

        private static bool IsGroundedLocomotion(string locomotionStateId) =>
            locomotionStateId == "IDLE" || locomotionStateId == "WALK" || locomotionStateId == "RUN";

        private static bool IsAirborneLocomotion(string locomotionStateId) =>
            locomotionStateId == "JUMP" || locomotionStateId == "FALL";

        private static bool IsCrouchAttack(string stateId) =>
            stateId == "CROUCH_ATTACK_1" || stateId == "CROUCH_ATTACK_2";

        private static bool IsAerialAttack(string stateId) => stateId == "JUMP_KICK" || stateId == "JUMP_PUNCH";
    }
}
