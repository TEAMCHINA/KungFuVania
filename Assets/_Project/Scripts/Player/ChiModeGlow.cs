using UnityEngine;

namespace KungFuVania.Player
{
    // No-new-ART-assets OUTER glow for Chi Mode (GAME_PLAN.md 3l) — replaces an earlier flat
    // pulsing-tint version. Fakes a soft/feathered halo by layering several runtime-only,
    // enlarged, increasingly transparent copies of the current sprite behind the real one; no
    // baked art, nothing saved to the scene beyond this component's own tunables (the layers are
    // plain child GameObjects created in Awake and never persisted). Uses one small custom
    // shader (ChiGlowSilhouette.shader) so the layers render as a solid glowColor silhouette
    // instead of the sprite's own colors — see CreateGlowMaterial for why plain SpriteRenderer
    // tinting can't do that.
    // Sprites here import as Bilinear (see KFV_Idle_*.png.meta), so scaling them up softens
    // the edge on its own instead of showing blocky point-filtered pixels.
    [RequireComponent(typeof(SpriteRenderer))]
    public class ChiModeGlow : MonoBehaviour
    {
        [SerializeField] private Color glowColor = new Color(1f, 0.85f, 0.35f);
        // Only the margin BEYOND the sprite (1.0 to maxScale) is ever visible — everything inside
        // that is hidden behind the opaque main sprite regardless of alpha. That margin's size in
        // actual screen PIXELS, not just the scale-multiplier span, is what limits how smooth the
        // additive gradient can look: shrink maxScale and the same layerCount lands with most of
        // its layers' edges clumped within a handful of pixels of each other, jumping in
        // accumulated brightness from one pixel to the next instead of blending smoothly — the
        // exact same "looks solid/stepped" symptom as too few layers, just caused by too little
        // physical space instead. If maxScale ever goes back down toward the smaller end of what's
        // been tried (around 1.1), layerCount likely needs to go back up toward ~36 to compensate
        // (kept in rough proportion so there's still roughly one layer-edge per available pixel);
        // there's a hard floor here regardless — no layer count makes a margin smoother than the
        // screen pixels it spans. 10/1.25 is the settled default for this game's sprite/camera
        // scale specifically, not a universal ratio.
        [SerializeField] private int layerCount = 10;
        [SerializeField] private float maxScale = 1.25f;
        // Shapes each individual layer's own alpha from the sprite's edge (t=0) out to maxScale
        // (t=1) — but the visible feather comes from many overlapping layers additively summing
        // (see ChiGlowSilhouette.shader's Blend comment), not from any single layer's alpha
        // being high. Keep this peak low: with ~20 layers additively stacking near the sprite's
        // edge, a 0.6 peak (the old alpha-over value) blows the core out to flat white instead of
        // a gradient.
        [SerializeField] private AnimationCurve alphaFalloff = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 0f);
        // "Living energy" breathing — set pulseSpeed to 0 for a perfectly static halo.
        [SerializeField] private float pulseSpeed = 6f;
        // Fraction (0-1) of each layer's full configured reach (layerScale[i], up to maxScale)
        // used at the pulse's peak. The pulse's TROUGH is always exactly 1.0 (the character's
        // own scale) by construction, regardless of this value — see the Lerp in LateUpdate.
        // The old version multiplied layerScale[i] by (1 +/- pulseAmount) directly, which could
        // push even the OUTERMOST layer's scale below 1.0 at the low point (e.g. a 1.1 maxScale
        // layer * 0.85 low-pulse = 0.935) — the entire glow would periodically shrink fully
        // inside the character and vanish each cycle instead of just dimming down to it.
        [SerializeField] [Range(0f, 1f)] private float pulseAmount = 1f;
        // Pulse trough floor — a floor of exactly 1.0 (the character's own scale) meant the
        // margin fully closed at the low point, so the whole glow vanished behind the opaque
        // sprite every cycle instead of just receding. Any value > 1.0 keeps an always-visible
        // sliver; 1.01 keeps that sliver deliberately thin so the breathing motion (1.01 up to
        // maxScale) reads as one continuous sweep rather than a visible floor-then-jump. Layers
        // whose own layerScale[i] is below this (only possible if maxScale/layerCount are later
        // tuned such that some inner layers land under the floor) just sit static there instead
        // of pulsing — see the Lerp/Max pairing below — only layers that reach further than the
        // floor actually breathe. At the current 10/1.25 settings every layer's own reach already
        // exceeds 1.01, so all 10 breathe.
        [SerializeField] private float pulseFloorScale = 1.01f;

        private SpriteRenderer mainRenderer;
        private PlayerController controller;
        private Material glowMaterial;
        private SpriteRenderer[] glowLayers;
        private float[] layerScale;
        private float[] layerBaseAlpha;
        private bool layersActive;
        private bool pendingRebuild;

        private void Awake()
        {
            mainRenderer = GetComponent<SpriteRenderer>();
            controller = GetComponent<PlayerController>();
            glowMaterial = CreateGlowMaterial();
            BuildLayers();
        }

        // layerCount/maxScale/alphaFalloff only get baked into the per-layer arrays inside
        // BuildLayers, not re-read every frame (LateUpdate just reads the cached arrays) — so
        // tweaking them in the Inspector during Play Mode silently did nothing until the next
        // Awake (stop/restart Play Mode). OnValidate fires on every Inspector edit, in Edit mode
        // too, so this is gated to Play Mode only — rebuilding in Edit mode would leave real,
        // persisted "ChiGlow_N" GameObjects sitting in the saved scene, which is exactly what
        // building them in Awake (Play-Mode-only, auto-discarded on stop) was meant to avoid.
        //
        // Only sets a flag here rather than rebuilding directly — BuildLayers creates
        // GameObjects/components, and Unity logs "SendMessage cannot be called during ...
        // OnValidate" if scene-graph mutation happens synchronously inside this callback.
        // Deferring the actual rebuild to the next LateUpdate avoids that entirely.
        private void OnValidate()
        {
            if (!Application.isPlaying || mainRenderer == null) return;
            pendingRebuild = true;
        }

