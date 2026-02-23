Shader "Custom/Bbox"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Bbox"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float4 _Rect;  // xy origin, zw width height
            float2 _SrcTexelSize; 

            struct Attributes 
            { 
                uint vertexID : SV_VertexID; 
            };

            struct Varyings  
            { 
                float4 posCS : SV_POSITION; 
                float2 uv : TEXCOORD0; 
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;

                // fullscreen triangle 
                float2 uv[3] =
                {
                    float2(0.0, 0.0),
                    float2(2.0, 0.0),
                    float2(0.0, 2.0)
                };

                o.uv = uv[v.vertexID];
                o.posCS = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);

                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 srcPixel = _Rect.xy + i.uv * _Rect.zw;
                float2 srcUV = srcPixel * _SrcTexelSize;

                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, srcUV);
            }
            ENDHLSL
        }
    }
}