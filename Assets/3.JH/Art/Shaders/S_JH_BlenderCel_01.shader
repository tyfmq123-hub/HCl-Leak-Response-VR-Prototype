Shader "JH/EntrySafety/BlenderCel"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5

        _CelThreshold("Light Threshold", Range(0, 1)) = 0.5
        _CelSoftness("Light Edge Softness", Range(0.001, 0.25)) = 0.04
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.55

        _SpecularSize("Specular Size", Range(0.001, 0.5)) = 0.08
        _SpecularStrength("Specular Strength", Range(0, 2)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                half _CelThreshold;
                half _CelSoftness;
                half _ShadowStrength;
                half _SpecularSize;
                half _SpecularStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(
                    normalInputs.normalWS
                );
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(
                    positionInputs.positionCS.z
                );

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 sampledBase =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv
                    );

                half4 surfaceColor =
                    sampledBase * _BaseColor;

                half3 normalWS =
                    NormalizeNormalPerPixel(input.normalWS);

                half3 viewDirectionWS =
                    SafeNormalize(
                        GetWorldSpaceViewDir(input.positionWS)
                    );

                float4 shadowCoord =
                    TransformWorldToShadowCoord(input.positionWS);

                Light mainLight = GetMainLight(shadowCoord);

                half normalDotLight =
                    saturate(
                        dot(normalWS, mainLight.direction)
                    );

                half lightBand = smoothstep(
                    _CelThreshold - _CelSoftness,
                    _CelThreshold + _CelSoftness,
                    normalDotLight
                );

                half shadowAttenuation =
                    mainLight.shadowAttenuation *
                    mainLight.distanceAttenuation;

                half celLighting = lerp(
                    1.0h - _ShadowStrength,
                    1.0h,
                    lightBand * shadowAttenuation
                );

                half3 ambient = SampleSH(normalWS);

                half3 diffuse =
                    surfaceColor.rgb *
                    (
                        ambient +
                        mainLight.color * celLighting
                    );

                half3 halfDirection =
                    SafeNormalize(
                        mainLight.direction + viewDirectionWS
                    );

                half normalDotHalf =
                    saturate(
                        dot(normalWS, halfDirection)
                    );

                half specularThreshold =
                    1.0h - _SpecularSize;

                half specularBand = smoothstep(
                    specularThreshold,
                    saturate(
                        specularThreshold + _CelSoftness
                    ),
                    normalDotHalf
                );

                half specularAmount =
                    specularBand *
                    _SpecularStrength *
                    lerp(0.35h, 1.0h, _Metallic) *
                    shadowAttenuation;

                half3 finalColor =
                    diffuse +
                    mainLight.color * specularAmount;

                finalColor =
                    MixFog(finalColor, input.fogFactor);

                return half4(
                    finalColor,
                    surfaceColor.a
                );
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack Off
}
