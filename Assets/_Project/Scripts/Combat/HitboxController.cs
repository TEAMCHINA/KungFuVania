using UnityEngine;

namespace KungFuVania.Combat
{
    public class HitboxController : MonoBehaviour
    {
        [SerializeField] private Collider2D hitboxCollider;
        [SerializeField] private KungFuVania.Player.PlayerController facingSource;

        private Vector3 baseLocalPosition;

        public HitboxDataSO activeHitboxData;

        private void Awake()
        {
            baseLocalPosition = hitboxCollider.transform.localPosition;
        }

        public void Activate(int index)
        {
            if (facingSource != null)
            {
                var pos = baseLocalPosition;
                pos.x = facingSource.FacingRight ? Mathf.Abs(pos.x) : -Mathf.Abs(pos.x);
                hitboxCollider.transform.localPosition = pos;
            }
            hitboxCollider.enabled = true;
        }

        public void Deactivate(int index) => hitboxCollider.enabled = false;
    }
}
