using KungFuVania.Core;

namespace KungFuVania.Combat
{
    // Trimmed to "player deals damage, no stats/equipment" — see SESSION_PLAN.md. attackerStats
    // is a null-checked stub seam for a future StatSheet/equipment system that doesn't exist yet,
    // so this can be extended in place later without changing callers.
    public static class DamageCalculator
    {
        public static void Resolve(HitboxDataSO hitboxData, HurtboxController target, bool isLowHit, object attackerStats = null)
        {
            var health = target.GetComponent<Health>();
            if (health == null) return;

            var damage = hitboxData.damage;
            health.TakeDamage(damage);

            EventBus.Publish(new OnEntityDamaged { Target = target.gameObject, Damage = damage, IsLowHit = isLowHit });
        }
    }
}
