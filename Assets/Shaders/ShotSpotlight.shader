// Spotlight: one flat colour drawn late in the frame. As a black card with no depth test it hides the world
// (fully on the death screen, partly when the player may shoot back: the colour's alpha); drawn again over a
// figure's own meshes with a depth test, it lifts only that figure out of the dark, cut wherever cover stands
// in front of it.
Shader "WaitASecond/ShotSpotlight"
{
    Properties
    {
        _Color ("Colour", Color) = (0, 0, 0, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+100" }

        Pass
        {
            Name "Spotlight"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            // Pulled slightly forward so the copy over the shooter wins the depth test against his own surface.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _ZTest;
            CBUFFER_END

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 Frag() : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
