using UnityEngine;

namespace KungFuVania.Combat
{
    // Drop-in regen behavior: attach alongside Health on any NPC that should passively heal after
    // a period without taking damage (e.g. a training dummy resetting between practice sessions).
    [RequireComponent(typeof(Health))]
    public class HealOverTime : MonoBehaviour
    {
        [SerializeField] private float delayAfterDamage = 3f;
        [SerializeField] private float healPerSecond = 5f;

        private Health health;
        private float lastDamageTime = float.NegativeInfinity;

        private void Awake() => health = GetComponent<Health>();
        private void OnEnable() => health.OnDamaged += HandleDamaged;
        private void OnDisable() => health.OnDamaged -= HandleDamaged;

        private void HandleDamaged() => lastDamageTime = Time.time;

        private void Update()
        {
            if (Time.time - lastDamageTime < delayAfterDamage) return;
            health.Heal(healPerSecond * Time.deltaTime);
        }
    }
}
