using UnityEngine;

namespace KungFuVania.Combat
{
    public enum HurtboxZoneType
    {
        Body,
        Low
    }

    // Wiring, not logic: each zone's own child GameObject carries one of these, tied 1:1 to that
    // zone's own collider, so a hit touching multiple zones in one step resolves unambiguously per
    // zone instead of collapsing into one shared OnTriggerEnter2D on a rigidbody-owning root.
    public class HurtboxZoneForwarder : MonoBehaviour
    {
        [SerializeField] private HurtboxController hurtboxController;
        [SerializeField] private HurtboxZoneType zoneType;

        private void OnTriggerEnter2D(Collider2D other) =>
            other.GetComponent<HitboxController>()?.OnZoneHit(zoneType, hurtboxController);
    }
}
