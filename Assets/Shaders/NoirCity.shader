// Two-tone comic city for mobile: every pixel is ink (black) or paper (white).
// Faces turned to the key light are paper, the rest ink; edges, windows and shop fronts take the opposite tone.
// Box mode reads metres from the object's scale (unit cube mesh), so one material draws windows and inked edges
// on any building; model mode (cars) outlines grazing faces instead. Lights fade into the fog slower than walls.
Shader "WaitASecond/NoirCity"
{
    Properties
    {
        _InkColor ("Ink", Color) = (0, 0, 0, 1)
        [HDR] _PaperColor ("Paper", Color) = (0.95, 0.94, 0.9, 1)
        _LightDirection ("Key Light Direction (towards the light)", Vector) = (0.35, 0.45, -1, 0)
        _LitThreshold ("Lit Threshold (face facing the light)", Range(-1, 1.01)) = 0.8
        [Toggle] _SelfLit ("Self Lit (a light source: resists fog)", Float) = 0

        [Header(Edges)]
        [Toggle] _BoxEdges ("Box Edges (unit cube mesh)", Float) = 1
        _EdgeWidth ("Edge Width (m)", Range(0, 0.3)) = 0.06
        _RimThreshold ("Model Rim (1 - N.V above this is paper)", Range(0, 1.01)) = 0.78

        [Header(Windows)]
        [Toggle] _Windows ("Windows", Float) = 1
        _WindowSpacing ("Window Spacing (horizontal, vertical, m)", Vector) = (2.4, 3.2, 0, 0)
        _WindowSize ("Window Size (fraction of cell)", Vector) = (0.42, 0.5, 0, 0)
        _LitRange ("Lit Share Per Building (min, max)", Vector) = (0.04, 0.8, 0, 0)
        _FacadeMargins ("Facade Margins (side, ground floor, top, m)", Vector) = (0.8, 3.5, 1.2, 0)

        [Header(Shop Fronts)]
        [Toggle] _ShopFronts ("Shop Fronts on street-facing ground floors", Float) = 1
        _ShopChance ("Shop Chance Per Building", Range(0, 1)) = 0.45
        _ShopBand ("Shop Band (bottom, top, side margin, m)", Vector) = (0.45, 2.9, 0.7, 0)
        _ShopMullions ("Mullions (spacing, width, m)", Vector) = (2.2, 0.12, 0, 0)

        [Header(Fog)]
        _LightFogScale ("Fog Scale for Lit Pixels (lower reaches further)", Range(0, 1)) = 0.55
        [Toggle] _FogHatch ("Hatched Fog (paper thins into diagonal lines instead of turning grey)", Float) = 1
        _HatchPixels ("Hatch Period (pixels at 1080p)", Range(2, 16)) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _InkColor;
            half4 _PaperColor;
            float4 _LightDirection;
            half _LitThreshold;
            half _SelfLit;
            half _BoxEdges;
            float _EdgeWidth;
            half _RimThreshold;
            half _Windows;
            float4 _WindowSpacing;
            float4 _WindowSize;
            float4 _LitRange;
            float4 _FacadeMargins;
            half _ShopFronts;
            half _ShopChance;
            float4 _ShopBand;
            float4 _ShopMullions;
            half _LightFogScale;
            half _FogHatch;
            float _HatchPixels;
        CBUFFER_END

        // Fog without grey: where it would fade paper towards the ink-coloured fog, diagonal screen-space
        // lines thin out instead, so the picture stays two-tone. Returns how much of the paper survives.
        half HatchedFog(float2 pixel, float fogFactor)
        {
            // Keep the first metres solid and let the last faint lines go, so hatching only shows in the middle distance.
            half visibility = saturate((ComputeFogIntensity(fogFactor) - 0.08) / 0.84);
            float period = _HatchPixels * max(_ScreenParams.y, 1.0) / 1080.0;
            float hatch = frac((pixel.x + pixel.y) / period);
            return step(hatch, visibility);
        }
        ENDHLSL

        Pass
        {
            Name "Ink"
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
                // Position on the face in metres from its centre (x, y) and the face half size (z, w).
                float4 face : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                // x: per-object seed, y: 1 on vertical faces, z: 1 on vertical faces looking at the street, w: fog factor.
                float4 data : TEXCOORD3;
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

            // Coverage of a band of half width `halfWidth` around zero, softened by one pixel.
            float Band(float distance, float halfWidth)
            {
                float blur = max(fwidth(distance), 1e-4);
                return 1.0 - smoothstep(halfWidth - blur, halfWidth + blur, distance);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);

                float3 scale = float3(
                    length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                    length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)),
                    length(float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22)));
                float3 metres = input.positionOS.xyz * scale;
                float3 n = abs(input.normalOS);
                if (n.x > 0.5)
                    output.face = float4(metres.z, metres.y, scale.z * 0.5, scale.y * 0.5);
                else if (n.y > 0.5)
                    output.face = float4(metres.x, metres.z, scale.x * 0.5, scale.z * 0.5);
                else
                    output.face = float4(metres.x, metres.y, scale.x * 0.5, scale.y * 0.5);

                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float vertical = n.y < 0.5 ? 1.0 : 0.0;
                // Street runs along Z through x = 0: a facade looks at it when its normal points back towards x = 0.
                float facesStreet = abs(output.normalWS.x) > 0.5 && output.normalWS.x * origin.x < 0.0 ? 1.0 : 0.0;
                output.data = float4(
                    Hash11(dot(origin, float3(0.1371, 0.7313, 0.4197))) * 97.0,
                    vertical,
                    facesStreet,
                    ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float seed = input.data.x;
                half lit = dot(normal, normalize(_LightDirection.xyz)) > _LitThreshold ? 1.0 : 0.0;
                // 0 = ink, 1 = paper; marks flip the base tone.
                half tone = lit;
                half light = _SelfLit;

                if (_BoxEdges > 0.5)
                {
                    float2 toEdge = input.face.zw - abs(input.face.xy);
                    float edge = Band(min(toEdge.x, toEdge.y), _EdgeWidth);
                    tone = lerp(tone, 1.0 - tone, edge);
                }
                else
                {
                    half rim = 1.0 - saturate(dot(normal, normalize(input.viewWS)));
                    tone = rim > _RimThreshold ? 1.0 - tone : tone;
                }

                if (input.data.y > 0.5)
                {
                    float2 p = float2(input.face.x, input.face.y + input.face.w);
                    float halfWidth = input.face.z;
                    float height = input.face.w * 2.0;

                    if (_Windows > 0.5)
                    {
                        float inside = step(abs(p.x), halfWidth - _FacadeMargins.x)
                            * step(_FacadeMargins.y, p.y) * step(p.y, height - _FacadeMargins.z);
                        float2 cellCoord = float2(p.x / _WindowSpacing.x + 0.5, (p.y - _FacadeMargins.y) / _WindowSpacing.y);
                        float2 offset = abs(frac(cellCoord) - 0.5);
                        float window = Band(offset.x, _WindowSize.x * 0.5) * Band(offset.y, _WindowSize.y * 0.5) * inside;
                        float litShare = lerp(_LitRange.x, _LitRange.y, Hash11(seed * 1.37 + 7.0));
                        float on = step(Hash21(floor(cellCoord) + seed), litShare) * window;
                        tone = lerp(tone, 1.0 - lit, on);
                        light = max(light, on);
                    }

                    if (_ShopFronts > 0.5 && input.data.z > 0.5 && Hash11(seed * 2.71 + 3.0) < _ShopChance)
                    {
                        float inBand = step(_ShopBand.x, p.y) * step(p.y, _ShopBand.y)
                            * step(abs(p.x), halfWidth - _ShopBand.z);
                        float mullion = Band(abs(frac(p.x / _ShopMullions.x) - 0.5) * _ShopMullions.x, _ShopMullions.y * 0.5);
                        float shop = inBand * (1.0 - mullion);
                        tone = lerp(tone, 1.0 - lit, shop);
                        light = max(light, shop);
                    }
                }

                // Exponential squared fog: scaling the factor by s raises visibility to the power s^2.
                float fogFactor = input.data.w * lerp(1.0, _LightFogScale, light);
                if (_FogHatch > 0.5)
                {
                    // Ink matches the black fog, so only paper needs to fade.
                    tone *= HatchedFog(input.positionCS.xy, fogFactor);
                    return half4(lerp(_InkColor.rgb, _PaperColor.rgb, tone), 1.0);
                }
                half3 color = lerp(_InkColor.rgb, _PaperColor.rgb, tone);
                return half4(MixFog(color, fogFactor), 1.0);
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
