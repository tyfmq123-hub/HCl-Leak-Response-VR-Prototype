Shader "Hidden/HCl/EditorFullEyeMirror"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ EYE_ARRAY
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #ifdef EYE_ARRAY
            TEXTURE2D_ARRAY(_EyeTexture);
            #else
            TEXTURE2D(_EyeTexture);
            #endif
            SAMPLER(sampler_EyeTexture);
            float4 _SourceRect;
            float _EyeSlice;
            float _ManualSRGBRead;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(id);
                output.uv = GetFullScreenTriangleTexCoord(id) * _SourceRect.xy + _SourceRect.zw;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                #ifdef EYE_ARRAY
                float4 color = SAMPLE_TEXTURE2D_ARRAY(_EyeTexture, sampler_EyeTexture, input.uv, _EyeSlice);
                #else
                float4 color = SAMPLE_TEXTURE2D(_EyeTexture, sampler_EyeTexture, input.uv);
                #endif
                if (_ManualSRGBRead > 0.5) color.rgb = SRGBToLinear(color.rgb);
                return float4(color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
