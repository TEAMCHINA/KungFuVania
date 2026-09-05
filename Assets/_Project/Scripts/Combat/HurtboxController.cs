using System;
using UnityEngine;

namespace KungFuVania.Combat
{
    public class HurtboxController : MonoBehaviour
    {
        [SerializeField] private Collider2D hurtboxCollider;
        // Optional sub-zone for a distinguishable low-height hit — see GAME_PLAN 3n "Hurtbox_Low".
        // A plain child collider on the same Rigidbody2D as hurtboxCollider, exactly like
        // Hurtbox_Head: it never resolves damage itself, it only flags whether the resolving hit
        // also touched it.
        [SerializeField] private Collider2D lowHurtboxCollider;

        public event Action<float, bool> OnHit; // (damage, isLowHit)

        private int invulnerabilityCount;
        private HitboxController pendingHitbox;
        private bool pendingIsLow;

        public void SetInvulnerable(bool active)
        {
            invulnerabilityCount += active ? 1 : -1;
            hurtboxCollider.enabled = invulnerabilityCount == 0;
        }

        // Hurtbox_Body and Hurtbox_Low share one Rigidbody2D, so a hit overlapping both fires this
        // callback once per collider touched, not once per hit — Unity attributes every trigger
        // contact on a rigidbody-less child to the Rigidbody2D owner's own scripts too (confirmed
        // with a live test rig: a second attached collider double-fired this method for one hit).
        // Collect into pending state here and resolve exactly once in LateUpdate, after all of this
        // step's callbacks have already arrived — mirrors GAME_PLAN's Hurtbox_Body/Hurtbox_Head
        // damage-resolution model (Body is the sole trigger for resolution, Head/Low just set a
        // flag Body's resolution reads).
        private void OnTriggerEnter2D(Collider2D other)
        {
            var hitbox = other.GetComponent<HitboxController>();
            if (hitbox == null) return;

            pendingHitbox = hitbox;
            if (lowHurtboxCollider != null && lowHurtboxCollider.IsTouching(other))
                pendingIsLow = true;
        }

        private void LateUpdate()
        {
            if (pendingHitbox == null) return;

            // Clear before invoking, not after: if a misconfigured hitbox (or any subscriber)
            // throws, an un-cleared pendingHitbox would re-throw the same exception every frame
            // forever instead of just this one.
            var hitbox = pendingHitbox;
            var isLow = pendingIsLow;
            pendingHitbox = null;
            pendingIsLow = false;

            OnHit?.Invoke(hitbox.activeHitboxData.damage, isLow);
        }
    }
}
