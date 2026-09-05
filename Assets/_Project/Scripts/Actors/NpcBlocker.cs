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

        // Moves horizontally by deltaX, casting from the leading edge of bodyCollider first and
        // clamping to whatever it hits — mirrors DodgeState.MoveWithoutTunneling, for the same
        // reason: MovePosition on a Kinematic body performs no collision response of its own, so
        // without this an NPC can be pushed straight through a wall. Returns the delta actually
        // applied, so the pusher can be kept in sync (e.g. stopped) when this NPC gets jammed.
        public float MoveBy(float deltaX, LayerMask obstructionMask)
        {
            if (deltaX == 0f) return 0f;

            var halfWidth = bodyCollider.bounds.extents.x;
            var castDirection = deltaX > 0f ? Vector2.right : Vector2.left;
            var origin = (Vector2)bodyCollider.bounds.center + castDirection * halfWidth;
            var distance = Mathf.Abs(deltaX);

            var hit = Physics2D.Raycast(origin, castDirection, distance, obstructionMask);
            if (hit.collider != null)
                distance = Mathf.Max(0f, hit.distance - 0.01f);

            rb.MovePosition(rb.position + castDirection * distance);
            return castDirection.x * distance;
        }
    }
}
