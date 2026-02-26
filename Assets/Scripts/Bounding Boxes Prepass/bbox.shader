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

            Varyings Vert (Attributes v)
            {
                Varyings o;
                o.posCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 srcPixel = _Rect.xy + i.uv * _Rect.zw;
                float2 srcUV = srcPixel * _SrcTexelSize;

                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, srcUV);
                //return half4(frac(_Rect.x / 1000.0), frac(_Rect.y / 1000.0), 0, 1); // DEBUG + this looks cool as well
            }
            ENDHLSL
        }
    }
}