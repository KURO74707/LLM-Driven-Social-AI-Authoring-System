Shader "FurtherDevelopment/PixelPalette"
{
    Properties
    {
        [MainColor] _BaseColor("Base color", Color) = (0.6, 0.7, 0.7, 1)
        _ShadowColor("Shadow ink", Color) = (0.07, 0.06, 0.1, 1)
        _Dither("Grain strength", Range(0, 1)) = 0.65
        _Ground("Ground texture", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadowColor;
                float _Dither;
                float _Ground;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // SV_POSITION uses low-resolution render-target pixels for every mesh.
                // A scaled ground plane and a capsule therefore share the same grain size.
                float2 cell = floor(input.positionCS.xy);
                float grain = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord);
                float illumination = saturate(dot(normalize(input.normalWS), sun.direction))
                    * sun.shadowAttenuation * sun.distanceAttenuation;
                float light = illumination;
                light = saturate(light + (grain - 0.5) * _Dither * 0.55);
                half3 mid = lerp(_ShadowColor.rgb, _BaseColor.rgb, 0.48);
                half3 highlight = lerp(_BaseColor.rgb, half3(0.86, 0.88, 0.78), 0.38);
                half3 color = light < 0.18 ? _ShadowColor.rgb : light < 0.44 ? mid
                    : light < 0.76 ? _BaseColor.rgb : highlight;
                // Subtle floor tiles provide a stationary reference during camera movement.
                float2 tile = frac(input.positionWS.xz / 2);
                float seam = step(0.975, max(tile.x, tile.y));
                half3 groundColor = _BaseColor.rgb * (0.92 + (grain - 0.5) * 0.12 * _Dither);
                groundColor *= (1 - seam * 0.18) * (0.3 + 0.7 * illumination);
                color = lerp(color, groundColor, _Ground);
                color *= lerp(half3(0.55, 0.6, 0.7), sun.color, saturate(illumination + 0.2));
                return half4(color, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;

            float4 ShadowVert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
