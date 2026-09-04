Shader "HClTraining/InteractionGhostFresnel"
{
    Properties
    {
        [Header(Base)]
        _BaseAlpha ("Base Alpha", Range(0, 0.2)) = 0.05

        [Header(Rim)]
        _RimColor ("Rim Color", Color) = (0.35, 0.85, 1.0, 1.0)
        _RimPower ("Rim Power", Range(0.5, 8.0)) = 3.0
        _RimIntensity ("Rim Intensity", Range(0, 4.0)) = 2.0

        [Header(Pulse)]
        _PulseSpeed ("Pulse Speed", Range(0, 4.0)) = 1.2
        _PulseAmount ("Pulse Amount", Range(0, 0.5)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "DepthPrepass"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GhostDepthVert
            #pragma fragment GhostDepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _BaseAlpha;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            struct AttributesD
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsD
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryingsD GhostDepthVert(AttributesD input)
            {
                VaryingsD output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 GhostDepthFrag(VaryingsD input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "GhostFresnel"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GhostVert
            #pragma fragment GhostFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _BaseAlpha;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _PulseSpeed;
                half _PulseAmount;
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
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GhostVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 GhostFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);

                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                float rimTerm = 1.0 - saturate(dot(normalWS, viewDirWS));
                float rim = pow(rimTerm, _RimPower) * _RimIntensity * pulse;

                half3 color = _RimColor.rgb * rim;
                half alpha = saturate(_BaseAlpha + rim * _RimColor.a);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
