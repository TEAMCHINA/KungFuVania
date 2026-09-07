Shader "KungFuVania/ChiGlowSilhouette"
{
    // Based on the URP 2D Renderer's own Sprite-Unlit-Default
    // (Packages/com.unity.render-pipelines.universal/Shaders/2D/Sprite-Unlit-Default.shader) —
    // same tags/vertex stage, but the fragment stage discards the sprite texture's own RGB
    // entirely and keeps only its alpha (the silhouette shape). Plain SpriteRenderer.color tinting
    // can't do this: it multiplies against the texture, so a black pixel (hair, pants) stays black
    // no matter the tint color. This makes RGB come solely from the SpriteRenderer's own .color
    // (ChiModeGlow sets that per-layer every frame), so the whole silhouette renders as one solid
    // glow color regardless of the source art underneath.
    //
    // Blend is additive (SrcAlpha One), not the source shader's normal alpha-over — with N stacked
    // same-color layers, alpha-over saturates to nearly-solid the moment 2-3 of them overlap
    // (1-(1-0.6)^2 is already ~84%), so only the outermost couple of layers ever showed a visible
    // falloff and everything closer read as one flat, solid-edged shape. Additive instead SUMS
    // brightness, so the visible falloff is driven by how many of the ~20 layers overlap at a
    // given point, not by any single layer's own alpha — a real gradient across the whole margin,
    // brightest where they all stack near the sprite's edge, fading smoothly outward as fewer
    // layers still reach that far. ChiModeGlow's alphaFalloff peak is tuned low (not ~0.6) to
    // match — additive with high per-layer alpha would blow the core out to flat white instead.
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        // Legacy properties, kept only so materials using this shader can gracefully fall back to
        // the legacy sprite shader (same reason Sprite-Unlit-Default keeps them).
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha One
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment SilhouetteFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 SilhouetteFragment(Varyings input) : SV_Target
            {
                half textureAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                return half4(input.color.rgb, input.color.a * textureAlpha);
            }
            ENDHLSL
        }
    }
}
