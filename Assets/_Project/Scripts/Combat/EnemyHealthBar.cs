using UnityEngine;

namespace KungFuVania.Combat
{
    // Drop-in world-space health bar: hidden at full health, otherwise a white-bordered bar with
    // current health filling green from the left and missing health showing red underneath.
    // Builds its own sprites procedurally, so it needs no scene wiring beyond adding the
    // component. Its visuals live on an independent, unparented GameObject that only copies
    // position each frame, so it is immune to the entity's own facing-flip (negative scale) or
    // rotation, and never needs to be re-parented into a future shared UI canvas by hand.
    [RequireComponent(typeof(Health))]
    public class EnemyHealthBar : MonoBehaviour
    {
        [SerializeField] private Vector2 size = new Vector2(1f, 0.15f);
        [SerializeField] private float borderThickness = 0.025f;
        [SerializeField] private float yOffset = 2.2f;

        private static Sprite squareSprite;

        private Health health;
        private Transform barRoot;
        private SpriteRenderer border;
        private SpriteRenderer missingFill;
        private SpriteRenderer currentFill;
        private Vector2 insetSize;

        private void Awake()
        {
            health = GetComponent<Health>();

            var sprite = GetSquareSprite();
            insetSize = size - new Vector2(borderThickness, borderThickness) * 2f;
            var insetOffset = new Vector2(borderThickness, 0f);

            barRoot = new GameObject(name + "_HealthBar").transform;
            border = CreateBar(barRoot, sprite, "Border", Color.white, size, Vector2.zero, 20);
            missingFill = CreateBar(barRoot, sprite, "Missing", Color.red, insetSize, insetOffset, 21);
            currentFill = CreateBar(barRoot, sprite, "Current", Color.green, insetSize, insetOffset, 22);
        }

        private void OnEnable() => health.OnHealthChanged += HandleHealthChanged;
        private void OnDisable() => health.OnHealthChanged -= HandleHealthChanged;
        private void OnDestroy() { if (barRoot != null) Destroy(barRoot.gameObject); }

        private void Start() => HandleHealthChanged(health.CurrentHealth, health.MaxHealth);

        // All three bars use a left-pivot sprite (see GetSquareSprite), so barRoot's position is
        // the bar's left edge, not its center — shift left by half the total width so the bar is
        // actually centered over the entity rather than starting there.
        private void LateUpdate() => barRoot.position = transform.position + new Vector3(-size.x * 0.5f, yOffset, 0f);

        private void HandleHealthChanged(float current, float max)
        {
            var atFullHealth = current >= max;
            border.enabled = !atFullHealth;
            missingFill.enabled = !atFullHealth;
            currentFill.enabled = !atFullHealth;

            var fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            currentFill.transform.localScale = new Vector3(insetSize.x * fraction, insetSize.y, 1f);
        }

        private static SpriteRenderer CreateBar(Transform parent, Sprite sprite, string barName, Color color, Vector2 barSize, Vector2 offset, int sortingOrder)
        {
            var go = new GameObject(barName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localScale = new Vector3(barSize.x, barSize.y, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        private static Sprite GetSquareSprite()
        {
            if (squareSprite != null) return squareSprite;

            var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            // Left-center pivot: shrinking localScale.x keeps the left edge fixed and drains the
            // sprite from the right, so the current-health fill empties from the right correctly.
            squareSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0f, 0.5f), 1f);
            return squareSprite;
        }
    }
}
