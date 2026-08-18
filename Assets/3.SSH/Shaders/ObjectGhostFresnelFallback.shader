Shader "HClTraining/Object Ghost Fresnel Fallback"
{
    Properties
    {
        _RimColor ("Ghost Color", Color) = (0.35, 0.85, 1.0, 1.0)
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.05
        _RimPower ("Rim Power", Range(0.1, 8)) = 3
        _RimIntensity ("Rim Intensity", Range(0, 5)) = 2
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.2
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _BaseAlpha;
                half _RimPower;
                half _RimIntensity;
                half _PulseAmount;
                half _PulseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half3 viewDirectionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirectionWS = normalize(input.viewDirectionWS);
                half rim = pow(saturate(1.0h - abs(dot(normalWS, viewDirectionWS))),
                    max(0.1h, _RimPower));
                half pulse = 1.0h + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                half fill = 0.18h;
                half brightness = max(fill, rim * _RimIntensity) * pulse;
                half alpha = saturate(max(fill, _BaseAlpha) + rim * 0.55h);
                return half4(_RimColor.rgb * brightness, alpha * _RimColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
