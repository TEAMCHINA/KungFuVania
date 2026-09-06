using System;
using UnityEngine;

namespace KungFuVania.Combat
{
    // Generic damageable-health tracker. Deliberately has no death/depletion event — actors that
    // should die at 0 HP add that behavior themselves later; this component never destroys or
    // disables anything, it just clamps at 0 and stays there until healed.
    [RequireComponent(typeof(HurtboxController))]
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 50f;

        public float MaxHealth => maxHealth;
        public float CurrentHealth { get; private set; }

        public event Action<float, float> OnHealthChanged; // (current, max)
        public event Action OnDamaged;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(float damage)
        {
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            OnDamaged?.Invoke();
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || CurrentHealth >= maxHealth) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }
    }
}
