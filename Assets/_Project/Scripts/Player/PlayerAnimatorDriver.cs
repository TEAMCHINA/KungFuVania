using UnityEngine;
using KungFuVania.Core;

namespace KungFuVania.Player
{
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private float landingHoldDuration = 0.15f;

        private Animator animator;
        private PlayerController controller;
        private SpriteRenderer spriteRenderer;
        private string pendingStateId;
        private float landingUntil;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            controller = GetComponent<PlayerController>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator.Update(0f);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerStateChanged>(HandleStateChanged);
            EventBus.Subscribe<OnPlayerLanded>(HandleLanded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerStateChanged>(HandleStateChanged);
            EventBus.Unsubscribe<OnPlayerLanded>(HandleLanded);
        }

        private void Update()
        {
            if (pendingStateId != null && Time.time >= landingUntil)
            {
                animator.Play(pendingStateId);
                pendingStateId = null;
            }

            if (controller.MoveInput.x != 0f)
                spriteRenderer.flipX = controller.MoveInput.x < 0f;
        }

        private void HandleStateChanged(PlayerStateChanged evt)
        {
            if (Time.time < landingUntil)
            {
                pendingStateId = evt.StateId;
                return;
            }

            animator.Play(evt.StateId);
        }

        private void HandleLanded(OnPlayerLanded evt)
        {
            landingUntil = Time.time + landingHoldDuration;
            animator.Play("LANDING");
        }
    }
}
