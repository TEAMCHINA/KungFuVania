using System;
using UnityEngine;

namespace KungFuVania.Input
{
    [CreateAssetMenu(fileName = "InputReader", menuName = "KungFuVania/Input Reader")]
    public class InputReader : ScriptableObject
    {
        public event Action<Vector2> OnMove;
        public event Action OnJump;
        public event Action OnJumpCancelled;
        public event Action OnAttackLight;
        public event Action OnAttackHeavy;
        public event Action OnDodge;
        public event Action OnChiModePressed;
        public event Action OnChiModeReleased;

        private PlayerControls controls;

        private void OnEnable()
        {
            if (controls == null)
            {
                controls = new PlayerControls();
                controls.Player.Move.performed += ctx => OnMove?.Invoke(ctx.ReadValue<Vector2>());
                controls.Player.Move.canceled += ctx => OnMove?.Invoke(Vector2.zero);
                controls.Player.Jump.performed += ctx => OnJump?.Invoke();
                controls.Player.Jump.canceled += ctx => OnJumpCancelled?.Invoke();
                controls.Player.AttackLight.performed += ctx => OnAttackLight?.Invoke();
                controls.Player.AttackHeavy.performed += ctx => OnAttackHeavy?.Invoke();
                controls.Player.Dodge.performed += ctx => OnDodge?.Invoke();
                controls.Player.ChiMode.performed += ctx => OnChiModePressed?.Invoke();
                controls.Player.ChiMode.canceled += ctx => OnChiModeReleased?.Invoke();
            }

            controls.Player.Enable();
        }

        private void OnDisable()
        {
            controls.Player.Disable();
        }
    }
}
