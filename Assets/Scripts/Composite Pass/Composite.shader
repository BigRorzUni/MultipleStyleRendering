Shader "Custom/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Composite"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SrcTex);
            SAMPLER(sampler_SrcTex);

            TEXTURE2D(_BBoxTex);
            SAMPLER(sampler_BBoxTex);

            float4 _Rect;
            float2 _ScreenTexelSize;

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
                o.posCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // get pixel coords from uv
                float2 screenPixel = i.uv / _ScreenTexelSize;

                // bounds of the bounding box to composite
                float2 rectMin = _Rect.xy;
                float2 rectMax = _Rect.xy + _Rect.zw;

                // if outside the bbox rect, show src texture
                if (screenPixel.x < rectMin.x || screenPixel.y < rectMin.y ||
                    screenPixel.x >= rectMax.x || screenPixel.y >= rectMax.y)
                {
                    return SAMPLE_TEXTURE2D(_SrcTex, sampler_SrcTex, i.uv);
                }

                // compute local UV into bbox texture and return that
                float2 localUV = (screenPixel - rectMin) / max(_Rect.zw, 1.0.xx);
                return SAMPLE_TEXTURE2D(_BBoxTex, sampler_BBoxTex, localUV);
            }

            ENDHLSL
        }
    }
}