using UnityEngine;
using KungFuVania.Core;
using KungFuVania.Combat;

namespace KungFuVania.Player
{
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private float landingHoldDuration = 0.15f;

        private Animator animator;
        private PlayerController controller;
        private string pendingStateId;
        private float landingUntil;

        private string currentLocomotionId = "IDLE";
        private string currentCombatId = "NONE";

        private void Awake()
        {
            animator = GetComponent<Animator>();
            controller = GetComponent<PlayerController>();
            animator.Update(0f);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerStateChanged>(HandleLocomotionChanged);
            EventBus.Subscribe<OnPlayerLanded>(HandleLanded);
            EventBus.Subscribe<CombatStateChanged>(HandleCombatChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerStateChanged>(HandleLocomotionChanged);
            EventBus.Unsubscribe<OnPlayerLanded>(HandleLanded);
            EventBus.Unsubscribe<CombatStateChanged>(HandleCombatChanged);
        }

        private void Update()
        {
            if (pendingStateId != null && Time.time >= landingUntil)
            {
                animator.Play(pendingStateId);
                pendingStateId = null;
            }

            // Mirrors the whole root (and its Hitbox/Hurtbox children) instead of just the sprite,
            // matching how NPCs already flip — see SESSION_PLAN.md hitbox authoring refactor.
            transform.localScale = new Vector3(controller.FacingRight ? 1f : -1f, 1f, 1f);
        }

        private void HandleLocomotionChanged(PlayerStateChanged evt)
        {
            currentLocomotionId = evt.StateId;
            RefreshAnimation();
        }

        private void HandleCombatChanged(CombatStateChanged evt)
        {
            currentCombatId = evt.StateId;
            RefreshAnimation();
        }

        // Combat wins whenever it isn't NONE — see GAME_PLAN.md 2 "Two Concurrent Layers".
        private void RefreshAnimation()
        {
            var stateToPlay = currentCombatId != "NONE" ? currentCombatId : currentLocomotionId;

            if (Time.time < landingUntil)
            {
                pendingStateId = stateToPlay;
                return;
            }

            animator.Play(stateToPlay);
        }

        private void HandleLanded(OnPlayerLanded evt)
        {
            landingUntil = Time.time + landingHoldDuration;
            animator.Play("LANDING");
        }
    }
}
