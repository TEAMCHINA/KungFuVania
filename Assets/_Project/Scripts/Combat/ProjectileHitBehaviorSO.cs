using UnityEngine;

namespace KungFuVania.Combat
{
    // Pluggable on-hit behavior for ProjectileController (GAME_PLAN.md 3l "Projectile System").
    // null on ProjectileController.onHitBehavior IS the default (despawn on first contact) --
    // no concrete "despawn" subclass exists because the common case needs none. Pierce/multi-
    // spawn variants are a later pass; this is just the plug point.
    public abstract class ProjectileHitBehaviorSO : ScriptableObject
    {
        public abstract void OnHit(ProjectileController projectile, HurtboxController target);
    }
}
