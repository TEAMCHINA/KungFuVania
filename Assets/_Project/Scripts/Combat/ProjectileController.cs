using UnityEngine;

namespace KungFuVania.Combat
{
    // Detached, independently-moving carrier for a traveling special (GAME_PLAN.md 3l
    // "Projectile System") -- unlike every melee attack's hitbox (a permanent child of its
    // attacker, keyframed on the attacker's own clip), this is its own GameObject with its own
    // HitboxController, so the existing attacker-driven OnZoneHit/LateFixedUpdate/
    // DamageCalculator.Resolve chain (3n) works completely unchanged -- a projectile is just an
    // "attacker" that also moves and despawns on its own.
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(HitboxController))]
    public class ProjectileController : MonoBehaviour
    {
        [SerializeField] private float speed = 10f;
        // Finite by default, deliberately deviating from the doc's "0 = unbounded" sketch: the
        // real intent for unbounded was "despawn on leaving the room," but no room-bounds system
        // (CameraManager, 3t) exists yet, and the doc itself calls the flat-distance fallback for
        // that case a TEMPORARY safety net. A real, finite range sidesteps needing that stand-in
        // at all for this pass -- revisit once room bounds exist and truly-unbounded specials
        // are actually wanted.
        [SerializeField] private float maxRange = 8f;
        [SerializeField] private bool despawnOnTerrainHit;
        // null = default to despawn on first contact (see OnTriggerEnter2D) -- no concrete
        // subclass needed for the common case, matching every other composed-behavior slot here.
        [SerializeField] private ProjectileHitBehaviorSO onHitBehavior;
        [SerializeField] private LayerMask terrainLayer;

        private Rigidbody2D rb;
        private HitboxController hitboxController;
        private ProjectileManager manager;
        private Vector2 spawnPosition;
        private Transform caster;
        // Guards against a single wide/overlapping hurtbox generating more than one
        // OnTriggerEnter2D for this contact before Despawn's Destroy() actually takes effect
        // (Destroy is deferred to end of frame, not immediate).
        private bool consumed;
        // Set instead of despawning directly from OnTriggerEnter2D on a hit -- this object's own
        // OnTriggerEnter2D and the target's HurtboxZoneForwarder->HitboxController.OnZoneHit are
        // two independent callbacks reacting to the same physics overlap, in Unity-unspecified
        // order. Despawning synchronously here risked winning that race and disabling/destroying
        // this object before the target's callback ever registered the hit for LateUpdate's
        // damage resolution -- confirmed as the reason damage never applied, every time (a
        // consistent dispatch order, not a flaky one). Consumed in LateUpdate instead, which runs
        // after every OnTriggerEnter2D for this step across every object, so the hit is always
        // recorded first.
        private bool pendingDespawn;

        public HitboxController HitboxController => hitboxController;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            hitboxController = GetComponent<HitboxController>();
        }

        // Called by ProjectileManager immediately after Instantiate. Matches the Spawn signature
        // below -- a Vector2 direction rather than just a facing sign, so a future non-horizontal
        // special isn't blocked, even though every caller today only ever passes Vector2.left/right.
        // caster is who fired this -- see OnTriggerEnter2D for why that has to be excluded from
        // hit detection: a spawn offset close to the caster's own body (a few tenths of a unit,
        // roughly chest height) overlaps their own hurtbox far more easily than it looks like it
        // should, and without this the projectile despawns on its own caster the instant it
        // spawns, completely independent of maxRange -- confirmed live: raising maxRange did
        // nothing because the despawn was never a range/terrain issue, it was self-hit.
        public void Launch(ProjectileManager owner, Vector2 position, Vector2 direction, HitboxDataSO hitboxData, Transform caster = null)
        {
            manager = owner;
            this.caster = caster;
            spawnPosition = position;
            transform.position = position;

            rb.linearVelocity = direction.normalized * speed;
            // Mirrors the visual to face travel direction via localScale.x, not a separate flip
            // flag -- same convention the player/NPCs already use (3n). Only the horizontal sign
            // matters for a 2D sprite flip.
            var facingSign = direction.x < 0f ? -1f : 1f;
            transform.localScale = new Vector3(facingSign, 1f, 1f);

            hitboxController.activeHitboxData = hitboxData;
            hitboxController.Activate();
        }

        private void FixedUpdate()
        {
            if (maxRange > 0f && Vector2.Distance(spawnPosition, rb.position) >= maxRange)
                Despawn();
        }

        // Runs after every OnTriggerEnter2D for this step (across every object) has already
        // fired -- see pendingDespawn.
        private void LateUpdate()
        {
            if (pendingDespawn) Despawn();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed) return;
            // IsChildOf covers both the caster's own root collider and any child hurtbox/collider
            // under it, not just an exact-transform match -- the caster's root and a separate
            // child "Hurtbox" object can both be independently overlapping at spawn (confirmed
            // live), and either one carrying HurtboxController would otherwise self-despawn this
            // the instant it's created.
            if (caster != null && other.transform.IsChildOf(caster)) return;

            if (((1 << other.gameObject.layer) & terrainLayer.value) != 0)
            {
                // Terrain is a flag, not a pluggable behavior (3l) -- the collider always
                // overlaps terrain regardless of despawnOnTerrainHit; the flag only gates what
                // happens on that contact, so passing through terrain costs nothing extra.
                if (despawnOnTerrainHit) Despawn();
                return;
            }

            var target = other.GetComponent<HurtboxController>();
            if (target == null) return;

            consumed = true;
            if (onHitBehavior != null) onHitBehavior.OnHit(this, target);
            // Deferred, not despawned here directly -- see pendingDespawn.
            else pendingDespawn = true;
        }

        public void Despawn()
        {
            hitboxController.Deactivate();
            manager.Despawn(this);
        }
    }
}
