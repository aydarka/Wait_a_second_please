// Unlit flat-shaded city blocks for mobile: tone by face direction, procedural window grid, fog.
// Window layout comes from the object's own scale and position, so one material serves every
// building and the SRP Batcher keeps them all in a few batches.
Shader "WaitASecond/NoirCity"
{
    Properties
    {
        _BaseColor ("Wall Tone", Color) = (0.08, 0.08, 0.08, 1)
        _ToneVariation ("Tone Variation Between Objects", Range(0, 0.6)) = 0.25
        _FaceShade ("Face Shade (top, side X, side Z)", Vector) = (1.4, 1.0, 0.75, 0)
        [Toggle] _WindowsEnabled ("Windows", Float) = 1
        [HDR] _WindowColor ("Lit Window", Color) = (0.85, 0.83, 0.78, 1)
        _DarkWindowShade ("Unlit Window Shade", Range(0, 1)) = 0.55
        _WindowSpacing ("Window Spacing (horizontal, vertical, m)", Vector) = (2.4, 3.2, 0, 0)
        _WindowSize ("Window Size (fraction of cell)", Vector) = (0.42, 0.5, 0, 0)
        _LitRange ("Lit Share Per Building (min, max)", Vector) = (0.05, 0.75, 0, 0)
        _FacadeMargins ("Facade Margins (side, ground floor, top, m)", Vector) = (0.8, 3.5, 1.2, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _ToneVariation;
            half4 _FaceShade;
            half _WindowsEnabled;
            half4 _WindowColor;
            half _DarkWindowShade;
            float4 _WindowSpacing;
            float4 _WindowSize;
            float4 _LitRange;
            float4 _FacadeMargins;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // x: across the face, y: height from the bottom, z: half face width, w: face height (metres).
                float4 facade : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                // x: per-object seed, y: 1 on vertical faces, z: fog factor.
                float3 data : TEXCOORD2;
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                // Facade coordinates in metres assume the unit cube mesh (-0.5..0.5 on every axis).
                float3 scale = float3(
                    length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                    length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)),
                    length(float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22)));
                float3 metres = input.positionOS.xyz * scale;
                bool facesX = abs(input.normalOS.x) > 0.5;
                output.facade = float4(
                    facesX ? metres.z : metres.x,
                    metres.y + scale.y * 0.5,
                    (facesX ? scale.z : scale.x) * 0.5,
                    scale.y);

                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                output.data = float3(
                    Hash11(dot(origin, float3(0.1371, 0.7313, 0.4197))) * 97.0,
                    abs(input.normalOS.y) < 0.5 ? 1.0 : 0.0,
                    ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float seed = input.data.x;
                half3 n = abs(normalize(input.normalWS));
                half shade = n.y * _FaceShade.x + n.x * _FaceShade.y + n.z * _FaceShade.z;
                half tone = 1.0 + (Hash11(seed) * 2.0 - 1.0) * _ToneVariation;
                half3 color = _BaseColor.rgb * shade * tone;

                if (_WindowsEnabled > 0.5 && input.data.y > 0.5)
                {
                    float2 p = input.facade.xy;
                    float inside = step(abs(p.x), input.facade.z - _FacadeMargins.x)
                        * step(_FacadeMargins.y, p.y)
                        * step(p.y, input.facade.w - _FacadeMargins.z);

                    float2 cellCoord = float2(p.x / _WindowSpacing.x + 0.5, (p.y - _FacadeMargins.y) / _WindowSpacing.y);
                    float2 cell = floor(cellCoord);
                    float2 offset = abs(frac(cellCoord) - 0.5);
                    // Soften the edges by one pixel so distant grids shimmer less on small screens.
                    float2 blur = max(fwidth(cellCoord), 1e-4);
                    float2 halfSize = _WindowSize.xy * 0.5;
                    float2 mask = 1.0 - smoothstep(halfSize - blur, halfSize + blur, offset);
                    float window = mask.x * mask.y * inside;

                    float litShare = lerp(_LitRange.x, _LitRange.y, Hash11(seed * 1.37 + 7.0));
                    float lit = step(Hash21(cell + seed), litShare);
                    half3 windowColor = lerp(color * _DarkWindowShade, _WindowColor.rgb, lit);
                    color = lerp(color, windowColor, window);
                }

                color = MixFog(color, input.data.z);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
