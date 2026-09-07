using UnityEngine;

namespace KungFuVania.Actors
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcBlocker : MonoBehaviour
    {
        [SerializeField] private bool blocksPlayer = true;
        // Game-design "how hard to push" weight, deliberately not Rigidbody2D.mass — Unity hides
        // that field in the Inspector for Kinematic bodies (it drives force/momentum dynamics we
        // never use here), which would make this unconfigurable without a script.
        [SerializeField] private float mass = 1f;
        // The physical footprint used for MoveBy's obstruction cast — the NpcBody trigger child's
        // own collider (see the scene's "NpcBody" child), not the Rigidbody2D's GameObject.
        [SerializeField] private Collider2D bodyCollider;

        private Rigidbody2D rb;

        public bool BlocksPlayer => blocksPlayer;
        public float Mass => mass;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
        }

        public Vector2 GetPosition() => rb.position;

        // Moves horizontally by deltaX, sweeping bodyCollider's own full box (not a single-height
        // raycast from its leading edge, the original approach — see below) and clamping to
        // whatever it hits. Mirrors DodgeState.MoveWithoutTunneling's intent (MovePosition on a
        // Kinematic body performs no collision response of its own, so without this an NPC can be
        // pushed straight through a wall), but not its implementation anymore. Returns the delta
        // actually applied, so the pusher can be kept in sync (e.g. stopped) when this NPC gets jammed.
        //
        // A single raycast at bodyCollider's own vertical center used to do this instead, and
        // missed short obstacles entirely: LeftWall (tall, roof-to-floor) was always caught, but
        // LeftPlatform (top edge well below NpcBody's center height) sat completely outside that
        // one ray's line, so a push toward it went through with zero clamping — confirmed live
        // (MoveBy(-1.5f, ...) returned -1.5, uncapped, aimed squarely at LeftPlatform). A BoxCast
        // of the real body size checks its whole vertical extent at once, so a short obstacle only
        // has to overlap any part of that height, not one exact line.
        // Vertical inset applied to the swept box, below. Not tunable per-instance — it only
        // exists to clear this body's own resting contact with the ground, not to express any
        // real gameplay footprint, so one project-wide constant is correct, not a compromise.
        private const float GroundContactSkin = 0.1f;

        public float MoveBy(float deltaX, LayerMask obstructionMask)
        {
            if (deltaX == 0f) return 0f;

            var bounds = bodyCollider.bounds;
            var castDirection = deltaX > 0f ? Vector2.right : Vector2.left;
            var distance = Mathf.Abs(deltaX);

            // Insetting vertically keeps the swept box from touching whatever this body is
            // already resting on (the ground sits exactly flush against its own bottom edge) --
            // BoxCast reports an already-touching collider as a distance-0 hit on every call
            // (Physics2D.queriesStartInColliders is true project-wide), which clamped ALL
            // movement to zero regardless of direction or any actual wall, caught live: even a
            // push with nothing whatsoever in its path returned actualDelta 0 before this inset.
            // Horizontal size is untouched -- the ground only ever touches vertically.
            var castSize = new Vector2(bounds.size.x, Mathf.Max(0.01f, bounds.size.y - GroundContactSkin));

            var hit = Physics2D.BoxCast(bounds.center, castSize, 0f, castDirection, distance, obstructionMask);
            if (hit.collider != null)
                distance = Mathf.Max(0f, hit.distance - 0.01f);

            rb.MovePosition(rb.position + castDirection * distance);
            return castDirection.x * distance;
        }
    }
}
