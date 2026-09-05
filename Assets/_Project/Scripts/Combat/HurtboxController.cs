using System;
using UnityEngine;

namespace KungFuVania.Combat
{
    public class HurtboxController : MonoBehaviour
    {
        [SerializeField] private Collider2D hurtboxCollider;

        public event Action<float> OnHit;

        private int invulnerabilityCount;

        public void SetInvulnerable(bool active)
        {
            invulnerabilityCount += active ? 1 : -1;
            hurtboxCollider.enabled = invulnerabilityCount == 0;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var hitbox = other.GetComponent<HitboxController>();
            if (hitbox == null) return;
            OnHit?.Invoke(hitbox.activeHitboxData.damage);
        }
    }
}
