// Bullet core: flat unlit colour with a black ink outline that stays a fixed number of pixels wide.
// The outline is the back faces pushed outwards on screen from the mesh centre, which suits a small
// convex mesh and leaves no gaps at hard edges. No fog: shots must read through the street haze.
Shader "WaitASecond/ShotSolid"
{
    Properties
    {
        _BaseColor ("Fill", Color) = (0.95, 0.76, 0.11, 1)
        _OutlineColor ("Outline", Color) = (0, 0, 0, 1)
        _OutlinePixels ("Outline Width (pixels at 1080p screen height)", Range(0, 6)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _OutlineColor;
            float _OutlinePixels;
        CBUFFER_END

        float4 FillVert(float4 positionOS : POSITION) : SV_POSITION
        {
            return TransformObjectToHClip(positionOS.xyz);
        }
        ENDHLSL

        Pass
        {
            Name "Fill"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex FillVert
            #pragma fragment Frag

            half4 Frag() : SV_Target
            {
                return half4(_BaseColor.rgb, 1.0);
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
            #pragma vertex FillVert
            #pragma fragment DepthFrag

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
