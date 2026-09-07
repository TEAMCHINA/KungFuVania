using UnityEngine;

namespace KungFuVania.Combat
{
    // Cycles a fixed set of sprites on a loop for a projectile's whole lifetime -- a plain
    // SpriteRenderer swap rather than another Animator Controller + AnimationClip pair, since a
    // short, always-looping travel visual with no combat-state-machine involvement doesn't need
    // the overhead of authoring a full clip/state for something this simple.
    [RequireComponent(typeof(SpriteRenderer))]
    public class ProjectilePulseAnimator : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.08f;

        private SpriteRenderer spriteRenderer;
        private int frameIndex;
        private float nextFrameTime;

        private void Awake() => spriteRenderer = GetComponent<SpriteRenderer>();

        private void OnEnable()
        {
            frameIndex = 0;
            nextFrameTime = Time.time + frameDuration;
            if (frames.Length > 0) spriteRenderer.sprite = frames[0];
        }

        private void Update()
        {
            if (frames.Length < 2 || Time.time < nextFrameTime) return;

            frameIndex = (frameIndex + 1) % frames.Length;
            spriteRenderer.sprite = frames[frameIndex];
            nextFrameTime = Time.time + frameDuration;
        }
    }
}
