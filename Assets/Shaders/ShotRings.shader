// Matrix-style air rings a bullet leaves hanging along its path: one quad per ring, the circle is drawn here.
// Each ring lives in game time (_ShotTime, set by ShotEffects): it opens from bullet size, then its line
// thins and crumbles into dashes. No transparency and no fog: a ring is either inked or not there.
Shader "WaitASecond/ShotRings"
{
    Properties
    {
        _InkColor ("Ring", Color) = (0.95, 0.94, 0.92, 1)
        _OutlineColor ("Outline", Color) = (0, 0, 0, 1)
        _MinLinePixels ("Min Line Width (pixels at 1080p screen height)", Range(0, 6)) = 1.6
        _OutlinePixels ("Outline Width (pixels at 1080p screen height)", Range(0, 6)) = 1.2
        _Segments ("Crumble Segments", Float) = 36
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Name "Rings"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InkColor;
                half4 _OutlineColor;
                float _MinLinePixels;
                float _OutlinePixels;
                float _Segments;
            CBUFFER_END

            float _ShotTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 shape : TEXCOORD0;   // corner x, corner y (-1..1), quad half size (m), ring radius (m)
                float4 life : TEXCOORD1;    // birth time, lifetime (game s), strength, seed
                float4 growth : TEXCOORD2;  // opening time (game s), line width (m)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 shape : TEXCOORD0;
                float4 life : TEXCOORD1;
                float4 growth : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.shape = input.shape;
                output.life = input.life;
                output.growth = input.growth;
                return output;
            }

            float Hash(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float alive = _ShotTime - input.life.x;
                float age = alive / max(input.life.y, 1e-4);
                // Not born yet, or already crumbled away.
                clip(min(age, 1.0 - age));

                float2 corner = input.shape.xy;
                float fromCentre = length(corner) * input.shape.z;
                float open = saturate(alive / max(input.growth.x, 1e-4));
                float radius = input.shape.w * (0.3 + 0.7 * open);

                // Metres per pixel here: line and outline keep a minimum width on screen.
                float pixel = max(fwidth(fromCentre), 1e-6) * _ScreenParams.y / 1080.0;
                float thinning = input.life.z * (1.0 - 0.55 * age);
                float halfLine = 0.5 * thinning * max(input.growth.y, _MinLinePixels * pixel);
                float edge = abs(fromCentre - radius);
                clip(halfLine + _OutlinePixels * pixel - edge);

                // Whole arc segments drop out as the ring ages, outline included.
                float angle = atan2(corner.y, corner.x);
                float segment = floor((angle / (2.0 * PI) + 0.5) * _Segments);
                clip(Hash(input.life.w * 97.0 + segment) - pow(saturate(age), 1.6) * 1.05);

                return edge <= halfLine ? _InkColor : _OutlineColor;
            }
            ENDHLSL
        }
    }
}
