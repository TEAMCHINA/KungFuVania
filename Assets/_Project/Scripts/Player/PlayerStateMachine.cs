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
        public string CurrentStateId => currentStateId;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();

            states["IDLE"] = new IdleState(this);
            states["WALK"] = new WalkState(this);
            states["RUN"] = new RunState(this);
            states["JUMP"] = new JumpState(this);
            states["FALL"] = new FallState(this);
            states["WALL_SLIDE"] = new WallSlideState(this);
            states["CROUCH"] = new CrouchState(this);
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

            SwitchTo(stateId);
            currentState.Enter();
            EventBus.Publish(new PlayerStateChanged { StateId = stateId });
        }

        private void SwitchTo(string stateId)
        {
            currentState?.Exit();
            currentStateId = stateId;
            currentState = states[stateId];
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

            if (currentStateId == "WALL_SLIDE")
            {
                // Jump means wall-jump exclusively while clinging — falling back to a generic
                // aerial charge-jump here would launch straight up while still held into the
                // wall, scraping along its face instead of launching away from it. No wall-jump
                // charge just means no jump yet, same as maxWallJumps = 0 meaning "not acquired".
                if (controller.TryWallJump())
                {
                    SwitchTo("JUMP");
                    EventBus.Publish(new PlayerStateChanged { StateId = "JUMP" });
                }
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
