using System.Collections.Generic;
using UnityEngine;

namespace KungFuVania.Combat
{
    public class HitboxController : MonoBehaviour
    {
        [SerializeField] private BoxCollider2D hitboxCollider;

        public HitboxDataSO activeHitboxData;

        // Keyed by target so a wide/sweep hit touching multiple hurtboxes in the same activation
        // resolves each independently instead of sharing one slot. Low never triggers resolution
        // by itself — it only flags a target that Body also touched this step.
        private readonly HashSet<HurtboxController> bodyContacts = new();
        private readonly HashSet<HurtboxController> lowContacts = new();

        public void Activate() => hitboxCollider.enabled = true;
        public void Deactivate() => hitboxCollider.enabled = false;

        public void OnZoneHit(HurtboxZoneType zoneType, HurtboxController target)
        {
            if (zoneType == HurtboxZoneType.Body) bodyContacts.Add(target);
            else if (zoneType == HurtboxZoneType.Low) lowContacts.Add(target);
        }

        // Unity has no real LateFixedUpdate message — this indirection exists purely so the
        // deferred resolution pass can carry GAME_PLAN.md's literal HitboxController.LateFixedUpdate
        // name. Deferring to LateUpdate (rather than resolving inline in OnZoneHit) lets a same-step
        // Low contact merge into a Body contact regardless of which forwarder's callback arrives first.
        private void LateUpdate() => LateFixedUpdate();

        private void LateFixedUpdate()
        {
            foreach (var target in bodyContacts)
                DamageCalculator.Resolve(activeHitboxData, target, lowContacts.Contains(target));

            bodyContacts.Clear();
            lowContacts.Clear();
        }
    }
}
