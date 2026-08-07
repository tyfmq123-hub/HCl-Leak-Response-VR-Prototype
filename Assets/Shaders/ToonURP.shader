Shader "Custom/ToonURP"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]  _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Header(Cel Shading)]
        _Steps ("Shading Steps", Range(1,6)) = 3
        _ShadowTint ("Shadow Tint", Color) = (0.35,0.35,0.38,1)
        _ShadowThreshold ("Shadow Threshold", Range(-0.5,0.5)) = 0.05
        _BandSmooth ("Band Smoothness", Range(0.001,0.25)) = 0.03

        [Header(Specular)]
        [Toggle(_SPECULAR_ON)] _SpecularOn ("Enable Specular", Float) = 0
        _SpecularColor ("Specular Color", Color) = (1,1,1,1)
        _SpecularSize ("Specular Size", Range(0,1)) = 0.15

        [Header(Rim Light)]
        [Toggle(_RIM_ON)] _RimOn ("Enable Rim", Float) = 1
        _RimColor ("Rim Color", Color) = (1,0.85,0.7,1)
        _RimPower ("Rim Power", Range(0.5,12)) = 4
        _RimAmount ("Rim Amount", Range(0,1)) = 0.55

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)] _OutlineOn ("Enable Outline", Float) = 1
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width (m)", Range(0,0.03)) = 0.004
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _Steps;
            half4  _ShadowTint;
            half   _ShadowThreshold;
            half   _BandSmooth;
            half4  _SpecularColor;
            half   _SpecularSize;
            half4  _RimColor;
            half   _RimPower;
            half   _RimAmount;
            half4  _OutlineColor;
            half   _OutlineWidth;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        // ------------------------------------------------------------
        // Outline pass  (inverted hull)
        // ------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma shader_feature_local _OUTLINE_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertOutline (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                half w = _OutlineWidth;
            #if !defined(_OUTLINE_ON)
                w = 0.0; // hull collapses onto the mesh -> hidden behind front faces
            #endif
                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * w;
                OUT.positionHCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 fragOutline (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return half4(_OutlineColor.rgb, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------
        // Main toon lit pass
        // ------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _SPECULAR_ON
            #pragma shader_feature_local _RIM_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.normalWS    = n.normalWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            // Quantize a 0..1 lighting value into _Steps bands with soft edges
            half ToonBand (half v)
            {
                v = saturate(v);
                half scaled  = v * _Steps;
                half lower   = floor(scaled) / _Steps;
                half f       = frac(scaled);
                half edge    = _BandSmooth * _Steps;
                half s       = smoothstep(0.5 - edge, 0.5 + edge, f);
                return saturate(lower + s / _Steps);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = dot(N, mainLight.direction);
                half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half band  = ToonBand((ndotl - _ShadowThreshold) * atten);

                half3 shade = lerp(_ShadowTint.rgb, half3(1,1,1), band);
                half3 color = albedo.rgb * mainLight.color * shade;

                // Ambient (flat, from spherical harmonics)
                color += SampleSH(N) * albedo.rgb;

                // Toon specular
                #ifdef _SPECULAR_ON
                {
                    float3 H = normalize(mainLight.direction + V);
                    half ndoth = saturate(dot(N, H));
                    half specPow = (1.0 - _SpecularSize) * 200.0 + 1.0;
                    half spec = pow(ndoth, specPow);
                    spec = smoothstep(0.45, 0.55, spec) * band;
                    color += _SpecularColor.rgb * mainLight.color * spec;
                }
                #endif

                // Additional lights (works in Forward and Forward+)
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    uint count = GetAdditionalLightsCount();
                    InputData inputData = (InputData)0;
                    inputData.positionWS = IN.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, IN.positionWS);
                        half nl = dot(N, l.direction);
                        half a  = l.shadowAttenuation * l.distanceAttenuation;
                        half b  = ToonBand(nl * a);
                        color += albedo.rgb * l.color * b;
                    LIGHT_LOOP_END
                }
                #endif

                // Rim light
                #ifdef _RIM_ON
                {
                    half rim = 1.0 - saturate(dot(N, V));
                    rim = pow(rim, _RimPower);
                    rim = smoothstep(_RimAmount - 0.03, _RimAmount + 0.03, rim);
                    color += _RimColor.rgb * rim;
                }
                #endif

                color = MixFog(color, IN.fogFactor);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------
        // Shadow caster
        // ------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            float4 GetShadowPositionHClip (Attributes IN)
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            Varyings vertShadow (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionHCS = GetShadowPositionHClip(IN);
                return OUT;
            }

            half4 fragShadow (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------
        // Depth only
        // ------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertDepth (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 fragDepth (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
