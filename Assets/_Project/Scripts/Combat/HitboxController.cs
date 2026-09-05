using UnityEngine;

namespace KungFuVania.Combat
{
    public class HitboxController : MonoBehaviour
    {
        // Reach heights for the single shared hitbox collider, selected per attack via the
        // Animation Event's intParameter (see AddEvent calls on each attack's clip). Purely
        // geometric — there's no "this attack is high/mid/low" flag anywhere; whichever offset
        // is applied determines what the collider can physically overlap.
        public const int ReachStanding = 0;
        public const int ReachCrouchMid = 1;
        public const int ReachCrouchLow = 2;

        [SerializeField] private BoxCollider2D hitboxCollider;
        [SerializeField] private KungFuVania.Player.PlayerController facingSource;
        [SerializeField] private float crouchMidLocalOffsetY = 0.55f;
        [SerializeField] private float crouchLowLocalOffsetY = 0.15f;
        // Crouch sprites are calibrated to a shorter silhouette than standing (see GAME_PLAN crouch
        // height note), so a crouch attack's reach must shrink to match — reusing the standing box
        // makes it reach well past where the fist/foot is actually drawn.
        [SerializeField] private Vector2 crouchHitboxSize = new Vector2(0.375f, 0.3125f);

        private Vector3 baseLocalPosition;
        private Vector2 baseSize;

        public HitboxDataSO activeHitboxData;

        private void Awake()
        {
            baseLocalPosition = hitboxCollider.transform.localPosition;
            baseSize = hitboxCollider.size;
        }

        public void Activate(int index)
        {
            var pos = baseLocalPosition;
            var isCrouch = index == ReachCrouchMid || index == ReachCrouchLow;
            if (index == ReachCrouchMid) pos.y = crouchMidLocalOffsetY;
            else if (index == ReachCrouchLow) pos.y = crouchLowLocalOffsetY;

            if (facingSource != null)
                pos.x = facingSource.FacingRight ? Mathf.Abs(pos.x) : -Mathf.Abs(pos.x);

            hitboxCollider.transform.localPosition = pos;
            hitboxCollider.size = isCrouch ? crouchHitboxSize : baseSize;
            hitboxCollider.enabled = true;
        }

        public void Deactivate(int index) => hitboxCollider.enabled = false;
    }
}