        // Deliberately NOT mainRenderer.sharedMaterial — the player's material is
        // Sprite-Lit-Default (URP 2D), and layering scaled, alpha-blended copies of a lit sprite
        // through 2D lighting produced unpredictable hue shifts (came out cyan against an
        // authored gold glowColor) instead of the plain color set below.
        //
        // Also deliberately NOT a plain unlit sprite shader — SpriteRenderer.color only
        // multiplies against the texture's own RGB, so a black pixel (hair, pants) stays black
        // no matter the tint: black * glowColor is still black. ChiGlowSilhouette.shader ignores
        // the texture's RGB entirely and keeps only its alpha (the silhouette shape), so RGB
        // comes solely from the SpriteRenderer's own .color (set per-layer every frame below) —
        // the whole silhouette renders as one solid glow color, sprite art invisible underneath.
        //
        // Never written to disk — a runtime-only Material instance.
        private static Material CreateGlowMaterial()
        {
            var shader = Shader.Find("KungFuVania/ChiGlowSilhouette")
                ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default");
            return shader != null ? new Material(shader) : null;
        }

        private void BuildLayers()
        {
            DestroyExistingLayers();

            glowLayers = new SpriteRenderer[layerCount];
            layerScale = new float[layerCount];
            layerBaseAlpha = new float[layerCount];

            for (var i = 0; i < layerCount; i++)
            {
                var t = layerCount > 1 ? i / (float)(layerCount - 1) : 0f;
                layerScale[i] = Mathf.Lerp(1.02f, maxScale, t);
                layerBaseAlpha[i] = alphaFalloff.Evaluate(t);

                var layerObject = new GameObject($"ChiGlow_{i}");
                layerObject.transform.SetParent(transform, false);
                layerObject.transform.localRotation = Quaternion.identity;

                var layerRenderer = layerObject.AddComponent<SpriteRenderer>();
                layerRenderer.sharedMaterial = glowMaterial != null ? glowMaterial : mainRenderer.sharedMaterial;
                layerRenderer.sortingLayerID = mainRenderer.sortingLayerID;
                // Fainter/bigger layers sit further back: i=0 (smallest, densest) lands just
                // behind the real sprite, i=layerCount-1 (biggest, faintest) sits furthest back.
                layerRenderer.sortingOrder = mainRenderer.sortingOrder - layerCount + i;
                layerRenderer.enabled = false;

                glowLayers[i] = layerRenderer;
            }

            // Force LateUpdate to re-apply .enabled on the (freshly rebuilt) layers next frame
            // rather than trusting the old layers' now-stale on/off state.
            layersActive = false;
        }

        private void DestroyExistingLayers()
        {
            if (glowLayers == null) return;
            foreach (var layer in glowLayers)
                if (layer != null) Destroy(layer.gameObject);
        }

        // After Update/animator evaluation so mainRenderer.sprite already reflects this frame's
        // pose — same ordering rationale PlayerAnimatorDriver's own comments call out.
        private void LateUpdate()
        {
            if (pendingRebuild)
            {
                BuildLayers();
                pendingRebuild = false;
            }

            var active = controller.LockFacingActive;

            if (active != layersActive)
            {
                foreach (var layer in glowLayers) layer.enabled = active;
                layersActive = active;
            }

            if (!active) return;

            // 0..1 oscillator, then scaled by pulseAmount so the peak can be dialed back — the
            // trough always stays 0 either way, which is what keeps the floor pinned to 1.0 below.
            var reach = pulseAmount * (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            // Deliberately scaling from the root — the player sprite's pivot, feet-anchored per
            // this project's convention (see sprite_pivot_convention) — rather than recentering
            // on the sprite's visual center. That recentering used to keep the halo symmetric
            // around the whole silhouette; scaling from the pivot instead grows it upward off
            // the feet, which reads as the glow radiating up from the ground rather than an even
            // aura, an intentional look, not a bug.

            for (var i = 0; i < layerCount; i++)
            {
                var layer = glowLayers[i];
                // Lerp(pulseFloorScale, peak, ...) rather than layerScale[i] * someFactor —
                // guarantees s can never drop below pulseFloorScale no matter what reach
                // evaluates to, instead of merely making it unlikely. peak is clamped up to at
                // least the floor too, so a layer whose own configured reach is smaller than the
                // floor doesn't invert (shrinking as reach increases) — it just sits flat at the
                // floor for its whole range instead of pulsing.
                var peak = Mathf.Max(layerScale[i], pulseFloorScale);
                var s = Mathf.Lerp(pulseFloorScale, peak, reach);

                // Alpha is deliberately NOT multiplied by reach — it used to fade to 0 right
                // alongside the scale floor, so the pulseFloorScale margin above existed but was
                // fully transparent at the trough, which is the exact same "disappears" symptom
                // the floor was meant to fix, just moved from scale to alpha. Alpha now stays at
                // its full configured layerBaseAlpha[i] throughout — only size breathes, not
                // visibility.
                layer.sprite = mainRenderer.sprite;
                layer.transform.localScale = Vector3.one * s;
                layer.color = new Color(glowColor.r, glowColor.g, glowColor.b, layerBaseAlpha[i]);
            }
        }
    }
}
