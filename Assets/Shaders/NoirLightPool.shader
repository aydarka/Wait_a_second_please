// Hard-edged white ellipse on a flat quad: the pool of light under a street lamp and its wet streaks on the road.
// Edges are softened by one pixel only, so it reads as a cut-out comic shape. It resists fog like other lights.
Shader "WaitASecond/NoirLightPool"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.95, 0.94, 0.9, 1)
        _LightFogScale ("Fog Scale (lower reaches further)", Range(0, 1)) = 0.55
        [Toggle] _FogHatch ("Hatched Fog (thins into diagonal lines instead of turning grey)", Float) = 1
        _HatchPixels ("Hatch Period (pixels at 1080p)", Range(2, 16)) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Pool"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _LightFogScale;
                half _FogHatch;
                float _HatchPixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2.0 - 1.0;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float distance = length(input.uv);
                float blur = max(fwidth(distance), 1e-4);
                half alpha = (1.0 - smoothstep(1.0 - blur, 1.0, distance)) * _Color.a;
                float fogFactor = input.fogFactor * _LightFogScale;
                if (_FogHatch > 0.5)
                {
                    // Same diagonal hatching as the city: the pool thins into lines instead of greying.
                    float period = _HatchPixels * max(_ScreenParams.y, 1.0) / 1080.0;
                    float hatch = frac((input.positionCS.x + input.positionCS.y) / period);
                    half visibility = saturate((ComputeFogIntensity(fogFactor) - 0.08) / 0.84);
                    return half4(_Color.rgb, alpha * step(hatch, visibility));
                }
                return half4(MixFogColor(_Color.rgb, half3(0, 0, 0), fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
