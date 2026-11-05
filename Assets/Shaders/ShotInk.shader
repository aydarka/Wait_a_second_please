// Flat vertex-coloured shapes for the shot tells: the red aim glint and the yellow muzzle flash.
// Unlit and without fog; drawn after the scene without depth writes, so later triangles of a mesh
// ink over earlier ones (the yellow star over its black outline).
Shader "WaitASecond/ShotInk"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Ink"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(input.color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
