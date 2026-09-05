using UnityEngine;

namespace KungFuVania.Combat
{
    public class HitboxEventRelay : MonoBehaviour
    {
        [SerializeField] private HitboxController hitboxController;

        public void OnHitboxActive(int hitboxIndex) => hitboxController.Activate(hitboxIndex);
        public void OnHitboxInactive(int hitboxIndex) => hitboxController.Deactivate(hitboxIndex);
    }
}
