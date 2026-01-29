Shader "Custom/ScreenspaceOutlines"
{
    Properties
    {
        _OutlineColour ("Outline Colour", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ScreenspaceOutlines"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SourceTex);
            TEXTURE2D(_NprEdgesTexture);

            float4 _OutlineColour;

            struct Attributes 
            { 
                uint vertexID : SV_VertexID;
            };
            
            struct Varyings  
            { 
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0; 
            };

            Varyings Vert (Attributes v)
            {
                Varyings output;
                output.posCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return output;
            }

            float4 Frag (Varyings i) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D(_SourceTex, sampler_LinearClamp, i.uv);
                float edge = SAMPLE_TEXTURE2D(_NprEdgesTexture, sampler_PointClamp, i.uv).r;

                float a = saturate(edge) * _OutlineColour.a;
                float3 rgb = lerp(src.rgb, _OutlineColour.rgb, a);

                return float4(rgb, src.a);
            }
            ENDHLSL
        }
    }
}