using UnityEngine;
using KungFuVania.Core;

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

        private float struckUntil;

        private void OnEnable() => EventBus.Subscribe<OnEntityDamaged>(HandleDamaged);
        private void OnDisable() => EventBus.Unsubscribe<OnEntityDamaged>(HandleDamaged);

        private void Update()
        {
            if (spriteRenderer != null && Time.time >= struckUntil)
                spriteRenderer.sprite = idleSprite;
        }

        private void HandleDamaged(OnEntityDamaged evt)
        {
            if (evt.Target != gameObject) return;

            struckUntil = Time.time + struckPoseDuration;
            if (spriteRenderer != null) spriteRenderer.sprite = evt.IsLowHit ? struckLowSprite : struckHighSprite;
        }
    }
}
