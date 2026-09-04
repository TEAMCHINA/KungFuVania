using System.Collections.Generic;
using UnityEngine;

using KungFuVania.Core;
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

            EventBus.Publish(new PlayerStateChanged { StateId = stateId });
        }

        public void ForceState(string stateId) => ChangeState(stateId);

        public void NotifyJumpPressed()
        {
            if (controller.IsGrounded())
            {
                if (currentStateId != "JUMP")
                    ChangeState("JUMP");
                return;
            }

            if (!controller.ConsumeJumpCharge()) return;

            if (currentStateId == "JUMP")
                (currentState as JumpState)?.Enter();
            else
                ChangeState("JUMP");
        }

        public void NotifyJumpReleased()
        {
            (currentState as JumpState)?.NotifyJumpReleased();
        }
    }
}
