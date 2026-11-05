// Bullet core: three flat tones of yellow, cut like comic shading, with a black ink outline that stays
// a fixed number of pixels wide. The tones follow the city's key light for volume, and the back of the
// bullet sits a tone darker so its direction reads from the side.
// The outline is the back faces pushed outwards on screen from the mesh centre, which suits a small
// convex mesh and leaves no gaps at hard edges. No fog: shots must read through the street haze.
Shader "WaitASecond/ShotSolid"
{
    Properties
    {
        _LightColor ("Lit Tone", Color) = (0.98, 0.89, 0.54, 1)
        _BaseColor ("Mid Tone", Color) = (0.95, 0.76, 0.11, 1)
        _ShadeColor ("Shade Tone", Color) = (0.69, 0.49, 0.06, 1)
        _LightDirection ("Key Light Direction (towards the light)", Vector) = (0.35, 0.45, -1, 0)
        _LitThreshold ("Lit Threshold", Range(-1, 1)) = 0.55
        _ShadeThreshold ("Shade Threshold", Range(-1, 1)) = -0.15
        _TailEnd ("Tail End (object-space z, m)", Float) = -0.003
        _OutlineColor ("Outline", Color) = (0, 0, 0, 1)
        _OutlinePixels ("Outline Width (pixels at 1080p screen height)", Range(0, 6)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _LightColor;
            half4 _BaseColor;
            half4 _ShadeColor;
            float4 _LightDirection;
            half _LitThreshold;
            half _ShadeThreshold;
            float _TailEnd;
            half4 _OutlineColor;
            float _OutlinePixels;
        CBUFFER_END

        float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
        {
            return TransformObjectToHClip(positionOS.xyz);
        }
        ENDHLSL

        Pass
        {
            Name "Fill"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float alongOS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // The mesh points its nose along +z, the flight direction.
                output.alongOS = input.positionOS.z;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half facing = dot(normalize(input.normalWS), normalize(_LightDirection.xyz));
                half tone = facing > _LitThreshold ? 2.0 : (facing > _ShadeThreshold ? 1.0 : 0.0);
                tone = max(0.0, tone - (input.alongOS < _TailEnd ? 1.0 : 0.0));
                half3 color = tone > 1.5 ? _LightColor.rgb : (tone > 0.5 ? _BaseColor.rgb : _ShadeColor.rgb);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                float4 positionCS = TransformObjectToHClip(positionOS.xyz);
                float4 centreCS = TransformObjectToHClip(float3(0.0, 0.0, 0.0));
                float2 outward = (positionCS.xy / positionCS.w - centreCS.xy / centreCS.w) * _ScreenParams.xy;
                float2 direction = outward / max(length(outward), 1e-6);
                float pixels = _OutlinePixels * _ScreenParams.y / 1080.0;
                positionCS.xy += direction * pixels * 2.0 / _ScreenParams.xy * positionCS.w;
                return positionCS;
            }

            half4 Frag() : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0);
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

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
