using System.Collections.Generic;
using UnityEngine;
using KungFuVania.Player.Locomotion;

namespace KungFuVania.Player
{
    public class PlayerStateMachine : MonoBehaviour
    {
        private readonly Dictionary<string, ILocomotionState> states = new();
        private ILocomotionState currentState;
        private string currentStateId;

        private PlayerController controller;
        public PlayerController Controller => controller;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();

            states["IDLE"] = new IdleState(this);
            states["WALK"] = new WalkState(this);
            states["RUN"] = new RunState(this);
            states["JUMP"] = new JumpState(this);
            states["FALL"] = new FallState(this);
        }

        private void Start()
        {
            ChangeState("IDLE");
        }

        private void Update()
        {
            currentState?.Tick(Time.deltaTime);
        }

        public void ChangeState(string stateId)
        {
            if (stateId == currentStateId) return;

            currentState?.Exit();
            currentStateId = stateId;
            currentState = states[stateId];
            currentState.Enter();
        }

        public void ForceState(string stateId) => ChangeState(stateId);

        public void NotifyJumpPressed()
        {
            if (currentStateId != "JUMP" && controller.IsGrounded())
                ChangeState("JUMP");
        }

        public void NotifyJumpReleased()
        {
            (currentState as JumpState)?.NotifyJumpReleased();
        }
    }
}
