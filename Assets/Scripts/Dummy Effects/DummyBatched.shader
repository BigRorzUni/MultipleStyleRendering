Shader "Custom/DummyBatched"
{
    Properties
    {
        _SourceTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DummyBatched"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct InstanceData
            {
                float4 rect; 
            };

            StructuredBuffer<InstanceData> _InstanceData;
            float4 _NprScreenSize; 

            TEXTURE2D(_NprIdTexture);
            SAMPLER(sampler_NprIdTexture);

            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            // each quad is made of 2 triangles based on the rect of the instance data
            float2 GetQuadUV(uint vertexID)
            {
                switch (vertexID)
                {
                    case 0: 
                        return float2(0, 0);
                    case 1: 
                        return float2(1, 0);
                    case 2: 
                        return float2(1, 1);
                    case 3: 
                        return float2(0, 0);
                    case 4: 
                        return float2(1, 1);
                    case 5: 
                        return float2(0, 1);


                    default:
                        return float2(0, 0); // should never happen
                }
            }

            Varyings Vert (Attributes v)
            {
                Varyings o;

                float2 uv = GetQuadUV(v.vertexID);
                float4 rect = _InstanceData[v.instanceID].rect;

                // map local quad UV to pixel coords within bbox
                float2 pixelPos = rect.xy + uv * rect.zw;

                // convert to clip space
                float2 ndc;
                ndc.x = pixelPos.x * _NprScreenSize.z * 2.0 - 1.0;
                ndc.y = 1.0 - pixelPos.y * _NprScreenSize.w * 2.0; // flip y for Unity's screen space

                o.posCS = float4(ndc, 0.0, 1.0);

                // convert pixel coords to texture UVs
                o.screenUV = pixelPos * _NprScreenSize.zw;

                return o;
            }

            uint ReadMask8(float2 uv)
            {
                float m = SAMPLE_TEXTURE2D(_NprIdTexture, sampler_NprIdTexture, uv).r;
                return (uint)round(saturate(m) * 255.0); // unnormalise texture
            }

            float4 Frag (Varyings i) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.screenUV);
                uint mask = ReadMask8(i.screenUV);

                // if no style applied then do nothing
                if (mask == 0u)
                    return col;

                return float4(1, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}