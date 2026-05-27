Shader "Custom/TransparentVideo"
{
    Properties
    {
        _MainTex ("Video RenderTexture", 2D) = "white" {}

        // --- Transparency mode ---
        // WebM_Alpha : use the video's own alpha channel (WebM VP8/VP9 + alpha)
        // Chroma_Key : shader-based colour-difference keying with spill suppression (BONUS)
        // Luma_Key   : luminance-based keying (black-background videos)
        [KeywordEnum(WebM_Alpha, Chroma_Key, Luma_Key)]
        _TransparencyMode ("Transparency Mode", Float) = 0

        // Chroma / spill settings
        _KeyColor        ("Key Colour",          Color)        = (0, 1, 0, 1)
        _Threshold       ("Key Threshold",       Range(0,1))   = 0.35
        _Smoothing       ("Edge Smoothing",      Range(0,0.5)) = 0.08
        _SpillSuppress   ("Spill Suppression",   Range(0,1))   = 0.6

        // Luma key settings
        _LumaThreshold   ("Luma Threshold",      Range(0,1))   = 0.1
        _LumaSmoothing   ("Luma Smoothing",      Range(0,0.5)) = 0.05

        // Global
        _Opacity         ("Opacity",             Range(0,1))   = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off        // double-sided so wall is visible from both sides

        Pass
        {
            Name "TransparentVideoForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // One variant per mode; shader_feature strips unused variants from build
            #pragma shader_feature_local _TRANSPARENCYMODE_WEBM_ALPHA _TRANSPARENCYMODE_CHROMA_KEY _TRANSPARENCYMODE_LUMA_KEY

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _KeyColor;
                float  _Threshold;
                float  _Smoothing;
                float  _SpillSuppress;
                float  _LumaThreshold;
                float  _LumaSmoothing;
                float  _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                // Flip U so the video reads correctly when viewed from the front face
                OUT.uv.x        = 1.0 - OUT.uv.x;
                return OUT;
            }

            // ---------------------------------------------------------------
            // Shader-based chroma key masking (BONUS)
            //   Uses colour-difference keying in opponent colour space.
            //   More robust than a plain RGB threshold — handles lighting
            //   variations and partial transparency at edges.
            // ---------------------------------------------------------------
            float ChromaKeyAlpha(float3 col, float3 keyCol, float threshold, float smoothing)
            {
                // Project onto opponent colour axes relative to key hue
                float Cg = col.g - (col.r * 0.5 + col.b * 0.5);       // green-opponent
                float Co = col.r - col.b;                               // orange-teal opponent
                float kCg = keyCol.g - (keyCol.r * 0.5 + keyCol.b * 0.5);
                float kCo = keyCol.r - keyCol.b;

                float dist = length(float2(Cg - kCg, Co - kCo));
                return smoothstep(threshold - smoothing, threshold + smoothing, dist);
            }

            // Green spill suppression — desaturates the key hue on semi-transparent edges
            float3 SpillSuppression(float3 col, float3 keyCol, float alpha, float suppression)
            {
                float spill = (1.0 - alpha) * suppression;
                // Replace the key channel with the max of the other two channels
                float3 result = col;
                // Find dominant key channel index
                float maxOther = max(col.r, col.b);
                result.g = lerp(col.g, min(col.g, maxOther), spill);
                return result;
            }

            // ---------------------------------------------------------------
            // Luminance key — keys out dark backgrounds
            // ---------------------------------------------------------------
            float LumaKeyAlpha(float3 col, float threshold, float smoothing)
            {
                float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
                return smoothstep(threshold - smoothing, threshold + smoothing, luma);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float alpha = _Opacity;

            #if defined(_TRANSPARENCYMODE_CHROMA_KEY)
                // --- Shader-based masking (BONUS) ---
                float keyAlpha   = ChromaKeyAlpha(col.rgb, _KeyColor.rgb, _Threshold, _Smoothing);
                col.rgb          = SpillSuppression(col.rgb, _KeyColor.rgb, keyAlpha, _SpillSuppress);
                alpha           *= keyAlpha;

            #elif defined(_TRANSPARENCYMODE_LUMA_KEY)
                // --- Luminance masking ---
                alpha *= LumaKeyAlpha(col.rgb, _LumaThreshold, _LumaSmoothing);

            #else
                // --- WebM VP8/VP9 native alpha channel (default) ---
                alpha *= col.a;
            #endif

                return half4(col.rgb, saturate(alpha));
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}
