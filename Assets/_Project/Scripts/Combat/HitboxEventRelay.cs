using UnityEngine;

namespace KungFuVania.Combat
{
    public class HitboxEventRelay : MonoBehaviour
    {
        [SerializeField] private HitboxController hitboxController;

        public void OnHitboxActive() => hitboxController.Activate();
        public void OnHitboxInactive() => hitboxController.Deactivate();
    }
}
