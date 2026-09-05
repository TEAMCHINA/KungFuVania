using UnityEngine;

namespace KungFuVania.Combat
{
    [RequireComponent(typeof(HurtboxController))]
    public class TrainingDummy : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite idleSprite;
        // All attacks are high hits for now — no low attacks or crouch exist yet to trigger a
        // low reaction. Add a struckLowSprite + height check on HitboxDataSO when that lands.
        [SerializeField] private Sprite struckHighSprite;
        [SerializeField] private float struckPoseDuration = 0.2f;

        private HurtboxController hurtbox;
        private float struckUntil;

        private void Awake()
        {
            hurtbox = GetComponent<HurtboxController>();
        }

        private void OnEnable() => hurtbox.OnHit += HandleHit;
        private void OnDisable() => hurtbox.OnHit -= HandleHit;

        private void Update()
        {
            if (spriteRenderer != null && Time.time >= struckUntil)
                spriteRenderer.sprite = idleSprite;
        }

        private void HandleHit(float damage)
        {
            struckUntil = Time.time + struckPoseDuration;
            if (spriteRenderer != null) spriteRenderer.sprite = struckHighSprite;
        }
    }
}
