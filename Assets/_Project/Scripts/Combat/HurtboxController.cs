using UnityEngine;

namespace KungFuVania.Combat
{
    public class HurtboxController : MonoBehaviour
    {
        [SerializeField] private Collider2D hurtboxCollider;

        // I-frame gate, reference-counted so overlapping sources (e.g. dodge i-frames layered
        // with an attack-embedded invulnerability window) don't cancel each other early.
        private int invulnerabilityCount;

        public void SetInvulnerable(bool active)
        {
            invulnerabilityCount += active ? 1 : -1;
            hurtboxCollider.enabled = invulnerabilityCount == 0;
        }
    }
}
