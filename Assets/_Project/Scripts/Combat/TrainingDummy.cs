using UnityEngine;

namespace KungFuVania.Combat
{
    [RequireComponent(typeof(HurtboxController))]
    public class TrainingDummy : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite idleSprite;
        [SerializeField] private Sprite struckHighSprite;
        [SerializeField] private Sprite struckLowSprite;
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

        private void HandleHit(float damage, bool isLow)
        {
            struckUntil = Time.time + struckPoseDuration;
            if (spriteRenderer != null) spriteRenderer.sprite = isLow ? struckLowSprite : struckHighSprite;
        }
    }
}
