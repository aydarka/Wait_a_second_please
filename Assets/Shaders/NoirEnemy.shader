// Enemy silhouette for mobile: flat unlit body plus a one-sided white rim, like a comic back light.
// The rim is a copy of the mesh shifted a few pixels on screen and pushed slightly back in depth:
// it shows only where the body does not cover it, so the edge width stays constant in pixels and
// a part held in front of the body (the gun) gets its own inked edge.
// The rim narrows to nothing with distance, so enemies come out of the dark instead of popping in.
Shader "WaitASecond/NoirEnemy"
{
    Properties
    {
        _BaseColor ("Body", Color) = (0, 0, 0, 1)
        _RimColor ("Rim", Color) = (0.95, 0.94, 0.92, 1)
        _RimWidth ("Rim Width (pixels at 1080p screen height)", Range(0, 10)) = 3
        _RimDirection ("Rim Direction (screen x, y)", Vector) = (-1, 0.35, 0, 0)
        _RimDepthBias ("Rim Depth Bias (m)", Range(0, 1)) = 0.2
        _RimFade ("Rim Fade (hidden beyond, full within, m)", Vector) = (50, 35, 0, 0)
        // Set per enemy by EnemyGunman: a crouching enemy sinks below its spot and everything under the floor is cut away.
        _FloorHeight ("Cut Below (world height)", Float) = -100000
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _RimColor;
            float _RimWidth;
            float4 _RimDirection;
            float _RimDepthBias;
            float4 _RimFade;
            float _FloorHeight;
        CBUFFER_END

        struct ClippedVaryings
        {
            float4 positionCS : SV_POSITION;
            float heightWS : TEXCOORD0;
        };

        ClippedVaryings ClippedVert(float4 positionOS : POSITION)
        {
            ClippedVaryings output;
            float3 positionWS = TransformObjectToWorld(positionOS.xyz);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.heightWS = positionWS.y;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Body"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex ClippedVert
            #pragma fragment Frag

            half4 Frag(ClippedVaryings input) : SV_Target
            {
                clip(input.heightWS - _FloorHeight);
                // No fog: black fades into the black street fog anyway, and the rim handles distance.
                return half4(_BaseColor.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            ClippedVaryings Vert(float4 positionOS : POSITION)
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);

                // Push the copy back along the view ray without changing its screen footprint.
                float3 positionVS = TransformWorldToView(positionWS);
                positionVS.z -= _RimDepthBias;
                float4 pushedCS = TransformWViewToHClip(positionVS);
                positionCS.z = pushedCS.z / max(pushedCS.w, 1e-5) * positionCS.w;

                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float distanceToCamera = distance(origin, GetCameraPositionWS());
                float reveal = saturate((_RimFade.x - distanceToCamera) / max(_RimFade.x - _RimFade.y, 1e-3));

                float2 direction = _RimDirection.xy / max(length(_RimDirection.xy), 1e-5);
                // Flipped projections (rendering into textures on some APIs) keep the rim on the same side.
                direction.y *= _ProjectionParams.x;
                float pixels = _RimWidth * reveal * _ScreenParams.y / 1080.0;
                positionCS.xy += direction * pixels * 2.0 / _ScreenParams.xy * positionCS.w;
                ClippedVaryings output;
                output.positionCS = positionCS;
                output.heightWS = positionWS.y;
                return output;
            }

            half4 Frag(ClippedVaryings input) : SV_Target
            {
                clip(input.heightWS - _FloorHeight);
                return half4(_RimColor.rgb, 1.0);
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
            #pragma vertex ClippedVert
            #pragma fragment DepthFrag

            half DepthFrag(ClippedVaryings input) : SV_Target
            {
                clip(input.heightWS - _FloorHeight);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                float heightWS : TEXCOORD1;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.heightWS = positionWS.y;
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                clip(input.heightWS - _FloorHeight);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
